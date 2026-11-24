using System.Security.Claims;
using System.Text.RegularExpressions;

namespace GestorManutencao.Api;

// Lugares de uma peça no armazém de manutenção (u_pecaloc). Estante guardada normalizada (A-03).
public sealed record LugarDto(string Corredor, string Estante, int Nivel, int Posicao, decimal Qtt)
{
    public string Texto => $"{Estante}-{Nivel}-{Posicao:00}";
}

public sealed record LocalizacoesPecaDto(string Ref, decimal Stock, decimal Localizado, decimal PorArrumar, IReadOnlyList<LugarDto> Lugares);

// Peça arrumada num lugar, com a quantidade que lá está.
public sealed record PecaNoLugarDto(string Ref, string Design, decimal Qtt);

// Local = texto da etiqueta (LOC-A-02-1-01) ou o texto do lugar (A-02-1-01).
public sealed record PedidoArrumar(string? Ref, string? Local, decimal? Qtt);

// Origem e destino = texto do lugar completo (A-02-1-01) ou etiqueta (LOC-A-02-1-01).
public sealed record PedidoMover(string? Origem, string? Destino, decimal? Qtt);

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

    // Lugar completo (com posição) a partir do texto; sem posição não serve para descontar.
    public static LugarDto? LerLugar(string? texto) =>
        LerLocal(texto) is { Posicao: { } posicao } l ? new LugarDto(l.Corredor, NormalizarEstante(l.Corredor, l.Estante), l.Nivel, posicao, 0) : null;

    public static string NormalizarEstante(string corredor, int estante) => $"{corredor.Trim().ToUpperInvariant()}-{estante:00}";

    public static decimal Calcular(decimal stock, decimal localizado) => Math.Max(0, stock - localizado);

    // Buffer = u_configman.localentrada (o da LandeiroMaui): onde as entradas esperam por lugar. Texto livre (BUFFER) fica no corredor, sem estante.
    public static LugarDto? LerBuffer(string? texto)
    {
        var t = (texto ?? "").Trim();
        return t.Length == 0 ? null : LerLugar(t) ?? new LugarDto(t.ToUpperInvariant(), "", 0, 0, 0);
    }

    public static bool EBuffer(LugarDto lugar, LugarDto? buffer) =>
        buffer is not null && string.Equals(lugar.Corredor.Trim(), buffer.Corredor, StringComparison.OrdinalIgnoreCase)
        && string.Equals(lugar.Estante.Trim(), buffer.Estante, StringComparison.OrdinalIgnoreCase) && lugar.Nivel == buffer.Nivel && lugar.Posicao == buffer.Posicao;

    public static bool MesmoLugar(LugarDto a, LugarDto b) => EBuffer(a, b);

    // Dossier de movimentos de arrumação: origem em bo.lobs, destino em bo.lobs2, uma linha bi por peça.
    public const int NdosArrumacao = 66;

    // Texto do lugar como o PHC o mostra (A-02-1-01); o buffer em texto livre (BUFFER) fica como está; sem lugar, vazio.
    public static string TextoLugar(LugarDto? l) => l is null ? "" : l.Estante == "" ? l.Corredor : l.Texto;

    // Rearrumar: quantidade existente 0 = "não indicada" na u_pecaloc; só se move o lugar todo e o destino não recebe número inventado.
    public static string? ErroQuantidadeMover(decimal existente, decimal qtt) =>
        existente > 0 && qtt > existente ? $"Só há {existente:0.###} nesse lugar." : null;

    public static decimal QuantidadeMovida(decimal existente, decimal qtt) => existente > 0 ? qtt : 0;

    public const string ErroMoverDoBuffer = "O que está no buffer arruma-se em Arrumação, não em Rearrumar.";
    public const string ErroMoverParaBuffer = "Escolha um lugar diferente do de entrada (buffer).";
    public const string ErroMoverSemLinha = "Esta peça não tem stock nesse lugar.";

    // Só se arruma o que está no buffer (stock do armazém menos o já localizado fora dele).
    public static string? ErroForaDoBuffer(decimal pedida, decimal porArrumar) =>
        porArrumar <= 0 ? "Esta peça não tem nada no buffer para arrumar."
        : pedida > porArrumar ? $"Só pode arrumar o que está no buffer: {porArrumar:0.###} por arrumar."
        : null;
}

