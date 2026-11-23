using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Só online. Recusas (400/409, sem rede) chegam como InvalidOperationException com a mensagem para o utilizador.
public interface IPecasService
{
    Task<ResultadoPecas> PesquisarAsync(string texto, CancellationToken ct = default);
    Task<Peca?> ObterPorCodigoAsync(string codigo, CancellationToken ct = default);
    // null se a peça não existe.
    Task<LocalizacoesPeca?> LugaresAsync(string refPeca, CancellationToken ct = default);
    // local = texto da etiqueta (LOC-A-02-1-01); devolve os lugares já atualizados.
    Task<LocalizacoesPeca> ArrumarAsync(string refPeca, string local, decimal qtt, CancellationToken ct = default);
}
