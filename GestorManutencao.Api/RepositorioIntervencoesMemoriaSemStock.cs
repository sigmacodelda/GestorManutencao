namespace GestorManutencao.Api;

public sealed partial class RepositorioIntervencoesMemoria
{
    // Sem necessidades definidas não há peças em falta; os testes definem-nas por ficha e o stock por peça (armazém da manutenção).
    private readonly Dictionary<string, List<(string Ref, string Design, decimal Qtt)>> _necessidades = [];
    private readonly Dictionary<string, decimal> _stockPecas = new(StringComparer.OrdinalIgnoreCase);

    public void DefinirNecessidades(string id, params (string Ref, string Design, decimal Qtt)[] pecas)
    {
        lock (Trinco()) _necessidades[id] = [.. pecas];
    }

    public void DefinirStock(string refPeca, decimal stock)
    {
        lock (Trinco()) _stockPecas[refPeca] = stock;
    }

    public Task<IReadOnlyList<string>> PecasSemStockAsync(string id, CancellationToken ct)
    {
        lock (Trinco()) return Task.FromResult<IReadOnlyList<string>>(SemStock(id));
    }

    private List<string> SemStock(string id)
    {
        var precisa = new Dictionary<string, (string Design, decimal Qtt)>(StringComparer.OrdinalIgnoreCase);
        if (_necessidades.TryGetValue(id, out var lista))
            foreach (var (r, d, q) in lista) RegrasSemStock.Somar(precisa, r, d, q);
        return RegrasSemStock.EmFalta(precisa, r => _stockPecas.GetValueOrDefault(r));
    }
}
