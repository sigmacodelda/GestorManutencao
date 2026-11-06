namespace GestorManutencao.Api;

// Para desenvolvimento e testes. Segue as mesmas regras do repositório SQL (ver docs/ligacao-phc-intervencoes.md).
public sealed class RepositorioIntervencoesMemoria(TimeProvider relogio, ConfigManutencao? configuracao = null) : IRepositorioIntervencoes
{
    private sealed class Ficha
    {
        public required IntervencaoDto D { get; set; }
        public int Tecno { get; set; }
        public string Tarefa { get; set; } = "";
        public List<MaterialDto> Materiais { get; } = [];
        public string Motivo { get; set; } = "";
    }

    private static readonly EquipamentoDto[] Equipamentos =
    [
        new("E1", "Compressor de ar 02", "Produção"), new("E2", "Empilhador 05", "Armazém"), new("E3", "Câmara de frio 01", "Frio")
    ];

    private readonly ConfigManutencao _cfg = configuracao ?? new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false);
    private readonly object _trinco = new();
    private readonly List<Ficha> _lista = Inicial(DateOnly.FromDateTime(relogio.GetLocalNow().DateTime));

    private DateOnly Hoje => DateOnly.FromDateTime(relogio.GetLocalNow().DateTime);

    private static IntervencaoDto Nova(string id, string eq, string zona, TipoIntervencao tipo, DateOnly data, EstadoIntervencao estado,
        string? anomalia = null, string? feito = null, TimeOnly? ini = null, TimeOnly? fim = null, string? tecnico = null, int tecno = 0) =>
        new(id, eq, zona, tipo, data, estado, anomalia, feito, ini, fim, tecnico, tecno);

    private static List<Ficha> Inicial(DateOnly hoje) =>
        [
            new() { D = Nova("F1", "Compressor de ar 02", "Produção", TipoIntervencao.Preventiva, hoje, EstadoIntervencao.Planeada), Tarefa = "TAREFA1" },
            new() { D = Nova("F2", "Câmara de frio 01", "Frio", TipoIntervencao.Preventiva, hoje, EstadoIntervencao.Planeada, tecnico: "Outra Pessoa", tecno: 999), Tecno = 999 },
            new() { D = Nova("F3", "Balança 03", "Expedição", TipoIntervencao.Curativa, hoje, EstadoIntervencao.Concluida, "Mostrador apagado", "Trocado o cabo",
                new TimeOnly(8, 0), new TimeOnly(9, 0), "Ana Silva", 101), Tecno = 101 },
            new() { D = Nova("F4", "Câmara de frio 01", "Frio", TipoIntervencao.Preventiva, hoje.AddDays(30), EstadoIntervencao.Planeada) }
        ];

    private IntervencaoDto Ver(Ficha f, bool podeValidar = true) => f.D with { Tecno = f.Tecno, PodeValidar = podeValidar };

    private static bool Minha(Ficha f, TecnicoAtual t) => f.Tecno == t.Tecno || f.Tecno == 0;

    private static bool Aberta(Ficha f) => f.D.Estado is EstadoIntervencao.Planeada or EstadoIntervencao.EmCurso;

    private static (ResultadoIntervencao, T?) Falha<T>(ResultadoIntervencao r) => (r, default);

    public Task<IReadOnlyList<IntervencaoDto>> ListarAsync(TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (Trinco())
        {
            var ate = Hoje.AddDays(_cfg.Antecedencia);
            return Task.FromResult<IReadOnlyList<IntervencaoDto>>(_lista
                .Where(f => Aberta(f) && (f.Tecno == tecnico.Tecno || (f.Tecno == 0 && f.D.DataPlaneada <= ate)))
                .OrderBy(f => f.D.DataPlaneada).Select(f => Ver(f)).ToList());
        }
    }

    public Task<IntervencaoDto?> ObterAsync(string id, TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (Trinco()) return Task.FromResult(_lista.Where(f => f.D.Id == id && Minha(f, tecnico)).Select(f => (IntervencaoDto?)Ver(f)).FirstOrDefault());
    }

    public Task<IReadOnlyList<EquipamentoDto>> PesquisarEquipamentosAsync(string texto, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EquipamentoDto>>(Equipamentos.Where(e => e.Nome.Contains(texto, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<(ResultadoIntervencao, IntervencaoDto?)> RegistarAvariaAsync(string equipamentoId, string anomalia, TecnicoAtual tecnico, CancellationToken ct)
    {
        var e = Equipamentos.FirstOrDefault(x => x.Id == equipamentoId);
        if (e is null) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.NaoExiste));
        var agora = relogio.GetLocalNow().DateTime;
        var nova = new Ficha
        {
            D = Nova(Guid.NewGuid().ToString("N")[..25], e.Nome, e.Zona, TipoIntervencao.Curativa, DateOnly.FromDateTime(agora), EstadoIntervencao.EmCurso,
                anomalia, null, TimeOnly.FromDateTime(agora), null, tecnico.Nome, tecnico.Tecno),
            Tecno = tecnico.Tecno
        };
        lock (Trinco()) _lista.Add(nova);
        return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(nova)));
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> IniciarAsync(string id, TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (f is null || !Minha(f, tecnico)) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.NaoExiste));
            if (f.D.Estado != EstadoIntervencao.Planeada) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.Conflito));

            // A primeira planeada da fila da mesma zona, por data.
            var primeira = _lista.Where(x => x.D.Estado == EstadoIntervencao.Planeada && x.D.Zona == f.D.Zona).OrderBy(x => x.D.DataPlaneada).First();
            if (primeira != f && !_cfg.AvancaOrdem) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.ForaDeOrdem));

            var semDono = f.Tecno == 0;
            f.D = f.D with
            {
                Estado = EstadoIntervencao.EmCurso,
                HoraInicio = f.D.HoraInicio ?? TimeOnly.FromDateTime(relogio.GetLocalNow().DateTime),
                Tecnico = semDono ? tecnico.Nome : f.D.Tecnico
            };
            if (semDono) f.Tecno = tecnico.Tecno;
            return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(f)));
        }
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> ConcluirAsync(string id, TecnicoAtual tecnico, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (f is null || !Minha(f, tecnico)) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.NaoExiste));
            if (f.D.Estado != EstadoIntervencao.EmCurso) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.Conflito));

            f.D = f.D with { Estado = EstadoIntervencao.Concluida, Feito = feito, HoraInicio = inicio, HoraFim = fim, DataPlaneada = Hoje };
            GerarSeguinte(f);
            return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(f)));
        }
    }

    // Uma só ficha aberta por tarefa: ao concluir gera-se a seguinte, como no PHC.
    private void GerarSeguinte(Ficha feita)
    {
        if (!_cfg.GerarPlaneadas || feita.Tarefa == "" || _lista.Any(x => x.Tarefa == feita.Tarefa && Aberta(x))) return;
        _lista.Add(new Ficha
        {
            D = Nova(Guid.NewGuid().ToString("N")[..25], feita.D.Equipamento, feita.D.Zona, TipoIntervencao.Preventiva, Hoje.AddDays(30), EstadoIntervencao.Planeada),
            Tarefa = feita.Tarefa
        });
    }

    public Task<IReadOnlyList<IntervencaoDto>> ListarPorValidarAsync(TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult<IReadOnlyList<IntervencaoDto>>(_lista.Where(f => f.D.Estado == EstadoIntervencao.Concluida)
                .Select(f => Ver(f, _cfg.ValidarPropria || supervisor.Tecno <= 0 || f.Tecno != supervisor.Tecno)).ToList());
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> ValidarAsync(string id, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (f is null) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.NaoExiste));
            if (f.D.Estado != EstadoIntervencao.Concluida) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.Conflito));
            if (!_cfg.ValidarPropria && supervisor.Tecno > 0 && f.Tecno == supervisor.Tecno) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.Propria));
            f.D = f.D with { Estado = EstadoIntervencao.Validada };
            return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(f)));
        }
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> DevolverAsync(string id, string motivo, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (f is null) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.NaoExiste));
            if (f.D.Estado != EstadoIntervencao.Concluida) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.Conflito));
            // A seguinte ainda por tocar sai: volta a gerar-se quando esta for concluída outra vez.
            if (f.Tarefa != "") _lista.RemoveAll(x => x != f && x.Tarefa == f.Tarefa && x.D.Estado == EstadoIntervencao.Planeada && x.Tecno == 0 && x.Materiais.Count == 0);
            f.Motivo = motivo;
            f.D = f.D with { Estado = EstadoIntervencao.EmCurso, HoraFim = null };
            return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(f)));
        }
    }

    public Task<IReadOnlyList<IntervencaoDto>> ListarPlaneadasAsync(CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult<IReadOnlyList<IntervencaoDto>>(_lista.Where(Aberta).OrderBy(f => f.D.DataPlaneada).Select(f => Ver(f)).ToList());
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> AtribuirAsync(string id, int tecno, string nome, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (f is null) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.NaoExiste));
            if (!Aberta(f)) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.Conflito));
            f.Tecno = tecno;
            f.D = f.D with { Tecnico = tecno == 0 ? null : nome };
            return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(f)));
        }
    }

    public Task<RelatorioGeracao> GerarPlaneadasAsync(TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var antes = _lista.Count;
            foreach (var f in _lista.Where(x => x.Tarefa != "" && x.D.Estado is EstadoIntervencao.Concluida or EstadoIntervencao.Validada).ToList()) GerarSeguinte(f);
            return Task.FromResult(new RelatorioGeracao(_lista.Count - antes, 0, 0, 0));
        }
    }

    public Task<IReadOnlyList<MaterialDto>?> ListarMateriaisAsync(string id, TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult(_lista.FirstOrDefault(x => x.D.Id == id && Minha(x, tecnico)) is { } f ? (IReadOnlyList<MaterialDto>?)f.Materiais.ToList() : null);
    }

    public Task<(ResultadoIntervencao, MaterialDto?)> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id && Minha(x, tecnico));
            if (f is null) return Task.FromResult(Falha<MaterialDto>(ResultadoIntervencao.NaoExiste));
            if (f.D.Estado != EstadoIntervencao.EmCurso || f.Tecno != tecnico.Tecno) return Task.FromResult(Falha<MaterialDto>(ResultadoIntervencao.Conflito));
            var m = new MaterialDto(Guid.NewGuid().ToString("N")[..25], RegrasManutencao.Cortar(design, RegrasManutencao.TamanhoDesignMaterial), qtt, epv, RegrasManutencao.CustoMaterial(qtt, epv));
            f.Materiais.Add(m);
            return Task.FromResult<(ResultadoIntervencao, MaterialDto?)>((ResultadoIntervencao.Ok, m));
        }
    }

    public Task<ResultadoIntervencao> ApagarMaterialAsync(string id, string materialId, TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id && Minha(x, tecnico));
            if (f is null) return Task.FromResult(ResultadoIntervencao.NaoExiste);
            if (f.D.Estado != EstadoIntervencao.EmCurso || f.Tecno != tecnico.Tecno) return Task.FromResult(ResultadoIntervencao.Conflito);
            return Task.FromResult(f.Materiais.RemoveAll(m => m.Id == materialId) > 0 ? ResultadoIntervencao.Ok : ResultadoIntervencao.NaoExiste);
        }
    }

    private object Trinco() => _trinco;
}
