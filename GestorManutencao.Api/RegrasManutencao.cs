namespace GestorManutencao.Api;

public enum UnidadePeriodo { Dias, Meses, Nenhum }

// Configuração da manutenção (u_configman, uma linha); sem linha valem estes valores, como na LandeiroMaui.
public sealed record ConfigManutencao(
    int Antecedencia = 7, bool GerarPlaneadas = true, bool AvancaOrdem = false, bool ValidarPropria = false,
    bool ExtExigeDoc = true, bool ComprasFichaInt = true);

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
}
