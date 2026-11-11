namespace GestorManutencao.Api;

// Gestão (só supervisor, só online): equipamentos (u_equip), histórico de estado (u_histstats), instruções (u_itmanut), tarefas (u_itmanutli) e periodicidades (u_periocid).
// Concorrencia/Versao: usrdata + usrhora lidos; se a gravação o trouxer e já não coincidir, a resposta é 409 (alguém mexeu entretanto).

public sealed record EquipamentoGestaoDto(
    string Id, int NoInterno, string Ref, string Design, string Tipo, string Marca, string Modelo, string Serie,
    int Ano, decimal Valor, long NoFornecedor, string NomeFornecedor, DateOnly? Data, string Local, string Estado, bool Obsoleto,
    string ManualPdf, string Email, string Versao);

// Campos a null ficam como estão (na criação, vazios). O nome do fornecedor vem sempre do número (fl).
// PlaneadasConfirmadas: ao marcar obsoleto, o nº de planeadas que o utilizador viu e aceitou cancelar; se já não for esse, a gravação é recusada.
public sealed record PedidoEquipamento(
    string? Ref = null, string? Design = null, string? Tipo = null, string? Marca = null, string? Modelo = null, string? Serie = null,
    int? Ano = null, decimal? Valor = null, long? NoFornecedor = null, DateOnly? Data = null, string? Local = null, string? Estado = null,
    bool? Obsoleto = null, string? ManualPdf = null, string? Email = null, string? Versao = null, int? PlaneadasConfirmadas = null);

public sealed record OpcoesEquipamentoDto(IReadOnlyList<string> Tipos, IReadOnlyList<string> Marcas, IReadOnlyList<string> Locais, IReadOnlyList<string> Estados);

public sealed record HistoricoEstadoDto(DateOnly Data, string Estado, string Quem);

public sealed record PlaneadasACancelarDto(int Planeadas);

public sealed record InstrucaoDto(string Id, string EquipamentoId, string Versao, DateOnly Data, bool Obsoleta, string Obs, int NumTarefas, string Concorrencia);

public sealed record PedidoInstrucao(string? Versao = null, DateOnly? Data = null, bool? Obsoleta = null, string? Obs = null, string? Concorrencia = null);

public sealed record TarefaGestaoDto(string Id, string InstrucaoId, string Periodo, string Responsabilidade, string Responsavel, string Descricao, string TipoManut, string Concorrencia, string? Aviso = null);

public sealed record PedidoTarefa(string? Periodo = null, string? Responsabilidade = null, string? Responsavel = null, string? Descricao = null, string? TipoManut = null, string? Concorrencia = null);

public sealed record PeriodicidadeDto(string Id, string Periodo, string Unidade, int Intervalo, bool Aviso, string Concorrencia);

public sealed record PedidoPeriodicidade(string? Unidade = null, int? Intervalo = null, bool? Aviso = null, string? Concorrencia = null);

// Contrato igual no repositório SQL e no de memória. Quem grava vem do supervisor autenticado (auditoria ousr*/usr*).
public interface IRepositorioGestao
{
    Task<OpcoesEquipamentoDto> ObterOpcoesAsync(CancellationToken ct);
    Task<EquipamentoGestaoDto?> ObterEquipamentoGestaoAsync(string id, CancellationToken ct);
    // id null = criar (nasce com a instrução de manutenção vazia, na mesma transação).
    Task<(ResultadoIntervencao, EquipamentoGestaoDto?)> GravarEquipamentoAsync(string? id, PedidoEquipamento pedido, TecnicoAtual supervisor, CancellationToken ct);
    // Planeadas que ficam canceladas se o equipamento passar a obsoleto (null se o equipamento não existe).
    Task<int?> ContarPlaneadasEquipamentoAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<HistoricoEstadoDto>?> ListarHistoricoEstadoAsync(string equipamentoId, CancellationToken ct);

    Task<IReadOnlyList<InstrucaoDto>?> ListarInstrucoesAsync(string equipamentoId, CancellationToken ct);
    // Com id null cria uma versão nova do equipamento; marcar obsoleta cancela as planeadas das tarefas dessa versão.
    Task<(ResultadoIntervencao, InstrucaoDto?)> GravarInstrucaoAsync(string? equipamentoId, string? id, PedidoInstrucao pedido, TecnicoAtual supervisor, CancellationToken ct);

