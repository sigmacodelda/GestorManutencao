using System.Security.Claims;
using System.Text.Json.Serialization;

namespace GestorManutencao.Api;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoIntervencao { Planeada, EmCurso, Concluida, Validada, Cancelada }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoIntervencao { Preventiva, Curativa }

// Tecno é o número de utilizador do PHC de quem tem a ficha (0 = sem dono). PodeValidar só conta na lista de validação.
public sealed record IntervencaoDto(
    string Id, string Equipamento, string Zona, TipoIntervencao Tipo, DateOnly DataPlaneada, EstadoIntervencao Estado,
    string? Anomalia = null, string? Feito = null, TimeOnly? HoraInicio = null, TimeOnly? HoraFim = null,
    string? Tecnico = null, int Tecno = 0, bool PodeValidar = true);

public sealed record EquipamentoDto(string Id, string Nome, string Zona);

public sealed record MaterialDto(string Id, string Design, decimal Qtt, decimal Epv, decimal Custo);

public sealed record PedidoAvaria(string? EquipamentoId, string? Anomalia);

public sealed record PedidoConclusao(string? Feito, TimeOnly? Inicio, TimeOnly? Fim);

public sealed record PedidoMaterial(string? Design, decimal? Qtt, decimal? Epv);

public sealed record PedidoDevolucao(string? Motivo);

public sealed record PedidoAtribuicao(int? Tecno);

public sealed record RelatorioGeracao(int Criadas, int JaTinham, int SemPeriodicidade, int Erros);

public enum ResultadoIntervencao { Ok, Conflito, NaoExiste, ForaDeOrdem, ExigeDocumento, Propria }

// O nome vai em tecnome (45); nas colunas de auditoria vai o código (30); a identidade da pessoa nas fichas é o Tecno.
public sealed record TecnicoAtual(string Codigo, int Tecno, string Nome);

public static class RegrasIntervencao
{
    public const int MaxAnomalia = 200, MaxFeito = 254;

    public static string? ValidarAvaria(string? equipamentoId, string? anomalia) =>
        string.IsNullOrWhiteSpace(equipamentoId) || equipamentoId.Length > 25 ? "Equipamento em falta."
        : string.IsNullOrWhiteSpace(anomalia) || anomalia.Trim().Length > MaxAnomalia ? "Descrição em falta ou demasiado longa."
        : null;

    public static string? ValidarConclusao(string? feito, TimeOnly? inicio, TimeOnly? fim) =>
        string.IsNullOrWhiteSpace(feito) || feito.Trim().Length > MaxFeito ? "Descrição em falta ou demasiado longa."
        : inicio is null || fim is null || fim < inicio ? "Horas inválidas."
        : null;

    public static string? ValidarMaterial(string? design, decimal? qtt, decimal? epv) =>
        string.IsNullOrWhiteSpace(design) || design.Trim().Length > RegrasManutencao.TamanhoDesignMaterial ? "Material em falta ou com nome demasiado longo."
        : qtt is null or <= 0 or > 999_999 ? "Quantidade inválida."
        : epv is < 0 or > 999_999_999 ? "Preço inválido."
        : null;
}

