namespace GestorManutencao.Api;

public sealed record TempoDto(string Id, int Tecno, string Responsavel, decimal Horas, decimal Custo, decimal CustoHora);

// Linha de compra (fn) ligada à ficha, só leitura; Valor já vem com o sinal invertido nos documentos a débito.
public sealed record CompraLigadaDto(DateOnly? Data, string Documento, string Fornecedor, string Ref, string Design, decimal Qtt, decimal Valor, string Tipo);

public sealed record FornecedorDto(long No, string Nome);

public sealed record ContadoresMenuDto(int PlaneadasAtrasadas, int PorValidar);

// Ficha completa. Antiga = estado vazio no PHC (Estado já vem como Validada se fechada, senão Concluida). Motivo = devolução (em curso) ou cancelamento.
// Os bloqueios dizem o que o utilizador que pediu pode fazer: PodeEditar vale também para material e mão de obra; PodeEditarGestao é técnico, data, tipos e periodicidade.
// ExternosAMao: sem compras na ficha (configuração), mão de obra e material externos escrevem-se à mão.
public sealed record FichaDetalheDto(
    string Id, int Numero, string EquipamentoId, string Equipamento, string Zona, string Ref,
    TipoIntervencao Tipo, string TipoManut, string Periodicidade, DateOnly Data, EstadoIntervencao Estado, bool Antiga, bool Fechada, bool Supervisao,
    int Tecno, string? Tecnico, IReadOnlyList<TecnicoFichaDto>? Adicionais,
    DateTime? HoraInicio, DateTime? HoraFim, decimal HorasFrio, decimal HorasConf,
    string Problema, string Resolucao, string Obs, bool LimRem,
    long NoFornecedor, string NomeFornecedor, string DocExterno,
    decimal MoInterna, decimal MoExterna, decimal MatInterno, decimal MatExterno, decimal TotMo, decimal TotRef,
    string? Motivo, IReadOnlyList<TempoDto> MaoDeObra, IReadOnlyList<MaterialDto> Materiais,
    bool ExternosAMao, bool PodeEditar, bool PodeEditarGestao, bool PodeCancelar, bool PodeFecharAntiga,
    string Versao = "", bool CustosOcultos = false);

// Campos a null ficam como estão; texto vazio apaga; hora de fim ou de início em 1900 apaga.
// Operador: só execução (horas, frio, confirmação, problema, resolução, obs, limrem, fornecedor e documento). Data, Tipo, TipoManut, Periodicidade, Tecno, MoExterna e MatExterno são do supervisor.
// Versao é a do detalhe lido: se a ficha mudou entretanto, a gravação é recusada (409). O nome do fornecedor vem sempre do número (fl).
// CustosOcultos (no detalhe): o operador não vê custos de terceiros, externos, totais nem compras.
public sealed record PedidoFicha(
    DateTime? HoraInicio = null, DateTime? HoraFim = null, decimal? HorasFrio = null, decimal? HorasConf = null,
    string? Problema = null, string? Resolucao = null, string? Obs = null, bool? LimRem = null,
    long? NoFornecedor = null, string? NomeFornecedor = null, string? DocExterno = null, decimal? MoExterna = null, decimal? MatExterno = null,
    DateOnly? Data = null, TipoIntervencao? Tipo = null, string? TipoManut = null, string? Periodicidade = null, int? Tecno = null, string? Versao = null);

public sealed record PedidoCancelamento(string? Motivo);

public sealed record PedidoFichaManual(string? EquipamentoId, TipoIntervencao? Tipo = null, string? TipoManut = null, string? Periodicidade = null, string? Problema = null, string? OpId = null);

public sealed record PedidoTempo(int? Tecno, decimal? Horas, decimal? CustoHora);

public sealed record PedidoFechoAntiga(bool? Supervisao, bool? Fechada);

