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

    public static bool QuantidadeValida(decimal? q) => q is > 0 and < QuantidadeMaxima + 1;
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
}

public static class EndpointsPecas
{
    public static void MapPecas(this WebApplication app)
    {
        var g = app.MapGroup("/pecas").RequireAuthorization();

        g.MapGet("", async (string? texto, IRepositorioPecas repo, CancellationToken ct) =>
            Results.Ok(await repo.ListarAsync(texto, 60, ct)));

        g.MapGet("/codigo/{codigo}", async (string codigo, IRepositorioPecas repo, CancellationToken ct) =>
            await repo.ObterPorCodigoAsync(codigo, ct) is { } p ? Results.Ok(p) : Results.NotFound());

        g.MapGet("/entradas/hoje", async (IRepositorioPecas repo, CancellationToken ct) =>
            Results.Ok(await repo.EntradasDeHojeAsync(ct)));

        g.MapPost("/entradas", (PedidoEntrada p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioPecas repo, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(p.Ref) || !RegrasPecas.QuantidadeValida(p.Qtt)
                ? Task.FromResult(Results.BadRequest())
                : EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, _) =>
                {
                    var r = await repo.DarEntradaAsync(p.Ref.Trim(), p.Qtt!.Value, t.Codigo, ct);
                    if (!r.Ok) return Results.BadRequest(new { erro = r.Erro });
                    var linha = (await repo.EntradasDeHojeAsync(ct)).First(e => e.Id == r.Id);
                    return Results.Ok(linha);
                }));

        g.MapDelete("/entradas/{id}", async (string id, IRepositorioPecas repo, CancellationToken ct) =>
            (await repo.AnularEntradaAsync(id, ct)).Ok ? Results.NoContent() : Results.NotFound());
    }
}
