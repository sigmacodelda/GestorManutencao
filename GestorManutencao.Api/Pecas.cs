using System.Security.Claims;

namespace GestorManutencao.Api;

// Peça = artigo st marcado em stobs.u_manut. Stock e local são os do armazém de manutenção (sa).
// EmConferencia = quantidade já reservada em dossiers 63 por validar; Disponivel é o que sobra para novas saídas.
public sealed record PecaDto(string Ref, string Design, string Codigo, string Local, decimal Stock, decimal StMin, decimal Preco, decimal EmConferencia = 0)
{
    public decimal Disponivel => Stock - EmConferencia;
    public bool AbaixoDoMinimo => StMin > 0 && Stock <= StMin;
}

// CampoExiste = false: falta criar stobs.u_manut no PHC (o ecrã diz o que falta). HaMais: pedir ao utilizador para escrever mais.
public sealed record ResultadoPecasDto(bool CampoExiste, IReadOnlyList<PecaDto> Pecas, bool HaMais);

// Repetir a saída de um material cuja ligação à peça não ficou gravada (a saída falhou); sem corpo vale a ref já ligada.
public sealed record PedidoSaida(string? Ref);

public static class RegrasPecas
{
    public const int NdosSaida = 63, Armazem = 104;
    public const string CampoStobs = "u_manut";
    public const decimal QuantidadeMaxima = 999_999m;

    public static string[] Palavras(string? texto)
        => (texto ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4).ToArray();

    public static string EscaparLike(string palavra)
        => palavra.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    public static string? AvisoStock(decimal pedida, decimal disponivel)
    {
        if (pedida <= 0) return null;
        if (disponivel <= 0) return "Sem stock desta peça.";
        return pedida > disponivel ? $"Stock insuficiente: há {disponivel:0.###} e pede {pedida:0.###}." : null;
    }

    // st.ref tem 18 caracteres no PHC; o movimento grava a quantidade com 3 casas.
    public const int TamanhoRef = 18;

    // Os repositórios devolvem estes textos e o endpoint decide o estado HTTP por eles.
    public const string ErroOcupado = "Outro movimento em curso, tente outra vez.";
    public const string ErroValidada = "A conferência desta ficha já foi validada.";

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

    // Dossier 63 da ficha em conferência: bo.emconf=1 (bo, bo2, bo3). Idempotente: se já existe devolve o id.
    Task<ResultadoMovimento> AbrirConferenciaAsync(string fichaId, string iniciais, CancellationToken ct);
    // Linha bi.emconf=1 do material (bi.lobs = materialId, armazém 104); cria o dossier se faltar. Grava também u_matinterv.ref.
    Task<ResultadoMovimento> DarSaidaAsync(string fichaId, string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct);
    // Apaga a linha em conferência e limpa u_matinterv.ref na mesma transação; o dossier mantém-se até validar ou cancelar.
    Task<ResultadoMovimento> AnularSaidaAsync(string materialId, string iniciais, CancellationToken ct);
    // Validar: bo.emconf=0 e bi.emconf=0 mais os "toques" que movem o stock. Idempotente.
    Task<ResultadoMovimento> ConfirmarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct);
    // Cancelar: apaga linhas, bi2 e o dossier da ficha, só se ainda em conferência.
    Task<ResultadoMovimento> CancelarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct);
    // Soma das quantidades em conferência (dossiers 63, armazém 104) por peça, sem contar a ficha indicada.
    Task<IReadOnlyDictionary<string, decimal>> EmConferenciaPorPecaAsync(IReadOnlyCollection<string> refs, string? excetoFichaId, CancellationToken ct);

    // Liga a peça ao material sem movimento (a saída falhou): Ref preenchida e SaidaDada = false = "por dar".
    Task<bool> LigarPecaAsync(string stampMaterial, string refPeca, string usrinis, CancellationToken ct);

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
    }
}
