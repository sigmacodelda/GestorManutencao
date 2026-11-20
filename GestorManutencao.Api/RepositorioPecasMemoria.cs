namespace GestorManutencao.Api;

// Modo Development e testes: mesmas regras do SQL, sem base. Stock = soma das entradas − saídas.
public sealed class RepositorioPecasMemoria(IEnumerable<PecaDto>? pecas = null) : IRepositorioPecas
{
    private readonly object _trinco = new();
    private readonly Dictionary<string, PecaDto> _pecas = (pecas ?? Amostra()).ToDictionary(p => p.Ref, StringComparer.OrdinalIgnoreCase);
    private readonly List<EntradaPecaDto> _entradas = [];
    private readonly Dictionary<string, (string Ref, decimal Qtt)> _saidas = []; // materialId → saída

    private static IEnumerable<PecaDto> Amostra() =>
    [
        new("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
        new("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
    ];

    private void Mexer(string refPeca, decimal delta)
    {
        var p = _pecas[refPeca];
        _pecas[refPeca] = p with { Stock = p.Stock + delta };
    }

    public Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct)
    {
        var palavras = RegrasPecas.Palavras(texto);
        lock (_trinco)
        {
            var todas = _pecas.Values
                .Where(p => palavras.All(w => p.Ref.Contains(w, StringComparison.OrdinalIgnoreCase)
                    || p.Design.Contains(w, StringComparison.OrdinalIgnoreCase) || p.Codigo.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p.Ref).ToList();
            return Task.FromResult(new ResultadoPecasDto(true, todas.Take(maximo).ToList(), todas.Count > maximo));
        }
    }

    public Task<PecaDto?> ObterPorCodigoAsync(string codigo, CancellationToken ct)
    {
        codigo = codigo.Trim();
        lock (_trinco)
            return Task.FromResult(_pecas.Values.FirstOrDefault(p => p.Codigo == codigo) ?? _pecas.GetValueOrDefault(codigo));
    }

    public Task<ResultadoMovimento> DarEntradaAsync(string refPeca, decimal qtt, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (!_pecas.TryGetValue(refPeca.Trim(), out var p)) return Task.FromResult(ResultadoMovimento.Falha("Referência inexistente."));
            var e = new EntradaPecaDto(Guid.NewGuid().ToString("N")[..25], p.Ref, p.Design, qtt, iniciais, DateTime.Now.ToString("HH:mm:ss"));
            _entradas.Add(e);
            Mexer(p.Ref, qtt);
            return Task.FromResult(ResultadoMovimento.Sucesso(e.Id));
        }
    }

    public Task<IReadOnlyList<EntradaPecaDto>> EntradasDeHojeAsync(CancellationToken ct)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<EntradaPecaDto>>(_entradas.AsEnumerable().Reverse().ToList());
    }

    public Task<ResultadoMovimento> AnularEntradaAsync(string id, CancellationToken ct)
    {
        lock (_trinco)
        {
            var e = _entradas.FirstOrDefault(x => x.Id == id);
            if (e is null) return Task.FromResult(ResultadoMovimento.Falha(RegrasPecas.ErroLinhaInexistente));
            _entradas.Remove(e);
            Mexer(e.Ref, -e.Qtt);
            return Task.FromResult(ResultadoMovimento.Sucesso(id));
        }
    }

    public Task<ResultadoMovimento> DarSaidaAsync(string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (!_pecas.ContainsKey(refPeca.Trim())) return Task.FromResult(ResultadoMovimento.Falha("Referência inexistente."));
            if (_saidas.ContainsKey(materialId)) return Task.FromResult(ResultadoMovimento.Falha("Já foi dada saída desta peça."));
            _saidas[materialId] = (refPeca.Trim(), qtt);
            Mexer(refPeca.Trim(), -qtt);
            return Task.FromResult(ResultadoMovimento.Sucesso(materialId));
        }
    }

    public Task<ResultadoMovimento> AnularSaidaAsync(string materialId, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (!_saidas.Remove(materialId, out var s)) return Task.FromResult(ResultadoMovimento.Sucesso(materialId));
            Mexer(s.Ref, s.Qtt);
            return Task.FromResult(ResultadoMovimento.Sucesso(materialId));
        }
    }
}