    Task<IReadOnlyList<TarefaGestaoDto>?> ListarTarefasAsync(string instrucaoId, CancellationToken ct);
    // Gravar uma tarefa gera a planeada (se a configuração gera planeadas).
    Task<(ResultadoIntervencao, TarefaGestaoDto?)> GravarTarefaAsync(string? instrucaoId, string? id, PedidoTarefa pedido, TecnicoAtual supervisor, CancellationToken ct);
    // Só se não houver fichas fora de Planeada e Cancelada (TarefaComFichas); as planeadas ficam canceladas.
    Task<ResultadoIntervencao> ApagarTarefaAsync(string id, TecnicoAtual supervisor, CancellationToken ct);

    Task<IReadOnlyList<PeriodicidadeDto>> ListarPeriodicidadesAsync(CancellationToken ct);
    // Só edita (unidade, intervalo, aviso); não cria nem apaga.
    Task<(ResultadoIntervencao, PeriodicidadeDto?)> GravarPeriodicidadeAsync(string id, PedidoPeriodicidade pedido, TecnicoAtual supervisor, CancellationToken ct);
}

// Tamanhos das colunas e validações da gestão, os mesmos da LandeiroMaui (ManutencaoRegras.Tamanho e ManutencaoService).
public static class RegrasGestao
{
    public const int Ref = 30, Design = 45, Tipo = 25, Marca = 25, Modelo = 25, Serie = 55, NomeFornecedor = 55, Local = 65, Estado = 55, EstadoHistorico = 25,
        ManualPdf = 200, Email = 254, Versao = 10, Periodo = 20, Responsabilidade = 45, Responsavel = 25, TipoManut = 20, Descricao = 4000, Obs = 4000;

    public static readonly string[] Estados = ["Em Serviço", "Fora de Serviço"];

    public const string MotivoObsoleto = "Equipamento obsoleto", MotivoInstrucaoObsoleta = "Instrução de manutenção obsoleta",
        MotivoTarefaApagada = "Tarefa apagada";

    public const string AvisoInstrucaoObsoleta = "A tarefa ficou gravada, mas esta versão da instrução está obsoleta e não gera planeadas.",
        AvisoEquipamentoObsoleto = "A tarefa ficou gravada, mas o equipamento está obsoleto e não gera planeadas.";


