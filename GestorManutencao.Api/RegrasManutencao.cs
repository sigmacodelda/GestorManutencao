namespace GestorManutencao.Api;

public enum UnidadePeriodo { Dias, Meses, Nenhum }

// Configuração da manutenção (u_configman, uma linha); sem linha valem estes valores, como na LandeiroMaui.
public sealed record ConfigManutencao(
    int Antecedencia = 7, bool GerarPlaneadas = true, bool AvancaOrdem = false, bool ValidarPropria = false,
    bool ExtExigeDoc = true, bool ComprasFichaInt = true, bool ExigeMaterial = false);

// Regras copiadas da LandeiroMaui (Helpers/ManutencaoRegras e PeriodicidadeHelper) para as duas apps tratarem as fichas da mesma forma.
public static class RegrasManutencao
{
    public const string TipoVeiculos = "Veículos";

    // Há números de ficha errados que não entram no "próximo número do ano".
    public const decimal LimiteNumeroFicha = 1_000_000m;

    // Tamanhos das colunas de u_fichaint e u_matinterv.
    public const int TamanhoNomeTecnico = 45, TamanhoDesignMaterial = 35, TamanhoMotivo = 254;

    public static string Cortar(string? texto, int tamanho)
    {
        var t = (texto ?? "").Trim();
        return t.Length <= tamanho ? t : t[..tamanho].TrimEnd();
    }

    public static UnidadePeriodo LerUnidade(string? unidade) => (unidade ?? "").Trim().ToLowerInvariant() switch
    {
        "dias" => UnidadePeriodo.Dias,
        "meses" => UnidadePeriodo.Meses,
        _ => UnidadePeriodo.Nenhum
    };

    // Próxima intervenção = data da última feita + periodicidade (mês curto usa o último dia).
    public static DateTime? ProximaDepoisDaUltima(DateTime ultima, UnidadePeriodo unidade, int intervalo)
    {
        if (intervalo <= 0 || ultima.Year <= 1900) return null;
        return unidade switch
        {
            UnidadePeriodo.Dias => ultima.Date.AddDays(intervalo),
            UnidadePeriodo.Meses => ultima.Date.AddMonths(intervalo),
            _ => null
        };
    }

    public static decimal ProximoNumeroFicha(IEnumerable<decimal> numerosDoAno)
    {
        var validos = numerosDoAno.Where(n => n > 0 && n < LimiteNumeroFicha).ToList();
        return validos.Count == 0 ? 1 : validos.Max() + 1;
    }

    // Manutenção externa só conclui e valida com fornecedor e documento externo.
    public static string? FaltaParaFechar(string? tipoManut, decimal noFornecedor, string? docExterno) =>
        string.Equals((tipoManut ?? "").Trim(), "Externa", StringComparison.OrdinalIgnoreCase) && (noFornecedor <= 0 || string.IsNullOrWhiteSpace(docExterno))
            ? "externa_sem_documento"
            : null;

    public static decimal CustoMaterial(decimal qtt, decimal precoUnitario) => Math.Round(qtt * precoUnitario, 3);

    // Limites da pesquisa de equipamentos e fichas; tamanhos das colunas u_equip.tipo e u_equip.local.
    public const int MaxTextoPesquisa = 60, TamanhoTipo = 30, TamanhoLocal = 65, FichasPorPagina = 20, FichasNoHistorico = 50, MaxEquipamentos = 500, MaxPesquisaAvaria = 20;

    // Mesmo limite da LandeiroMaui; o valor por defeito do PIN (pinmaxtent) fica por conta do PHC e desta API não o usa.
    public const int MaxAntecedencia = 90;

    public static string? ValidarConfiguracao(PedidoConfiguracao p) =>
        p.GerarPlaneadas is null || p.AvancaOrdem is null || p.ValidarPropria is null || p.ExtExigeDoc is null || p.ComprasFichaInt is null
            ? "Faltam opções por indicar."
        : p.Antecedencia is null or < 0 or > MaxAntecedencia ? $"A antecedência tem de ser um número inteiro de dias entre 0 e {MaxAntecedencia}."
        : null;

    // As fichas antigas do PHC não têm estado: fechadas contam como validadas, as que ficaram abertas como concluídas.
    public static EstadoIntervencao EstadoDaFicha(string? estado, bool fechada) =>
        Enum.TryParse<EstadoIntervencao>((estado ?? "").Trim(), out var e) && Enum.IsDefined(e) ? e
        : fechada ? EstadoIntervencao.Validada : EstadoIntervencao.Concluida;

    // Próxima data de uma tarefa = a mais recente entre a última feita (ou, nas fichas antigas, a do equipamento) e a última cancelada, mais a periodicidade;
    // sem nada feito nem cancelado, vale a data da ficha aberta (se existir).
    public static DateTime? ProximaDaTarefa(DateTime? feita, DateTime? cancelada, DateTime? feitaAntiga, UnidadePeriodo unidade, int intervalo, DateTime? dataAberta)
    {
        var ultima = feita ?? feitaAntiga;
        var baseData = cancelada.HasValue && (!ultima.HasValue || cancelada > ultima) ? cancelada : ultima;
        var calculada = baseData.HasValue ? ProximaDepoisDaUltima(baseData.Value, unidade, intervalo) : null;
        return calculada ?? dataAberta;
    }
}
