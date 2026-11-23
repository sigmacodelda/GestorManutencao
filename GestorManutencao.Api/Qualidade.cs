using System.Globalization;

namespace GestorManutencao.Api;

// Etapa do histórico da ficha. Quem vem como código do utilizador da app (a API troca-o pelo nome) ou como texto livre (higienização).
public sealed record EtapaDto(string Etapa, string? Quem, DateTime? Quando, string? Texto = null);

public sealed record PedidoConclusaoQualidade(string? Higienizacao);

// Última fase do circuito: Supervisor valida (Validada, fechada = 0) e a Qualidade conclui (fechada = 1, limrem = 1).
// Campos novos de u_fichaint: higienizadopor, higienizadata, qualinis, qualdata, histdevol (ver docs/circuito-qualidade.md).
public static class RegrasQualidade
{
    public const int MaxHigienizacao = 60;
    public const string EtapaExecucao = "Execução", EtapaValidacao = "Validação", EtapaHigienizacao = "Higienização", EtapaConclusao = "Conclusão", EtapaDevolucao = "Devolvida";

    public static string? ValidarHigienizacao(string? nome) =>
        string.IsNullOrWhiteSpace(nome) || nome.Trim().Length > MaxHigienizacao ? "Indique quem realizou a higienização." : null;

    // Validada ainda não fechada = a aguardar a Qualidade.
    public static bool AguardaQualidade(string? estado, bool fechada) => EstadosFicha.Normalizar(estado) == EstadosFicha.Validada && !fechada;

    // Cada devolução é uma linha "yyyy-MM-dd HH:mm|código|motivo"; o motivo perde quebras de linha e '|'.
    public static string AcrescentarDevolucao(string? historico, DateTime quando, string codigo, string motivo)
    {
        var limpo = motivo.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        var linha = quando.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "|" + codigo + "|" + limpo;
        return string.IsNullOrEmpty(historico) ? linha : historico.TrimEnd() + "\n" + linha;
    }

    public static IEnumerable<EtapaDto> LerDevolucoes(string? historico)
    {
        foreach (var linha in (historico ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = linha.Split('|', 3);
            if (p.Length == 3 && DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var q))
                yield return new EtapaDto(EtapaDevolucao, p[1], q, p[2]);
        }
    }

    public static IReadOnlyList<EtapaDto> Montar(string? tecnome, DateTime? fimExecucao, string? validou, DateTime? validouEm, string? historicoDevolucoes,
        string? higienizadoPor, DateTime? higienizadoEm, string? concluiuPor, DateTime? concluiuEm)
    {
        var lista = new List<EtapaDto>();
        if (!string.IsNullOrWhiteSpace(tecnome)) lista.Add(new(EtapaExecucao, tecnome.Trim(), fimExecucao));
        lista.AddRange(LerDevolucoes(historicoDevolucoes));
        if (!string.IsNullOrWhiteSpace(validou)) lista.Add(new(EtapaValidacao, validou.Trim(), validouEm));
        if (!string.IsNullOrWhiteSpace(higienizadoPor)) lista.Add(new(EtapaHigienizacao, higienizadoPor.Trim(), higienizadoEm));
        if (!string.IsNullOrWhiteSpace(concluiuPor)) lista.Add(new(EtapaConclusao, concluiuPor.Trim(), concluiuEm));
        return lista.OrderBy(e => e.Quando ?? DateTime.MaxValue).ToList();
    }
}
