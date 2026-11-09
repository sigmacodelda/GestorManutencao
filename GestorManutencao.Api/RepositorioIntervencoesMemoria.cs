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
        public List<(FotoDto Foto, byte[] Dados)> Fotos { get; } = [];
        public string Motivo { get; set; } = "";
        public string? OpId { get; set; }
    }

    private sealed record Tarefa(string Id, string EquipamentoId, string Descricao, string Periodo, UnidadePeriodo Unidade, int Intervalo);

    // UltimaIntervencao e FichasAbertas calculam-se a partir das fichas, como no SQL.
    private static readonly EquipamentoDto[] Equipamentos =
    [
        new("E1", "Compressor de ar 02", "Produção", "CMP-02", 2, "Compressor", "Atlas Copco", "GA 30", "AC-77120"),
        new("E2", "Empilhador 05", "Armazém", "EMP-05", 5, "Empilhador", "Toyota", "8FBE15", "TY-30418"),
        new("E3", "Câmara de frio 01", "Frio", "CF-01", 1, "Frio", "Kide", "CF-500", "KD-1190"),
        new("E4", "Linha de embalagem", "Produção", "LE-01", 4, "Linha", "Cavanna", "LP-2", "CV-2231"),
        new("E5", "Balança 03", "Expedição", "BAL-03", 3, "Balança", "Mettler", "IND-570", "MT-8841"),
        new("E6", "Porta seccionada 07", "Armazém", "PRT-07", 7, "Portas", "Hormann", "SPU-40", "HR-5522", ForaDeServico: true),
        new("E7", "Serra de fita antiga", "Produção", "SRF-01", 9, "Serra", "Bomar", "STG-280", "BM-0042", Obsoleto: true)
    ];

    private static readonly Tarefa[] Tarefas =
    [
        new("TAREFA1", "E1", "Verificar o nível de óleo e os filtros", "Mensal", UnidadePeriodo.Meses, 1),
        new("TAREFA2", "E1", "Drenar os condensados", "Semanal", UnidadePeriodo.Dias, 7),
        new("TAREFA3", "E3", "Limpar os evaporadores", "Trimestral", UnidadePeriodo.Meses, 3)
    ];

    private ConfigManutencao _cfg = configuracao ?? new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false);
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
            new() { D = Nova("F4", "Câmara de frio 01", "Frio", TipoIntervencao.Preventiva, hoje.AddDays(30), EstadoIntervencao.Planeada) },
            // Histórico (já fechado): não entra nas listas de trabalho nem na validação.
            new() { D = Nova("H1", "Compressor de ar 02", "Produção", TipoIntervencao.Preventiva, hoje.AddDays(-35), EstadoIntervencao.Validada, null, "Óleo e filtros em bom estado",
                new TimeOnly(8, 0), new TimeOnly(9, 0), "Ana Silva", 101), Tecno = 101, Tarefa = "TAREFA1" },
            new() { D = Nova("H2", "Empilhador 05", "Armazém", TipoIntervencao.Curativa, hoje.AddDays(-120), EstadoIntervencao.Validada, "Não arranca", "Substituída a bateria",
                new TimeOnly(14, 0), new TimeOnly(16, 30), "Chefe de Turno", 100), Tecno = 100 },
            new() { D = Nova("H3", "Câmara de frio 01", "Frio", TipoIntervencao.Preventiva, hoje.AddDays(-60), EstadoIntervencao.Cancelada), Tarefa = "TAREFA3" }
        ];

    // O motivo da devolução só se vê enquanto a ficha está em curso, como no SQL.
    private IntervencaoDto Ver(Ficha f, bool podeValidar = true) => f.D with
    {
        Tecno = f.Tecno, PodeValidar = podeValidar,
        Motivo = f.D.Estado == EstadoIntervencao.EmCurso && f.Motivo.Length > 0 ? f.Motivo : null
    };

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

    public Task<IReadOnlyList<IntervencaoDto>> ListarRecentesAsync(TecnicoAtual tecnico, int dias, CancellationToken ct)
    {
        lock (Trinco())
        {
            var desde = Hoje.AddDays(-dias);
            return Task.FromResult<IReadOnlyList<IntervencaoDto>>(_lista
                .Where(f => f.Tecno == tecnico.Tecno && f.D.Estado is EstadoIntervencao.Concluida or EstadoIntervencao.Validada or EstadoIntervencao.Cancelada && f.D.DataPlaneada >= desde)
                .OrderByDescending(f => f.D.DataPlaneada).Take(RegrasIntervencao.MaxRecentes).Select(f => Ver(f)).ToList());
        }
    }

    public Task<IntervencaoDto?> ObterAsync(string id, TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (Trinco()) return Task.FromResult(_lista.Where(f => f.D.Id == id && Minha(f, tecnico)).Select(f => (IntervencaoDto?)Ver(f)).FirstOrDefault());
    }

    private static bool Contem(string? valor, string texto) => valor?.Contains(texto, StringComparison.OrdinalIgnoreCase) == true;

    private EquipamentoDto Completo(EquipamentoDto e)
    {
        var fichas = _lista.Where(f => f.D.Equipamento == e.Nome).ToList();
        var ultima = fichas.Where(f => f.D.Estado is EstadoIntervencao.Concluida or EstadoIntervencao.Validada && f.D.DataPlaneada <= Hoje)
            .Select(f => (DateOnly?)f.D.DataPlaneada).Max();
        return e with { UltimaIntervencao = ultima, FichasAbertas = fichas.Count(f => f.D.Estado is EstadoIntervencao.EmCurso or EstadoIntervencao.Concluida) };
    }

    public Task<IReadOnlyList<EquipamentoDto>> ListarEquipamentosAsync(FiltroEquipamentos filtro, CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult<IReadOnlyList<EquipamentoDto>>(Equipamentos
                .Where(e => (filtro.Obsoletos || !e.Obsoleto)
                    && (filtro.Tipo.Length == 0 || string.Equals(e.Tipo, filtro.Tipo, StringComparison.OrdinalIgnoreCase))
                    && (filtro.Local.Length == 0 || string.Equals(e.Zona, filtro.Local, StringComparison.OrdinalIgnoreCase))
                    && (filtro.Texto.Length == 0 || Contem(e.Nome, filtro.Texto) || Contem(e.Ref, filtro.Texto) || Contem(e.Marca, filtro.Texto) || Contem(e.Modelo, filtro.Texto)))
                .OrderBy(e => e.Nome, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.NoInterno).Take(RegrasManutencao.MaxEquipamentos + 1).Select(Completo).ToList());
    }

    public Task<IReadOnlyList<EquipamentoDto>> PesquisarEquipamentosAsync(string texto, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EquipamentoDto>>(Equipamentos
            .Where(e => !e.Obsoleto && (Contem(e.Nome, texto) || Contem(e.Ref, texto)))
            .OrderBy(e => e.Nome, StringComparer.CurrentCultureIgnoreCase).Take(RegrasManutencao.MaxPesquisaAvaria)
            .Select(e => new EquipamentoDto(e.Id, e.Nome, e.Zona)).ToList());

    public Task<FichaEquipamentoDto?> ObterEquipamentoAsync(string id, int tecno, CancellationToken ct)
    {
        lock (Trinco())
        {
            var e = Equipamentos.FirstOrDefault(x => x.Id == id);
            if (e is null) return Task.FromResult<FichaEquipamentoDto?>(null);

            var tarefas = Tarefas.Where(t => t.EquipamentoId == id).Select(t =>
            {
                var fichas = _lista.Where(f => f.Tarefa == t.Id).ToList();
                DateTime? Maior(Func<Ficha, bool> onde) => fichas.Where(onde).Select(f => (DateTime?)f.D.DataPlaneada.ToDateTime(TimeOnly.MinValue)).Max();
                var feita = Maior(f => f.D.Estado is EstadoIntervencao.Concluida or EstadoIntervencao.Validada);
                var aberta = fichas.Where(Aberta).Select(f => (DateTime?)f.D.DataPlaneada.ToDateTime(TimeOnly.MinValue)).Min();
                var proxima = RegrasManutencao.ProximaDaTarefa(feita, Maior(f => f.D.Estado == EstadoIntervencao.Cancelada), null, t.Unidade, t.Intervalo, aberta);
                return new TarefaPlaneadaDto(t.Id, t.Descricao, t.Periodo, feita.HasValue ? DateOnly.FromDateTime(feita.Value) : null,
                    proxima.HasValue ? DateOnly.FromDateTime(proxima.Value) : null);
            }).ToList();

            var historico = _lista.Where(f => f.D.Equipamento == e.Nome && f.D.Estado != EstadoIntervencao.Planeada)
                .OrderByDescending(f => f.D.DataPlaneada).Take(RegrasManutencao.FichasNoHistorico)
                .Select(f => Ver(f) with { PodeAbrir = tecno > 0 && Minha(f, new TecnicoAtual("", tecno, "")) }).ToList();
            return Task.FromResult<FichaEquipamentoDto?>(new FichaEquipamentoDto(Completo(e), tarefas, historico));
        }
    }

    public Task<PaginaFichasDto> ListarTodasAsync(FiltroFichas filtro, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var todas = _lista
                .Where(f => (filtro.Estado is null || f.D.Estado == filtro.Estado)
                    && (filtro.Desde is null || f.D.DataPlaneada >= filtro.Desde) && (filtro.Ate is null || f.D.DataPlaneada <= filtro.Ate)
                    && (filtro.Tecno == 0 || f.Tecno == filtro.Tecno)
                    && (filtro.Texto.Length == 0 || Contem(f.D.Equipamento, filtro.Texto) || Contem(f.D.Anomalia, filtro.Texto) || Contem(f.D.Feito, filtro.Texto) || Contem(f.D.Tecnico, filtro.Texto)))
                .OrderByDescending(f => f.D.DataPlaneada).ThenByDescending(f => f.D.Id)
                .ToList();
            var pagina = todas.Skip((filtro.Pagina - 1) * RegrasManutencao.FichasPorPagina).Take(RegrasManutencao.FichasPorPagina)
                .Select(f => Ver(f) with { PodeAbrir = supervisor.Tecno > 0 && Minha(f, supervisor) }).ToList();
            return Task.FromResult(new PaginaFichasDto(pagina, todas.Count > filtro.Pagina * RegrasManutencao.FichasPorPagina));
        }
    }

    public Task<ConfigManutencao> ObterConfiguracaoAsync(CancellationToken ct) => Task.FromResult(_cfg);

    public Task GuardarConfiguracaoAsync(ConfigManutencao configuracao, TecnicoAtual supervisor, CancellationToken ct)
    {
        _cfg = configuracao;
        return Task.CompletedTask;
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> RegistarAvariaAsync(string equipamentoId, string anomalia, TecnicoAtual tecnico, CancellationToken ct, string? opId = null)
    {
        var e = Equipamentos.FirstOrDefault(x => x.Id == equipamentoId && !x.Obsoleto);
        if (e is null) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.NaoExiste));
        var agora = relogio.GetLocalNow().DateTime;
        var nova = new Ficha
        {
            D = Nova(Guid.NewGuid().ToString("N")[..25], e.Nome, e.Zona, TipoIntervencao.Curativa, DateOnly.FromDateTime(agora), EstadoIntervencao.EmCurso,
                anomalia, null, TimeOnly.FromDateTime(agora), null, tecnico.Nome, tecnico.Tecno),
            Tecno = tecnico.Tecno,
            OpId = opId
        };
        lock (Trinco())
        {
            if (opId is not null && _lista.FirstOrDefault(x => x.OpId == opId && x.Tecno == tecnico.Tecno) is { } repetida)
                return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(repetida)));
            _lista.Add(nova);
        }
        return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(nova)));
    }

    public Task<(ResultadoIntervencao, IntervencaoDto?)> IniciarAsync(string id, TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id);
            if (f is null || !Minha(f, tecnico)) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.NaoExiste));
            // Repetição do mesmo pedido (a resposta perdeu-se): a ficha já está em curso e é do mesmo técnico.
            if (f.D.Estado == EstadoIntervencao.EmCurso && f.Tecno == tecnico.Tecno)
                return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(f)));
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
            if (f.D.Estado == EstadoIntervencao.Concluida && f.Tecno == tecnico.Tecno && f.D.Feito == feito
                && Segundos(f.D.HoraInicio) == Segundos(inicio) && Segundos(f.D.HoraFim) == Segundos(fim))
                return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(f)));
            if (f.D.Estado != EstadoIntervencao.EmCurso) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.Conflito));
            if (_cfg.ExigeMaterial && f.Materiais.Count == 0) return Task.FromResult(Falha<IntervencaoDto>(ResultadoIntervencao.SemMaterial));

            f.D = f.D with { Estado = EstadoIntervencao.Concluida, Feito = feito, HoraInicio = inicio, HoraFim = fim, DataPlaneada = Hoje };
            GerarSeguinte(f);
            return Task.FromResult<(ResultadoIntervencao, IntervencaoDto?)>((ResultadoIntervencao.Ok, Ver(f)));
        }
    }

    private static TimeOnly? Segundos(TimeOnly? hora) => hora is { } h ? new TimeOnly(h.Hour, h.Minute, h.Second) : null;

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
            f.Motivo = motivo.Trim();
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

    public Task<IReadOnlyList<FotoDto>?> ListarFotosAsync(string id, TecnicoAtual quem, bool supervisor, CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult(_lista.FirstOrDefault(x => x.D.Id == id && (supervisor || Minha(x, quem))) is { } f
                ? (IReadOnlyList<FotoDto>?)f.Fotos.Select(x => x.Foto).ToList() : null);
    }

    public Task<FotoConteudo?> ObterFotoAsync(string id, string fotoId, TecnicoAtual quem, bool supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id && (supervisor || Minha(x, quem)));
            var foto = f?.Fotos.FirstOrDefault(x => x.Foto.Id == fotoId);
            return Task.FromResult(foto is { Foto: not null } ? new FotoConteudo(foto.Value.Foto.Nome, foto.Value.Dados) : null);
        }
    }

    public Task<(ResultadoIntervencao, FotoDto?)> AdicionarFotoAsync(string id, string nome, byte[] dados, TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id && Minha(x, tecnico));
            if (f is null) return Task.FromResult(Falha<FotoDto>(ResultadoIntervencao.NaoExiste));
            if (f.D.Estado != EstadoIntervencao.EmCurso || f.Tecno != tecnico.Tecno) return Task.FromResult(Falha<FotoDto>(ResultadoIntervencao.Conflito));
            if (f.Fotos.Count >= RegrasIntervencao.MaxFotos) return Task.FromResult(Falha<FotoDto>(ResultadoIntervencao.LimiteFotos));
            var foto = new FotoDto(Guid.NewGuid().ToString("N")[..25], nome, dados.Length);
            f.Fotos.Add((foto, dados));
            return Task.FromResult<(ResultadoIntervencao, FotoDto?)>((ResultadoIntervencao.Ok, foto));
        }
    }

    public Task<ResultadoIntervencao> ApagarFotoAsync(string id, string fotoId, TecnicoAtual tecnico, CancellationToken ct)
    {
        lock (Trinco())
        {
            var f = _lista.FirstOrDefault(x => x.D.Id == id && Minha(x, tecnico));
            if (f is null) return Task.FromResult(ResultadoIntervencao.NaoExiste);
            if (f.D.Estado != EstadoIntervencao.EmCurso || f.Tecno != tecnico.Tecno) return Task.FromResult(ResultadoIntervencao.Conflito);
            return Task.FromResult(f.Fotos.RemoveAll(x => x.Foto.Id == fotoId) > 0 ? ResultadoIntervencao.Ok : ResultadoIntervencao.NaoExiste);
        }
    }

    private object Trinco() => _trinco;
}
