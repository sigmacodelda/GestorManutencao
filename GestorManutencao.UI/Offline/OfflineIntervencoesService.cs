using System.Globalization;
using System.Text;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;
using static GestorManutencao.UI.Offline.ClassificacaoErros;

namespace GestorManutencao.UI.Offline;

// Decorator do serviço remoto para as ações do técnico: lê da API e guarda na cache; sem rede devolve a cache.
// As escritas tentam o servidor e, sem rede, vão para a fila com uma alteração otimista (ficha Pendente) até o SincronizadorFila as enviar.
// Validar, devolver, atribuir, gerar, configuração e "todas as fichas" (supervisor) passam direto e falham sem rede como antes.
// 401, 403 (sem_tecno) e 5xx nunca são tratados como offline: propagam sem mexer na cache nem na fila.
// A regra "exige material" não se conhece offline: o servidor recusa ao sincronizar e a conclusão vira conflito com aviso.
public sealed class OfflineIntervencoesService(
    IIntervencoesRemoto remoto, ManutencaoCache mc, IFilaSaida fila, IArmazemFotosPendentes fotos, IRedeEstado rede,
    SincronizadorFila sinc, TimeProvider tempo) : IIntervencoesService, IEquipamentosService, IEstadoLeitura
{
    private const string FotoGrande = "A foto é demasiado grande. Tente outra vez.";
    private ICacheLocal Cache => mc.Cache;

    public bool AMostrarCache { get; private set; }
    public DateTimeOffset? DadosDe { get; private set; }
    public string? TextoDados => !AMostrarCache ? null : DadosDe is { } d ? $"A mostrar dados de {d:dd/MM HH:mm}" : "A mostrar dados guardados no telemóvel";
    public event Action? Mudou;

    // Uma escrita foi aceite pelo servidor: é boa altura para enviar o que estiver na fila.
    public event Action? EscritaOnline;

    private async Task DefinirLeituraAsync(bool daCache)
    {
        var de = daCache ? await mc.LerAtualizadoEmAsync() : null;
        if (daCache == AMostrarCache && de == DadosDe) return;
        AMostrarCache = daCache;
        DadosDe = de;
        Mudou?.Invoke();
    }

    private async Task<bool> TemOperacoesAsync(string dono, string fichaId) =>
        (await fila.ListarAsync(dono)).Any(o => o.FichaId == fichaId && o.Estado != EstadoOperacao.Conflito);

    // Uma ficha com operações por enviar não vai ao servidor: as novas operações entram na fila para manter a ordem.
    private async Task<bool> PodeOnlineAsync(string dono, string fichaId) =>
        rede.Disponivel && !IdLocal(fichaId) && !await TemOperacoesAsync(dono, fichaId);

    private OperacaoSaida NovaOperacao(string dono, TipoOperacao tipo, string fichaId, string payload, string? opId, bool incerta) =>
        new(Guid.NewGuid().ToString("N"), dono, tipo, fichaId, payload, opId, tempo.GetUtcNow(), 0, incerta ? EstadoOperacao.Incerta : EstadoOperacao.Pendente);

    private static InvalidOperationException FichaMudou() => new(MensagensIntervencao.Mudou);

    // ---- Leituras ----

    public async Task<IReadOnlyList<Intervencao>> ListarMinhasAsync(CancellationToken ct = default)
    {
        if (await mc.GarantirDonoAsync() is not { } dono) return await remoto.ListarMinhasAsync(ct);
        if (rede.Disponivel)
            try
            {
                var marca = mc.Marca;
                var servidor = await remoto.ListarMinhasAsync(ct);
                await mc.MesclarMinhasAsync(servidor, dono, marca);
                await mc.MarcarAtualizadoAsync();
                await DefinirLeituraAsync(false);
                return await PorFazerAsync();
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }
        await DefinirLeituraAsync(true);
        return await PorFazerAsync();
    }

    private async Task<IReadOnlyList<Intervencao>> PorFazerAsync() =>
        (await Cache.ListarFichasAsync()).Where(f => f.PorFazer).OrderBy(f => f.DataPlaneada).ThenBy(f => f.Id, StringComparer.Ordinal).ToList();

    public async Task<Intervencao?> ObterAsync(string id, CancellationToken ct = default)
    {
        if (await mc.GarantirDonoAsync() is not { } dono) return await remoto.ObterAsync(id, ct);
        id = await mc.ResolverIdAsync(id);
        var soLocal = IdLocal(id) || await TemOperacoesAsync(dono, id);
        if (!soLocal && rede.Disponivel)
            try
            {
                var marca = mc.Marca;
                var servidor = await remoto.ObterAsync(id, ct);
                await DefinirLeituraAsync(false);
                if (servidor is not null) return await mc.GuardarFichaServidorAsync(servidor, marca);
                await mc.ComTrincoAsync(() => Cache.RemoverFichasAsync([id]));
                return null;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }
        if (!soLocal) await DefinirLeituraAsync(true);
        return await Cache.ObterFichaAsync(id);
    }

    public async Task<IReadOnlyList<Material>> ListarMateriaisAsync(string id, CancellationToken ct = default)
    {
        if (await mc.GarantirDonoAsync() is not { } dono) return await remoto.ListarMateriaisAsync(id, ct);
        id = await mc.ResolverIdAsync(id);
        if (await PodeOnlineAsync(dono, id))
            try
            {
                var marca = mc.Marca;
                var lista = await remoto.ListarMateriaisAsync(id, ct);
                await mc.ComTrincoAsync(async () => { if (!await TemOperacoesAsync(dono, id) && !mc.EnviadaDesde(id, marca)) await Cache.GuardarMateriaisAsync(id, lista); });
                return lista;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }
        return await Cache.LerMateriaisAsync(id) ?? [];
    }

    public async Task<IReadOnlyList<Foto>> ListarFotosAsync(string id, CancellationToken ct = default)
    {
        if (await mc.GarantirDonoAsync() is not { } dono) return await remoto.ListarFotosAsync(id, ct);
        id = await mc.ResolverIdAsync(id);
        if (await PodeOnlineAsync(dono, id))
            try
            {
                var marca = mc.Marca;
                var lista = await remoto.ListarFotosAsync(id, ct);
                if (await Cache.ObterFichaAsync(id) is { PorFazer: true })
                    await mc.ComTrincoAsync(async () => { if (!await TemOperacoesAsync(dono, id) && !mc.EnviadaDesde(id, marca)) await Cache.GuardarFotosAsync(id, lista); });
                return lista;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }
        return await Cache.LerFotosAsync(id) ?? [];
    }

    public async Task<byte[]?> ObterFotoAsync(string id, string fotoId, CancellationToken ct = default)
    {
        if (await mc.GarantirDonoAsync() is null) return await remoto.ObterFotoAsync(id, fotoId, ct);
        if (IdLocal(fotoId)) return await fotos.LerAsync(fotoId);
        id = await mc.ResolverIdAsync(id);
        if (await Cache.LerFotoBytesAsync(id, fotoId) is { } guardada) return guardada;
        if (rede.Disponivel)
            try
            {
                var bytes = await remoto.ObterFotoAsync(id, fotoId, ct);
                if (bytes is not null && await Cache.ObterFichaAsync(id) is { PorFazer: true }) await Cache.GuardarFotoBytesAsync(id, fotoId, bytes);
                return bytes;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }
        return null;
    }

    // ---- Escritas do técnico ----

    public async Task<Intervencao> RegistarAvariaAsync(string equipamentoId, string anomalia, CancellationToken ct = default)
    {
        if (RegrasIntervencao.ValidarAvaria(equipamentoId, anomalia) is { } erro) throw new InvalidOperationException(erro);
        if (await mc.GarantirDonoAsync() is not { } dono) return await remoto.RegistarAvariaAsync(equipamentoId, anomalia, ct);

        // O mesmo opId serve a todas as tentativas, online ou da fila, para o servidor não duplicar a avaria.
        var opId = NovoOpId();
        var texto = anomalia.Trim();
        if (rede.Disponivel)
            try
            {
                var criada = await remoto.RegistarAvariaAsync(equipamentoId, texto, opId, ct);
                var guardada = await mc.GuardarFichaServidorAsync(criada);
                AvisarEscrita();
                return guardada;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }

        var equipamento = await ProcurarEquipamentoAsync(equipamentoId) ?? throw FichaMudou();
        var local = new Intervencao(NovoIdLocal(), equipamento.Nome, equipamento.Zona, TipoIntervencao.Curativa, DateOnly.FromDateTime(tempo.GetLocalNow().DateTime),
            EstadoIntervencao.EmCurso, texto, null, TimeOnly.FromDateTime(tempo.GetLocalNow().DateTime), Tecnico: dono, Pendente: true, PodePausar: true);
        await mc.ComTrincoAsync(async () =>
        {
            await fila.AdicionarAsync(NovaOperacao(dono, TipoOperacao.RegistarAvaria, local.Id, CargasFila.Escrever(new CargaAvaria(equipamentoId, texto)), opId, false));
            await Cache.GuardarFichasAsync([local]);
        });
        await sinc.AtualizarContagensAsync();
        return local;
    }

    private async Task<Equipamento?> ProcurarEquipamentoAsync(string id) =>
        (await Cache.LerEquipamentosLeveAsync())?.FirstOrDefault(e => e.Id == id) ?? (await Cache.LerEquipamentosAsync())?.FirstOrDefault(e => e.Id == id);

    public async Task<Intervencao> IniciarAsync(string id, CancellationToken ct = default)
    {
        if (await mc.GarantirDonoAsync() is not { } dono) return await remoto.IniciarAsync(id, ct);
        id = await mc.ResolverIdAsync(id);
        if (await PodeOnlineAsync(dono, id))
            try
            {
                var ficha = await mc.GuardarFichaServidorAsync(await remoto.IniciarAsync(id, ct));
                AvisarEscrita();
                return ficha;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }

        return await EnfileirarFichaAsync(dono, id, TipoOperacao.Iniciar, "{}", f => f.Estado switch
        {
            // PodePausar só se confirma com o servidor: iniciar offline não garante que o utilizador seja o técnico principal.
            EstadoIntervencao.Planeada => f with { Estado = EstadoIntervencao.EmCurso, HoraInicio = TimeOnly.FromDateTime(tempo.GetLocalNow().DateTime) },
            EstadoIntervencao.EmCurso => f,
            _ => throw FichaMudou()
        });
    }

    public async Task<Intervencao> ConcluirAsync(string id, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct = default)
    {
        if (RegrasIntervencao.ValidarConclusao(feito, inicio, fim) is { } erro) throw new InvalidOperationException(erro);
        if (await mc.GarantirDonoAsync() is not { } dono) return await remoto.ConcluirAsync(id, feito, inicio, fim, ct);
        id = await mc.ResolverIdAsync(id);
        if (await PodeOnlineAsync(dono, id))
            try
            {
                var ficha = await mc.GuardarFichaServidorAsync(await remoto.ConcluirAsync(id, feito, inicio, fim, ct));
                AvisarEscrita();
                return ficha;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }

        return await EnfileirarFichaAsync(dono, id, TipoOperacao.Concluir, CargasFila.Escrever(new CargaConclusao(feito.Trim(), inicio, fim)), f =>
            f.Estado == EstadoIntervencao.EmCurso
                ? f with { Estado = EstadoIntervencao.Concluida, Feito = feito.Trim(), HoraInicio = inicio, HoraFim = fim, Motivo = null, EmPausa = false, PodePausar = false }
                : throw FichaMudou());
    }

    public Task<Intervencao> PausarAsync(string id, CancellationToken ct = default) => PausaAsync(id, true, ct);

    public Task<Intervencao> RetomarAsync(string id, CancellationToken ct = default) => PausaAsync(id, false, ct);

    // Como Começar/Feito: online se der, senão fila com alteração otimista (só a ficha em curso tem pausa).
    private async Task<Intervencao> PausaAsync(string id, bool pausar, CancellationToken ct)
    {
        if (await mc.GarantirDonoAsync() is not { } dono) return await (pausar ? remoto.PausarAsync(id, ct) : remoto.RetomarAsync(id, ct));
        id = await mc.ResolverIdAsync(id);
        if (await PodeOnlineAsync(dono, id))
            try
            {
                var ficha = await mc.GuardarFichaServidorAsync(await (pausar ? remoto.PausarAsync(id, ct) : remoto.RetomarAsync(id, ct)));
                AvisarEscrita();
                return ficha;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }

        return await EnfileirarFichaAsync(dono, id, pausar ? TipoOperacao.Pausar : TipoOperacao.Retomar, "{}", f =>
            f is { Estado: EstadoIntervencao.EmCurso, PodePausar: true } ? f with { EmPausa = pausar }
                : throw new InvalidOperationException(pausar ? MensagensIntervencao.NaoPausa : MensagensIntervencao.NaoRetoma));
    }

    private async Task<Intervencao> EnfileirarFichaAsync(string dono, string id, TipoOperacao tipo, string payload, Func<Intervencao, Intervencao> otimista)
    {
        var resultado = await mc.ComTrincoAsync(async () =>
        {
            var ficha = await Cache.ObterFichaAsync(id) ?? throw FichaMudou();
            var nova = otimista(ficha) with { Pendente = true };
            await fila.AdicionarAsync(NovaOperacao(dono, tipo, id, payload, null, false));
            await Cache.GuardarFichasAsync([nova]);
            return nova;
        });
        await sinc.AtualizarContagensAsync();
        return resultado;
    }

    public async Task<Material> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, CancellationToken ct = default)
    {
        if (RegrasMaterial.Validar(design, qtt, epv) is { } erro) throw new InvalidOperationException(erro);
        if (await mc.GarantirDonoAsync() is not { } dono) return await remoto.AdicionarMaterialAsync(id, design, qtt, epv, ct);
        id = await mc.ResolverIdAsync(id);
        var incerta = false;
        if (await PodeOnlineAsync(dono, id))
            try
            {
                var criado = await remoto.AdicionarMaterialAsync(id, design, qtt, epv, ct);
                await mc.ComTrincoAsync(async () =>
                {
                    if (await Cache.LerMateriaisAsync(id) is { } lista && lista.All(m => m.Id != criado.Id)) await Cache.GuardarMateriaisAsync(id, [.. lista, criado]);
                });
                AvisarEscrita();
                return criado;
            }
            // Sem resposta o servidor pode já ter gravado: a operação fica Incerta e verifica-se antes de reenviar.
            catch (Exception e) when (FalhaDeRede(e, ct)) { incerta = true; }

        var material = await mc.ComTrincoAsync(async () =>
        {
            var ficha = await Cache.ObterFichaAsync(id) ?? throw FichaMudou();
            if (ficha.Estado != EstadoIntervencao.EmCurso) throw FichaMudou();
            var local = new Material(NovoIdLocal(), design.Trim(), qtt, epv, Math.Round(qtt * epv, 3));
            var lista = (await Cache.LerMateriaisAsync(id))?.ToList() ?? [];
            lista.Add(local);
            await fila.AdicionarAsync(NovaOperacao(dono, TipoOperacao.AdicionarMaterial, id, CargasFila.Escrever(new CargaMaterial(local.Id, local.Design, qtt, epv)), null, incerta));
            await Cache.GuardarMateriaisAsync(id, lista);
            await Cache.GuardarFichasAsync([ficha with { Pendente = true }]);
            return local;
        });
        await sinc.AtualizarContagensAsync();
        return material;
    }

    public async Task ApagarMaterialAsync(string id, string materialId, CancellationToken ct = default)
    {
        if (await mc.GarantirDonoAsync() is not { } dono) { await remoto.ApagarMaterialAsync(id, materialId, ct); return; }
        id = await mc.ResolverIdAsync(id);
        if (IdLocal(materialId)) { await CancelarAdicaoAsync(dono, id, TipoOperacao.AdicionarMaterial, materialId, null); return; }

        if (await PodeOnlineAsync(dono, id))
            try
            {
                await remoto.ApagarMaterialAsync(id, materialId, ct);
                await mc.ComTrincoAsync(async () =>
                {
                    if (await Cache.LerMateriaisAsync(id) is { } lista) await Cache.GuardarMateriaisAsync(id, lista.Where(m => m.Id != materialId).ToList());
                });
                AvisarEscrita();
                return;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }

        await mc.ComTrincoAsync(async () =>
        {
            if (await Cache.ObterFichaAsync(id) is not { } ficha) throw FichaMudou();
            if (await Cache.LerMateriaisAsync(id) is { } lista) await Cache.GuardarMateriaisAsync(id, lista.Where(m => m.Id != materialId).ToList());
            await fila.AdicionarAsync(NovaOperacao(dono, TipoOperacao.ApagarMaterial, id, CargasFila.Escrever(new CargaApagar(materialId)), null, false));
            await Cache.GuardarFichasAsync([ficha with { Pendente = true }]);
        });
        await sinc.AtualizarContagensAsync();
    }

    // Apagar o que ainda só existe no telemóvel cancela a operação de adicionar em vez de criar uma de apagar.
    private async Task CancelarAdicaoAsync(string dono, string fichaId, TipoOperacao tipo, string itemId, string? fotoId)
    {
        await mc.ComTrincoAsync(async () =>
        {
            var operacao = (await fila.ListarAsync(dono)).FirstOrDefault(o => o.FichaId == fichaId && o.Tipo == tipo && o.Estado != EstadoOperacao.Conflito
                && (tipo == TipoOperacao.AdicionarMaterial ? CargasFila.Ler<CargaMaterial>(o.Payload).MaterialId : CargasFila.Ler<CargaFoto>(o.Payload).FotoId) == itemId);
            if (operacao is null) return;
            if (operacao.Estado == EstadoOperacao.Incerta) throw new InvalidOperationException("Este item está a ser enviado. Tente apagar daqui a pouco.");
            await fila.RemoverAsync(operacao.Id);
            if (tipo == TipoOperacao.AdicionarMaterial)
            {
                if (await Cache.LerMateriaisAsync(fichaId) is { } lista) await Cache.GuardarMateriaisAsync(fichaId, lista.Where(m => m.Id != itemId).ToList());
            }
            else
            {
                await fotos.RemoverAsync(itemId);
                if (await Cache.LerFotosAsync(fichaId) is { } lista) await Cache.GuardarFotosAsync(fichaId, lista.Where(f => f.Id != itemId).ToList());
            }
            await mc.RecalcularPendenteAsync(dono, fichaId);
        });
        await sinc.AtualizarContagensAsync();
    }

    public async Task<Foto> AdicionarFotoAsync(string id, string nome, byte[] jpeg, CancellationToken ct = default)
    {
        if (jpeg.Length > RegrasFoto.MaxBytes) throw new InvalidOperationException(FotoGrande);
        if (await mc.GarantirDonoAsync() is not { } dono) return await remoto.AdicionarFotoAsync(id, nome, jpeg, ct);
        id = await mc.ResolverIdAsync(id);
        var incerta = false;
        if (await PodeOnlineAsync(dono, id))
            try
            {
                var criada = await remoto.AdicionarFotoAsync(id, nome, jpeg, ct);
                await mc.ComTrincoAsync(async () =>
                {
                    if (await Cache.LerFotosAsync(id) is { } lista && lista.All(f => f.Id != criada.Id)) await Cache.GuardarFotosAsync(id, [.. lista, criada]);
                    if (await Cache.ObterFichaAsync(id) is { PorFazer: true }) await Cache.GuardarFotoBytesAsync(id, criada.Id, jpeg);
                });
                AvisarEscrita();
                return criada;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { incerta = true; }

        var foto = await mc.ComTrincoAsync(async () =>
        {
            var ficha = await Cache.ObterFichaAsync(id) ?? throw FichaMudou();
            if (ficha.Estado != EstadoIntervencao.EmCurso) throw FichaMudou();
            var lista = (await Cache.LerFotosAsync(id))?.ToList() ?? [];
            if (lista.Count >= RegrasFoto.Max) throw new InvalidOperationException(MensagensIntervencao.Para("limite_fotos"));
            var local = new Foto(NovoIdLocal(), nome, jpeg.Length);
            lista.Add(local);
            await fotos.GuardarAsync(local.Id, jpeg);
            await fila.AdicionarAsync(NovaOperacao(dono, TipoOperacao.AdicionarFoto, id, CargasFila.Escrever(new CargaFoto(local.Id, nome, jpeg.Length)), null, incerta));
            await Cache.GuardarFotosAsync(id, lista);
            await Cache.GuardarFichasAsync([ficha with { Pendente = true }]);
            return local;
        });
        await sinc.AtualizarContagensAsync();
        return foto;
    }

    public async Task ApagarFotoAsync(string id, string fotoId, CancellationToken ct = default)
    {
        if (await mc.GarantirDonoAsync() is not { } dono) { await remoto.ApagarFotoAsync(id, fotoId, ct); return; }
        id = await mc.ResolverIdAsync(id);
        if (IdLocal(fotoId)) { await CancelarAdicaoAsync(dono, id, TipoOperacao.AdicionarFoto, fotoId, fotoId); return; }

        if (await PodeOnlineAsync(dono, id))
            try
            {
                await remoto.ApagarFotoAsync(id, fotoId, ct);
                await mc.ComTrincoAsync(async () =>
                {
                    if (await Cache.LerFotosAsync(id) is { } lista) await Cache.GuardarFotosAsync(id, lista.Where(f => f.Id != fotoId).ToList());
                });
                AvisarEscrita();
                return;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }

        await mc.ComTrincoAsync(async () =>
        {
            if (await Cache.ObterFichaAsync(id) is not { } ficha) throw FichaMudou();
            if (await Cache.LerFotosAsync(id) is { } lista) await Cache.GuardarFotosAsync(id, lista.Where(f => f.Id != fotoId).ToList());
            await fila.AdicionarAsync(NovaOperacao(dono, TipoOperacao.ApagarFoto, id, CargasFila.Escrever(new CargaApagar(fotoId)), null, false));
            await Cache.GuardarFichasAsync([ficha with { Pendente = true }]);
        });
        await sinc.AtualizarContagensAsync();
    }

    // ---- Equipamentos ----

    public async Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default)
    {
        texto ??= "";
        if (await mc.GarantirDonoAsync() is null) return await remoto.PesquisarAsync(texto, ct);
        if (rede.Disponivel)
            try
            {
                var lista = await remoto.PesquisarAsync(texto, ct);
                // Juntar à lista leve dá ao registo de avaria sem rede os equipamentos que o técnico já viu.
                var atual = (await Cache.LerEquipamentosLeveAsync())?.ToList() ?? [];
                if (texto.Trim().Length == 0) atual = lista.ToList();
                else foreach (var e in lista) { var pos = atual.FindIndex(x => x.Id == e.Id); if (pos >= 0) atual[pos] = e; else atual.Add(e); }
                if (atual.Count > 0) await Cache.GuardarEquipamentosLeveAsync(atual);
                return lista;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }

        if (!RegrasPesquisa.TextoValido(texto)) return [];
        var busca = TextoPesquisa.Normalizar(texto);
        var fonte = await Cache.LerEquipamentosLeveAsync() ?? await Cache.LerEquipamentosAsync() ?? [];
        var resultado = fonte.Where(e => !e.Obsoleto && (busca.Length == 0 || TextoPesquisa.Contem(busca, e.Nome, e.Ref, e.Zona)))
            .OrderBy(e => e.Nome, StringComparer.CurrentCultureIgnoreCase);
        return busca.Length == 0 ? resultado.Take(RegrasPesquisa.MaxEquipamentos + 1).ToList() : resultado.Take(MaxResultadosPesquisa).ToList();
    }

    private const int MaxResultadosPesquisa = 20;

    public async Task<IReadOnlyList<Equipamento>> ListarAsync(FiltroEquipamentos filtro, CancellationToken ct = default)
    {
        if (await mc.GarantirDonoAsync() is null) return await remoto.ListarAsync(filtro, ct);
        if (rede.Disponivel)
            try
            {
                var lista = await remoto.ListarAsync(filtro, ct);
                if (string.IsNullOrWhiteSpace(filtro.Texto) && string.IsNullOrEmpty(filtro.Tipo) && string.IsNullOrEmpty(filtro.Local) && filtro.Obsoletos)
                    await Cache.GuardarEquipamentosAsync(lista);
                return lista;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }

        var texto = TextoPesquisa.Normalizar(filtro.Texto);
        return (await Cache.LerEquipamentosAsync() ?? [])
            .Where(e => (filtro.Obsoletos || !e.Obsoleto)
                && (string.IsNullOrEmpty(filtro.Tipo) || string.Equals(e.Tipo, filtro.Tipo, StringComparison.CurrentCultureIgnoreCase))
                && (string.IsNullOrEmpty(filtro.Local) || string.Equals(e.Zona, filtro.Local, StringComparison.CurrentCultureIgnoreCase))
                && (texto.Length == 0 || TextoPesquisa.Contem(texto, e.Nome, e.Ref, e.Marca, e.Modelo)))
            .OrderBy(e => e.Nome, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.NoInterno).ToList();
    }

    public async Task<FichaEquipamento?> ObterFichaAsync(string id, CancellationToken ct = default)
    {
        if (await mc.GarantirDonoAsync() is null) return await remoto.ObterFichaAsync(id, ct);
        if (rede.Disponivel)
            try
            {
                var ficha = await remoto.ObterFichaAsync(id, ct);
                if (ficha is not null) await Cache.GuardarFichaEquipamentoAsync(new FichaEquipamentoGuardada(ficha, tempo.GetUtcNow()));
                return ficha;
            }
            catch (Exception e) when (FalhaDeRede(e, ct)) { }
        return (await Cache.LerFichaEquipamentoAsync(id))?.Ficha;
    }

    // ---- Só online (supervisor) ----

    // Depois de uma escrita de gestão que pode ter cancelado planeadas (equipamento obsoleto, instrução, tarefa): contadores limpos e cópia local das fichas atualizada.
    public async Task AposEscritaDeGestaoAsync()
    {
        _contadores = null;
        try { await ListarMinhasAsync(); }
        catch (Exception) { /* sem rede ou com erro, a cópia refresca-se na próxima leitura */ }
    }

    // Os contadores do menu deixam de valer com qualquer escrita remota.
    private void AvisarEscrita() { _contadores = null; EscritaOnline?.Invoke(); }

    private async Task<T> EscritaRemotaAsync<T>(Func<Task<T>> acao)
    {
        var r = await acao();
        _contadores = null;
        return r;
    }

    public Task<IReadOnlyList<Intervencao>> ListarPorValidarAsync(CancellationToken ct = default) => remoto.ListarPorValidarAsync(ct);
    public Task<Intervencao> ValidarAsync(string id, CancellationToken ct = default) => EscritaRemotaAsync(() => remoto.ValidarAsync(id, ct));
    public Task<Intervencao> DevolverAsync(string id, string motivo, CancellationToken ct = default) => EscritaRemotaAsync(() => remoto.DevolverAsync(id, motivo, ct));
    public Task<IReadOnlyList<Intervencao>> ListarPlaneadasAsync(CancellationToken ct = default) => remoto.ListarPlaneadasAsync(ct);
    public Task<Intervencao> AtribuirAsync(string id, int tecno, CancellationToken ct = default) => EscritaRemotaAsync(() => remoto.AtribuirAsync(id, tecno, ct));
    public Task<Intervencao> DefinirAdicionaisAsync(string id, IReadOnlyList<int> tecnos, CancellationToken ct = default) => EscritaRemotaAsync(() => remoto.DefinirAdicionaisAsync(id, tecnos, ct));
    public Task<RelatorioGeracao> GerarPlaneadasAsync(CancellationToken ct = default) => EscritaRemotaAsync(() => remoto.GerarPlaneadasAsync(ct));
    public Task<PaginaFichas> ListarTodasAsync(FiltroFichas filtro, int pagina, CancellationToken ct = default) => remoto.ListarTodasAsync(filtro, pagina, ct);
    public Task<ConfiguracaoManutencao> ObterConfiguracaoAsync(CancellationToken ct = default) => remoto.ObterConfiguracaoAsync(ct);
    public Task<ConfiguracaoManutencao> GuardarConfiguracaoAsync(ConfiguracaoManutencao configuracao, CancellationToken ct = default) => remoto.GuardarConfiguracaoAsync(configuracao, ct);

    // ---- Ficha completa e ações novas: só online, nunca vão para a fila ----

    // Sem rede, ou com a rede a falhar a meio, a ação não se faz e a mensagem é clara. As escritas numa ficha com operações por enviar
    // (ou que ainda só existe no telemóvel) também não passam: a ordem das alterações perdia-se.
    // O id local (ficha criada sem rede) passa ao id real se já foi enviada; a ação recebe o id resolvido. Em criações (POST) uma falha a meio
    // pode ter gravado mesmo assim: a mensagem avisa para confirmar antes de repetir.
    private async Task<T> SoOnlineAsync<T>(string? fichaId, Func<string, Task<T>> acao, bool escrita = false, bool criacao = false, CancellationToken ct = default)
    {
        if (!rede.Disponivel) throw new InvalidOperationException(MensagensIntervencao.SemRede);
        var id = fichaId is null ? "" : await mc.ResolverIdAsync(fichaId);
        if (escrita && id.Length > 0 && (IdLocal(id) || (await mc.GarantirDonoAsync() is { } dono && await TemOperacoesAsync(dono, id))))
            throw new InvalidOperationException(MensagensIntervencao.ComPendentes);
        T resultado;
        try { resultado = await acao(id); }
        catch (Exception e) when (FalhaDeRede(e, ct)) { throw new InvalidOperationException(criacao ? MensagensIntervencao.SemRedeAMeio : MensagensIntervencao.SemRede); }
        if (escrita) { AvisarEscrita(); _contadores = null; }
        return resultado;
    }

    private Task<bool> SoOnlineSemResultadoAsync(string fichaId, Func<string, Task> acao, CancellationToken ct) =>
        SoOnlineAsync(fichaId, async i => { await acao(i); return true; }, true, false, ct);

    // Contadores do menu: 30 segundos em memória; qualquer escrita online deita-os fora.
    private (ContadoresMenu Valor, DateTimeOffset Quando)? _contadores;

    // Depois de cancelar ou guardar online, a cópia local da ficha acompanha o servidor (falhar aqui nunca desfaz a ação).
    private async Task AtualizarCacheAsync(string id, Func<Intervencao, Intervencao> mudar)
    {
        try
        {
            if (await Cache.ObterFichaAsync(id) is { } atual) await mc.ComTrincoAsync(() => Cache.GuardarFichasAsync([mudar(atual)]));
        }
        catch (Exception) { /* a cache refresca-se na próxima leitura */ }
    }

    public Task<FichaDetalhe?> ObterDetalheAsync(string id, CancellationToken ct = default) => SoOnlineAsync(id, i => remoto.ObterDetalheAsync(i, ct), ct: ct);

    public async Task<FichaDetalhe> GuardarFichaAsync(string id, EdicaoFicha edicao, CancellationToken ct = default)
    {
        var d = await SoOnlineAsync(id, i => remoto.GuardarFichaAsync(i, edicao, ct), true, ct: ct);
        await AtualizarCacheAsync(d.Id, f => f with
        {
            Anomalia = string.IsNullOrWhiteSpace(d.Problema) ? null : d.Problema, Feito = string.IsNullOrWhiteSpace(d.Resolucao) ? null : d.Resolucao,
            HoraInicio = d.HoraInicio is { } i ? TimeOnly.FromDateTime(i) : null, HoraFim = d.HoraFim is { } fim ? TimeOnly.FromDateTime(fim) : null,
            DataPlaneada = d.Data, Tipo = d.Tipo, Tecno = d.Tecno, Tecnico = d.Tecnico
        });
        return d;
    }

    public async Task<Intervencao> CancelarAsync(string id, string motivo, CancellationToken ct = default)
    {
        var cancelada = await SoOnlineAsync(id, i => remoto.CancelarAsync(i, motivo, ct), true, ct: ct);
        await AtualizarCacheAsync(cancelada.Id, f => f with { Estado = EstadoIntervencao.Cancelada, Motivo = null, Pendente = false });
        return cancelada;
    }

    public Task<Intervencao> CriarFichaManualAsync(NovaFicha ficha, CancellationToken ct = default) =>
        SoOnlineAsync(null, _ => remoto.CriarFichaManualAsync(ficha.OpId is null ? ficha with { OpId = NovoOpId() } : ficha, ct), true, true, ct);

    public Task<FichaDetalhe> FecharFichaAntigaAsync(string id, bool supervisao, bool fechada, CancellationToken ct = default) =>
        SoOnlineAsync(id, i => remoto.FecharFichaAntigaAsync(i, supervisao, fechada, ct), true, ct: ct);
    public Task<Material> EditarMaterialAsync(string id, string materialId, string design, decimal qtt, decimal epv, CancellationToken ct = default) =>
        SoOnlineAsync(id, i => remoto.EditarMaterialAsync(i, materialId, design, qtt, epv, ct), true, ct: ct);
    public Task<IReadOnlyList<TempoMaoDeObra>> ListarMaoDeObraAsync(string id, CancellationToken ct = default) => SoOnlineAsync(id, i => remoto.ListarMaoDeObraAsync(i, ct), ct: ct);
    public Task<TempoMaoDeObra> AdicionarMaoDeObraAsync(string id, int tecno, decimal horas, decimal custoHora, CancellationToken ct = default) =>
        SoOnlineAsync(id, i => remoto.AdicionarMaoDeObraAsync(i, tecno, horas, custoHora, ct), true, true, ct);
    public Task<TempoMaoDeObra> EditarMaoDeObraAsync(string id, string tempoId, int tecno, decimal horas, decimal custoHora, CancellationToken ct = default) =>
        SoOnlineAsync(id, i => remoto.EditarMaoDeObraAsync(i, tempoId, tecno, horas, custoHora, ct), true, ct: ct);
    public Task ApagarMaoDeObraAsync(string id, string tempoId, CancellationToken ct = default) => SoOnlineSemResultadoAsync(id, i => remoto.ApagarMaoDeObraAsync(i, tempoId, ct), ct);
    public Task<decimal> CustoHoraSugeridoAsync(int tecno, CancellationToken ct = default) => SoOnlineAsync(null, _ => remoto.CustoHoraSugeridoAsync(tecno, ct), ct: ct);
    public Task<IReadOnlyList<CompraLigada>> ListarComprasAsync(string id, CancellationToken ct = default) => SoOnlineAsync(id, i => remoto.ListarComprasAsync(i, ct), ct: ct);
    public Task<IReadOnlyList<Fornecedor>> PesquisarFornecedoresAsync(string texto, CancellationToken ct = default) => SoOnlineAsync(null, _ => remoto.PesquisarFornecedoresAsync(texto, ct), ct: ct);

    public async Task<ContadoresMenu> ObterContadoresAsync(CancellationToken ct = default)
    {
        if (_contadores is { } c && tempo.GetUtcNow() - c.Quando < TimeSpan.FromSeconds(30)) return c.Valor;
        var novo = await SoOnlineAsync(null, _ => remoto.ObterContadoresAsync(ct), ct: ct);
        _contadores = (novo, tempo.GetUtcNow());
        return novo;
    }
}

internal static class TextoPesquisa
{
    // Sem acentos nem maiúsculas.
    public static string Normalizar(string? texto)
    {
        var sb = new StringBuilder();
        foreach (var c in (texto ?? "").Trim().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool Contem(string busca, params string?[] campos) => campos.Any(c => Normalizar(c).Contains(busca, StringComparison.Ordinal));
}
