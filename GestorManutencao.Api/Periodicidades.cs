namespace GestorManutencao.Api;

// Periodicidades dos planos de manutenção, copiadas do PeriodicidadeHelper da LandeiroMaui (funções puras, só datas).
// Em uso pela geração: LerUnidade + ProximaDepoisDaUltima (RegrasManutencao). Plano, Ocorrencias e Gerar (calendário com meses marcados) servem a regras e testes.
public static class Periodicidades
{
    public const int MaxHorizonteDias = 3650, MaxIntervalo = 9999;

    public sealed record Periodicidade(string Periodo, UnidadePeriodo Unidade, int Intervalo, bool GeraPlaneadas);

    // Lista única: os 8 textos da ficha mais Trimestral e Anual. Textos com 20 caracteres, como na u_fichaint.
    public static readonly IReadOnlyList<Periodicidade> Lista =
    [
        new("Diária", UnidadePeriodo.Dias, 1, true),
        new("Semanal", UnidadePeriodo.Dias, 7, true),
        new("Quinzenal", UnidadePeriodo.Dias, 14, true),
        new("Mensal", UnidadePeriodo.Meses, 1, true),
        new("Trimestral", UnidadePeriodo.Meses, 3, true),
        new("Semestral", UnidadePeriodo.Meses, 6, true),
        new("Anual", UnidadePeriodo.Meses, 12, true),
        new("Regular", UnidadePeriodo.Nenhum, 0, true),
        new("De acordo com proced", UnidadePeriodo.Nenhum, 0, true),
        new("Sempre que necessári", UnidadePeriodo.Nenhum, 0, false)
    ];

    public static Periodicidade? Obter(string? periodo)
    {
        var p = (periodo ?? "").Trim();
        return Lista.FirstOrDefault(x => string.Equals(x.Periodo, p, StringComparison.OrdinalIgnoreCase));
    }

    // Unidade e intervalo gravados na u_periocid: "nenhum" não tem intervalo; "dias" e "meses" pedem de 1 a 9999.
    public static string? ValidarUnidadeEIntervalo(string? unidade, int? intervalo)
    {
        var u = RegrasManutencao.LerUnidade(unidade);
        var texto = (unidade ?? "").Trim().ToLowerInvariant();
        if (texto is not ("dias" or "meses" or "nenhum")) return "A unidade é dias, meses ou nenhum.";
        if (u != UnidadePeriodo.Nenhum && (intervalo is null or <= 0 or > MaxIntervalo)) return $"Indique o intervalo (nº de dias ou de meses, de 1 a {MaxIntervalo}).";
        return null;
    }

    public sealed record Plano(string Periodo, string Meses, int Dia, DateTime DataInicio, DateTime UltimaData, bool Ativo, bool EquipamentoObsoleto);

    public sealed record ResultadoGeracao(List<DateTime> Criar, List<DateTime> JaExistentes, List<DateTime> Saltadas, DateTime? ProximaData, string? Motivo)
    {
        public static ResultadoGeracao Nada(string motivo) => new([], [], [], null, motivo);
    }

    // 12 caracteres JAN..DEZ, '1' = marcado.
    public static bool[] LerMeses(string? meses)
    {
        var r = new bool[12];
        var m = meses ?? "";
        for (var i = 0; i < 12 && i < m.Length; i++) r[i] = m[i] == '1';
        return r;
    }

    private static DateTime DiaNoMes(int ano, int mes, int dia) => new(ano, mes, Math.Clamp(dia, 1, DateTime.DaysInMonth(ano, mes)));

    private static bool Vazia(DateTime d) => d.Year <= 1900;

