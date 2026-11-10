namespace GestorManutencao.Api;

// Gestão em memória (desenvolvimento e testes), com as mesmas regras do repositório SQL.
public sealed partial class RepositorioIntervencoesMemoria : IRepositorioGestao
{
    private sealed class DadosEquipamento
    {
        public required EquipamentoGestaoDto D { get; set; }
        public int Versao { get; set; }
    }

    private sealed class Instrucao
    {
        public required string Id { get; init; }
        public required string EquipamentoId { get; init; }
        public string Versao { get; set; } = "";
        public DateOnly Data { get; set; }
        public bool Obsoleta { get; set; }
        public string Obs { get; set; } = "";
        public int Ver { get; set; }
    }

    private sealed class Periodicidade
    {
        public required string Id { get; init; }
        public required string Periodo { get; init; }
        public string Unidade { get; set; } = "nenhum";
        public int Intervalo { get; set; }
        public bool Aviso { get; set; }
        public int Ver { get; set; }
    }

    private readonly Dictionary<string, DadosEquipamento> _dadosEquipamento = [];
    private readonly Dictionary<string, List<HistoricoEstadoDto>> _historicoEstado = [];

    private readonly List<Instrucao> _instrucoes = [];

    // Os nomes terminam em _ para não colidirem com o tipo Periodicidade.
    private readonly List<Periodicidade> Periodicidades_ = Periodicidades.Lista.Select((p, i) => new Periodicidade
    {
        Id = "P" + (i + 1), Periodo = p.Periodo, Unidade = p.Unidade.ToString().ToLowerInvariant(), Intervalo = p.Intervalo
    }).ToList();

    private bool InstrucaoObsoleta(string id) => _instrucoes.FirstOrDefault(i => i.Id == id)?.Obsoleta == true;

    private void GarantirInstrucoesIniciais()
    {
        if (_instrucoes.Count > 0) return;
        // As viaturas ficam de fora do módulo: não há instruções para gerir.
        foreach (var e in Equipamentos.Where(e => !RegrasGestao.EhViatura(e.Tipo))) _instrucoes.Add(new Instrucao { Id = "I-" + e.Id, EquipamentoId = e.Id, Data = Hoje });
    }

    private bool EquipamentoEViatura(string equipamentoId) => Equipamentos.FirstOrDefault(e => e.Id == equipamentoId) is { } e && RegrasGestao.EhViatura(e.Tipo);

    private bool InstrucaoDeViatura(string instrucaoId) => _instrucoes.FirstOrDefault(i => i.Id == instrucaoId) is { } i && EquipamentoEViatura(i.EquipamentoId);

    private DadosEquipamento DadosDe(EquipamentoDto e)
    {
        if (_dadosEquipamento.TryGetValue(e.Id, out var existente)) return existente;
        var d = new DadosEquipamento
        {
            D = new EquipamentoGestaoDto(e.Id, e.NoInterno, e.Ref, e.Nome, e.Tipo, e.Marca, e.Modelo, e.Serie, 0, 0, 0, "", Hoje, e.Zona,
                e.ForaDeServico ? RegrasGestao.Estados[1] : RegrasGestao.Estados[0], e.Obsoleto, "", "", "0")
        };
        _dadosEquipamento[e.Id] = d;
        return d;
    }

    private EquipamentoGestaoDto Detalhe(EquipamentoDto e)
    {
        var d = DadosDe(e);
        return d.D with { Versao = d.Versao.ToString() };
    }

    private void CancelarPlaneadas(Func<Ficha, bool> onde, string motivo)
    {
        foreach (var f in _lista.Where(f => f.D.Estado == EstadoIntervencao.Planeada && onde(f)))
        {
            f.Motivo = motivo;
            f.D = f.D with { Estado = EstadoIntervencao.Cancelada };
        }
    }

    public Task<OpcoesEquipamentoDto> ObterOpcoesAsync(CancellationToken ct)
    {
        lock (Trinco())
        {
            static List<string> Distintos(IEnumerable<string> valores) => valores.Where(v => v.Length > 0).Distinct().Order().ToList();
            var visiveis = Equipamentos.Where(e => !RegrasGestao.EhViatura(e.Tipo)).ToList();
            return Task.FromResult(new OpcoesEquipamentoDto(Distintos(visiveis.Select(e => e.Tipo)), Distintos(visiveis.Select(e => e.Marca)), Distintos(visiveis.Select(e => e.Zona)), RegrasGestao.Estados));
        }
    }

    public Task<EquipamentoGestaoDto?> ObterEquipamentoGestaoAsync(string id, CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult(Equipamentos.FirstOrDefault(e => e.Id == id && !RegrasGestao.EhViatura(e.Tipo)) is { } e ? Detalhe(e) : null);
    }

