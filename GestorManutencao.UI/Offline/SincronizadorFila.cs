using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;
using static GestorManutencao.UI.Offline.ClassificacaoErros;

namespace GestorManutencao.UI.Offline;

public enum EstadoSincronizacao { Sincronizado, ASincronizar, PorEnviar, ComConflitos }

// Envia a fila do dono atual para o servidor, por ordem e por ficha. O servidor ganha sempre: uma recusa permanente
// (400, 404, 409, 413, 422) passa a operação e as dependentes da mesma ficha a Conflito, com um aviso que guarda o texto do técnico.
// Falha de rede ou 401: pára sem perder nada e retoma depois. Qualquer outra falha (403, 5xx, foto ilegível, dados corrompidos)
// bloqueia só essa ficha nesta passagem, as outras continuam; à 5.ª tentativa falhada a operação passa a Conflito com aviso.
// A app chama SincronizarAsync quando a rede volta, no arranque, depois de uma escrita online com sucesso e a cada 60 s enquanto houver pendentes.
// O trinco do envio é partilhado por todos os âmbitos que usem a mesma fila. Se o utilizador mudar a meio, nada do anterior é enviado
// com a sessão do novo nem gravado na cache dele.
public sealed class SincronizadorFila(IIntervencoesRemoto remoto, ManutencaoCache mc, IFilaSaida fila, IArmazemFotosPendentes fotos, IDonoDados donoDados, TimeProvider tempo)
{
    public const int MaxTentativas = 5;

    private readonly SemaphoreSlim _emCurso = TrincosPartilhados.Obter(fila, "envio");
    private ICacheLocal Cache => mc.Cache;
    private volatile bool _aSincronizar;
    private int _conflitos;

    public int PendentesCount { get; private set; }
    public int ConflitosCount => _conflitos;
    public EstadoSincronizacao Estado => _aSincronizar ? EstadoSincronizacao.ASincronizar
        : _conflitos > 0 ? EstadoSincronizacao.ComConflitos
        : PendentesCount > 0 ? EstadoSincronizacao.PorEnviar : EstadoSincronizacao.Sincronizado;

    public event Action? Mudou;

    public async Task AtualizarContagensAsync()
    {
        if (donoDados.Dono is { } dono)
        {
            var operacoes = await fila.ListarAsync(dono);
            PendentesCount = operacoes.Count(o => o.Estado != EstadoOperacao.Conflito);
            _conflitos = (await fila.ListarAvisosAsync(dono)).Count;
        }
        else { PendentesCount = 0; _conflitos = 0; }
        Mudou?.Invoke();
    }

    public async Task<IReadOnlyList<AvisoConflito>> ListarAvisosAsync() =>
        donoDados.Dono is { } dono ? await fila.ListarAvisosAsync(dono) : [];