// O que está gravado na ficha (texto como no PHC, problema sem o marcador da operação). Serve a validar, a aplicar o pedido e a montar o detalhe.
public sealed record FichaLida(
    string Id, int Numero, string EquipamentoId, string Equipamento, string Zona, string Ref, string Tipo, string TipoManut, string Periodo,
    DateTime Data, string Estado, bool Fechada, bool Supervisao, int Tecno, string Tecnome, string Tecnos,
    DateTime HIni, DateTime HFim, decimal HorasFrio, decimal HorasConf, string Problema, string Resolucao, string Obs, bool LimRem,
    decimal NoFornec, string NomeFornec, string DocExterno,
    decimal MoInterna, decimal MoExterna, decimal MatInterno, decimal MatExterno, decimal TotMo, decimal TotRef, string ValObs, string Versao = "");

public static class RegrasFicha
{
    public static readonly DateTime SemData = new(1900, 1, 1);

    // O número do fornecedor cabe em decimal(16,0).
    public const long MaxNoFornecedor = 9_999_999_999_999_999;

    // O operador só vê o custo das suas próprias linhas de mão de obra; o supervisor vê tudo.
    public static IReadOnlyList<TempoDto> OcultarCustos(IReadOnlyList<TempoDto> tempos, bool supervisor, int tecno) =>
        supervisor ? tempos : tempos.Select(t => OcultarCusto(t, supervisor, tecno)).ToList();

    public static TempoDto OcultarCusto(TempoDto t, bool supervisor, int tecno) => supervisor || t.Tecno == tecno ? t : t with { Custo = 0, CustoHora = 0 };

    // Supervisor: qualquer ficha que não seja final. Operador: só as suas (principal ou adicional) e em curso.
    public static bool PodeEditar(string? estado, bool supervisor, bool pertence) =>
        supervisor ? !EstadosFicha.SoLeitura(estado) : EstadosFicha.Normalizar(estado) == EstadosFicha.EmCurso && pertence;

    public static bool PodeCancelar(string? estado, bool supervisor) =>
        supervisor && EstadosFicha.Normalizar(estado) is EstadosFicha.Planeada or EstadosFicha.EmCurso;

    public static bool PodeFecharAntiga(string? estado, bool supervisor) => supervisor && EstadosFicha.Normalizar(estado) == EstadosFicha.Legado;

    public static string? NormalizarTipoManut(string? valor) => (valor ?? "").Trim().ToLowerInvariant() switch
    {
        "interna" => RegrasManutencao.TipoInterna,
        "externa" => RegrasManutencao.TipoExterna,
        _ => null
    };

    public static TipoIntervencao TipoDe(string? tipo) =>
        string.Equals((tipo ?? "").Trim(), "Curativa", StringComparison.OrdinalIgnoreCase) ? TipoIntervencao.Curativa : TipoIntervencao.Preventiva;

    private static bool DataValida(DateTime d) => d.Year is >= 1900 and <= 2999;

