using System.Security.Claims;

namespace GestorManutencao.Api;

// Rotas de gestão: política Supervisor mais confirmação na base de que o utilizador continua ativo e supervisor.
public static partial class EndpointsIntervencoes
{
    public static void MapGestao(this WebApplication app)
    {
        var g = app.MapGroup("/gestao").RequireAuthorization("Supervisor");
        g.AddEndpointFilter(async (ctx, next) =>
        {
            if (ctx.HttpContext.Request.RouteValues.TryGetValue("id", out var id) && id?.ToString()?.Length > 25) return Results.NotFound();
            try { return await next(ctx); }
            catch (RepositorioIndisponivelException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        });

        g.MapGet("/opcoes", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => Results.Ok(await repo.ObterOpcoesAsync(ct))));

        g.MapGet("/equipamentos/{id}", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => await repo.ObterEquipamentoGestaoAsync(id, ct) is { } e ? Results.Ok(e) : Results.NotFound()));

        g.MapPost("/equipamentos", (PedidoEquipamento p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            RegrasGestao.ValidarEquipamento(p, true) is { } erro
                ? Task.FromResult(PedidoInvalido(erro))
                : ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.GravarEquipamentoAsync(null, p, s, ct))));

        g.MapPut("/equipamentos/{id}", (string id, PedidoEquipamento p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            RegrasGestao.ValidarEquipamento(p, false) is { } erro
                ? Task.FromResult(PedidoInvalido(erro))
                : ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.GravarEquipamentoAsync(id, p, s, ct))));

        g.MapGet("/equipamentos/{id}/planeadas-a-cancelar", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => await repo.ContarPlaneadasEquipamentoAsync(id, ct) is { } n ? Results.Ok(new PlaneadasACancelarDto(n)) : Results.NotFound()));

        g.MapGet("/equipamentos/{id}/historico-estado", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => await repo.ListarHistoricoEstadoAsync(id, ct) is { } l ? Results.Ok(l) : Results.NotFound()));

        g.MapGet("/equipamentos/{id}/instrucoes", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => await repo.ListarInstrucoesAsync(id, ct) is { } l ? Results.Ok(l) : Results.NotFound()));

        g.MapPost("/equipamentos/{id}/instrucoes", (string id, PedidoInstrucao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            RegrasGestao.ValidarInstrucao(p) is { } erro
                ? Task.FromResult(PedidoInvalido(erro))
                : ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.GravarInstrucaoAsync(id, null, p, s, ct))));

        g.MapPut("/instrucoes/{id}", (string id, PedidoInstrucao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            RegrasGestao.ValidarInstrucao(p) is { } erro
                ? Task.FromResult(PedidoInvalido(erro))
                : ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.GravarInstrucaoAsync(null, id, p, s, ct))));

        g.MapGet("/instrucoes/{id}/tarefas", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => await repo.ListarTarefasAsync(id, ct) is { } l ? Results.Ok(l) : Results.NotFound()));

        g.MapPost("/instrucoes/{id}/tarefas", (string id, PedidoTarefa p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            RegrasGestao.ValidarTarefa(p, true) is { } erro
                ? Task.FromResult(PedidoInvalido(erro))
                : ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.GravarTarefaAsync(id, null, p, s, ct))));

        g.MapPut("/tarefas/{id}", (string id, PedidoTarefa p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            RegrasGestao.ValidarTarefa(p, false) is { } erro
                ? Task.FromResult(PedidoInvalido(erro))
                : ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.GravarTarefaAsync(null, id, p, s, ct))));

        g.MapDelete("/tarefas/{id}", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async s => await repo.ApagarTarefaAsync(id, s, ct) is var r && r == ResultadoIntervencao.Ok
                ? Results.NoContent()
                : Resposta<object>((r, null))));

        g.MapGet("/periodicidades", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => Results.Ok(await repo.ListarPeriodicidadesAsync(ct))));

        g.MapPut("/periodicidades/{id}", (string id, PedidoPeriodicidade p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioGestao repo, CancellationToken ct) =>
            RegrasGestao.ValidarPeriodicidade(p) is { } erro
                ? Task.FromResult(PedidoInvalido(erro))
                : ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.GravarPeriodicidadeAsync(id, p, s, ct))));
    }
}
