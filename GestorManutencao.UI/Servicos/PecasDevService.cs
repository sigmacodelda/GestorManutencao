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

    private readonly List<(string Ref, Lugar Lugar)> _lugares = [("P001", new Lugar("A", "A-01", 1, 1, 4, "A-01-1-01"))];

    public Task<LocalizacoesPeca?> LugaresAsync(string refPeca, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult(Montar(refPeca));
    }

    public Task<IReadOnlyList<PecaNoLugar>> PecasNoLugarAsync(string local, CancellationToken ct = default)
    {
        var texto = LogicaFichaPeca.TextoLugar(local);
        lock (_trinco)
            return Task.FromResult<IReadOnlyList<PecaNoLugar>>(_lugares.Where(x => x.Lugar.Qtt > 0 && x.Lugar.Texto == texto)
                .OrderByDescending(x => x.Lugar.Qtt)
                .Select(x => new PecaNoLugar(x.Ref, _pecas.FirstOrDefault(p => p.Ref == x.Ref)?.Design ?? x.Ref, x.Lugar.Qtt)).ToList());
    }

    // Aceita LOC-A-01-1-01 ou A-01-1-01 (quatro partes).
    public Task<LocalizacoesPeca> ArrumarAsync(string refPeca, string local, decimal qtt, CancellationToken ct = default)
    {
        var p = System.Text.RegularExpressions.Regex.Split((local ?? "").Trim().ToUpperInvariant(), @"[^\p{L}\p{N}]+").Where(x => x.Length > 0).ToList();
        if (p.Count > 0 && p[0] == "LOC") p.RemoveAt(0);
        if (p.Count != 4 || !int.TryParse(p[1], out var estante) || !int.TryParse(p[2], out var nivel) || !int.TryParse(p[3], out var pos) || qtt <= 0)
            throw new InvalidOperationException("Indique a peça, o local completo (corredor, estante, nível e posição) e uma quantidade positiva.");
        lock (_trinco)
        {
            if (!_pecas.Any(x => x.Ref.Equals(refPeca, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Peça não encontrada.");
            if (qtt > Montar(refPeca)!.PorArrumar) throw new InvalidOperationException("Só pode arrumar o que está no buffer.");
            var texto = $"{p[0]}-{estante:00}-{nivel}-{pos:00}";
            var i = _lugares.FindIndex(x => x.Ref.Equals(refPeca, StringComparison.OrdinalIgnoreCase) && x.Lugar.Texto == texto);
            if (i >= 0) _lugares[i] = (_lugares[i].Ref, _lugares[i].Lugar with { Qtt = _lugares[i].Lugar.Qtt + qtt });
            else _lugares.Add((refPeca, new Lugar(p[0], $"{p[0]}-{estante:00}", nivel, pos, qtt, texto)));
            return Task.FromResult(Montar(refPeca)!);
        }
    }

    private LocalizacoesPeca? Montar(string refPeca)
    {
        if (_pecas.FirstOrDefault(x => x.Ref.Equals(refPeca, StringComparison.OrdinalIgnoreCase)) is not { } peca) return null;
        var lugares = _lugares.Where(x => x.Ref == peca.Ref).Select(x => x.Lugar).OrderByDescending(l => l.Qtt).ToList();
        var localizado = lugares.Sum(l => l.Qtt);
        return new LocalizacoesPeca(peca.Ref, peca.Stock, localizado, Math.Max(0, peca.Stock - localizado), lugares);
    }

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
