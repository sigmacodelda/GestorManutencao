using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Só online. Recusas (400/403/409, sem rede) chegam como InvalidOperationException com a mensagem para o utilizador.
// Cada escrita devolve o inventário já atualizado.
public interface IInventarioService
{
    Task<IReadOnlyList<Inventario>> ListarAsync(CancellationToken ct = default);
    // null se já não existe.
    Task<InventarioDetalhe?> ObterAsync(string id, CancellationToken ct = default);
    // local = etiqueta (LOC-A-03-2-04) ou código do lugar; soma à linha da mesma peça e lugar.
    Task<InventarioDetalhe> ContarAsync(string id, string refPeca, string local, decimal qtt, CancellationToken ct = default);
    Task<InventarioDetalhe> AlterarQuantidadeAsync(string id, string linhaId, decimal qtt, CancellationToken ct = default);
    Task<InventarioDetalhe> ApagarLinhaAsync(string id, string linhaId, CancellationToken ct = default);
    Task<InventarioDetalhe> ConcluirAsync(string id, CancellationToken ct = default);
}