    public static string? ValidarPedido(PedidoFicha p)
    {
        static int Tam(string? x) => (x ?? "").Trim().Length;
        if (Tam(p.Problema) > RegrasManutencao.TamanhoProblema) return $"O problema tem de ter no máximo {RegrasManutencao.TamanhoProblema} letras.";
        if (Tam(p.Resolucao) > RegrasManutencao.TamanhoResolucao) return $"A resolução tem de ter no máximo {RegrasManutencao.TamanhoResolucao} letras.";
        if (Tam(p.Obs) > RegrasManutencao.TamanhoObs) return $"As observações têm de ter no máximo {RegrasManutencao.TamanhoObs} letras.";
        if (Tam(p.DocExterno) > RegrasManutencao.TamanhoDocExterno) return $"O documento externo tem de ter no máximo {RegrasManutencao.TamanhoDocExterno} letras.";
        if (Tam(p.NomeFornecedor) > RegrasManutencao.TamanhoNomeFornecedor) return $"O nome do fornecedor tem de ter no máximo {RegrasManutencao.TamanhoNomeFornecedor} letras.";
        if (Tam(p.Periodicidade) > RegrasManutencao.TamanhoPeriodo) return $"A periodicidade tem de ter no máximo {RegrasManutencao.TamanhoPeriodo} letras.";
        if (p.HorasFrio is < 0 or > RegrasManutencao.MaxHoras || p.HorasConf is < 0 or > RegrasManutencao.MaxHoras) return "As horas têm de estar entre 0 e 999,99.";
        if (p.MoExterna is < 0 or > RegrasManutencao.MaxValor || p.MatExterno is < 0 or > RegrasManutencao.MaxValor) return "Os valores externos têm de estar entre 0 e 999 999 999.";
        if (p.NoFornecedor is < 0 or > MaxNoFornecedor) return "Fornecedor inválido.";
        if (!string.IsNullOrWhiteSpace(p.TipoManut) && NormalizarTipoManut(p.TipoManut) is null) return "O tipo de manutenção é Interna ou Externa.";
        if (p.Tipo is { } t && !Enum.IsDefined(t)) return "Tipo de intervenção inválido.";
        if (p.Tecno is < 1 or > RegrasIntervencao.MaxTecnoPhc) return "Escolha um técnico válido.";
        if (p.Data is { } d && (d.Year <= 1900 || d.Year > 2999)) return "Indique a data da intervenção.";
        if ((p.HoraInicio is { } hi && !DataValida(hi)) || (p.HoraFim is { } hf && !DataValida(hf))) return "Hora inválida.";
        if (p.HoraInicio is { } i && p.HoraFim is { } f && i.Year > 1900 && f.Year > 1900 && f < i) return "A hora de fim não pode ser antes da de início.";
        return null;
    }

    public static string? ValidarCancelamento(string? motivo) =>
        string.IsNullOrWhiteSpace(motivo) ? "Escreva o motivo do cancelamento."
        : motivo.Trim().Length > RegrasManutencao.TamanhoMotivoCancelamento ? $"O motivo tem de ter no máximo {RegrasManutencao.TamanhoMotivoCancelamento} letras."
        : null;

    public static string? ValidarFichaManual(PedidoFichaManual p) =>
        string.IsNullOrWhiteSpace(p.EquipamentoId) || p.EquipamentoId.Length > 25 ? "Escolha o equipamento."
        : p.OpId is not null && !RegrasIntervencao.OpIdValido(p.OpId) ? "Identificador da operação inválido."
        : p.Tipo is { } t && !Enum.IsDefined(t) ? "Tipo de intervenção inválido."
        : !string.IsNullOrWhiteSpace(p.TipoManut) && NormalizarTipoManut(p.TipoManut) is null ? "O tipo de manutenção é Interna ou Externa."
        : (p.Periodicidade ?? "").Trim().Length > RegrasManutencao.TamanhoPeriodo ? $"A periodicidade tem de ter no máximo {RegrasManutencao.TamanhoPeriodo} letras."
        : (p.Problema ?? "").Trim().Length > RegrasManutencao.TamanhoProblema
            ? $"O problema tem de ter no máximo {RegrasManutencao.TamanhoProblema} letras."
        : null;

    public static string? ValidarTempo(int? tecno, decimal? horas, decimal? custoHora) =>
        tecno is null or < 1 or > RegrasIntervencao.MaxTecnoPhc ? "Escolha o técnico."
        : horas is null or <= 0 or > RegrasManutencao.MaxHoras ? "Indique as horas (mais de 0 e até 999,99)."
        : custoHora is < 0 or > RegrasManutencao.MaxCustoHora ? "O custo por hora é inválido."
        : null;

