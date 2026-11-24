using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Sem rede, para o modo Dev e testes: um inventário por contar com as mesmas regras da API.
public sealed class InventarioDevService : IInventarioService
{
    private const string Id = "inv-dev-1";
    private readonly object _trinco = new();
    private readonly List<LinhaInventario> _linhas = [];
    private bool _concluido;
    private readonly List<(string Ref, string Design)> _pecas = [("P001", "Parafuso M8"), ("P002", "Rolamento 6204")];

    private InventarioDetalhe Montar() => new(
        new Inventario(Id, 1, DateOnly.FromDateTime(DateTime.Today), "Inventário de teste", 101, "Ana Silva", _linhas.Count, _concluido, false, !_concluido), _linhas.ToList());

    public Task<IReadOnlyList<Inventario>> ListarAsync(CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<IReadOnlyList<Inventario>>([Montar().Inventario]);
    }

    public Task<InventarioDetalhe?> ObterAsync(string id, CancellationToken ct = default)
    {
        lock (_trinco) return Task.FromResult<InventarioDetalhe?>(id == Id ? Montar() : null);
    }

    private void Aberto(string id)
    {
        if (id != Id) throw new InvalidOperationException("O inventário ou a linha já não existe.");
        if (_concluido) throw new InvalidOperationException("Este inventário já foi concluído: já não se pode alterar.");
    }

    public Task<InventarioDetalhe> ContarAsync(string id, string refPeca, string local, decimal qtt, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            Aberto(id);
            if (!LogicaInventario.LugarCompleto(local)) throw new InvalidOperationException(LogicaInventario.ErroLugar);
            var peca = _pecas.FirstOrDefault(p => p.Ref.Equals(refPeca, StringComparison.OrdinalIgnoreCase));
            if (peca.Ref is null) throw new InvalidOperationException("Peça não encontrada.");
            var lugar = LogicaFichaPeca.TextoLugar(local);
            var i = _linhas.FindIndex(l => l.Ref == peca.Ref && l.Lugar == lugar);
            if (i >= 0) _linhas[i] = _linhas[i] with { Qtt = _linhas[i].Qtt + qtt };
            else _linhas.Add(new LinhaInventario(Guid.NewGuid().ToString("N")[..8], peca.Ref, peca.Design, lugar, qtt));
            return Task.FromResult(Montar());
        }
    }

    public Task<InventarioDetalhe> AlterarQuantidadeAsync(string id, string linhaId, decimal qtt, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            Aberto(id);
            var i = _linhas.FindIndex(l => l.Id == linhaId);
            if (i < 0) throw new InvalidOperationException("O inventário ou a linha já não existe.");
            _linhas[i] = _linhas[i] with { Qtt = qtt };
            return Task.FromResult(Montar());
        }
    }

    public Task<InventarioDetalhe> ApagarLinhaAsync(string id, string linhaId, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            Aberto(id);
            if (_linhas.RemoveAll(l => l.Id == linhaId) == 0) throw new InvalidOperationException("O inventário ou a linha já não existe.");
            return Task.FromResult(Montar());
        }
    }

    public Task<InventarioDetalhe> ConcluirAsync(string id, CancellationToken ct = default)
    {
        lock (_trinco)
        {
            Aberto(id);
            if (_linhas.Count == 0) throw new InvalidOperationException("Conte pelo menos uma peça antes de concluir o inventário.");
            _concluido = true;
            return Task.FromResult(Montar());
        }
    }
}
