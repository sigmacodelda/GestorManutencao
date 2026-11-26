namespace GestorManutencao.Api;

// Um dossier 66 (arrumação): uma linha bi com a peça, a origem (bi.lobs) e o destino (bi.lobs2).
public sealed record MovimentoArrumacao(int Ndos, string Ref, string Origem, string Destino, decimal Qtt, string Iniciais);

// Modo Development e testes: mesmas regras do SQL (vista v_sc_pecaloc_disponivel e trigger sc_bi_pecaloc), sem base. O stock vem da peça do repositório de peças.
// O saldo de um lugar = o que lá entrou menos as saídas 63 já validadas (emconf 0); as saídas em conferência (emconf 1) só contam como reservado.
// Como no trigger, apagar uma saída em conferência não repõe nada: nunca saiu.
public sealed class RepositorioLocalizacoesMemoria(IRepositorioPecas pecas) : IRepositorioLocalizacoes
{
    private readonly object _trinco = new();
    // (ref, lugar) -> o que lá entrou; a amostra casa com a peça P001 (stock 10, 3 arrumadas).
    private readonly List<(string Ref, LugarDto Lugar)> _lugares = [("P001", new LugarDto("A", "A-02", 1, 1, 3))];

    private LugarDto? _buffer;
    private readonly List<MovimentoArrumacao> _movimentos = [];

    // Dossiers 66 criados; como no trigger, o movimento também ajusta os lugares em memória.
    public IReadOnlyList<MovimentoArrumacao> Movimentos { get { lock (_trinco) return _movimentos.ToList(); } }

    // Sem buffer por defeito (como u_configman.localentrada vazio); os testes definem-no.
    public void DefinirBuffer(string? texto)
    {
        lock (_trinco) _buffer = RegrasLocalizacao.LerBuffer(texto);
    }

    // Soma ao lugar o que o trigger somaria (entrada 64, arrumação 66): só para os testes e a amostra prepararem saldos.
    public void Semear(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt) => Somar(refPeca, corredor, estante, nivel, posicao, qtt);

    private sealed record Linha(string Ref, LugarDto Base, decimal Saldo, decimal Reservado)
    {
        public decimal Disponivel => Math.Max(0, Saldo - Reservado);
        // O trigger apaga as linhas que ficam a 0; uma linha criada a 0 ("não indicada") continua visível.
        public bool Visivel => Saldo > 0 || Reservado > 0 || Base.Qtt == 0;
        public LugarDto ComoLugar() => Base with { Qtt = Disponivel, Reservado = Reservado };
    }

