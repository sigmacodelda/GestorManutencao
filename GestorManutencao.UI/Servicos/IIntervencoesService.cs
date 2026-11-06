using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

public interface IIntervencoesService
{
    // Intervenções atribuídas ao utilizador autenticado. Lança NotSupportedException se a fonte ainda não existe.
    Task<IReadOnlyList<Intervencao>> ListarMinhasAsync(CancellationToken ct = default);
}

// Só para desenvolvimento: dados fictícios relativos a hoje, até a ligação ao PHC estar confirmada.
public sealed class DevIntervencoesService : IIntervencoesService
{
    public async Task<IReadOnlyList<Intervencao>> ListarMinhasAsync(CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        return
        [
            new("T1", "Compressor de ar 02", "Produção", TipoIntervencao.Preventiva, hoje.AddDays(-3), EstadoIntervencao.Planeada),
            new("T2", "Empilhador 05", "Armazém", TipoIntervencao.Curativa, hoje, EstadoIntervencao.EmCurso),
            new("T3", "Câmara de frio 01", "Frio", TipoIntervencao.Preventiva, hoje, EstadoIntervencao.Planeada),
            new("T4", "Linha de embalagem", "Produção", TipoIntervencao.Preventiva, hoje.AddDays(4), EstadoIntervencao.Planeada),
            new("T5", "Balança 03", "Expedição", TipoIntervencao.Preventiva, hoje.AddDays(-10), EstadoIntervencao.Concluida)
        ];
    }
}

// Quando não há fonte de dados configurada (Release sem ligação ao PHC).
public sealed class IntervencoesIndisponiveis : IIntervencoesService
{
    public Task<IReadOnlyList<Intervencao>> ListarMinhasAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();
}