// Tudo é pedido em nome de um técnico (número do PHC); um técnico só mexe no que é seu ou ainda não tem dono.
public interface IRepositorioIntervencoes
{
    Task<IReadOnlyList<IntervencaoDto>> ListarAsync(TecnicoAtual tecnico, CancellationToken ct);
    Task<IntervencaoDto?> ObterAsync(string id, TecnicoAtual tecnico, CancellationToken ct);
    Task<IReadOnlyList<EquipamentoDto>> PesquisarEquipamentosAsync(string texto, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> RegistarAvariaAsync(string equipamentoId, string anomalia, TecnicoAtual tecnico, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> IniciarAsync(string id, TecnicoAtual tecnico, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> ConcluirAsync(string id, TecnicoAtual tecnico, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct);

    // Supervisor: fichas concluídas por validar, validação e devolução (volta a "em curso" com o motivo).
    Task<IReadOnlyList<IntervencaoDto>> ListarPorValidarAsync(TecnicoAtual supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> ValidarAsync(string id, TecnicoAtual supervisor, CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> DevolverAsync(string id, string motivo, TecnicoAtual supervisor, CancellationToken ct);

    // Supervisor: planeadas e em curso de todos, atribuição de técnico (tecno 0 = sem dono) e geração das planeadas.
    Task<IReadOnlyList<IntervencaoDto>> ListarPlaneadasAsync(CancellationToken ct);
    Task<(ResultadoIntervencao, IntervencaoDto?)> AtribuirAsync(string id, int tecno, string nome, TecnicoAtual supervisor, CancellationToken ct);
    Task<RelatorioGeracao> GerarPlaneadasAsync(TecnicoAtual supervisor, CancellationToken ct);

    // Materiais usados (u_matinterv): só se vêem e mexem nas fichas do técnico; só se lançam com a ficha em curso.
    Task<IReadOnlyList<MaterialDto>?> ListarMateriaisAsync(string id, TecnicoAtual tecnico, CancellationToken ct);
    Task<(ResultadoIntervencao, MaterialDto?)> AdicionarMaterialAsync(string id, string design, decimal qtt, decimal epv, TecnicoAtual tecnico, CancellationToken ct);
    Task<ResultadoIntervencao> ApagarMaterialAsync(string id, string materialId, TecnicoAtual tecnico, CancellationToken ct);
}

public static class EndpointsIntervencoes
{
    public static void MapIntervencoes(this WebApplication app)
    {
        var g = app.MapGroup("").RequireAuthorization();
        g.AddEndpointFilter(async (ctx, next) =>
        {
            var rotas = ctx.HttpContext.Request.RouteValues;
            if ((rotas.TryGetValue("id", out var id) && id?.ToString()?.Length > 25)
                || (rotas.TryGetValue("materialId", out var mat) && mat?.ToString()?.Length > 25)) return Results.NotFound();
            try { return await next(ctx); }
            catch (RepositorioIndisponivelException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        });

        g.MapGet("/intervencoes", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => Results.Ok(await repo.ListarAsync(t, ct))));

        g.MapGet("/intervencoes/{id}", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => await repo.ObterAsync(id, t, ct) is { } i ? Results.Ok(i) : Results.NotFound()));

        g.MapGet("/equipamentos", async (string? q, IRepositorioIntervencoes repo, CancellationToken ct) =>
            Results.Ok(string.IsNullOrWhiteSpace(q) || q.Trim().Length is < 2 or > 60 ? [] : await repo.PesquisarEquipamentosAsync(q.Trim(), ct)));

        g.MapPost("/intervencoes/avaria", (PedidoAvaria p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasIntervencao.ValidarAvaria(p.EquipamentoId, p.Anomalia) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.RegistarAvariaAsync(p.EquipamentoId!, p.Anomalia!.Trim(), t, ct))));

        g.MapPost("/intervencoes/{id}/iniciar", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.IniciarAsync(id, t, ct))));

        g.MapPost("/intervencoes/{id}/concluir", (string id, PedidoConclusao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasIntervencao.ValidarConclusao(p.Feito, p.Inicio, p.Fim) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.ConcluirAsync(id, t, p.Feito!.Trim(), p.Inicio!.Value, p.Fim!.Value, ct))));

        g.MapGet("/intervencoes/{id}/materiais", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => await repo.ListarMateriaisAsync(id, t, ct) is { } m ? Results.Ok(m) : Results.NotFound()));

