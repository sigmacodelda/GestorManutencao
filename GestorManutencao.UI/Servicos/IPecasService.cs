using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Só online. Recusas (400/409, sem rede) chegam como InvalidOperationException com a mensagem para o utilizador.
public interface IPecasService
{
    Task<ResultadoPecas> PesquisarAsync(string texto, CancellationToken ct = default);
    Task<Peca?> ObterPorCodigoAsync(string codigo, CancellationToken ct = default);
    // null se a peça não existe.
    Task<LocalizacoesPeca?> LugaresAsync(string refPeca, CancellationToken ct = default);
    // Peças com quantidade no lugar (etiqueta LOC-A-02-1-01 ou A-02-1-01).
    Task<IReadOnlyList<PecaNoLugar>> PecasNoLugarAsync(string local, CancellationToken ct = default);
    // local = texto da etiqueta (LOC-A-02-1-01); devolve os lugares já atualizados.
    Task<LocalizacoesPeca> ArrumarAsync(string refPeca, string local, decimal qtt, CancellationToken ct = default);
    // Rearrumar: muda qtt do lugar de origem (texto A-01-1-01) para o destino (etiqueta ou texto); devolve os lugares já atualizados.
    Task<LocalizacoesPeca> MoverAsync(string refPeca, string origem, string destino, decimal qtt, CancellationToken ct = default);
    // Estantes onde se pode arrumar (sem o buffer), para escolher corredor, estante, nível e posição.
    Task<IReadOnlyList<EstanteArmazem>> EstruturaArmazemAsync(CancellationToken ct = default);
}