    // Aplica o pedido à ficha lida. Operador que tente mexer em campos de gestão: SemPermissao; valores impossíveis: Invalido.
    // Início e fim novos têm de cair num intervalo razoável (a data da ficha é a base da próxima planeada): o operador, entre o dia da ficha e hoje;
    // o supervisor, de um ano antes do dia da ficha até amanhã.
    // O limite inferior do operador é o dia do início já gravado, se existir (ficha planeada para daqui a uma semana e iniciada hoje), senão o dia da ficha.
    public static bool HoraForaDoIntervalo(DateTime hora, DateTime dataFicha, DateOnly hoje, bool supervisor, DateTime? inicioGravado = null)
    {
        if (hora.Year <= 1900) return false;
        var dia = DateOnly.FromDateTime(hora);
        var diaFicha = DateOnly.FromDateTime(dataFicha);
        var inferior = inicioGravado is { Year: > 1900 } g ? DateOnly.FromDateTime(g) : diaFicha;
        return supervisor ? dia < diaFicha.AddYears(-1) || dia > hoje.AddDays(1) : dia < inferior || dia > hoje;
    }

    public static (FichaLida? Nova, ResultadoIntervencao Resultado) Aplicar(FichaLida a, PedidoFicha p, bool supervisor, string? nomeTecnico, bool externosAMao, DateOnly hoje)
    {
        var data = a.Data; var tipo = a.Tipo; var tipoManut = a.TipoManut; var periodo = a.Periodo;
        var tecno = a.Tecno; var tecnome = a.Tecnome; var tecnos = a.Tecnos;

        if (p.Data is { } d && d.ToDateTime(TimeOnly.MinValue) != a.Data.Date)
        {
            if (!supervisor) return (null, ResultadoIntervencao.SemPermissao);
            data = d.ToDateTime(TimeOnly.MinValue);
        }
        if (p.Tipo is { } t && t != TipoDe(a.Tipo))
        {
            if (!supervisor) return (null, ResultadoIntervencao.SemPermissao);
            tipo = t.ToString();
        }
        if (p.TipoManut is { } tm && !string.Equals(tm.Trim(), a.TipoManut.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            if (!supervisor) return (null, ResultadoIntervencao.SemPermissao);
            tipoManut = NormalizarTipoManut(tm) ?? "";
            if (tipoManut.Length == 0) return (null, ResultadoIntervencao.Invalido);
        }
        if (p.Periodicidade is { } pe && !string.Equals(RegrasManutencao.Cortar(pe, RegrasManutencao.TamanhoPeriodo), a.Periodo.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            if (!supervisor) return (null, ResultadoIntervencao.SemPermissao);
            periodo = RegrasManutencao.Cortar(pe, RegrasManutencao.TamanhoPeriodo);
        }
        if (p.Tecno is { } novo && novo != a.Tecno)
        {
            if (!supervisor) return (null, ResultadoIntervencao.SemPermissao);
            if (nomeTecnico is null) return (null, ResultadoIntervencao.TecnicoInvalido);
            tecno = novo;
            tecnome = RegrasManutencao.Cortar(nomeTecnico, RegrasManutencao.TamanhoNomeTecnico);
            tecnos = RegrasIntervencao.EscreverTecnos(RegrasIntervencao.LerTecnos(a.Tecnos), novo);
        }

        var hini = p.HoraInicio is { } hi ? (hi.Year <= 1900 ? SemData : hi) : a.HIni;
        var hfim = p.HoraFim is { } hf ? (hf.Year <= 1900 ? SemData : hf) : a.HFim;
        if (hini.Year > 1900 && hfim.Year > 1900 && hfim < hini) return (null, ResultadoIntervencao.Invalido);
        if ((hini != a.HIni && HoraForaDoIntervalo(hini, data, hoje, supervisor, a.HIni)) || (hfim != a.HFim && HoraForaDoIntervalo(hfim, data, hoje, supervisor, a.HIni)))
            return (null, ResultadoIntervencao.Invalido);

        var nova = a with
        {
            Data = data, Tipo = tipo, TipoManut = tipoManut, Periodo = periodo, Tecno = tecno, Tecnome = tecnome, Tecnos = tecnos,
            HIni = hini, HFim = hfim,
            HorasFrio = p.HorasFrio is { } frio ? Math.Round(Math.Clamp(frio, 0, RegrasManutencao.MaxHoras), 2, MidpointRounding.AwayFromZero) : a.HorasFrio,
            HorasConf = p.HorasConf is { } conf ? Math.Clamp(conf, 0, RegrasManutencao.MaxHoras) : a.HorasConf,
            Problema = p.Problema is { } pr ? RegrasManutencao.Cortar(pr, RegrasManutencao.TamanhoProblema) : a.Problema,
            Resolucao = p.Resolucao is { } re ? RegrasManutencao.Cortar(re, RegrasManutencao.TamanhoResolucao) : a.Resolucao,
            Obs = p.Obs is { } ob ? ob.Trim() : a.Obs,
            LimRem = p.LimRem ?? a.LimRem,
            NoFornec = p.NoFornecedor is { } nf ? Math.Max(0, nf) : a.NoFornec,
            NomeFornec = p.NomeFornecedor is { } nn ? RegrasManutencao.Cortar(nn, RegrasManutencao.TamanhoNomeFornecedor) : a.NomeFornec,
            DocExterno = p.DocExterno is { } dx ? RegrasManutencao.Cortar(dx, RegrasManutencao.TamanhoDocExterno) : a.DocExterno,
            MoExterna = externosAMao && supervisor && p.MoExterna is { } mo ? Math.Clamp(mo, 0, RegrasManutencao.MaxValor) : a.MoExterna,
            MatExterno = externosAMao && supervisor && p.MatExterno is { } ma ? Math.Clamp(ma, 0, RegrasManutencao.MaxValor) : a.MatExterno
        };
        return (nova, ResultadoIntervencao.Ok);
    }

    // Operador: sem custos de terceiros, externos, totais (a 0) e sem a flag de externos à mão; só vê o custo das suas linhas.
    public static FichaDetalheDto Montar(FichaLida f, IReadOnlyList<TempoDto> tempos, IReadOnlyList<MaterialDto> materiais, ConfigManutencao cfg, bool supervisor, bool pertence, int tecnoQuem = 0)
    {
        if (!supervisor)
            f = f with { MoInterna = 0, MoExterna = 0, MatInterno = 0, MatExterno = 0, TotMo = 0, TotRef = 0 };
        tempos = OcultarCustos(tempos, supervisor, tecnoQuem);
        var estado = EstadosFicha.Normalizar(f.Estado);
        var editar = PodeEditar(estado, supervisor, pertence);
        var motivo = estado is EstadosFicha.EmCurso or EstadosFicha.Cancelada ? RegrasManutencao.Visivel(f.ValObs) : "";
        return new FichaDetalheDto(
            f.Id, f.Numero, f.EquipamentoId, f.Equipamento, f.Zona, f.Ref,
            TipoDe(f.Tipo), f.TipoManut.Trim(), RegrasManutencao.Visivel(f.Periodo), DateOnly.FromDateTime(f.Data), RegrasManutencao.EstadoDaFicha(estado, f.Fechada), estado == EstadosFicha.Legado,
            f.Fechada, f.Supervisao,
            f.Tecno, RegrasManutencao.Visivel(f.Tecnome) is { Length: > 0 } nome ? nome : null, RegrasIntervencao.AdicionaisDe(f.Tecnos, f.Tecno),
            RegrasManutencao.DataOuNula(f.HIni), RegrasManutencao.DataOuNula(f.HFim), f.HorasFrio, f.HorasConf,
            RegrasManutencao.Visivel(f.Problema), RegrasManutencao.Visivel(f.Resolucao), RegrasManutencao.Visivel(f.Obs), f.LimRem,
            (long)f.NoFornec, RegrasManutencao.Visivel(f.NomeFornec), RegrasManutencao.Visivel(f.DocExterno),
            f.MoInterna, f.MoExterna, f.MatInterno, f.MatExterno, f.TotMo, f.TotRef,
            motivo.Length > 0 ? motivo : null, tempos, materiais,
            !cfg.ComprasFichaInt && supervisor, editar, supervisor && editar, PodeCancelar(estado, supervisor), PodeFecharAntiga(estado, supervisor), f.Versao, !supervisor);
    }
}
