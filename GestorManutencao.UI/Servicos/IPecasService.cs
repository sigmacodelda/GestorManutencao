using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Só online. Recusas (400/409, sem rede) chegam como InvalidOperationException com a mensagem para o utilizador.
public interface IPecasService
{
    Task<ResultadoPecas> PesquisarAsync(string texto, CancellationToken ct = default);
    Task<Peca?> ObterPorCodigoAsync(string codigo, CancellationToken ct = default);
}
