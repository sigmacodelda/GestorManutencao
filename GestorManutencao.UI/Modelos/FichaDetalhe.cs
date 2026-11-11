namespace GestorManutencao.UI.Modelos;

public sealed record TempoMaoDeObra(string Id, int Tecno, string Responsavel, decimal Horas, decimal Custo, decimal CustoHora);

// Linha de compra ligada à ficha, só leitura.
public sealed record CompraLigada(DateOnly? Data, string Documento, string Fornecedor, string Ref, string Design, decimal Qtt, decimal Valor, string Tipo);

public sealed record Fornecedor(long No, string Nome);

public sealed record ContadoresMenu(int PlaneadasAtrasadas, int PorValidar);

// Ficha completa tal como a API a devolve. Antiga = ficha do PHC sem estado. Os bloqueios (Pode...) já vêm calculados para quem pediu:
// PodeEditar vale também para o material e a mão de obra; PodeEditarGestao é técnico, data, tipos e periodicidade.
// ExternosAMao: sem compras na ficha, a mão de obra e o material externos escrevem-se à mão.
public sealed record FichaDetalhe(
    string Id, string Equipamento, EstadoIntervencao Estado, DateOnly Data,
    int Numero = 0, string EquipamentoId = "", string Zona = "", string Ref = "", TipoIntervencao Tipo = TipoIntervencao.Preventiva, string TipoManut = "Interna",
    string Periodicidade = "", bool Antiga = false, bool Fechada = false, bool Supervisao = false,
    int Tecno = 0, string? Tecnico = null, IReadOnlyList<TecnicoFicha>? Adicionais = null,
    DateTime? HoraInicio = null, DateTime? HoraFim = null, decimal HorasFrio = 0, decimal HorasConf = 0,
    string Problema = "", string Resolucao = "", string Obs = "", bool LimRem = false,
    long NoFornecedor = 0, string NomeFornecedor = "", string DocExterno = "",
    decimal MoInterna = 0, decimal MoExterna = 0, decimal MatInterno = 0, decimal MatExterno = 0, decimal TotMo = 0, decimal TotRef = 0,
    string? Motivo = null, IReadOnlyList<TempoMaoDeObra>? MaoDeObra = null, IReadOnlyList<Material>? Materiais = null,
    bool ExternosAMao = false, bool PodeEditar = false, bool PodeEditarGestao = false, bool PodeCancelar = false, bool PodeFecharAntiga = false,
    string Versao = "", bool CustosOcultos = false)
{
    public decimal Total => TotMo + TotRef;

    public IReadOnlyList<TempoMaoDeObra> Tempos => MaoDeObra ?? [];

    public IReadOnlyList<Material> ListaMateriais => Materiais ?? [];
}

// Ficha manual: nasce em curso com o utilizador como técnico. A periodicidade (opcional) é uma das do equipamento.
public sealed record NovaFicha(
    string EquipamentoId, TipoIntervencao Tipo = TipoIntervencao.Preventiva, string TipoManut = "Interna", string Periodicidade = "", string Problema = "",
    string? OpId = null);

// Campos a null ficam como estão; texto vazio apaga; hora a 1900 apaga. O operador só mexe na execução (horas, frio, confirmação, problema,
// resolução, obs, limrem, externo); Data, Tipo, TipoManut, Periodicidade e Tecno são do supervisor.
public sealed record EdicaoFicha(
    DateTime? HoraInicio = null, DateTime? HoraFim = null, decimal? HorasFrio = null, decimal? HorasConf = null,
    string? Problema = null, string? Resolucao = null, string? Obs = null, bool? LimRem = null,
    long? NoFornecedor = null, string? NomeFornecedor = null, string? DocExterno = null, decimal? MoExterna = null, decimal? MatExterno = null,
    DateOnly? Data = null, TipoIntervencao? Tipo = null, string? TipoManut = null, string? Periodicidade = null, int? Tecno = null, string? Versao = null);

// Mesmos limites do servidor, só para avisar logo no ecrã; devolvem a mensagem para o utilizador ou null.
public static class RegrasFichaDetalhe
{
    public const int MaxDevolucao = 254, MaxMotivoCancelamento = 240, MaxProblema = 200, MaxResolucao = 254, MaxObs = 4000, MaxDocExterno = 25, MaxNomeFornecedor = 55, MaxPeriodo = 20;
    public const decimal MaxHoras = 999.99m, MaxCustoHora = 99_999m, MaxValor = 999_999_999m;

    public static string? ValidarCancelamento(string? motivo) =>
        string.IsNullOrWhiteSpace(motivo) ? "Escreva o motivo do cancelamento."
        : motivo.Trim().Length > MaxMotivoCancelamento ? $"Escreva menos: no máximo {MaxMotivoCancelamento} letras."
        : null;

    public static string? ValidarTempo(int tecno, decimal horas, decimal custoHora) =>
        tecno <= 0 ? "Escolha o técnico."
        : horas is <= 0 or > MaxHoras ? "Indique as horas (mais de 0 e até 999,99)."
        : custoHora is < 0 or > MaxCustoHora ? "O custo por hora é inválido."
        : null;

    public static decimal CustoTempo(decimal horas, decimal custoHora) => Math.Round(horas * custoHora, 3);

    public static string? ValidarNova(NovaFicha f) =>
        string.IsNullOrWhiteSpace(f.EquipamentoId) ? "Escolha o equipamento."
        : f.TipoManut is not ("Interna" or "Externa") ? "Escolha se a manutenção é interna ou externa."
        : (f.Periodicidade ?? "").Trim().Length > MaxPeriodo ? $"A periodicidade tem de ter no máximo {MaxPeriodo} letras."
        : (f.Problema ?? "").Trim().Length > RegrasIntervencao.MaxAnomalia ? $"Escreva menos: no máximo {RegrasIntervencao.MaxAnomalia} letras."
        : null;

    public static string? ValidarEdicao(EdicaoFicha e)
    {
        static int Tam(string? x) => (x ?? "").Trim().Length;
        if (Tam(e.Problema) > MaxProblema) return $"O problema tem de ter no máximo {MaxProblema} letras.";
        if (Tam(e.Resolucao) > MaxResolucao) return $"A resolução tem de ter no máximo {MaxResolucao} letras.";
        if (Tam(e.Obs) > MaxObs) return $"As observações têm de ter no máximo {MaxObs} letras.";
        if (Tam(e.DocExterno) > MaxDocExterno) return $"O documento externo tem de ter no máximo {MaxDocExterno} letras.";
        if (Tam(e.NomeFornecedor) > MaxNomeFornecedor) return $"O nome do fornecedor tem de ter no máximo {MaxNomeFornecedor} letras.";
        if (Tam(e.Periodicidade) > MaxPeriodo) return $"A periodicidade tem de ter no máximo {MaxPeriodo} letras.";
        if (e.HorasFrio is < 0 or > MaxHoras || e.HorasConf is < 0 or > MaxHoras) return "As horas têm de estar entre 0 e 999,99.";
        if (e.MoExterna is < 0 or > MaxValor || e.MatExterno is < 0 or > MaxValor) return "Os valores externos não podem ser negativos.";
        if (e.HoraInicio is { } i && e.HoraFim is { } f && i.Year > 1900 && f.Year > 1900 && f < i) return "A hora de fim não pode ser antes da de início.";
        return null;
    }
}
