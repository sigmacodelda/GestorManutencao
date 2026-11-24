using System.Security.Claims;

namespace GestorManutencao.Api;

// Inventário do armazém de peças = dossier PHC ndos 65, criado pelo desktop (LandeiroMaui) e atribuído a um técnico em bo.maquina.
// A app só conta: grava linhas (bi.ref, bi.qtt, bi.lobs3 = lugar) e conclui (bo.logi1 = 1). Nunca cria o dossier, nunca lança (bo.logi2)
// e nunca mexe em stock nem em u_pecaloc: o lançamento é do desktop.
// Sem preços: o técnico só vê peça, lugar e quantidade.
public sealed record InventarioDto(string Id, decimal Numero, DateOnly Data, string Descricao, int Tecno, string Tecnico, int Linhas, bool Concluido, bool Lancado, bool PodeEditar);

public sealed record LinhaInventarioDto(string Id, string Ref, string Design, string Lugar, decimal Qtt);

public sealed record InventarioDetalheDto(InventarioDto Inventario, IReadOnlyList<LinhaInventarioDto> Linhas);

public sealed record PedidoLinhaInventario(string? Ref, string? Local, decimal? Qtt);

public sealed record PedidoQuantidadeInventario(decimal? Qtt);

public enum EstadoInventario { Ok, NaoExiste, OutroTecnico, Fechado, LinhaInexistente, PecaInexistente, Invalido }

public sealed record ResultadoInventario(EstadoInventario Estado, InventarioDetalheDto? Detalhe = null, string? Erro = null)
{
    public static ResultadoInventario Sucesso(InventarioDetalheDto d) => new(EstadoInventario.Ok, d);
    public static ResultadoInventario De(EstadoInventario estado, string? erro = null) => new(estado, null, erro);
}

public static class RegrasInventario
{
    public const int Ndos = 65;
    public const decimal QuantidadeMaxima = 999_999m;
    // Os concluídos/lançados só se mostram durante este tempo (o lançamento faz-se depois no PHC; depois a lista fica limpa).
    public const int DiasConcluidosVisiveis = 60;

    public const string ErroFechado = "Este inventário já foi concluído: já não se pode alterar.";
    public const string ErroLugar = "O lugar tem de ter corredor, estante, nível e posição (por exemplo A-03-2-04).";
    public const string ErroQuantidade = "Indique a quantidade contada (zero ou mais, com no máximo 3 casas decimais).";
    public const string ErroSemLinhas = "Conte pelo menos uma peça antes de concluir o inventário.";
    public const string ErroOutroTecnico = "Este inventário pertence a outro técnico.";

    // Contar zero é válido: o desktop trata "lugar contado a zero" como lugar vazio.
    public static bool QuantidadeValida(decimal? q) => q is >= 0 and <= QuantidadeMaxima && decimal.Round(q.Value, 3) == q.Value;

    // Texto normalizado do lugar (A-03-2-04) a partir da etiqueta (LOC-A-03-2-04) ou do código; null se faltar corredor, estante, nível ou posição
    // (o PHC ignora essas linhas ao lançar).
    public static string? LugarNormalizado(string? texto) => RegrasLocalizacao.LerLugar(texto)?.Texto;

    public static bool IdValido(string? id) => !string.IsNullOrWhiteSpace(id) && id.Trim().Length <= 25;
}

public interface IRepositorioInventarios
{
    // Do técnico (bo.maquina = tecno), por concluir sempre e concluídos dos últimos dias; o supervisor vê os de todos (só leitura nos dos outros).
    Task<IReadOnlyList<InventarioDto>> ListarAsync(int tecno, bool supervisor, CancellationToken ct);
    // null se o dossier 65 não existe; quem chama decide se o utilizador o pode ver.
    Task<InventarioDetalheDto?> ObterAsync(string id, int tecno, CancellationToken ct);
    // Soma à linha da mesma peça e lugar (nunca duplica). local já normalizado (A-03-2-04).
    Task<ResultadoInventario> GravarLinhaAsync(string id, int tecno, string refPeca, string local, decimal qtt, string iniciais, CancellationToken ct);
    Task<ResultadoInventario> AlterarQuantidadeAsync(string id, string linhaId, int tecno, decimal qtt, string iniciais, CancellationToken ct);
    Task<ResultadoInventario> ApagarLinhaAsync(string id, string linhaId, int tecno, string iniciais, CancellationToken ct);
    Task<ResultadoInventario> ConcluirAsync(string id, int tecno, string iniciais, CancellationToken ct);
}

