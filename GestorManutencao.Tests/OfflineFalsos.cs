using System.Net;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

internal sealed class DonoFalso : IDonoDados
{
    public string? Dono { get; set; } = "ana";
}

internal sealed class TempoFalso : TimeProvider
{
    public DateTimeOffset Agora { get; set; } = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Agora;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

// Servidor falso em memória, com falhas injetáveis: Rede=false corta tudo; Falha devolve a exceção a lançar (por método, antes do efeito);
// DepoisFalha lança uma vez depois de o efeito ter ficado gravado (a resposta perde-se).
internal sealed class RemotoFalso : IIntervencoesRemoto
{
    private readonly object _trinco = new();
    private int _seq, _atual;
    public bool Rede { get; set; } = true;
    public Func<string, Exception?>? Falha { get; set; }
    public Dictionary<string, Exception> DepoisFalha { get; } = [];
    public TimeSpan Atraso { get; set; }
    public int MaxConcorrencia { get; private set; }
    public bool ExigeMaterial { get; set; }

    public List<string> Chamadas { get; } = [];
    public List<string> OpIds { get; } = [];
    public Dictionary<string, Intervencao> Fichas { get; } = [];
    public Dictionary<string, List<Material>> Materiais { get; } = [];
    public Dictionary<string, List<(Foto Foto, byte[] Bytes)>> Fotos { get; } = [];
    public List<Equipamento> Equipamentos { get; } = [];
    private readonly Dictionary<string, string> _porOpId = [];

    public int Contar(string metodo) => Chamadas.Count(c => c == metodo || c.StartsWith(metodo + ":"));

    private void Entrar(string metodo, string? alvo = null)
    {
        lock (_trinco) Chamadas.Add(alvo is null ? metodo : $"{metodo}:{alvo}");
        if (!Rede) throw new HttpRequestException("sem rede");
        if (Falha?.Invoke(metodo) is { } erro) throw erro;
    }

    private void Sair(string metodo)
    {
        Exception? erro;
        lock (_trinco) erro = DepoisFalha.Remove(metodo, out var e) ? e : null;
        if (erro is not null) throw erro;
    }

    public static Intervencao NovaFicha(string id, string equipamento, EstadoIntervencao estado, DateOnly data, string? anomalia = null) =>
        new(id, equipamento, "Produção", anomalia is null ? TipoIntervencao.Preventiva : TipoIntervencao.Curativa, data, estado, anomalia, null,
            estado == EstadoIntervencao.EmCurso ? new TimeOnly(8, 0) : null);

    public Task<IReadOnlyList<Intervencao>> ListarMinhasAsync(CancellationToken ct = default)
    {
        Entrar("ListarMinhas");
        lock (_trinco) return Task.FromResult<IReadOnlyList<Intervencao>>(Fichas.Values.Where(f => f.PorFazer).ToList());
    }

    public Task<IReadOnlyList<Intervencao>> ListarRecentesAsync(int dias, CancellationToken ct = default)
    {
        Entrar("ListarRecentes");
        lock (_trinco) return Task.FromResult<IReadOnlyList<Intervencao>>(Fichas.Values.Where(f => !f.PorFazer).ToList());
    }

    public Task<Intervencao?> ObterAsync(string id, CancellationToken ct = default)
    {
        Entrar("Obter", id);
        lock (_trinco) return Task.FromResult(Fichas.GetValueOrDefault(id));
    }

