using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Sem rede, para o modo Dev e testes; mesma lógica de stock da API em memória.
public sealed class PecasDevService : IPecasService
{
    private readonly object _trinco = new();
    private readonly List<Peca> _pecas =
    [
        new("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
        new("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
    ];

    public Task<ResultadoPecas> PesquisarAsync(string texto, CancellationToken ct = default)
    {
        var palavras = (texto ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        lock (_trinco)
        {
            var todas = _pecas.Where(p => palavras.All(w => p.Ref.Contains(w, StringComparison.OrdinalIgnoreCase)
                || p.Design.Contains(w, StringComparison.OrdinalIgnoreCase) || p.Codigo.Contains(w, StringComparison.OrdinalIgnoreCase))).OrderBy(p => p.Ref).ToList();
            return Task.FromResult(new ResultadoPecas(true, todas.Take(60).ToList(), todas.Count > 60));
        }
    }

    public Task<Peca?> ObterPorCodigoAsync(string codigo, CancellationToken ct = default)
    {
        codigo = codigo.Trim();
        lock (_trinco)
            return Task.FromResult(_pecas.FirstOrDefault(p => p.Codigo == codigo || p.Ref.Equals(codigo, StringComparison.OrdinalIgnoreCase)));
    }
}