public interface IRepositorioLocalizacoes
{
    // Lugares definitivos por qtt desc (sem a linha do buffer); PorArrumar = max(0, stock do armazém 104 - localizado fora do buffer).
    Task<LocalizacoesPecaDto> ListarAsync(string refPeca, CancellationToken ct);
    // Soma ao lugar ou cria-o e tira o mesmo do buffer; Falha se o lugar é o próprio buffer.
    Task<ResultadoMovimento> ArrumarAsync(string refPeca, string corredor, int estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct);
    // Ok mesmo que o lugar fique a 0; Falha se o lugar não tem quantidade suficiente.
    Task<ResultadoMovimento> DescontarAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct);
    // Soma de volta (anulação da saída), sem validar stock.
    Task<ResultadoMovimento> ReporAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct);
    // "" se a tabela está pronta, senão o que falta.
    Task<string> VerificarTabelaAsync(CancellationToken ct);
    // Peças com quantidade (> 0) num lugar, por quantidade desc. Só leitura; sem tabela devolve vazio.
    Task<IReadOnlyList<(string Ref, decimal Qtt)>> ListarPecasDoLugarAsync(string corredor, string estante, int nivel, int posicao, CancellationToken ct);
    // Rearrumar: soma ao destino (cria a linha se faltar) e depois tira da origem, que se apaga se ficar vazia; nunca mexe no buffer nem no stock do PHC.
    // Falha: origem ou destino no buffer, origem sem linha, quantidade acima do que lá está, lugar ocupado.
    Task<ResultadoMovimento> MoverAsync(string refPeca, LugarDto origem, LugarDto destino, decimal qtt, string iniciais, CancellationToken ct) => throw new NotSupportedException();
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

        // Que peças estão num lugar: ao picar a etiqueta do lugar na ficha, a pessoa escolhe a peça.
        g.MapGet("/no-lugar", (string? local, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioPecas pecas, IRepositorioLocalizacoes repo, CancellationToken ct) =>
        {
            if (RegrasLocalizacao.LerLugar(local) is not { } lugar)
                return Task.FromResult(EndpointsIntervencoes.PedidoInvalido("Indique o local completo (corredor, estante, nível e posição), por exemplo A-02-1-01."));
            return EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (_, _) =>
            {
                var lista = new List<PecaNoLugarDto>();
                foreach (var (refPeca, qtt) in await repo.ListarPecasDoLugarAsync(lugar.Corredor, lugar.Estante, lugar.Nivel, lugar.Posicao, ct))
                    lista.Add(new PecaNoLugarDto(refPeca, (await pecas.ObterPorCodigoAsync(refPeca, ct))?.Design ?? refPeca, qtt));
                return Results.Ok(lista);
            });
        });

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
                if (RegrasLocalizacao.ErroForaDoBuffer(p.Qtt!.Value, (await repo.ListarAsync(peca.Ref, ct)).PorArrumar) is { } foraDoBuffer)
                    return EndpointsIntervencoes.PedidoInvalido(foraDoBuffer);
                var r = await repo.ArrumarAsync(peca.Ref, local.Value.Corredor, local.Value.Estante, local.Value.Nivel, posicao, p.Qtt!.Value, t.Codigo, ct);
                if (!r.Ok) return r.Erro == RegrasPecas.ErroOcupado
                    ? Results.Json(new { erro = r.Erro }, statusCode: StatusCodes.Status409Conflict)
                    : EndpointsIntervencoes.PedidoInvalido(r.Erro ?? "Não foi possível gravar a localização.");
                return Results.Ok(await repo.ListarAsync(peca.Ref, ct));
            });
        });

        g.MapPost("/{refPeca}/mover", (string refPeca, PedidoMover p, ClaimsPrincipal eu, IRepositorioUtilizadores utilizadores, IRepositorioPecas pecas, IRepositorioLocalizacoes repo, CancellationToken ct) =>
        {
            if (!RegrasPecas.ReferenciaValida(refPeca)) return Task.FromResult(Results.NotFound());
            if (RegrasLocalizacao.LerLugar(p.Origem) is not { } origem || RegrasLocalizacao.LerLugar(p.Destino) is not { } destino)
                return Task.FromResult(EndpointsIntervencoes.PedidoInvalido("Indique a origem e o destino completos (corredor, estante, nível e posição), por exemplo A-02-1-01."));
            if (!RegrasPecas.QuantidadeValida(p.Qtt))
                return Task.FromResult(EndpointsIntervencoes.PedidoInvalido("Indique uma quantidade positiva, com no máximo 3 casas decimais."));
            if (RegrasLocalizacao.MesmoLugar(origem, destino))
                return Task.FromResult(EndpointsIntervencoes.PedidoInvalido("A origem e o destino são o mesmo lugar."));
            return EndpointsIntervencoes.ComoTecnicoOuSupervisor(eu, utilizadores, ct, async (t, _) =>
            {
                if (await pecas.ObterPorCodigoAsync(refPeca.Trim(), ct) is not { } peca) return Results.NotFound();
                var falta = await repo.VerificarTabelaAsync(ct);
                if (falta != "") return Results.Json(new { erro = falta }, statusCode: StatusCodes.Status409Conflict);
                var r = await repo.MoverAsync(peca.Ref, origem, destino, p.Qtt!.Value, t.Codigo, ct);
                if (!r.Ok) return r.Erro == RegrasPecas.ErroOcupado
                    ? Results.Json(new { erro = r.Erro }, statusCode: StatusCodes.Status409Conflict)
                    : EndpointsIntervencoes.PedidoInvalido(r.Erro ?? "Não foi possível mudar o lugar.");
                return Results.Ok(await repo.ListarAsync(peca.Ref, ct));
            });
        });
    }
}
