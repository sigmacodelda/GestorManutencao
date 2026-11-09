using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Offline;

public sealed record FichaEquipamentoGuardada(FichaEquipamento Ficha, DateTimeOffset Aberta);

// Cópia local dos dados do dono atual. Nunca guarda token nem PIN. Os métodos são atómicos individualmente.
public interface ICacheLocal
{
    // Pares chave/valor de apoio (dono, hora da última atualização, remapeamentos de ids locais). Valor null apaga.
    Task<string?> LerMetaAsync(string chave);
    Task GuardarMetaAsync(string chave, string? valor);
    // Apaga tudo, meta incluída.
    Task LimparAsync();

    Task<IReadOnlyList<Intervencao>> ListarFichasAsync();
    Task<Intervencao?> ObterFichaAsync(string id);
    // Insere ou substitui por Id.
    Task GuardarFichasAsync(IEnumerable<Intervencao> fichas);
    // Apaga também os materiais e as fotos das fichas.
    Task RemoverFichasAsync(IEnumerable<string> ids);

    // null quando a lista nunca foi guardada.
    Task<IReadOnlyList<Material>?> LerMateriaisAsync(string fichaId);
    Task GuardarMateriaisAsync(string fichaId, IReadOnlyList<Material> materiais);
    Task<IReadOnlyList<Foto>?> LerFotosAsync(string fichaId);
    Task GuardarFotosAsync(string fichaId, IReadOnlyList<Foto> fotos);
    Task<byte[]?> LerFotoBytesAsync(string fichaId, string fotoId);
    Task GuardarFotoBytesAsync(string fichaId, string fotoId, byte[] bytes);
    // Lista e bytes das fotos da ficha.
    Task RemoverFotosAsync(string fichaId);

    // Lista leve (pesquisa da avaria) e lista completa (ecrã de equipamentos, com obsoletos), guardadas por inteiro.
    Task<IReadOnlyList<Equipamento>?> LerEquipamentosLeveAsync();
    Task GuardarEquipamentosLeveAsync(IReadOnlyList<Equipamento> equipamentos);
    Task<IReadOnlyList<Equipamento>?> LerEquipamentosAsync();
    Task GuardarEquipamentosAsync(IReadOnlyList<Equipamento> equipamentos);

    Task<FichaEquipamentoGuardada?> LerFichaEquipamentoAsync(string id);
    Task GuardarFichaEquipamentoAsync(FichaEquipamentoGuardada ficha);
    Task RemoverFichasEquipamentoAbertasAntesDeAsync(DateTimeOffset limite);
}
