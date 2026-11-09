using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.UI.Offline;

// O que o servidor oferece à app: o contrato normal mais o que só o modo offline precisa.
public interface IIntervencoesRemoto : IIntervencoesService, IEquipamentosService
{
    // Fichas do técnico concluídas, validadas ou canceladas nos últimos dias (1 a 90).
    Task<IReadOnlyList<Intervencao>> ListarRecentesAsync(int dias, CancellationToken ct = default);

    // Repetir o mesmo opId devolve a mesma ficha; com opId o texto tem no máximo RegrasIntervencao.MaxAnomaliaComOpId letras.
    Task<Intervencao> RegistarAvariaAsync(string equipamentoId, string anomalia, string opId, CancellationToken ct = default);
}
