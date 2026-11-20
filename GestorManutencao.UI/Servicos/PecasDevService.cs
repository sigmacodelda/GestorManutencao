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
    private readonly List<EntradaPeca> _entradas = [];

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

    public Task<IReadOnlyList<EntradaPeca>> EntradasDeHojeAsync(CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<EntradaPeca>>(Enumerable.Reverse(_entradas).ToList());
    }

    public Task<EntradaPeca> DarEntradaAsync(string refPeca, decimal qtt, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            var i = _pecas.FindIndex(p => p.Ref.Equals(refPeca.Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0) throw new InvalidOperationException("Referência inexistente.");
            if (qtt <= 0) throw new InvalidOperationException("Indique uma quantidade positiva.");
            var p = _pecas[i];
            _pecas[i] = p with { Stock = p.Stock + qtt };
            var e = new EntradaPeca(Guid.NewGuid().ToString("N")[..25], p.Ref, p.Design, qtt, "DEV", DateTime.Now.ToString("HH:mm:ss"));
            _entradas.Add(e);
            return Task.FromResult(e);
        }
    }

    public Task AnularEntradaAsync(string id, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            var e = _entradas.FirstOrDefault(x => x.Id == id);
            if (e is null) return Task.CompletedTask;
            var i = _pecas.FindIndex(p => p.Ref == e.Ref);
            if (_pecas[i].Stock < e.Qtt) throw new InvalidOperationException("Já saiu material desta peça: não dá para anular a entrada.");
            _pecas[i] = _pecas[i] with { Stock = _pecas[i].Stock - e.Qtt };
            _entradas.Remove(e);
            return Task.CompletedTask;
        }
    }
}