    // Datas do plano, por ordem, a partir da base (exclusive) até ao limite (inclusive).
    public static IEnumerable<DateTime> Ocorrencias(Plano plano, DateTime limite)
    {
        var per = Obter(plano.Periodo);
        if (per is null || !per.GeraPlaneadas) yield break;

        var meses = LerMeses(plano.Meses);
        var temMeses = meses.Any(x => x);
        var dia = plano.Dia <= 0 ? 1 : plano.Dia;
        limite = limite.Date;

        var nunca = Vazia(plano.UltimaData);
        var baseData = (nunca ? plano.DataInicio : plano.UltimaData).Date;
        if (Vazia(baseData)) yield break;

        if (per.Unidade == UnidadePeriodo.Dias)
        {
            var d = nunca ? baseData : baseData.AddDays(per.Intervalo);
            while (d <= limite)
            {
                if (!temMeses || meses[d.Month - 1]) yield return d;
                d = d.AddDays(per.Intervalo);
            }
            yield break;
        }

        if (temMeses)
        {
            var cursor = new DateTime(baseData.Year, baseData.Month, 1);
            for (var guarda = 0; guarda < 1200; guarda++)
            {
                if (meses[cursor.Month - 1])
                {
                    var d = DiaNoMes(cursor.Year, cursor.Month, dia);
                    if (d > limite) yield break;
                    if (d > baseData || (nunca && d == baseData)) yield return d;
                }
                cursor = cursor.AddMonths(1);
                if (cursor > limite) yield break;
            }
            yield break;
        }

        if (per.Unidade == UnidadePeriodo.Nenhum) yield break;

        var n = nunca ? 0 : 1;
        for (var guarda = 0; guarda < 1200; guarda++, n++)
        {
            var m = new DateTime(baseData.Year, baseData.Month, 1).AddMonths(n * per.Intervalo);
            var d = DiaNoMes(m.Year, m.Month, dia);
            if (nunca && d < baseData) continue;
            if (d > limite) yield break;
            yield return d;
        }
    }

    public static DateTime? ProximaData(Plano plano)
    {
        foreach (var d in Ocorrencias(plano, DateTime.MaxValue.Date.AddYears(-1))) return d;
        return null;
    }

    // Das datas passadas fica só a(s) mais recente(s) (maxAtrasadas); as outras ficam "saltadas"; datas que já têm ficha não se repetem.
    public static ResultadoGeracao Gerar(Plano plano, DateTime hoje, int horizonteDias, int maxAtrasadas, int maiorAntecedencia, IEnumerable<DateTime> jaExistentes)
    {
        if (!plano.Ativo) return ResultadoGeracao.Nada("Plano inativo.");
        if (plano.EquipamentoObsoleto) return ResultadoGeracao.Nada("Equipamento obsoleto.");
        var per = Obter(plano.Periodo);
        if (per is null) return ResultadoGeracao.Nada($"Periodicidade \"{plano.Periodo}\" fora da lista.");
        if (!per.GeraPlaneadas) return ResultadoGeracao.Nada("Esta periodicidade nunca gera intervenções planeadas.");

        hoje = hoje.Date;
        if (Vazia(plano.UltimaData) && Vazia(plano.DataInicio)) return ResultadoGeracao.Nada("Plano sem data de início nem última data.");
        var horizonte = Math.Clamp(Math.Max(horizonteDias, maiorAntecedencia), 0, MaxHorizonteDias);
        var limite = hoje.AddDays(horizonte);
        var existentes = new HashSet<DateTime>(jaExistentes.Select(d => d.Date));

        var todas = Ocorrencias(plano, limite).ToList();
        var passadas = todas.Where(d => d < hoje).ToList();
        var manterPassadas = passadas.Skip(Math.Max(0, passadas.Count - Math.Max(0, maxAtrasadas))).ToList();
        var saltadas = passadas.Except(manterPassadas).ToList();

        var candidatas = manterPassadas.Concat(todas.Where(d => d >= hoje)).ToList();
        var criar = candidatas.Where(d => !existentes.Contains(d)).ToList();
        var ja = candidatas.Where(existentes.Contains).ToList();

        var futuras = todas.Where(d => d >= hoje).ToList();
        DateTime? proxima = futuras.Count > 0 ? futuras[0] : ProximaDepois(plano, limite);
        return new ResultadoGeracao(criar, ja, saltadas, proxima, null);
    }

    private static DateTime? ProximaDepois(Plano plano, DateTime limite)
    {
        foreach (var d in Ocorrencias(plano, limite.AddYears(10)))
            if (d > limite) return d;
        return null;
    }
}
