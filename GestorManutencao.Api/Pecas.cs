using System.Security.Claims;

namespace GestorManutencao.Api;

// Peça = artigo st marcado em stobs.u_manut. Stock e local são os do armazém de manutenção (sa).
public sealed record PecaDto(string Ref, string Design, string Codigo, string Local, decimal Stock, decimal StMin, decimal Preco)
{
    public bool AbaixoDoMinimo => StMin > 0 && Stock <= StMin;
}

// CampoExiste = false: falta criar stobs.u_manut no PHC (o ecrã diz o que falta). HaMais: pedir ao utilizador para escrever mais.
public sealed record ResultadoPecasDto(bool CampoExiste, IReadOnlyList<PecaDto> Pecas, bool HaMais);

public sealed record EntradaPecaDto(string Id, string Ref, string Design, decimal Qtt, string Quem, string Hora);

public sealed record PedidoEntrada(string? Ref, decimal? Qtt);

// Repetir a saída de um material cuja ligação à peça não ficou gravada (a saída falhou); sem corpo vale a ref já ligada.
public sealed record PedidoSaida(string? Ref);

public static class RegrasPecas
{
    public const int NdosSaida = 63, NdosEntrada = 64, Armazem = 104;
    public const string CampoStobs = "u_manut";
    public const decimal QuantidadeMaxima = 999_999m;

    public static string[] Palavras(string? texto)
        => (texto ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4).ToArray();

    public static string EscaparLike(string palavra)
        => palavra.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    public static string? AvisoStock(decimal pedida, decimal stock)
    {
        if (pedida <= 0) return null;
        if (stock <= 0) return "Sem stock desta peça.";
        return pedida > stock ? $"Stock insuficiente: há {stock:0.###} e pede {pedida:0.###}." : null;
    }

    // st.ref tem 18 caracteres no PHC; o movimento grava a quantidade com 3 casas.
    public const int TamanhoRef = 18;

    // Os repositórios devolvem estes textos e o endpoint decide o estado HTTP por eles.
    public const string ErroLinhaInexistente = "A linha já não existe.";
    public const string ErroOcupado = "Outro movimento em curso, tente outra vez.";

    public static bool ReferenciaValida(string? r) => !string.IsNullOrWhiteSpace(r) && r.Trim().Length <= TamanhoRef;

    public static bool QuantidadeValida(decimal? q) => q is > 0 and < QuantidadeMaxima + 1 && decimal.Round(q.Value, 3) == q.Value;
}

public sealed record ResultadoMovimento(bool Ok, string? Id, string? Erro)
{
    public static ResultadoMovimento Sucesso(string id) => new(true, id, null);
    public static ResultadoMovimento Falha(string erro) => new(false, null, erro);
}

public interface IRepositorioPecas
{
    Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct);
    Task<PecaDto?> ObterPorCodigoAsync(string codigo, CancellationToken ct);

    // Entrada: dossier 64 do dia (um por dia), linha por peça.
    Task<ResultadoMovimento> DarEntradaAsync(string refPeca, decimal qtt, string iniciais, CancellationToken ct);
    Task<IReadOnlyList<EntradaPecaDto>> EntradasDeHojeAsync(CancellationToken ct);
    Task<ResultadoMovimento> AnularEntradaAsync(string id, CancellationToken ct);

    // Saída: dossier 63 da ficha, linha por material (bi.lobs = materialId). Grava também u_matinterv.ref.
    Task<ResultadoMovimento> DarSaidaAsync(string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct);
    Task<ResultadoMovimento> AnularSaidaAsync(string materialId, CancellationToken ct);

    // Junta a Ref ligada e se a saída existe (SaidaDada) aos materiais lidos da ficha.
    Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct);
}

public static class EndpointsPecas
{
    public static void MapPecas(this WebApplication app)
    {
        var g = app.MapGroup("/pecas").RequireAuthorization();
        g.AddEndpointFilter(async (ctx, next) =>
        {
            try { return await next(ctx); }
            catch (RepositorioIndisponivelException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        });

        g.MapGet("", (string? texto, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioPecas repo, CancellationToken ct) =>
            EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (_, _) =>
                Results.Ok(await repo.ListarAsync(texto, 60, ct))));

        g.MapGet("/codigo/{codigo}", (string codigo, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioPecas repo, CancellationToken ct) =>
            EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (_, _) =>
                await repo.ObterPorCodigoAsync(codigo, ct) is { } p ? Results.Ok(p) : Results.NotFound()));

        g.MapGet("/entradas/hoje", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioPecas repo, CancellationToken ct) =>
            EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (_, _) =>
                Results.Ok(await repo.EntradasDeHojeAsync(ct))));

        g.MapPost("/entradas", (PedidoEntrada p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioPecas repo, CancellationToken ct) =>
            !RegrasPecas.ReferenciaValida(p.Ref) || !RegrasPecas.QuantidadeValida(p.Qtt)
                ? Task.FromResult(EndpointsIntervencoes.PedidoInvalido("Indique uma referência (até 18 caracteres) e uma quantidade positiva, com no máximo 3 casas decimais."))
                : EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, _) =>
                {
                    var r = await repo.DarEntradaAsync(p.Ref!.Trim(), p.Qtt!.Value, t.Codigo, ct);
                    if (!r.Ok) return r.Erro == RegrasPecas.ErroOcupado
                        ? Results.Json(new { erro = r.Erro }, statusCode: StatusCodes.Status409Conflict)
                        : EndpointsIntervencoes.PedidoInvalido(r.Erro ?? "Não foi possível gravar o movimento.");
                    var linha = (await repo.EntradasDeHojeAsync(ct)).FirstOrDefault(e => e.Id == r.Id)
                        ?? new EntradaPecaDto(r.Id!, p.Ref.Trim(), "", p.Qtt.Value, t.Codigo, DateTime.Now.ToString("HH:mm:ss"));
                    return Results.Ok(linha);
                }));

        g.MapDelete("/entradas/{id}", (string id, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioPecas repo, CancellationToken ct) =>
            EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (_, _) =>
            {
                var r = await repo.AnularEntradaAsync(id, ct);
                if (r.Ok) return Results.NoContent();
                return r.Erro == RegrasPecas.ErroLinhaInexistente
                    ? Results.NotFound()
                    : Results.Json(new { erro = r.Erro }, statusCode: StatusCodes.Status409Conflict);
            }));
    }
}
