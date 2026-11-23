namespace GestorManutencao.Api;

// Modo Development e testes: mesmas regras do SQL, sem base. O stock vem da peça do repositório de peças.
public sealed class RepositorioLocalizacoesMemoria(IRepositorioPecas pecas) : IRepositorioLocalizacoes
{
    private readonly object _trinco = new();
    // (ref, estante, nivel, posicao) -> lugar; a amostra casa com a peça P001 (stock 10, 3 arrumadas).
    private readonly List<(string Ref, LugarDto Lugar)> _lugares = [("P001", new LugarDto("A", "A-02", 1, 1, 3))];

    public async Task<LocalizacoesPecaDto> ListarAsync(string refPeca, CancellationToken ct)
    {
        var stock = (await pecas.ObterPorCodigoAsync(refPeca, ct))?.Stock ?? 0;
        lock (_trinco)
        {
            var lugares = _lugares.Where(x => Igual(x.Ref, refPeca)).Select(x => x.Lugar)
                .OrderByDescending(l => l.Qtt).ThenBy(l => l.Estante).ThenBy(l => l.Nivel).ThenBy(l => l.Posicao).ToList();
            var localizado = lugares.Sum(l => l.Qtt);
            return new LocalizacoesPecaDto(refPeca, stock, localizado, RegrasLocalizacao.Calcular(stock, localizado), lugares);
        }
    }

    public Task<ResultadoMovimento> ArrumarAsync(string refPeca, string corredor, int estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct)
        => Task.FromResult(Somar(refPeca, corredor, RegrasLocalizacao.NormalizarEstante(corredor, estante), nivel, posicao, qtt));

    public Task<ResultadoMovimento> ReporAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct)
        => Task.FromResult(Somar(refPeca, corredor, estante, nivel, posicao, qtt));

    public Task<ResultadoMovimento> DescontarAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            var i = _lugares.FindIndex(x => Corresponde(x, refPeca, corredor, estante, nivel, posicao));
            if (i < 0 ||_lugares[i].Lugar.Qtt < qtt) return Task.FromResult(ResultadoMovimento.Falha("Não há quantidade suficiente neste lugar."));
            _lugares[i] = (_lugares[i].Ref, _lugares[i].Lugar with { Qtt = _lugares[i].Lugar.Qtt - qtt });
            return Task.FromResult(ResultadoMovimento.Sucesso(Guid.NewGuid().ToString("N")[..25]));
        }
    }

    public Task<string> VerificarTabelaAsync(CancellationToken ct) => Task.FromResult("");

    public Task<IReadOnlyList<(string Ref, decimal Qtt)>> ListarPecasDoLugarAsync(string corredor, string estante, int nivel, int posicao, CancellationToken ct)
    {
        lock (_trinco)
            return Task.FromResult<IReadOnlyList<(string Ref, decimal Qtt)>>(_lugares
                .Where(x => x.Lugar.Qtt > 0 && Igual(x.Lugar.Corredor, corredor) && Igual(x.Lugar.Estante, estante) && x.Lugar.Nivel == nivel && x.Lugar.Posicao == posicao)
                .OrderByDescending(x => x.Lugar.Qtt).ThenBy(x => x.Ref).Select(x => (x.Ref, x.Lugar.Qtt)).ToList());
    }

    private ResultadoMovimento Somar(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt)
    {
        lock (_trinco)
        {
            var i = _lugares.FindIndex(x => Corresponde(x, refPeca, corredor, estante, nivel, posicao));
            if (i >= 0) _lugares[i] = (_lugares[i].Ref, _lugares[i].Lugar with { Qtt = _lugares[i].Lugar.Qtt + qtt });
            else _lugares.Add((refPeca, new LugarDto(corredor.Trim().ToUpperInvariant(), estante.Trim().ToUpperInvariant(), nivel, posicao, qtt)));
            return ResultadoMovimento.Sucesso(Guid.NewGuid().ToString("N")[..25]);
        }
    }

    private static bool Igual(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Corresponde((string Ref, LugarDto Lugar) x, string refPeca, string corredor, string estante, int nivel, int posicao) =>
        Igual(x.Ref, refPeca) && Igual(x.Lugar.Corredor, corredor) && Igual(x.Lugar.Estante, estante) && x.Lugar.Nivel == nivel && x.Lugar.Posicao == posicao;
}