    public Task<int?> ContarPlaneadasEquipamentoAsync(string id, CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult(Equipamentos.FirstOrDefault(e => e.Id == id && !RegrasGestao.EhViatura(e.Tipo)) is { } e
                ? (int?)_lista.Count(f => f.D.Equipamento == e.Nome && f.D.Estado == EstadoIntervencao.Planeada)
                : null);
    }

    public Task<IReadOnlyList<HistoricoEstadoDto>?> ListarHistoricoEstadoAsync(string equipamentoId, CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult(Equipamentos.Any(e => e.Id == equipamentoId && !RegrasGestao.EhViatura(e.Tipo))
                ? (IReadOnlyList<HistoricoEstadoDto>?)(_historicoEstado.GetValueOrDefault(equipamentoId)?.AsEnumerable().Reverse().ToList() ?? [])
                : null);
    }

    public Task<(ResultadoIntervencao, EquipamentoGestaoDto?)> GravarEquipamentoAsync(string? id, PedidoEquipamento pedido, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            GarantirInstrucoesIniciais();
            var novo = id is null;
            EquipamentoDto? existente = null;
            EquipamentoGestaoDto atual;
            if (novo) atual = RegrasGestao.EquipamentoVazio(Hoje);
            else
            {
                existente = Equipamentos.FirstOrDefault(e => e.Id == id && !RegrasGestao.EhViatura(e.Tipo));
                if (existente is null) return Task.FromResult(Falha<EquipamentoGestaoDto>(ResultadoIntervencao.NaoExiste));
                atual = Detalhe(existente);
                if (!string.IsNullOrEmpty(pedido.Versao) && pedido.Versao != atual.Versao) return Task.FromResult(Falha<EquipamentoGestaoDto>(ResultadoIntervencao.Conflito));
            }

            var nova = RegrasGestao.AplicarEquipamento(atual, pedido);
            if (nova.Ref.Length == 0 || nova.Design.Length == 0) return Task.FromResult(Falha<EquipamentoGestaoDto>(ResultadoIntervencao.Invalido));
            if (RegrasGestao.EhViatura(nova.Tipo)) return Task.FromResult(Falha<EquipamentoGestaoDto>(ResultadoIntervencao.Viatura));
            if (nova.NoFornecedor > 0)
            {
                if (Fornecedores.FirstOrDefault(f => f.No == nova.NoFornecedor) is not { } fornecedor) return Task.FromResult(Falha<EquipamentoGestaoDto>(ResultadoIntervencao.Invalido));
                nova = nova with { NomeFornecedor = fornecedor.Nome };
            }
            else nova = nova with { NomeFornecedor = "" };

            if ((novo || !string.Equals(nova.Ref, atual.Ref, StringComparison.OrdinalIgnoreCase))
                && Equipamentos.Any(e => e.Id != id && string.Equals(e.Ref, nova.Ref, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(Falha<EquipamentoGestaoDto>(ResultadoIntervencao.RefDuplicada));

            var passaAObsoleto = nova.Obsoleto && !atual.Obsoleto;
            var nomeAntigo = existente?.Nome;
            // Sem confirmação conta como 0: só passa se não houver planeadas a cancelar.
            if (passaAObsoleto && (pedido.PlaneadasConfirmadas ?? 0) != _lista.Count(f => f.D.Equipamento == nomeAntigo && f.D.Estado == EstadoIntervencao.Planeada))
                return Task.FromResult(Falha<EquipamentoGestaoDto>(ResultadoIntervencao.PlaneadasMudaram));

            var stamp = id ?? "E" + (Equipamentos.Count + 1 + _dadosEquipamento.Count(d => d.Key.StartsWith("EN")));
            if (novo)
            {
                stamp = "EN" + (Equipamentos.Count + 1);
                nova = nova with { Id = stamp, NoInterno = Equipamentos.Max(e => e.NoInterno) + 1 };
                _instrucoes.Add(new Instrucao { Id = "I-" + stamp, EquipamentoId = stamp, Data = Hoje });
            }
            var dto = new EquipamentoDto(stamp, nova.Design, nova.Local, nova.Ref, nova.NoInterno, nova.Tipo, nova.Marca, nova.Modelo, nova.Serie,
                existente?.UltimaIntervencao, existente?.FichasAbertas ?? 0, nova.Estado == RegrasGestao.Estados[1], nova.Obsoleto);
            if (novo) Equipamentos.Add(dto); else Equipamentos[Equipamentos.FindIndex(e => e.Id == stamp)] = dto;

            if (!novo && nomeAntigo is not null)
                foreach (var f in _lista.Where(f => f.D.Equipamento == nomeAntigo)) f.D = f.D with { Equipamento = nova.Design, Zona = nova.Local };

            var dados = DadosDe(dto);
            dados.D = nova;
            dados.Versao++;

            if (nova.Estado.Length > 0 && !string.Equals(nova.Estado, novo ? "" : atual.Estado, StringComparison.OrdinalIgnoreCase))
            {
                if (!_historicoEstado.TryGetValue(stamp, out var historico)) _historicoEstado[stamp] = historico = [];
                historico.Add(new HistoricoEstadoDto(Hoje, nova.Estado, supervisor.Codigo));
            }
            // Em cada gravação com obsoleto as planeadas por fazer ficam canceladas (nunca as em curso), como no Landeiro.
            if (nova.Obsoleto) CancelarPlaneadas(f => f.D.Equipamento == nova.Design, RegrasGestao.MotivoObsoleto);
            return Task.FromResult<(ResultadoIntervencao, EquipamentoGestaoDto?)>((ResultadoIntervencao.Ok, Detalhe(dto)));
        }
    }

    private InstrucaoDto ParaDto(Instrucao i) =>
        new(i.Id, i.EquipamentoId, i.Versao, i.Data, i.Obsoleta, i.Obs, Tarefas.Count(t => t.InstrucaoId == i.Id), i.Ver.ToString());

    public Task<IReadOnlyList<InstrucaoDto>?> ListarInstrucoesAsync(string equipamentoId, CancellationToken ct)
    {
        lock (Trinco())
        {
            GarantirInstrucoesIniciais();
            return Task.FromResult(Equipamentos.Any(e => e.Id == equipamentoId && !RegrasGestao.EhViatura(e.Tipo))
                ? (IReadOnlyList<InstrucaoDto>?)_instrucoes.Where(i => i.EquipamentoId == equipamentoId)
                    .OrderBy(i => i.Obsoleta).ThenByDescending(i => i.Data).ThenByDescending(i => i.Versao, StringComparer.Ordinal).Select(ParaDto).ToList()
                : null);
        }
    }

    public Task<(ResultadoIntervencao, InstrucaoDto?)> GravarInstrucaoAsync(string? equipamentoId, string? id, PedidoInstrucao pedido, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            GarantirInstrucoesIniciais();
            if (id is null)
            {
                if (!Equipamentos.Any(e => e.Id == equipamentoId && !RegrasGestao.EhViatura(e.Tipo))) return Task.FromResult(Falha<InstrucaoDto>(ResultadoIntervencao.NaoExiste));
                var nova = new Instrucao
                {
                    Id = Guid.NewGuid().ToString("N")[..25].ToUpperInvariant(), EquipamentoId = equipamentoId!, Versao = RegrasManutencao.Cortar(pedido.Versao, RegrasGestao.Versao),
                    Data = pedido.Data ?? Hoje, Obsoleta = pedido.Obsoleta ?? false, Obs = (pedido.Obs ?? "").Trim()
                };
                _instrucoes.Add(nova);
                return Task.FromResult<(ResultadoIntervencao, InstrucaoDto?)>((ResultadoIntervencao.Ok, ParaDto(nova)));
            }

            var i = _instrucoes.FirstOrDefault(x => x.Id == id);
            if (i is null || InstrucaoDeViatura(id)) return Task.FromResult(Falha<InstrucaoDto>(ResultadoIntervencao.NaoExiste));
            if (!string.IsNullOrEmpty(pedido.Concorrencia) && pedido.Concorrencia != i.Ver.ToString()) return Task.FromResult(Falha<InstrucaoDto>(ResultadoIntervencao.Conflito));
            if (pedido.Versao is not null) i.Versao = RegrasManutencao.Cortar(pedido.Versao, RegrasGestao.Versao);
            if (pedido.Data is { } data) i.Data = data;
            if (pedido.Obs is not null) i.Obs = pedido.Obs.Trim();
            i.Obsoleta = pedido.Obsoleta ?? i.Obsoleta;
            i.Ver++;
            // Versão obsoleta: as planeadas por fazer das tarefas dela ficam canceladas.
            if (i.Obsoleta)
            {
                var tarefas = Tarefas.Where(t => t.InstrucaoId == id).Select(t => t.Id).ToHashSet();
                CancelarPlaneadas(f => tarefas.Contains(f.Tarefa), RegrasGestao.MotivoInstrucaoObsoleta);
            }
            return Task.FromResult<(ResultadoIntervencao, InstrucaoDto?)>((ResultadoIntervencao.Ok, ParaDto(i)));
        }
    }

    private TarefaGestaoDto ParaDto(Tarefa t) => new(t.Id, t.InstrucaoId, t.Periodo, t.Responsabilidade, t.Responsavel, t.Descricao, t.TipoManut, t.Versao.ToString());

    public Task<IReadOnlyList<TarefaGestaoDto>?> ListarTarefasAsync(string instrucaoId, CancellationToken ct)
    {
        lock (Trinco())
        {
            GarantirInstrucoesIniciais();
            return Task.FromResult(_instrucoes.Any(i => i.Id == instrucaoId) && !InstrucaoDeViatura(instrucaoId)
                ? (IReadOnlyList<TarefaGestaoDto>?)Tarefas.Where(t => t.InstrucaoId == instrucaoId).OrderBy(t => t.Periodo).Select(ParaDto).ToList()
                : null);
        }
    }

    private bool PeriodoConhecido(string periodo) =>
        Periodicidades_.Any(p => string.Equals(p.Periodo, periodo, StringComparison.OrdinalIgnoreCase)) || Tarefas.Any(t => string.Equals(t.Periodo, periodo, StringComparison.OrdinalIgnoreCase));

    // A tarefa gera a planeada (se a configuração gera, não há uma aberta, o equipamento e a instrução estão ativos e a periodicidade gera).
    private void GerarDaTarefa(Tarefa t)
    {
        var (unidade, intervalo) = Regra(t);
        var e = Equipamentos.FirstOrDefault(x => x.Id == t.EquipamentoId);
        if (!_cfg.GerarPlaneadas || e is null || e.Obsoleto || InstrucaoObsoleta(t.InstrucaoId) || unidade == UnidadePeriodo.Nenhum || intervalo <= 0) return;
        if (_lista.Any(f => f.Tarefa == t.Id && Aberta(f))) return;
        var fichas = _lista.Where(f => f.Tarefa == t.Id).ToList();
        DateTime? Maior(Func<Ficha, bool> onde) => fichas.Where(onde).Select(f => (DateTime?)f.D.DataPlaneada.ToDateTime(TimeOnly.MinValue)).Max();
        var prevista = RegrasManutencao.ProximaDaTarefa(Maior(f => f.D.Estado is EstadoIntervencao.Concluida or EstadoIntervencao.Validada),
            Maior(f => f.D.Estado == EstadoIntervencao.Cancelada), null, unidade, intervalo, null);
        var data = prevista.HasValue ? DateOnly.FromDateTime(prevista.Value) : Hoje;
        if (data < Hoje) data = Hoje;
        _lista.Add(new Ficha
        {
            D = Nova(Guid.NewGuid().ToString("N")[..25], e.Nome, e.Zona, TipoIntervencao.Preventiva, data, EstadoIntervencao.Planeada, RegrasManutencao.Cortar(t.Descricao, RegrasManutencao.TamanhoProblema)),
            Tarefa = t.Id, Numero = ProximoNumero(), Periodo = t.Periodo,
            TipoManut = RegrasFicha.NormalizarTipoManut(t.TipoManut) ?? (string.Equals(t.Responsabilidade, "Empresa Externa", StringComparison.OrdinalIgnoreCase) ? RegrasManutencao.TipoExterna : RegrasManutencao.TipoInterna)
        });
    }

    public Task<(ResultadoIntervencao, TarefaGestaoDto?)> GravarTarefaAsync(string? instrucaoId, string? id, PedidoTarefa pedido, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            GarantirInstrucoesIniciais();
            Tarefa? t = null;
            if (id is null)
            {
                var instrucao = _instrucoes.FirstOrDefault(i => i.Id == instrucaoId);
                if (instrucao is null || InstrucaoDeViatura(instrucaoId!)) return Task.FromResult(Falha<TarefaGestaoDto>(ResultadoIntervencao.NaoExiste));
                if (instrucao.Obsoleta) return Task.FromResult(Falha<TarefaGestaoDto>(ResultadoIntervencao.InstrucaoObsoleta));
            }
            else
            {
                t = Tarefas.FirstOrDefault(x => x.Id == id);
                if (t is null || EquipamentoEViatura(t.EquipamentoId)) return Task.FromResult(Falha<TarefaGestaoDto>(ResultadoIntervencao.NaoExiste));
                if (!string.IsNullOrEmpty(pedido.Concorrencia) && pedido.Concorrencia != t.Versao.ToString()) return Task.FromResult(Falha<TarefaGestaoDto>(ResultadoIntervencao.Conflito));
            }

            var periodo = pedido.Periodo is null ? t?.Periodo ?? "" : RegrasManutencao.Cortar(pedido.Periodo, RegrasGestao.Periodo);
            if (periodo.Length > 0 && !string.Equals(periodo, t?.Periodo, StringComparison.OrdinalIgnoreCase) && !PeriodoConhecido(periodo))
                return Task.FromResult(Falha<TarefaGestaoDto>(ResultadoIntervencao.PeriodicidadeInvalida));

            if (t is null)
            {
                var equipamentoId = _instrucoes.First(i => i.Id == instrucaoId).EquipamentoId;
                t = new Tarefa(Guid.NewGuid().ToString("N")[..25].ToUpperInvariant(), instrucaoId!, equipamentoId, (pedido.Descricao ?? "").Trim(), periodo);
                Tarefas.Add(t);
            }
            else
            {
                t.Periodo = periodo;
                t.Versao++;
                if (pedido.Descricao is not null) t.Descricao = pedido.Descricao.Trim();
            }
            if (pedido.Responsabilidade is not null) t.Responsabilidade = pedido.Responsabilidade.Trim();
            if (pedido.Responsavel is not null) t.Responsavel = pedido.Responsavel.Trim();
            if (pedido.TipoManut is not null) t.TipoManut = RegrasFicha.NormalizarTipoManut(pedido.TipoManut) ?? "";

            // Com a instrução ou o equipamento obsoletos fica gravada mas não gera, e diz-se.
            string? aviso = null;
            if (InstrucaoObsoleta(t.InstrucaoId)) aviso = RegrasGestao.AvisoInstrucaoObsoleta;
            else if (Equipamentos.FirstOrDefault(x => x.Id == t.EquipamentoId)?.Obsoleto == true) aviso = RegrasGestao.AvisoEquipamentoObsoleto;
            else GerarDaTarefa(t);
            return Task.FromResult<(ResultadoIntervencao, TarefaGestaoDto?)>((ResultadoIntervencao.Ok, ParaDto(t) with { Aviso = aviso }));
        }
    }