    public Task<Intervencao> RegistarAvariaAsync(string equipamentoId, string anomalia, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<Intervencao> RegistarAvariaAsync(string equipamentoId, string anomalia, string opId, CancellationToken ct = default)
    {
        Entrar("RegistarAvaria", opId);
        Intervencao ficha;
        lock (_trinco)
        {
            OpIds.Add(opId);
            if (!_porOpId.TryGetValue(opId, out var id))
            {
                var eq = Equipamentos.First(e => e.Id == equipamentoId);
                id = "S" + ++_seq;
                _porOpId[opId] = id;
                Fichas[id] = NovaFicha(id, eq.Nome, EstadoIntervencao.EmCurso, new DateOnly(2026, 11, 9), anomalia);
            }
            ficha = Fichas[id];
        }
        Sair("RegistarAvaria");
        return Task.FromResult(ficha);
    }

    public async Task<Intervencao> IniciarAsync(string id, CancellationToken ct = default)
    {
        Entrar("Iniciar", id);
        var atual = Interlocked.Increment(ref _atual);
        lock (_trinco) MaxConcorrencia = Math.Max(MaxConcorrencia, atual);
        try
        {
            if (Atraso > TimeSpan.Zero) await Task.Delay(Atraso, ct);
            lock (_trinco)
            {
                var f = Fichas.GetValueOrDefault(id) ?? throw new InvalidOperationException(MensagensIntervencao.Mudou);
                if (f.Estado == EstadoIntervencao.Planeada) f = Fichas[id] = f with { Estado = EstadoIntervencao.EmCurso, HoraInicio = new TimeOnly(9, 0) };
                else if (f.Estado != EstadoIntervencao.EmCurso) throw new InvalidOperationException(MensagensIntervencao.Mudou);
                return f;
            }
        }
        finally { Interlocked.Decrement(ref _atual); }
    }

    public Task<Intervencao> ConcluirAsync(string id, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct = default)
    {
        Entrar("Concluir", id);
        lock (_trinco)
        {
            var f = Fichas.GetValueOrDefault(id) ?? throw new InvalidOperationException(MensagensIntervencao.Mudou);
            if (f.Estado == EstadoIntervencao.EmCurso)
            {
                if (ExigeMaterial && !(Materiais.GetValueOrDefault(id)?.Count > 0)) throw new InvalidOperationException(MensagensIntervencao.Para("sem_material"));
                f = Fichas[id] = f with { Estado = EstadoIntervencao.Concluida, Feito = feito, HoraInicio = inicio, HoraFim = fim };
            }
            else if (f.Estado != EstadoIntervencao.Concluida) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            return Task.FromResult(f);
        }
    }

    public Task<IReadOnlyList<Material>> ListarMateriaisAsync(string id, CancellationToken ct = default)
    {
        Entrar("ListarMateriais", id);
        lock (_trinco) return Task.FromResult<IReadOnlyList<Material>>(Materiais.GetValueOrDefault(id)?.ToList() ?? []);
    }

    public Task<Material> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, CancellationToken ct = default)
    {
        Entrar("AdicionarMaterial", id);
        Material m;
        lock (_trinco)
        {
            if (Fichas.GetValueOrDefault(id) is not { Estado: EstadoIntervencao.EmCurso }) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            m = new Material("M" + ++_seq, design.Trim(), qtt, epv, qtt * epv);
            if (!Materiais.TryGetValue(id, out var lista)) Materiais[id] = lista = [];
            lista.Add(m);
        }
        Sair("AdicionarMaterial");
        return Task.FromResult(m);
    }

