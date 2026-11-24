namespace GestorManutencao.Api;

// Modo Development e testes: mesmas regras do SQL, sem base. O desktop cria os dossiers; aqui Criar faz esse papel nos testes.
public sealed class RepositorioInventariosMemoria(IRepositorioPecas pecas, IEnumerable<(int Tecno, string Nome)>? tecnicos = null) : IRepositorioInventarios
{
    private sealed record Lin(string Id, string Ref, string Design, string Lugar, decimal Qtt);

    private sealed class Inv
    {
        public required string Id { get; init; }
        public required decimal Numero { get; init; }
        public required DateOnly Data { get; init; }
        public required string Descricao { get; init; }
        public required int Tecno { get; init; }
        public bool Concluido { get; set; }
        public bool Lancado { get; set; }
        public List<Lin> Linhas { get; } = [];
    }

    private readonly object _trinco = new();
    private readonly List<Inv> _inventarios = [];
    private readonly Dictionary<int, string> _nomes = (tecnicos ?? []).ToDictionary(t => t.Tecno, t => t.Nome);
    private decimal _proximo = 1;

    // Equivale a o desktop criar o dossier 65 e atribuí-lo ao técnico.
    public string Criar(int tecno, string descricao, bool concluido = false, bool lancado = false, DateOnly? data = null)
    {
        lock (_trinco)
        {
            var i = new Inv { Id = Guid.NewGuid().ToString("N")[..25], Numero = _proximo++, Data = data ?? DateOnly.FromDateTime(DateTime.Today), Descricao = descricao, Tecno = tecno, Concluido = concluido, Lancado = lancado };
            _inventarios.Add(i);
            return i.Id;
        }
    }

    private InventarioDto Dto(Inv i, int tecno) => new(i.Id, i.Numero, i.Data, i.Descricao, i.Tecno, _nomes.GetValueOrDefault(i.Tecno, ""), i.Linhas.Count, i.Concluido, i.Lancado,
        i.Tecno == tecno && !i.Concluido && !i.Lancado);

    private InventarioDetalheDto Detalhe(Inv i, int tecno) => new(Dto(i, tecno),
        i.Linhas.Select(l => new LinhaInventarioDto(l.Id, l.Ref, l.Design, l.Lugar, l.Qtt)).ToList());

    public Task<IReadOnlyList<InventarioDto>> ListarAsync(int tecno, bool supervisor, CancellationToken ct)
    {
        var limite = DateOnly.FromDateTime(DateTime.Today.AddDays(-RegrasInventario.DiasConcluidosVisiveis));
        lock (_trinco)
            return Task.FromResult<IReadOnlyList<InventarioDto>>(_inventarios
                .Where(i => (supervisor || i.Tecno == tecno) && (!i.Concluido && !i.Lancado || i.Data >= limite))
                .OrderByDescending(i => i.Data).ThenByDescending(i => i.Numero).Select(i => Dto(i, tecno)).ToList());
    }

    public Task<InventarioDetalheDto?> ObterAsync(string id, int tecno, CancellationToken ct)
    {
        lock (_trinco) return Task.FromResult(_inventarios.FirstOrDefault(i => i.Id == id) is { } i ? Detalhe(i, tecno) : null);
    }

    // Dono e estado: a mesma ordem de verificações do SQL.
    private ResultadoInventario? Recusa(Inv? i, int tecno) =>
        i is null ? ResultadoInventario.De(EstadoInventario.NaoExiste)
        : i.Tecno != tecno ? ResultadoInventario.De(EstadoInventario.OutroTecnico, RegrasInventario.ErroOutroTecnico)
        : i.Concluido || i.Lancado ? ResultadoInventario.De(EstadoInventario.Fechado, RegrasInventario.ErroFechado)
        : null;

    public async Task<ResultadoInventario> GravarLinhaAsync(string id, int tecno, string refPeca, string local, decimal qtt, string iniciais, CancellationToken ct)
    {
        var peca = await pecas.ObterPorCodigoAsync(refPeca, ct);
        lock (_trinco)
        {
            var i = _inventarios.FirstOrDefault(x => x.Id == id);
            if (Recusa(i, tecno) is { } r) return r;
            if (peca is null) return ResultadoInventario.De(EstadoInventario.PecaInexistente, "Peça não encontrada.");
            var pos = i!.Linhas.FindIndex(l => string.Equals(l.Ref, peca.Ref, StringComparison.OrdinalIgnoreCase) && l.Lugar == local);
            if (pos >= 0)
            {
                var soma = i.Linhas[pos].Qtt + qtt;
                if (soma > RegrasInventario.QuantidadeMaxima) return ResultadoInventario.De(EstadoInventario.Invalido, "A quantidade somada é demasiado grande.");
                i.Linhas[pos] = i.Linhas[pos] with { Qtt = soma };
            }
            else i.Linhas.Add(new Lin(Guid.NewGuid().ToString("N")[..25], peca.Ref, peca.Design, local, qtt));
            return ResultadoInventario.Sucesso(Detalhe(i, tecno));
        }
    }

    public Task<ResultadoInventario> AlterarQuantidadeAsync(string id, string linhaId, int tecno, decimal qtt, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            var i = _inventarios.FirstOrDefault(x => x.Id == id);
            if (Recusa(i, tecno) is { } r) return Task.FromResult(r);
            var pos = i!.Linhas.FindIndex(l => l.Id == linhaId);
            if (pos < 0) return Task.FromResult(ResultadoInventario.De(EstadoInventario.LinhaInexistente));
            i.Linhas[pos] = i.Linhas[pos] with { Qtt = qtt };
            return Task.FromResult(ResultadoInventario.Sucesso(Detalhe(i, tecno)));
        }
    }

    public Task<ResultadoInventario> ApagarLinhaAsync(string id, string linhaId, int tecno, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            var i = _inventarios.FirstOrDefault(x => x.Id == id);
            if (Recusa(i, tecno) is { } r) return Task.FromResult(r);
            if (i!.Linhas.RemoveAll(l => l.Id == linhaId) == 0) return Task.FromResult(ResultadoInventario.De(EstadoInventario.LinhaInexistente));
            return Task.FromResult(ResultadoInventario.Sucesso(Detalhe(i, tecno)));
        }
    }

    public Task<ResultadoInventario> ConcluirAsync(string id, int tecno, string iniciais, CancellationToken ct)
    {
        lock (_trinco)
        {
            var i = _inventarios.FirstOrDefault(x => x.Id == id);
            if (Recusa(i, tecno) is { } r) return Task.FromResult(r);
            if (i!.Linhas.Count == 0) return Task.FromResult(ResultadoInventario.De(EstadoInventario.Invalido, RegrasInventario.ErroSemLinhas));
            i.Concluido = true;
            return Task.FromResult(ResultadoInventario.Sucesso(Detalhe(i, tecno)));
        }
    }
}