    public Task<ResultadoIntervencao> ApagarTarefaAsync(string id, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var t = Tarefas.FirstOrDefault(x => x.Id == id);
            if (t is null || EquipamentoEViatura(t.EquipamentoId)) return Task.FromResult(ResultadoIntervencao.NaoExiste);
            if (_lista.Any(f => f.Tarefa == id && f.D.Estado is not (EstadoIntervencao.Planeada or EstadoIntervencao.Cancelada))) return Task.FromResult(ResultadoIntervencao.TarefaComFichas);
            CancelarPlaneadas(f => f.Tarefa == id, RegrasGestao.MotivoTarefaApagada);
            Tarefas.Remove(t);
            return Task.FromResult(ResultadoIntervencao.Ok);
        }
    }

    private static PeriodicidadeDto ParaDto(Periodicidade p) => new(p.Id, p.Periodo, p.Unidade, p.Intervalo, p.Aviso, p.Ver.ToString());

    public Task<IReadOnlyList<PeriodicidadeDto>> ListarPeriodicidadesAsync(CancellationToken ct)
    {
        lock (Trinco())
            return Task.FromResult<IReadOnlyList<PeriodicidadeDto>>(Periodicidades_
                .OrderBy(p => p.Unidade == "dias" ? 0 : p.Unidade == "meses" ? 1 : 2).ThenBy(p => p.Intervalo).ThenBy(p => p.Periodo).Select(ParaDto).ToList());
    }

    public Task<(ResultadoIntervencao, PeriodicidadeDto?)> GravarPeriodicidadeAsync(string id, PedidoPeriodicidade pedido, TecnicoAtual supervisor, CancellationToken ct)
    {
        lock (Trinco())
        {
            var p = Periodicidades_.FirstOrDefault(x => x.Id == id);
            if (p is null) return Task.FromResult(Falha<PeriodicidadeDto>(ResultadoIntervencao.NaoExiste));
            if (!string.IsNullOrEmpty(pedido.Concorrencia) && pedido.Concorrencia != p.Ver.ToString()) return Task.FromResult(Falha<PeriodicidadeDto>(ResultadoIntervencao.Conflito));
            var (unidade, intervalo) = RegrasGestao.NormalizarPeriodicidade(pedido);
            p.Unidade = unidade;
            p.Intervalo = intervalo;
            p.Aviso = pedido.Aviso ?? p.Aviso;
            p.Ver++;
            return Task.FromResult<(ResultadoIntervencao, PeriodicidadeDto?)>((ResultadoIntervencao.Ok, ParaDto(p)));
        }
    }
}
