namespace GestorManutencao.Api;

// Modo Development e testes: mesmas regras do SQL, sem base. Stock inicial menos as saídas.
public sealed class RepositorioPecasMemoria(IEnumerable<PecaDto>? pecas = null) : IRepositorioPecas
{
    private readonly object _trinco = new();
    private readonly Dictionary<string, PecaDto> _pecas = (pecas ?? Amostra()).ToDictionary(p => p.Ref, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string FichaId, string Ref, decimal Qtt, string? Local)> _saidas = []; // materialId → linha do dossier
    private readonly Dictionary<string, bool> _dossiers = []; // fichaId → em conferência
    private readonly Dictionary<string, string> _ligacoes = []; // materialId → peça por dar (sem saída)

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

    private decimal EmConf(string refPeca, string? excetoFichaId = null) => _saidas.Values
        .Where(s => s.Ref == refPeca && s.FichaId != excetoFichaId && _dossiers[s.FichaId]).Sum(s => s.Qtt);

    private PecaDto Com(PecaDto p) => p with { EmConferencia = EmConf(p.Ref) };

    public Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct)
    {
        var palavras = RegrasPecas.Palavras(texto);
        lock (_trinco)
        {
            var todas = _pecas.Values
                .Where(p => palavras.All(w => p.Ref.Contains(w, StringComparison.OrdinalIgnoreCase)
                    || p.Design.Contains(w, StringComparison.OrdinalIgnoreCase) || p.Codigo.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p.Ref).Select(Com).ToList();
            return Task.FromResult(new ResultadoPecasDto(true, todas.Take(maximo).ToList(), todas.Count > maximo));
        }
    }

    public Task<PecaDto?> ObterPorCodigoAsync(string codigo, CancellationToken ct)
    {
        codigo = codigo.Trim();
        lock (_trinco)
        {
            var p = _pecas.Values.FirstOrDefault(x => x.Codigo == codigo) ?? _pecas.GetValueOrDefault(codigo);
            return Task.FromResult(p is null ? null : Com(p));
        }
    }

    public Task<ResultadoMovimento> AbrirConferenciaAsync(string fichaId, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            _dossiers.TryAdd(fichaId, true);
            return Task.FromResult(ResultadoMovimento.Sucesso(fichaId));
        }
    }

    public Task<ResultadoMovimento> DarSaidaAsync(string fichaId, string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct, string? local = null)
    {
        lock (_trinco)
        {
            if (!_pecas.ContainsKey(refPeca.Trim())) return Task.FromResult(ResultadoMovimento.Falha("Referência inexistente."));
            if (_saidas.ContainsKey(materialId)) return Task.FromResult(ResultadoMovimento.Falha("Já foi dada saída desta peça."));
            if (RegrasPecas.MontarLobs(materialId, local) is null) return Task.FromResult(ResultadoMovimento.Falha("O lugar da saída não cabe em bi.lobs."));
            if (_dossiers.TryGetValue(fichaId, out var emConf) && !emConf) return Task.FromResult(ResultadoMovimento.Falha(RegrasPecas.ErroValidada));
            _dossiers[fichaId] = true;
            _ligacoes.Remove(materialId);
            _saidas[materialId] = (fichaId, _pecas[refPeca.Trim()].Ref, qtt, local);
            return Task.FromResult(ResultadoMovimento.Sucesso(materialId));
        }
    }

    public Task<bool> LigarPecaAsync(string stampMaterial, string refPeca, string usrinis, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (!RegrasPecas.ReferenciaValida(refPeca) || !_pecas.TryGetValue(refPeca.Trim(), out var p)) return Task.FromResult(false);
            _ligacoes[stampMaterial] = p.Ref;
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct)
    {
        lock (_trinco)
            return Task.FromResult<IReadOnlyList<MaterialDto>>(materiais
                .Select(m => _saidas.TryGetValue(m.Id, out var s) ? m with { Ref = s.Ref, SaidaDada = true, Local = s.Local }
                    : _ligacoes.TryGetValue(m.Id, out var l) ? m with { Ref = l, SaidaDada = false } : m).ToList());
    }

    public Task<ResultadoMovimento> AnularSaidaAsync(string materialId, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (_saidas.TryGetValue(materialId, out var s))
            {
                if (!_dossiers[s.FichaId]) return Task.FromResult(ResultadoMovimento.Falha(RegrasPecas.ErroValidada));
                _saidas.Remove(materialId);
            }
            _ligacoes.Remove(materialId);
            return Task.FromResult(ResultadoMovimento.Sucesso(materialId));
        }
    }

    public Task<ResultadoMovimento> ConfirmarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (_dossiers.TryGetValue(fichaId, out var emConf) && emConf)
            {
                _dossiers[fichaId] = false;
                foreach (var s in _saidas.Values.Where(x => x.FichaId == fichaId)) Mexer(s.Ref, -s.Qtt);
            }
            return Task.FromResult(ResultadoMovimento.Sucesso(fichaId));
        }
    }

    public Task<ResultadoMovimento> CancelarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (!_dossiers.TryGetValue(fichaId, out var emConf)) return Task.FromResult(ResultadoMovimento.Sucesso(fichaId));
            if (!emConf) return Task.FromResult(ResultadoMovimento.Falha(RegrasPecas.ErroValidada));
            foreach (var m in _saidas.Where(x => x.Value.FichaId == fichaId).Select(x => x.Key).ToList()) { _saidas.Remove(m); _ligacoes.Remove(m); }
            _dossiers.Remove(fichaId);
            return Task.FromResult(ResultadoMovimento.Sucesso(fichaId));
        }
    }

    public Task<bool> ConferenciaPendenteAsync(string fichaId, CancellationToken ct)
    {
        lock (_trinco) return Task.FromResult(_dossiers.TryGetValue(fichaId, out var emConf) && emConf);
    }

    public Task<IReadOnlyDictionary<string, decimal>> EmConferenciaPorPecaAsync(IReadOnlyCollection<string> refs, string? excetoFichaId, CancellationToken ct)
    {
        lock (_trinco)
            return Task.FromResult<IReadOnlyDictionary<string, decimal>>(refs
                .Where(r => _pecas.ContainsKey(r.Trim())).Select(r => _pecas[r.Trim()].Ref).Distinct()
                .ToDictionary(r => r, r => EmConf(r, excetoFichaId)));
    }
}