    // Se já há uma sincronização a decorrer, esta chamada não faz nada, a não ser que esperar seja true
    // (arranque de um novo utilizador: espera que a do anterior, já cancelada, termine).
    public async Task SincronizarAsync(CancellationToken ct = default, bool esperar = false)
    {
        if (donoDados.Dono is not { } dono) return;
        try
        {
            if (esperar) await _emCurso.WaitAsync(ct);
            else if (!await _emCurso.WaitAsync(0, CancellationToken.None)) return;
        }
        catch (OperationCanceledException) { return; }
        try
        {
            if (!mc.EDono(dono)) return;
            _aSincronizar = true;
            Mudou?.Invoke();
            await DrenarAsync(dono, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _aSincronizar = false;
            _emCurso.Release();
            await AtualizarContagensAsync();
        }
    }

    private async Task DrenarAsync(string dono, CancellationToken ct)
    {
        var bloqueadas = new HashSet<string>();
        var servidor = new Dictionary<string, Intervencao>();
        while (!ct.IsCancellationRequested && mc.EDono(dono))
        {
            var operacoes = await fila.ListarAsync(dono);
            var operacao = operacoes.FirstOrDefault(o => o.Estado != EstadoOperacao.Conflito && !bloqueadas.Contains(o.FichaId));
            if (operacao is null) return;
            try
            {
                var fichaId = await EnviarAsync(operacao, dono, servidor, ct);
                await FinalizarFichaAsync(dono, fichaId, servidor);
            }
            catch (Exception e) when (FalhaDeRede(e, ct))
            {
                await RegistarFalhaAsync(operacao, dono, "Sem ligação ao servidor.", contar: false);
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException && SemSessao(e))
            {
                await RegistarFalhaAsync(operacao, dono, e.GetType().Name, contar: false);
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException && Recusa(e))
            {
                // Pausar/Retomar recusados são só descartados: não marcam nem bloqueiam as outras operações da ficha (Concluir, material, fotos).
                if (operacao.Tipo is TipoOperacao.Pausar or TipoOperacao.Retomar) await DescartarPausaAsync(operacao, dono, ct);
                else
                {
                    await MarcarConflitoAsync(operacao, Motivo(e), dono, ct);
                    bloqueadas.Add(operacao.FichaId);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // 403, 5xx, foto ilegível, dados corrompidos: a operação não deve travar as outras fichas.
                var atual = await RegistarFalhaAsync(operacao, dono, e.GetType().Name, contar: true);
                bloqueadas.Add(operacao.FichaId);
                if (atual.Tentativas >= MaxTentativas) await MarcarConflitoAsync(atual, null, dono, ct, AvisoSemEnvio);
            }
            await AtualizarContagensAsync();
        }
    }

    // Relê a operação: o envio pode tê-la marcado Incerta e essa marca não se perde. Só as falhas que não são de rede contam para as 5 tentativas.
    private async Task<OperacaoSaida> RegistarFalhaAsync(OperacaoSaida operacao, string dono, string erro, bool contar)
    {
        var atual = (await fila.ListarAsync(dono)).FirstOrDefault(o => o.Id == operacao.Id);
        if (atual is null) return operacao;
        var nova = atual with { Tentativas = atual.Tentativas + (contar ? 1 : 0), UltimoErro = erro };
        await fila.AtualizarAsync(nova);
        return nova;
    }

    // Se o utilizador mudou, o que vem a seguir não pode mexer na cache do novo.
    private Task NoDonoAsync(string dono, Func<Task> acao) => mc.ComTrincoAsync(async () =>
    {
        if (mc.EDono(dono)) await acao();
    });

    private void ConfirmarDono(string dono, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!mc.EDono(dono)) throw new OperationCanceledException(ct);
    }

    // Devolve o id da ficha depois do envio (muda quando a avaria local é criada no servidor).
    private async Task<string> EnviarAsync(OperacaoSaida operacao, string dono, Dictionary<string, Intervencao> servidor, CancellationToken ct)
    {
        var fichaId = operacao.FichaId;
        ConfirmarDono(dono, ct);
        switch (operacao.Tipo)
        {
            case TipoOperacao.RegistarAvaria:
                var avaria = CargasFila.Ler<CargaAvaria>(operacao.Payload);
                var criada = await remoto.RegistarAvariaAsync(avaria.EquipamentoId, avaria.Anomalia, operacao.OpId ?? NovoOpId(), ct);
                await mc.RemapearAsync(dono, fichaId, criada.Id);
                servidor[criada.Id] = criada;
                fichaId = criada.Id;
                break;
            case TipoOperacao.Iniciar:
                servidor[fichaId] = await remoto.IniciarAsync(fichaId, ct);
                break;
            case TipoOperacao.Pausar:
                servidor[fichaId] = await remoto.PausarAsync(fichaId, ct);
                break;
            case TipoOperacao.Retomar:
                servidor[fichaId] = await remoto.RetomarAsync(fichaId, ct);
                break;
            case TipoOperacao.Concluir:
                var conclusao = CargasFila.Ler<CargaConclusao>(operacao.Payload);
                servidor[fichaId] = await remoto.ConcluirAsync(fichaId, conclusao.Feito, conclusao.Inicio, conclusao.Fim, ct);
                break;
            case TipoOperacao.AdicionarMaterial:
                await EnviarMaterialAsync(operacao, dono, ct);
                break;
            case TipoOperacao.AdicionarFoto:
                await EnviarFotoAsync(operacao, dono, ct);
                break;
            case TipoOperacao.ApagarMaterial:
                await remoto.ApagarMaterialAsync(fichaId, CargasFila.Ler<CargaApagar>(operacao.Payload).ItemId, ct);
                break;
            case TipoOperacao.ApagarFoto:
                await remoto.ApagarFotoAsync(fichaId, CargasFila.Ler<CargaApagar>(operacao.Payload).ItemId, ct);
                break;
        }
        mc.FichaEnviada(fichaId);
        await fila.RemoverAsync(operacao.Id);
        return fichaId;
    }

    // Antes de enviar fica Incerta: se a app fechar a meio, ao retomar verifica-se se o servidor já o gravou.
    // Dentro do trinco e relendo a operação: se entretanto foi cancelada (apagar um item só local), devolve null e não se envia nada.
    private Task<OperacaoSaida?> MarcarIncertaAsync(OperacaoSaida operacao) => mc.ComTrincoAsync(async () =>
    {
        var atual = (await fila.ListarAsync(operacao.Dono)).FirstOrDefault(o => o.Id == operacao.Id);
        if (atual is null || atual.Estado == EstadoOperacao.Conflito) return null;
        if (atual.Estado == EstadoOperacao.Incerta) return atual;
        var marcada = atual with { Estado = EstadoOperacao.Incerta };
        await fila.AtualizarAsync(marcada);
        return marcada;
    });

    private async Task EnviarMaterialAsync(OperacaoSaida operacao, string dono, CancellationToken ct)
    {
        var carga = CargasFila.Ler<CargaMaterial>(operacao.Payload);
        var verificar = operacao.Estado == EstadoOperacao.Incerta;
        if (await MarcarIncertaAsync(operacao) is null) return;
        ConfirmarDono(dono, ct);

        Material? material = null;
        if (verificar)
        {
            // Já existe no servidor um igual que a cache não conhecia: foi este que chegou antes de a resposta se perder.
            var conhecidos = (await Cache.LerMateriaisAsync(operacao.FichaId) ?? []).Where(m => !IdLocal(m.Id)).Select(m => m.Id).ToHashSet();
            material = (await remoto.ListarMateriaisAsync(operacao.FichaId, ct))
                .FirstOrDefault(m => !conhecidos.Contains(m.Id) && m.Design.Trim() == carga.Design.Trim() && m.Qtt == carga.Qtt && m.Epv == carga.Epv);
        }
        material ??= await remoto.AdicionarMaterialAsync(operacao.FichaId, carga.Design, carga.Qtt, carga.Epv, ct);

        await NoDonoAsync(dono, async () =>
        {
            if (await Cache.LerMateriaisAsync(operacao.FichaId) is { } lista)
                await Cache.GuardarMateriaisAsync(operacao.FichaId, lista.Select(m => m.Id == carga.MaterialId ? material : m).ToList());
        });
    }

    private async Task EnviarFotoAsync(OperacaoSaida operacao, string dono, CancellationToken ct)
    {
        var carga = CargasFila.Ler<CargaFoto>(operacao.Payload);
        var verificar = operacao.Estado == EstadoOperacao.Incerta;
        if (await MarcarIncertaAsync(operacao) is null) return;

        byte[]? lidos;
        try { lidos = await fotos.LerAsync(carga.FotoId); }
        catch (IOException e) { throw new ErroLocalException(e); }
        if (lidos is not { } bytes)
        {
            // Já marcada como a enviar, só desaparece por aqui; sem a operação (cancelada) não há nada a avisar.
            if ((await fila.ListarAsync(dono)).All(o => o.Id != operacao.Id)) return;
            throw new InvalidOperationException(FotoJaNaoExiste);
        }
        ConfirmarDono(dono, ct);

        Foto? foto = null;
        if (verificar)
        {
            var conhecidas = (await Cache.LerFotosAsync(operacao.FichaId) ?? []).Where(f => !IdLocal(f.Id)).Select(f => f.Id).ToHashSet();
            foto = (await remoto.ListarFotosAsync(operacao.FichaId, ct)).FirstOrDefault(f => !conhecidas.Contains(f.Id) && f.Nome == carga.Nome && f.Bytes == bytes.Length);
        }
        foto ??= await remoto.AdicionarFotoAsync(operacao.FichaId, carga.Nome, bytes, ct);

        await NoDonoAsync(dono, async () =>
        {
            if (await Cache.LerFotosAsync(operacao.FichaId) is { } lista)
                await Cache.GuardarFotosAsync(operacao.FichaId, lista.Select(f => f.Id == carga.FotoId ? foto : f).ToList());
            if (await Cache.ObterFichaAsync(operacao.FichaId) is { PorFazer: true }) await Cache.GuardarFotoBytesAsync(operacao.FichaId, foto.Id, bytes);
        });
        try { await fotos.RemoverAsync(carga.FotoId); }
        catch (IOException) { }
    }

    // Sem mais operações pendentes a ficha passa a ser a do servidor e deixa de estar "por enviar".
    private Task FinalizarFichaAsync(string dono, string fichaId, Dictionary<string, Intervencao> servidor) => NoDonoAsync(dono, async () =>
    {
        var restantes = (await fila.ListarAsync(dono)).Any(o => o.FichaId == fichaId && o.Estado != EstadoOperacao.Conflito);
        if (restantes) return;
        if (servidor.Remove(fichaId, out var doServidor)) await Cache.GuardarFichasAsync([doServidor with { Pendente = false }]);
        else await mc.RecalcularPendenteAsync(dono, fichaId);
    });

    private async Task MarcarConflitoAsync(OperacaoSaida operacao, string? motivo, string dono, CancellationToken ct, string? mensagem = null)
    {
        var afetadas = (await fila.ListarAsync(dono)).Where(o => o.FichaId == operacao.FichaId && o.Estado != EstadoOperacao.Conflito).ToList();
        var textos = new List<TextoDoTecnico>();
        foreach (var a in afetadas)
            try { if (TextoDe(a) is { } t) textos.Add(t); }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { }
        var equipamento = mc.EDono(dono) ? (await Cache.ObterFichaAsync(operacao.FichaId))?.Equipamento ?? "" : "";
        var aviso = new AvisoConflito(Guid.NewGuid().ToString("N"), dono, operacao.FichaId, equipamento,
            mensagem ?? $"Não foi possível enviar o que registou sem rede. {motivo}", textos, tempo.GetUtcNow());
        await fila.AdicionarAvisoAsync(aviso);
        foreach (var a in afetadas) await fila.AtualizarAsync(a with { Estado = EstadoOperacao.Conflito, AvisoId = aviso.Id, UltimoErro = motivo ?? NaoAceitou });
        await RefrescarDoServidorAsync(operacao.FichaId, dono, ct);
    }

    // Só a pausa da ficha volta ao que o servidor diz (sem mexer no resto da ficha, que pode ter operações por enviar).
    private async Task DescartarPausaAsync(OperacaoSaida operacao, string dono, CancellationToken ct)
    {
        await fila.RemoverAsync(operacao.Id);
        Intervencao? doServidor = null;
        try
        {
            ConfirmarDono(dono, ct);
            if (!IdLocal(operacao.FichaId)) doServidor = await remoto.ObterAsync(operacao.FichaId, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException) { }
        await NoDonoAsync(dono, async () =>
        {
            if (await Cache.ObterFichaAsync(operacao.FichaId) is { Estado: EstadoIntervencao.EmCurso } local)
                await Cache.GuardarFichasAsync([local with { EmPausa = doServidor?.EmPausa ?? false, PodePausar = doServidor?.PodePausar ?? false }]);
            await mc.RecalcularPendenteAsync(dono, operacao.FichaId);
        });
    }

    private static TextoDoTecnico? TextoDe(OperacaoSaida operacao) => operacao.Tipo switch
    {
        TipoOperacao.RegistarAvaria => new("Descrição da avaria", CargasFila.Ler<CargaAvaria>(operacao.Payload).Anomalia),
        TipoOperacao.Concluir => new("O que foi feito", CargasFila.Ler<CargaConclusao>(operacao.Payload).Feito),
        TipoOperacao.AdicionarMaterial => new("Material", CargasFila.Ler<CargaMaterial>(operacao.Payload).Design),
        _ => null
    };

    // A ficha passa a mostrar o estado do servidor. Se não for possível agora, a próxima atualização da cache corrige-a.
    private async Task RefrescarDoServidorAsync(string fichaId, string dono, CancellationToken ct)
    {
        if (IdLocal(fichaId)) { await NoDonoAsync(dono, () => Cache.RemoverFichasAsync([fichaId])); return; }
        try
        {
            ConfirmarDono(dono, ct);
            var ficha = await remoto.ObterAsync(fichaId, ct);
            var materiais = ficha is null ? null : await remoto.ListarMateriaisAsync(fichaId, ct);
            await NoDonoAsync(dono, async () =>
            {
                if (ficha is null) { await Cache.RemoverFichasAsync([fichaId]); return; }
                await Cache.GuardarFichasAsync([ficha with { Pendente = false }]);
                await Cache.GuardarMateriaisAsync(fichaId, materiais!);
                await Cache.RemoverFotosAsync(fichaId);
            });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await NoDonoAsync(dono, async () =>
            {
                if (await Cache.ObterFichaAsync(fichaId) is { } ficha) await Cache.GuardarFichasAsync([ficha with { Pendente = false }]);
            });
        }
    }

    // O técnico já viu o aviso: apaga-o e as operações recusadas (e as fotos que ficaram guardadas).
    public async Task DescartarConflitoAsync(string avisoId)
    {
        if (donoDados.Dono is not { } dono) return;
        await mc.ComTrincoAsync(async () =>
        {
            foreach (var o in (await fila.ListarAsync(dono)).Where(o => o.AvisoId == avisoId))
            {
                if (o.Tipo == TipoOperacao.AdicionarFoto)
                    try { await fotos.RemoverAsync(CargasFila.Ler<CargaFoto>(o.Payload).FotoId); }
                    catch (Exception e) when (e is IOException or ArgumentException or System.Text.Json.JsonException) { }
                await fila.RemoverAsync(o.Id);
            }
            await fila.RemoverAvisoAsync(avisoId);
        });
        await AtualizarContagensAsync();
    }

    // Apaga do armazém as fotos que nenhuma operação (de qualquer utilizador) referencia: restos de envios ou cancelamentos interrompidos.
    public Task LimparFotosOrfasAsync() => mc.ComTrincoAsync(async () =>
    {
        var usadas = new HashSet<string>();
        foreach (var o in await fila.ListarTodasAsync())
            if (o.Tipo == TipoOperacao.AdicionarFoto)
                try { usadas.Add(CargasFila.Ler<CargaFoto>(o.Payload).FotoId); }
                catch (System.Text.Json.JsonException) { }
        await fotos.RemoverExcetoAsync(usadas);
    });
}
