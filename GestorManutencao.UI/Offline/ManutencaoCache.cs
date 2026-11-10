using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Offline;

// Regras da cópia local: dono, retenção e junção com o que vem do servidor (sem nunca perder o que ainda está por enviar).
// Trinco serializa as alterações compostas (cache + fila) entre os ecrãs e o sincronizador; não é reentrante.
public sealed class ManutencaoCache(ICacheLocal cache, IFilaSaida fila, IDonoDados donoDados, TimeProvider tempo)
{
    public const int DiasRetencao = 30;
    private const string ChaveDono = "dono", ChaveAtualizado = "atualizado", PrefixoRemap = "remap:";

    private readonly SemaphoreSlim _trinco = TrincosPartilhados.Obter(cache, "cache");
    private readonly SemaphoreSlim _trincoDono = TrincosPartilhados.Obter(cache, "dono");

    private readonly object _marcas = new();
    private readonly Dictionary<string, long> _fichasEnviadas = [];
    private long _versao;

    public ICacheLocal Cache => cache;

    public bool EDono(string dono) => donoDados.Dono == dono;

    // Versão a ler antes de pedir dados ao servidor; se uma operação da ficha terminar depois disso, a resposta já é antiga.
    public long Marca { get { lock (_marcas) return _versao; } }

    // Chamar antes de remover a operação da fila (assim nenhuma fusão a vê "sem operações" sem ver também esta marca).
    public void FichaEnviada(string fichaId)
    {
        lock (_marcas) _fichasEnviadas[fichaId] = ++_versao;
    }

    public bool EnviadaDesde(string fichaId, long marca)
    {
        lock (_marcas) return _fichasEnviadas.TryGetValue(fichaId, out var v) && v > marca;
    }

    public async Task<T> ComTrincoAsync<T>(Func<Task<T>> acao)
    {
        await _trinco.WaitAsync();
        try { return await acao(); }
        finally { _trinco.Release(); }
    }

    public async Task ComTrincoAsync(Func<Task> acao) => await ComTrincoAsync(async () => { await acao(); return 0; });

    // Outro utilizador no mesmo telemóvel: a cache do anterior apaga-se. A fila e as fotos pendentes ficam até ele voltar.
    public async Task<string?> GarantirDonoAsync()
    {
        var atual = donoDados.Dono;
        if (atual is null) return null;
        await _trincoDono.WaitAsync();
        try
        {
            // A limpeza passa pelo trinco: nenhuma gravação do dono anterior fica a meio por cima dela.
            if (await cache.LerMetaAsync(ChaveDono) != atual)
                await ComTrincoAsync(async () =>
                {
                    await cache.LimparAsync();
                    await cache.GuardarMetaAsync(ChaveDono, atual);
                });
        }
        finally { _trincoDono.Release(); }
        return atual;
    }

    public async Task<DateTimeOffset?> LerAtualizadoEmAsync() =>
        DateTimeOffset.TryParse(await cache.LerMetaAsync(ChaveAtualizado), out var quando) ? quando : null;

    public Task MarcarAtualizadoAsync() => cache.GuardarMetaAsync(ChaveAtualizado, tempo.GetLocalNow().ToString("o"));

    // A página pode ainda ter na rota o id local de uma avaria que entretanto foi criada no servidor.
    public async Task<string> ResolverIdAsync(string id) =>
        ClassificacaoErros.IdLocal(id) ? await cache.LerMetaAsync(PrefixoRemap + id) ?? id : id;

    // Avaria criada no servidor: o id local passa a real na fila e na cache (a ficha mantém o estado otimista até as restantes operações saírem).
    public Task RemapearAsync(string dono, string idLocal, string idReal) => ComTrincoAsync(async () =>
    {
        FichaEnviada(idLocal);
        FichaEnviada(idReal);
        await fila.RemapearFichaAsync(dono, idLocal, idReal);
        if (donoDados.Dono != dono) return;
        await cache.GuardarMetaAsync(PrefixoRemap + idLocal, idReal);
        var ficha = await cache.ObterFichaAsync(idLocal);
        var materiais = await cache.LerMateriaisAsync(idLocal);
        var fotos = await cache.LerFotosAsync(idLocal);
        await cache.RemoverFichasAsync([idLocal]);
        if (ficha is not null) await cache.GuardarFichasAsync([ficha with { Id = idReal }]);
        if (materiais is not null) await cache.GuardarMateriaisAsync(idReal, materiais);
        if (fotos is not null) await cache.GuardarFotosAsync(idReal, fotos);
    });

    private async Task<HashSet<string>> FichasComOperacoesAsync(string? dono) =>
        dono is null ? [] : (await fila.ListarAsync(dono)).Select(o => o.FichaId).ToHashSet();

    public async Task<bool> TemOperacoesAsync(string dono, string fichaId) =>
        (await fila.ListarAsync(dono)).Any(o => o.FichaId == fichaId && o.Estado != EstadoOperacao.Conflito);

