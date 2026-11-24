namespace GestorManutencao.UI.Modelos;

// Estante do armazém (A-03 = corredor A, estante 3): níveis 1..Niveis e posições 1..Posicoes.
public sealed record EstanteArmazem(string Corredor, int Estante, int Niveis, int Posicoes);

// Escolha do lugar em cascata: cada passo só tem opções depois de escolhido o anterior.
public static class LogicaEstrutura
{
    public static IReadOnlyList<string> Corredores(IEnumerable<EstanteArmazem> e) =>
        e.Select(x => x.Corredor).Distinct().Order(StringComparer.Ordinal).ToList();

    public static IReadOnlyList<int> Estantes(IEnumerable<EstanteArmazem> e, string? corredor) =>
        e.Where(x => x.Corredor == corredor).Select(x => x.Estante).Distinct().Order().ToList();

    public static IReadOnlyList<int> Niveis(IEnumerable<EstanteArmazem> e, string? corredor, int? estante) =>
        Intervalo(e, corredor, estante, x => x.Niveis);

    public static IReadOnlyList<int> Posicoes(IEnumerable<EstanteArmazem> e, string? corredor, int? estante) =>
        Intervalo(e, corredor, estante, x => x.Posicoes);

    private static IReadOnlyList<int> Intervalo(IEnumerable<EstanteArmazem> e, string? corredor, int? estante, Func<EstanteArmazem, int> medida) =>
        e.FirstOrDefault(x => x.Corredor == corredor && x.Estante == estante) is { } s ? Enumerable.Range(1, Math.Max(0, medida(s))).ToList() : [];
}