        g.MapPost("/intervencoes/{id}/materiais", (string id, PedidoMaterial p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            RegrasIntervencao.ValidarMaterial(p.Design, p.Qtt, p.Epv) is not null
                ? Task.FromResult(Results.BadRequest())
                : ComoTecnico(eu, utilizadores, ct, async t => Resposta(await repo.AdicionarMaterialAsync(id, p.Design!.Trim(), p.Qtt!.Value, p.Epv ?? 0, t, ct))));

        g.MapDelete("/intervencoes/{id}/materiais/{materialId}", (string id, string materialId, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoTecnico(eu, utilizadores, ct, async t => await repo.ApagarMaterialAsync(id, materialId, t, ct) is var r && r == ResultadoIntervencao.Ok
                ? Results.NoContent()
                : Resposta<object>((r, null))));

        g.MapGet("/validacao", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async s => Results.Ok(await repo.ListarPorValidarAsync(s, ct)))).RequireAuthorization("Supervisor");

        g.MapPost("/validacao/{id}", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.ValidarAsync(id, s, ct)))).RequireAuthorization("Supervisor");

        g.MapPost("/validacao/{id}/devolver", (string id, PedidoDevolucao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(p.Motivo) || p.Motivo.Trim().Length > RegrasManutencao.TamanhoMotivo
                ? Task.FromResult(Results.BadRequest())
                : ComoSupervisor(eu, utilizadores, ct, async s => Resposta(await repo.DevolverAsync(id, p.Motivo.Trim(), s, ct)))).RequireAuthorization("Supervisor");

        g.MapGet("/planeadas", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async _ => Results.Ok(await repo.ListarPlaneadasAsync(ct)))).RequireAuthorization("Supervisor");

        g.MapPost("/planeadas/gerar", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            ComoSupervisor(eu, utilizadores, ct, async s => Results.Ok(await repo.GerarPlaneadasAsync(s, ct)))).RequireAuthorization("Supervisor");

        g.MapPut("/planeadas/{id}/tecnico", (string id, PedidoAtribuicao p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioIntervencoes repo, CancellationToken ct) =>
            p.Tecno is null or < 0 or > 99999
                ? Task.FromResult(Results.BadRequest())
                : ComoSupervisor(eu, utilizadores, ct, async s =>
                {
                    var nome = "";
                    if (p.Tecno > 0)
                    {
                        var alvo = (await utilizadores.ListarAsync(ct)).FirstOrDefault(u => u.Ativo && u.Tecno == p.Tecno);
                        if (alvo is null) return Results.NotFound();
                        nome = Truncar(alvo.Nome);
                    }
                    return Resposta(await repo.AtribuirAsync(id, p.Tecno!.Value, nome, s, ct));
                })).RequireAuthorization("Supervisor");
    }

    private static async Task<UtilizadorApp?> Ativo(ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, CancellationToken ct)
    {
        var codigo = eu.FindFirstValue("sub") ?? eu.FindFirstValue(ClaimTypes.NameIdentifier);
        return codigo is null ? null : await utilizadores.ObterAsync(codigo, ct) is { Ativo: true, PinProvisorio: false } u ? u : null;
    }

    // Sem utilizador ativo não há acesso; sem número de técnico do PHC não há fichas (a app explica e pede ao supervisor).
    private static async Task<IResult> ComoTecnico(ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, CancellationToken ct, Func<TecnicoAtual, Task<IResult>> acao)
    {
        if (await Ativo(eu, utilizadores, ct) is not { } u) return Results.Forbid();
        if (u.Tecno <= 0) return Results.Json(new { erro = "sem_tecno" }, statusCode: StatusCodes.Status403Forbidden);
        return await acao(new TecnicoAtual(u.Codigo, u.Tecno, Truncar(u.Nome)));
    }

    // O token dura um turno; as ações do supervisor confirmam na base que continua ativo e supervisor.
    private static async Task<IResult> ComoSupervisor(ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, CancellationToken ct, Func<TecnicoAtual, Task<IResult>> acao)
    {
        if (await Ativo(eu, utilizadores, ct) is not { Perfil: Perfil.Supervisor } u) return Results.Forbid();
        return await acao(new TecnicoAtual(u.Codigo, u.Tecno, Truncar(u.Nome)));
    }

    // Na ficha, o nome do técnico cabe em 45 caracteres.
    internal static string Truncar(string nome) => nome.Length > 45 ? nome[..45] : nome;

    private static IResult Conflito(string codigo) => Results.Json(new { erro = codigo }, statusCode: StatusCodes.Status409Conflict);

    private static IResult Resposta<T>((ResultadoIntervencao Resultado, T? Dados) r) => r.Resultado switch
    {
        ResultadoIntervencao.Ok => Results.Ok(r.Dados),
        ResultadoIntervencao.NaoExiste => Results.NotFound(),
        ResultadoIntervencao.ForaDeOrdem => Conflito("fora_de_ordem"),
        ResultadoIntervencao.ExigeDocumento => Conflito("externa_sem_documento"),
        ResultadoIntervencao.Propria => Conflito("propria"),
        _ => Conflito("conflito")
    };
}
