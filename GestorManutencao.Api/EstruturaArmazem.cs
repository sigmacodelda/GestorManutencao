using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GestorManutencao.Api;

// Uma estante do armazém: posições 1..Posicoes e níveis 1..Niveis. Estante é o número (A-03 = corredor A, estante 3).
public sealed record EstanteDto(string Corredor, int Estante, int Niveis, int Posicoes);

public static class RegrasEstrutura
{
    // Mesmos limites de RegrasLocalizacao.LerLocal: acima disto a etiqueta não seria aceite.
    private const int MaxEstante = 999, MaxNivelPosicao = 99, MaxCorredor = 3;

    private static readonly Regex IdEstante = new(@"^\s*(\p{L}+)\s*[-_ ]?\s*(\d+)\s*$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Contrato da LandeiroMaui (u_armazemlay.config): só interessam id, colunas e niveis das estantes.
    private sealed class DesenhoJson { public List<EstanteJson?>? Estantes { get; set; } }
    private sealed class EstanteJson { public string? Id { get; set; } public int Colunas { get; set; } = 6; public int Niveis { get; set; } = 4; }

    private static EstanteDto? Criar(string corredor, int estante, int niveis, int posicoes)
    {
        corredor = corredor.Trim().ToUpperInvariant();
        if (corredor.Length is 0 or > MaxCorredor || estante is <= 0 or > MaxEstante || niveis <= 0 || posicoes <= 0) return null;
        return new EstanteDto(corredor, estante, Math.Min(niveis, MaxNivelPosicao), Math.Min(posicoes, MaxNivelPosicao));
    }

    public static (string Corredor, int Estante)? LerIdEstante(string? id)
    {
        var m = IdEstante.Match(id ?? "");
        return m.Success && int.TryParse(m.Groups[2].Value, out var n) ? (m.Groups[1].Value.ToUpperInvariant(), n) : null;
    }

    // null = sem desenho utilizável (vazio, JSON danificado ou sem estantes válidas): quem chama usa as localizações existentes.
    public static IReadOnlyList<EstanteDto>? LerDesenho(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        DesenhoJson? d;
        try { d = JsonSerializer.Deserialize<DesenhoJson>(json, Json); }
        catch (JsonException) { return null; }
        if (d?.Estantes is null) return null;
        var lista = Juntar(d.Estantes.Select(e => e is not null && LerIdEstante(e.Id) is { } id ? Criar(id.Corredor, id.Estante, e.Niveis, e.Colunas) : null));
        return lista.Count == 0 ? null : lista;
    }

    // Alternativa sem desenho: o maior nível e posição já usados em cada estante de u_pecaloc.
    public static IReadOnlyList<EstanteDto> DeLugares(IEnumerable<LugarDto> lugares) =>
        Juntar(lugares.Select(l =>
        {
            var id = LerIdEstante(l.Estante);
            var corredor = l.Corredor.Trim() != "" ? l.Corredor : id?.Corredor ?? "";
            return id is { } i ? Criar(corredor, i.Estante, l.Nivel, l.Posicao) : null;
        }));

    // Buffer em texto livre (BUFFER) tira o corredor com esse nome; com lugar completo, sai a estante que o contém.
    public static IReadOnlyList<EstanteDto> SemBuffer(IEnumerable<EstanteDto> estantes, LugarDto? buffer)
    {
        if (buffer is null) return estantes.ToList();
        if (buffer.Estante == "") return estantes.Where(e => !string.Equals(e.Corredor, buffer.Corredor, StringComparison.OrdinalIgnoreCase)).ToList();
        var b = LerIdEstante(buffer.Estante);
        return estantes.Where(e => b is not { } x || !(string.Equals(e.Corredor, x.Corredor, StringComparison.OrdinalIgnoreCase) && e.Estante == x.Estante)).ToList();
    }

    public static IReadOnlyList<EstanteDto> Montar(string? jsonDesenho, IEnumerable<LugarDto> lugares, LugarDto? buffer) =>
        LerDesenho(jsonDesenho) is { } desenho
            ? SemBuffer(desenho, buffer)
            : DeLugares(lugares.Where(l => !RegrasLocalizacao.EBuffer(l, buffer)).ToList());

    private static IReadOnlyList<EstanteDto> Juntar(IEnumerable<EstanteDto?> estantes) =>
        estantes.OfType<EstanteDto>().GroupBy(e => (e.Corredor, e.Estante))
            .Select(g => new EstanteDto(g.Key.Corredor, g.Key.Estante, g.Max(x => x.Niveis), g.Max(x => x.Posicoes)))
            .OrderBy(e => e.Corredor, StringComparer.Ordinal).ThenBy(e => e.Estante).ToList();
}

public interface IRepositorioEstrutura
{
    // Estantes onde se pode arrumar (desenho de u_armazemlay ou, sem ele, as localizações existentes), sem o buffer. Só leitura.
    Task<IReadOnlyList<EstanteDto>> ListarAsync(CancellationToken ct);
}

public static class EndpointsEstrutura
{
    public static void MapEstruturaArmazem(this WebApplication app)
    {
        var g = app.MapGroup("/pecas").RequireAuthorization();
        g.AddEndpointFilter(async (ctx, next) =>
        {
            try { return await next(ctx); }
            catch (RepositorioIndisponivelException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        });

        g.MapGet("/estrutura-armazem", (ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioEstrutura repo, CancellationToken ct) =>
            EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (_, _) => Results.Ok(await repo.ListarAsync(ct))));
    }
}