    // null se o valor não é um dos estados; sem texto vale o primeiro (em serviço) só na criação.
    public static string? NormalizarEstado(string? estado) => Estados.FirstOrDefault(e => string.Equals(e, (estado ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? Tamanho(string? valor, int max, string nome) =>
        (valor ?? "").Trim().Length > max ? $"{nome} pode ter no máximo {max} letras." : null;

    public static string? ValidarEquipamento(PedidoEquipamento p, bool novo)
    {
        if (novo && string.IsNullOrWhiteSpace(p.Ref)) return "Indique o nº da infraestrutura.";
        if (novo && string.IsNullOrWhiteSpace(p.Design)) return "Indique a designação.";
        if (p.Ref is not null && p.Ref.Trim().Length == 0) return "Indique o nº da infraestrutura.";
        if (p.Design is not null && p.Design.Trim().Length == 0) return "Indique a designação.";
        var erro = Tamanho(p.Ref, Ref, "O nº") ?? Tamanho(p.Design, Design, "A designação") ?? Tamanho(p.Tipo, Tipo, "O tipo") ?? Tamanho(p.Marca, Marca, "A marca")
            ?? Tamanho(p.Modelo, Modelo, "O modelo") ?? Tamanho(p.Serie, Serie, "O nº de série") ?? Tamanho(p.Local, Local, "O local")
            ?? Tamanho(p.ManualPdf, ManualPdf, "O manual") ?? Tamanho(p.Email, Email, "O e-mail");
        if (erro is not null) return erro;
        if (!string.IsNullOrWhiteSpace(p.Estado) && NormalizarEstado(p.Estado) is null) return "O estado é Em Serviço ou Fora de Serviço.";
        if (p.Ano is < 0 or > 9999) return "O ano tem de estar entre 0 e 9999.";
        if (p.Valor is < 0 or > RegrasManutencao.MaxValor) return "O valor de aquisição é inválido.";
        if (p.NoFornecedor is < 0 or > RegrasFicha.MaxNoFornecedor) return "Fornecedor inválido.";
        if (p.Data is { } d && (d.Year < 1900 || d.Year > 2999)) return "A data tem de estar entre 1900 e 2999.";
        if (!string.IsNullOrWhiteSpace(p.Email) && (p.Email.Trim().Contains(' ') || !p.Email.Contains('@') || p.Email.Trim().StartsWith('@') || p.Email.Trim().EndsWith('@')))
            return "O e-mail não é válido.";
        if (p.PlaneadasConfirmadas is < 0) return "Confirmação inválida.";
        return null;
    }

    // Aplica o pedido ao que está gravado (criação: atual = vazio); o nome do fornecedor vem à parte.
    public static EquipamentoGestaoDto AplicarEquipamento(EquipamentoGestaoDto a, PedidoEquipamento p) => a with
    {
        Ref = p.Ref is { } r ? r.Trim() : a.Ref,
        Design = p.Design is { } d ? d.Trim() : a.Design,
        Tipo = p.Tipo is { } t ? t.Trim() : a.Tipo,
        Marca = p.Marca is { } m ? m.Trim() : a.Marca,
        Modelo = p.Modelo is { } mo ? mo.Trim() : a.Modelo,
        Serie = p.Serie is { } s ? s.Trim() : a.Serie,
        Ano = p.Ano ?? a.Ano,
        Valor = p.Valor ?? a.Valor,
        NoFornecedor = p.NoFornecedor ?? a.NoFornecedor,
        Data = p.Data ?? a.Data,
        Local = p.Local is { } l ? l.Trim() : a.Local,
        Estado = string.IsNullOrWhiteSpace(p.Estado) ? a.Estado : NormalizarEstado(p.Estado) ?? a.Estado,
        Obsoleto = p.Obsoleto ?? a.Obsoleto,
        ManualPdf = p.ManualPdf is { } pdf ? pdf.Trim() : a.ManualPdf,
        Email = p.Email is { } e ? e.Trim() : a.Email
    };

    public static EquipamentoGestaoDto EquipamentoVazio(DateOnly hoje) =>
        new("", 0, "", "", "", "", "", "", 0, 0, 0, "", hoje, "", Estados[0], false, "", "", "");

    public static string? ValidarInstrucao(PedidoInstrucao p) =>
        Tamanho(p.Versao, Versao, "A versão") ?? Tamanho(p.Obs, Obs, "As observações")
        ?? (p.Data is { } d && (d.Year < 1900 || d.Year > 2999) ? "A data tem de estar entre 1900 e 2999." : null);

    public static string? ValidarTarefa(PedidoTarefa p, bool nova)
    {
        if ((nova && string.IsNullOrWhiteSpace(p.Descricao)) || (p.Descricao is not null && p.Descricao.Trim().Length == 0)) return "Descreva a tarefa.";
        var erro = Tamanho(p.Periodo, Periodo, "A periodicidade") ?? Tamanho(p.Responsabilidade, Responsabilidade, "A responsabilidade")
            ?? Tamanho(p.Responsavel, Responsavel, "O responsável") ?? Tamanho(p.Descricao, Descricao, "A descrição");
        if (erro is not null) return erro;
        if (!string.IsNullOrWhiteSpace(p.TipoManut) && RegrasFicha.NormalizarTipoManut(p.TipoManut) is null) return "O tipo de manutenção é Interna ou Externa.";
        return null;
    }

    // Sem tipo, deduz-se da responsabilidade ("Empresa Externa" = Externa).
    public static string TipoManutDaTarefa(string? tipoManut, string? responsabilidade) =>
        RegrasFicha.NormalizarTipoManut(tipoManut)
        ?? (string.Equals((responsabilidade ?? "").Trim(), "Empresa Externa", StringComparison.OrdinalIgnoreCase) ? RegrasManutencao.TipoExterna : RegrasManutencao.TipoInterna);

    public static string? ValidarPeriodicidade(PedidoPeriodicidade p) =>
        p.Unidade is null ? "Indique a unidade." : Periodicidades.ValidarUnidadeEIntervalo(p.Unidade, p.Intervalo);

    public static (string Unidade, int Intervalo) NormalizarPeriodicidade(PedidoPeriodicidade p)
    {
        var unidade = RegrasManutencao.LerUnidade(p.Unidade);
        return (unidade.ToString().ToLowerInvariant(), unidade == UnidadePeriodo.Nenhum ? 0 : p.Intervalo ?? 0);
    }
}