    public Task ApagarMaterialAsync(string id, string materialId, CancellationToken ct = default)
    {
        Entrar("ApagarMaterial", id);
        lock (_trinco) Materiais.GetValueOrDefault(id)?.RemoveAll(m => m.Id == materialId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Foto>> ListarFotosAsync(string id, CancellationToken ct = default)
    {
        Entrar("ListarFotos", id);
        lock (_trinco) return Task.FromResult<IReadOnlyList<Foto>>(Fotos.GetValueOrDefault(id)?.Select(f => f.Foto).ToList() ?? []);
    }

    public Task<byte[]?> ObterFotoAsync(string id, string fotoId, CancellationToken ct = default)
    {
        Entrar("ObterFoto", id);
        lock (_trinco) return Task.FromResult(Fotos.GetValueOrDefault(id)?.FirstOrDefault(f => f.Foto.Id == fotoId).Bytes);
    }

    public Task<Foto> AdicionarFotoAsync(string id, string nome, byte[] jpeg, CancellationToken ct = default)
    {
        Entrar("AdicionarFoto", id);
        Foto foto;
        lock (_trinco)
        {
            if (Fichas.GetValueOrDefault(id) is not { Estado: EstadoIntervencao.EmCurso }) throw new InvalidOperationException(MensagensIntervencao.Mudou);
            foto = new Foto("F" + ++_seq, nome, jpeg.Length);
            if (!Fotos.TryGetValue(id, out var lista)) Fotos[id] = lista = [];
            lista.Add((foto, jpeg));
        }
        Sair("AdicionarFoto");
        return Task.FromResult(foto);
    }

    public Task ApagarFotoAsync(string id, string fotoId, CancellationToken ct = default)
    {
        Entrar("ApagarFoto", id);
        lock (_trinco) Fotos.GetValueOrDefault(id)?.RemoveAll(f => f.Foto.Id == fotoId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default)
    {
        Entrar("Pesquisar");
        return Task.FromResult<IReadOnlyList<Equipamento>>(Equipamentos.Where(e => !e.Obsoleto && e.Nome.Contains(texto ?? "", StringComparison.OrdinalIgnoreCase)).ToList());
    }

    public Task<IReadOnlyList<Equipamento>> ListarAsync(FiltroEquipamentos filtro, CancellationToken ct = default)
    {
        Entrar("ListarEquipamentos");
        return Task.FromResult<IReadOnlyList<Equipamento>>(Equipamentos.Where(e => filtro.Obsoletos || !e.Obsoleto).ToList());
    }

    public Task<FichaEquipamento?> ObterFichaAsync(string id, CancellationToken ct = default)
    {
        Entrar("ObterFichaEquipamento", id);
        var e = Equipamentos.FirstOrDefault(x => x.Id == id);
        return Task.FromResult(e is null ? null : new FichaEquipamento(e, [], []));
    }

    public Task<IReadOnlyList<Intervencao>> ListarPorValidarAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> ValidarAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> DevolverAsync(string id, string motivo, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Intervencao>> ListarPlaneadasAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Intervencao> AtribuirAsync(string id, int tecno, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<RelatorioGeracao> GerarPlaneadasAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PaginaFichas> ListarTodasAsync(FiltroFichas filtro, int pagina, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ConfiguracaoManutencao> ObterConfiguracaoAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ConfiguracaoManutencao> GuardarConfiguracaoAsync(ConfiguracaoManutencao configuracao, CancellationToken ct = default) => throw new NotSupportedException();
}

internal sealed class Cenario
{
    public RemotoFalso Remoto { get; } = new();
    public RedeEstadoManual Rede { get; } = new();
    public DonoFalso Dono { get; } = new();
    public TempoFalso Tempo { get; } = new();
    public ICacheLocal Cache { get; }
    public IFilaSaida Fila { get; }
    public IArmazemFotosPendentes Fotos { get; }
    public ManutencaoCache Mc { get; }
    public SincronizadorFila Sinc { get; }
    public OfflineIntervencoesService Svc { get; }
    public AtualizadorCache Atualizador { get; }

    public static DateOnly Hoje => new(2026, 11, 9);

    public Cenario() : this(new CacheLocalMemoria(), new FilaSaidaMemoria(), new ArmazemFotosPendentesMemoria()) { }

    public Cenario(ICacheLocal cache, IFilaSaida fila, IArmazemFotosPendentes fotos)
    {
        Cache = cache; Fila = fila; Fotos = fotos;
        Mc = new ManutencaoCache(Cache, Fila, Dono, Tempo);
        Sinc = new SincronizadorFila(Remoto, Mc, Fila, Fotos, Dono, Tempo);
        Svc = new OfflineIntervencoesService(Remoto, Mc, Fila, Fotos, Rede, Sinc, Tempo);
        Atualizador = new AtualizadorCache(Remoto, Mc, Rede, Tempo);
        Remoto.Equipamentos.Add(new Equipamento("E1", "Compressor de ar 02", "Produção", "CMP-02", 2, "Compressor"));
        Remoto.Equipamentos.Add(new Equipamento("E2", "Câmara de frio 01", "Frio", "CF-01", 1, "Frio"));
        Remoto.Equipamentos.Add(new Equipamento("E3", "Serra antiga", "Produção", "SRF-01", 9, "Serra", Obsoleto: true));
    }

    public void Semear(Intervencao ficha) => Remoto.Fichas[ficha.Id] = ficha;

    public async Task<Intervencao> SemearEmCacheAsync(string id, string equipamento, EstadoIntervencao estado)
    {
        var ficha = RemotoFalso.NovaFicha(id, equipamento, estado, Hoje);
        Semear(ficha);
        await Svc.ListarMinhasAsync();
        return ficha;
    }
}