    private IEnumerable<Linha> Linhas(string? soRef = null)
    {
        var saidas = new Dictionary<string, IReadOnlyList<SaidaNoLugar>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (r, lugar) in _lugares.ToList())
        {
            if (soRef is not null && !Igual(r, soRef)) continue;
            if (!saidas.TryGetValue(r.Trim(), out var s)) saidas[r.Trim()] = s = (pecas as ISaidasPorLugar)?.SaidasComLugar(r) ?? [];
            var doLugar = s.Where(x => Igual(x.Local, lugar.Texto)).ToList();
            var validado = doLugar.Where(x => !x.EmConferencia).Sum(x => x.Qtt);
            var reservado = doLugar.Where(x => x.EmConferencia).Sum(x => x.Qtt);
            yield return new Linha(r, lugar, lugar.Qtt - validado, reservado);
        }
    }

    public async Task<LocalizacoesPecaDto> ListarAsync(string refPeca, CancellationToken ct)
    {
        var stock = (await pecas.ObterPorCodigoAsync(refPeca, ct))?.Stock ?? 0;
        lock (_trinco)
        {
            var linhas = Linhas(refPeca).Where(x => x.Visivel && !RegrasLocalizacao.EBuffer(x.Base, _buffer)).ToList();
            var lugares = linhas.Select(x => x.ComoLugar())
                .OrderByDescending(l => l.Qtt).ThenBy(l => l.Estante).ThenBy(l => l.Nivel).ThenBy(l => l.Posicao).ToList();
            // O saldo físico de cada lugar: o reservado pode exceder-lo, mas o localizado nunca.
            var localizado = linhas.Sum(x => Math.Max(0, x.Saldo));
            return new LocalizacoesPecaDto(refPeca, stock, localizado, RegrasLocalizacao.Calcular(stock, localizado), lugares);
        }
    }

    public Task<ResultadoMovimento> ArrumarAsync(string refPeca, string corredor, int estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct)
    {
        var estanteNormal = RegrasLocalizacao.NormalizarEstante(corredor, estante);
        lock (_trinco)
        {
            if (RegrasLocalizacao.EBuffer(new LugarDto(corredor.Trim().ToUpperInvariant(), estanteNormal, nivel, posicao, 0), _buffer))
                return Task.FromResult(ResultadoMovimento.Falha("Escolha um lugar diferente do de entrada (buffer)."));
            var r = Somar(refPeca, corredor, estanteNormal, nivel, posicao, qtt);
            _movimentos.Add(new(RegrasLocalizacao.NdosArrumacao, refPeca.Trim(), RegrasLocalizacao.TextoLugar(_buffer),
                RegrasLocalizacao.TextoLugar(new LugarDto(corredor.Trim().ToUpperInvariant(), estanteNormal, nivel, posicao, 0)), qtt, iniciais));
            // Tira do buffer o que passou (nunca abaixo de 0; linha esvaziada apaga-se).
            var b = _lugares.FindIndex(x => Igual(x.Ref, refPeca) && RegrasLocalizacao.EBuffer(x.Lugar, _buffer));
            if (b >= 0)
            {
                var resto = _lugares[b].Lugar.Qtt - qtt;
                if (resto > 0) _lugares[b] = (_lugares[b].Ref, _lugares[b].Lugar with { Qtt = resto });
                else _lugares.RemoveAt(b);
            }
            return Task.FromResult(r);
        }
    }

    public Task<ResultadoMovimento> MoverAsync(string refPeca, LugarDto origem, LugarDto destino, decimal qtt, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            if (RegrasLocalizacao.EBuffer(destino, _buffer)) return Task.FromResult(ResultadoMovimento.Falha(RegrasLocalizacao.ErroMoverParaBuffer));
            if (RegrasLocalizacao.EBuffer(origem, _buffer)) return Task.FromResult(ResultadoMovimento.Falha(RegrasLocalizacao.ErroMoverDoBuffer));
            var linha = Linhas(refPeca).FirstOrDefault(x => x.Visivel && Corresponde((x.Ref, x.Base), refPeca, origem.Corredor, origem.Estante, origem.Nivel, origem.Posicao));
            if (linha is null) return Task.FromResult(ResultadoMovimento.Falha(RegrasLocalizacao.ErroMoverSemLinha));
            if (linha.Disponivel <= 0 && (linha.Saldo > 0 || linha.Reservado > 0)) return Task.FromResult(ResultadoMovimento.Falha(RegrasLocalizacao.ErroMoverReservado));
            // Como o trigger: um movimento sem quantidade (origem "não indicada") não move nada, por isso nem se regista.
            if (RegrasLocalizacao.ErroQuantidadeMover(linha.Disponivel, qtt) is { } erro) return Task.FromResult(ResultadoMovimento.Falha(erro));
            Somar(refPeca, destino.Corredor, destino.Estante, destino.Nivel, destino.Posicao, qtt);
            _movimentos.Add(new(RegrasLocalizacao.NdosArrumacao, refPeca.Trim(), RegrasLocalizacao.TextoLugar(origem), RegrasLocalizacao.TextoLugar(destino), qtt, iniciais));
            var i = _lugares.FindIndex(x => Corresponde(x, refPeca, origem.Corredor, origem.Estante, origem.Nivel, origem.Posicao));
            var resto = _lugares[i].Lugar.Qtt - qtt;
            if (resto <= 0) _lugares.RemoveAt(i);
            else _lugares[i] = (_lugares[i].Ref, _lugares[i].Lugar with { Qtt = resto });
            return Task.FromResult(ResultadoMovimento.Sucesso(Guid.NewGuid().ToString("N")[..25]));
        }
    }

    public Task<string> VerificarTabelaAsync(CancellationToken ct) => Task.FromResult("");

    public Task<IReadOnlyList<(string Ref, decimal Qtt)>> ListarPecasDoLugarAsync(string corredor, string estante, int nivel, int posicao, CancellationToken ct)
    {
        lock (_trinco)
            return Task.FromResult<IReadOnlyList<(string Ref, decimal Qtt)>>(Linhas()
                .Where(x => x.Disponivel > 0 && Igual(x.Base.Corredor, corredor) && Igual(x.Base.Estante, estante) && x.Base.Nivel == nivel && x.Base.Posicao == posicao)
                .OrderByDescending(x => x.Disponivel).ThenBy(x => x.Ref).Select(x => (x.Ref, x.Disponivel)).ToList());
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