    // Chamar dentro do trinco: a ficha deixa de estar "por enviar" quando já não tem operações pendentes.
    public async Task RecalcularPendenteAsync(string dono, string fichaId)
    {
        if (await cache.ObterFichaAsync(fichaId) is not { } ficha) return;
        var pendente = (await fila.ListarAsync(dono)).Any(o => o.FichaId == fichaId && o.Estado != EstadoOperacao.Conflito);
        if (pendente != ficha.Pendente) await cache.GuardarFichasAsync([ficha with { Pendente = pendente }]);
    }

    // Grava o que veio do servidor, exceto fichas que ainda têm alterações por enviar (essas mandam). Devolve a versão a mostrar.
    // marca: o valor de Marca lido antes do pedido ao servidor; uma ficha cuja operação terminou entretanto não volta ao estado antigo.
    public Task<Intervencao> GuardarFichaServidorAsync(Intervencao servidor, long? marca = null) => ComTrincoAsync(async () =>
    {
        var existente = await cache.ObterFichaAsync(servidor.Id);
        if (existente is { Pendente: true }) return existente;
        if (marca is { } m && EnviadaDesde(servidor.Id, m) && existente is not null) return existente;
        await cache.GuardarFichasAsync([servidor with { Pendente = false }]);
        return servidor;
    });

    public Task MesclarFichasAsync(IEnumerable<Intervencao> servidor, string? dono = null, long? marca = null) => ComTrincoAsync(async () =>
    {
        if (dono is not null && donoDados.Dono != dono) return;
        var existentes = (await cache.ListarFichasAsync()).ToDictionary(f => f.Id);
        await GuardarDoServidorAsync(servidor, existentes, marca, null);
    });

    // Uma só gravação para todas: a cache SQLite faz um commit por gravação e, ficha a ficha, a lista demorava segundos no telemóvel.
    private async Task GuardarDoServidorAsync(IEnumerable<Intervencao> servidor, Dictionary<string, Intervencao> existentes, long? marca, HashSet<string>? comOperacoes)
    {
        var gravar = servidor
            .Where(s => !(existentes.TryGetValue(s.Id, out var e) && e.Pendente)
                        && !(comOperacoes?.Contains(s.Id) ?? false)
                        && !(marca is { } m && EnviadaDesde(s.Id, m)))
            .Select(s => s with { Pendente = false })
            .ToList();
        if (gravar.Count > 0) await cache.GuardarFichasAsync(gravar);
    }

    // A lista das minhas por fazer é completa: as por fazer que já não vêm (atribuídas a outro, concluídas) saem da cache.
    public Task MesclarMinhasAsync(IReadOnlyList<Intervencao> servidor, string? donoEsperado = null, long? marca = null) => ComTrincoAsync(async () =>
    {
        var dono = donoDados.Dono;
        if (donoEsperado is not null && dono != donoEsperado) return;
        var comOperacoes = await FichasComOperacoesAsync(dono);
        var ids = servidor.Select(s => s.Id).ToHashSet();
        var fichas = await cache.ListarFichasAsync();
        await GuardarDoServidorAsync(servidor, fichas.ToDictionary(f => f.Id), marca, comOperacoes);
        var obsoletas = fichas.Where(f => f.PorFazer && !f.Pendente && !ClassificacaoErros.IdLocal(f.Id) && !ids.Contains(f.Id) && !comOperacoes.Contains(f.Id)
            && !(marca is { } m2 && EnviadaDesde(f.Id, m2))).Select(f => f.Id);
        await cache.RemoverFichasAsync(obsoletas);
    });

    // Por fazer ficam todas; o resto fica se a data for dos últimos 30 dias. Nunca apaga fichas locais nem com operações por enviar ou em conflito.
    // O modelo não tem data de conclusão: usa-se a data planeada, por isso corre-se antes de gravar as "recentes" do servidor.
    public Task AplicarRetencaoAsync(DateOnly hoje, string? dono = null) => ComTrincoAsync(async () =>
    {
        if (dono is not null && donoDados.Dono != dono) return;
        var limite = hoje.AddDays(-DiasRetencao);
        var comOperacoes = await FichasComOperacoesAsync(donoDados.Dono);
        var fichas = await cache.ListarFichasAsync();
        bool Protegida(Intervencao f) => f.Pendente || ClassificacaoErros.IdLocal(f.Id) || comOperacoes.Contains(f.Id);

        var apagar = fichas.Where(f => !f.PorFazer && f.DataPlaneada < limite && !Protegida(f)).Select(f => f.Id).ToHashSet();
        await cache.RemoverFichasAsync(apagar);
        foreach (var f in fichas.Where(f => !f.PorFazer && !apagar.Contains(f.Id) && !Protegida(f))) await cache.RemoverFotosAsync(f.Id);
        await cache.RemoverFichasEquipamentoAbertasAntesDeAsync(new DateTimeOffset(limite.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    });
}