public static class EndpointsInventarios
{
    public static void MapInventarios(this WebApplication app)
    {
        var g = app.MapGroup("/inventarios").RequireAuthorization();
        g.AddEndpointFilter(async (ctx, next) =>
        {
            try { return await next(ctx); }
            catch (RepositorioIndisponivelException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        });

        g.MapGet("", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioInventarios repo, CancellationToken ct) =>
            EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, sup) =>
                Results.Ok(await repo.ListarAsync(t.Tecno, sup, ct))));

        g.MapGet("/{id}", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioInventarios repo, CancellationToken ct) =>
            !RegrasInventario.IdValido(id)
                ? Task.FromResult(Results.NotFound())
                : EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, sup) =>
                {
                    if (await repo.ObterAsync(id.Trim(), t.Tecno, ct) is not { } d) return Results.NotFound();
                    return sup || d.Inventario.Tecno == t.Tecno ? Results.Ok(d) : Results.Forbid();
                }));

        g.MapPost("/{id}/linhas", (string id, PedidoLinhaInventario p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioInventarios repo, IRepositorioPecas pecas, CancellationToken ct) =>
            !RegrasInventario.IdValido(id)
                ? Task.FromResult(Results.NotFound())
                : EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, _) =>
                {
                    if (t.Tecno <= 0) return Results.Forbid();
                    if (!RegrasPecas.ReferenciaValida(p.Ref)) return EndpointsIntervencoes.PedidoInvalido("Indique a peça.");
                    if (RegrasInventario.LugarNormalizado(p.Local) is not { } lugar) return EndpointsIntervencoes.PedidoInvalido(RegrasInventario.ErroLugar);
                    if (!RegrasInventario.QuantidadeValida(p.Qtt)) return EndpointsIntervencoes.PedidoInvalido(RegrasInventario.ErroQuantidade);
                    // Verifica o acesso antes de ir à peça: quem não é o dono nunca chega a gravar nada.
                    if (await repo.ObterAsync(id.Trim(), t.Tecno, ct) is not { } d) return Results.NotFound();
                    if (d.Inventario.Tecno != t.Tecno) return Results.Forbid();
                    if (await pecas.ObterPorCodigoAsync(p.Ref!.Trim(), ct) is not { } peca)
                        return EndpointsIntervencoes.PedidoInvalido("Peça não encontrada.");
                    return Resposta(await repo.GravarLinhaAsync(id.Trim(), t.Tecno, peca.Ref, lugar, p.Qtt!.Value, t.Codigo, ct));
                }));

        g.MapPut("/{id}/linhas/{linhaId}", (string id, string linhaId, PedidoQuantidadeInventario p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioInventarios repo, CancellationToken ct) =>
            !RegrasInventario.IdValido(id) || !RegrasInventario.IdValido(linhaId)
                ? Task.FromResult(Results.NotFound())
                : !RegrasInventario.QuantidadeValida(p.Qtt)
                    ? Task.FromResult(EndpointsIntervencoes.PedidoInvalido(RegrasInventario.ErroQuantidade))
                    : EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, _) =>
                        t.Tecno <= 0 ? Results.Forbid() : Resposta(await repo.AlterarQuantidadeAsync(id.Trim(), linhaId.Trim(), t.Tecno, p.Qtt!.Value, t.Codigo, ct))));

        g.MapDelete("/{id}/linhas/{linhaId}", (string id, string linhaId, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioInventarios repo, CancellationToken ct) =>
            !RegrasInventario.IdValido(id) || !RegrasInventario.IdValido(linhaId)
                ? Task.FromResult(Results.NotFound())
                : EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, _) =>
                    t.Tecno <= 0 ? Results.Forbid() : Resposta(await repo.ApagarLinhaAsync(id.Trim(), linhaId.Trim(), t.Tecno, t.Codigo, ct))));

        g.MapPost("/{id}/concluir", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioInventarios repo, CancellationToken ct) =>
            !RegrasInventario.IdValido(id)
                ? Task.FromResult(Results.NotFound())
                : EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, _) =>
                    t.Tecno <= 0 ? Results.Forbid() : Resposta(await repo.ConcluirAsync(id.Trim(), t.Tecno, t.Codigo, ct))));
    }

    private static IResult Resposta(ResultadoInventario r) => r.Estado switch
    {
        EstadoInventario.Ok => Results.Ok(r.Detalhe),
        EstadoInventario.NaoExiste or EstadoInventario.LinhaInexistente => Results.NotFound(),
        EstadoInventario.OutroTecnico => Results.Forbid(),
        EstadoInventario.Fechado => Results.Json(new { erro = r.Erro ?? RegrasInventario.ErroFechado }, statusCode: StatusCodes.Status409Conflict),
        _ => EndpointsIntervencoes.PedidoInvalido(r.Erro ?? "Não foi possível gravar o inventário.")
    };
}
