using System.Security.Claims;
using System.Text.RegularExpressions;

namespace GestorManutencao.Api;

// Lugares de uma peça no armazém de manutenção (u_pecaloc). Estante guardada normalizada (A-03).
public sealed record LugarDto(string Corredor, string Estante, int Nivel, int Posicao, decimal Qtt)
{
    public string Texto => $"{Estante}-{Nivel}-{Posicao:00}";
}

public sealed record LocalizacoesPecaDto(string Ref, decimal Stock, decimal Localizado, decimal PorArrumar, IReadOnlyList<LugarDto> Lugares);

// Local = texto da etiqueta (LOC-A-02-1-01) ou o texto do lugar (A-02-1-01).
public sealed record PedidoArrumar(string? Ref, string? Local, decimal? Qtt);

public static class RegrasLocalizacao
{
    private const string Prefixo = "LOC";

    // Qualquer coisa que não seja letra ou número separa (leitores com o teclado noutra língua escrevem apóstrofos em vez de hífenes).
    private static string[] Partes(string? texto) =>
        Regex.Split((texto ?? "").Trim().ToUpperInvariant(), @"[^\p{L}\p{N}]+").Where(x => x.Length > 0).ToArray();

    public static (string Corredor, int Estante, int Nivel, int Posicao)? LerEtiqueta(string? texto)
    {
        var p = Partes(texto);
        if (p.Length != 5 || p[0] != Prefixo) return null;
        return LerLocal(texto) is { Posicao: { } pos } l ? (l.Corredor, l.Estante, l.Nivel, pos) : null;
    }

    // LOC-A-03-2(-04) ou A-03-2(-04); a posição é opcional.
    public static (string Corredor, int Estante, int Nivel, int? Posicao)? LerLocal(string? texto)
    {
        var p = Partes(texto);
        if (p.Length > 0 && p[0] == Prefixo) p = p[1..];
        if (p.Length is not (3 or 4)) return null;
        // Limites provisórios até conferir os tamanhos reais das colunas de u_pecaloc na Task 7.
        if (p[0].Length > 3) return null;
        if (!int.TryParse(p[1], out var estante) || !int.TryParse(p[2], out var nivel) || estante is <= 0 or > 999 || nivel is <= 0 or > 99) return null;
        int? posicao = null;
        if (p.Length == 4)
        {
            if (!int.TryParse(p[3], out var po) || po is <= 0 or > 99) return null;
            posicao = po;
        }
        return (p[0], estante, nivel, posicao);
    }

    public static string NormalizarEstante(string corredor, int estante) => $"{corredor.Trim().ToUpperInvariant()}-{estante:00}";

    public static decimal Calcular(decimal stock, decimal localizado) => Math.Max(0, stock - localizado);
}

public interface IRepositorioLocalizacoes
{
    // Lugares por qtt desc; PorArrumar = max(0, stock do armazém 104 - localizado).
    Task<LocalizacoesPecaDto> ListarAsync(string refPeca, CancellationToken ct);
    // Soma ao lugar ou cria-o.
    Task<ResultadoMovimento> ArrumarAsync(string refPeca, string corredor, int estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct);
    // Ok mesmo que o lugar fique a 0; Falha se o lugar não tem quantidade suficiente.
    Task<ResultadoMovimento> DescontarAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct);
    // Soma de volta (anulação da saída), sem validar stock.
    Task<ResultadoMovimento> ReporAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct);
    // "" se a tabela está pronta, senão o que falta.
    Task<string> VerificarTabelaAsync(CancellationToken ct);
}

public static class EndpointsLocalizacoes
{
    public static void MapLocalizacoes(this WebApplication app)
    {
        var g = app.MapGroup("/pecas").RequireAuthorization();
        g.AddEndpointFilter(async (ctx, next) =>
        {
            try { return await next(ctx); }
            catch (RepositorioIndisponivelException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        });

        g.MapGet("/{refPeca}/localizacoes", (string refPeca, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioPecas pecas, IRepositorioLocalizacoes repo, CancellationToken ct) =>
            !RegrasPecas.ReferenciaValida(refPeca)
                ? Task.FromResult(Results.NotFound())
                : EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (_, _) =>
                    await pecas.ObterPorCodigoAsync(refPeca.Trim(), ct) is { } p
                        ? Results.Ok(await repo.ListarAsync(p.Ref, ct))
                        : Results.NotFound()));

        g.MapPost("/localizacoes", (PedidoArrumar p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioPecas pecas, IRepositorioLocalizacoes repo, CancellationToken ct) =>
        {
            var local = RegrasLocalizacao.LerLocal(p.Local);
            if (!RegrasPecas.ReferenciaValida(p.Ref) || local is not { Posicao: { } posicao } || !RegrasPecas.QuantidadeValida(p.Qtt))
                return Task.FromResult(EndpointsIntervencoes.PedidoInvalido("Indique a peça, o local completo (corredor, estante, nível e posição) e uma quantidade positiva, com no máximo 3 casas decimais."));
            return EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, _) =>
            {
                if (await pecas.ObterPorCodigoAsync(p.Ref!.Trim(), ct) is not { } peca) return Results.NotFound();
                var falta = await repo.VerificarTabelaAsync(ct);
                if (falta != "") return Results.Json(new { erro = falta }, statusCode: StatusCodes.Status409Conflict);
                var r = await repo.ArrumarAsync(peca.Ref, local.Value.Corredor, local.Value.Estante, local.Value.Nivel, posicao, p.Qtt!.Value, t.Codigo, ct);
                if (!r.Ok) return r.Erro == RegrasPecas.ErroOcupado
                    ? Results.Json(new { erro = r.Erro }, statusCode: StatusCodes.Status409Conflict)
                    : EndpointsIntervencoes.PedidoInvalido(r.Erro ?? "Não foi possível gravar a localização.");
                return Results.Ok(await repo.ListarAsync(peca.Ref, ct));
            });
        });
    }
}
