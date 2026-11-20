using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;

namespace GestorManutencao.UI.Servicos;

// Peças através da API; o token vem da sessão atual.
public sealed class PecasApiService(HttpClient http, SessaoService sessao) : IPecasService
{
    private sealed record PedidoEntrada(string Ref, decimal Qtt);
    private sealed record CorpoErro(string? Erro);

    public async Task<ResultadoPecas> PesquisarAsync(string texto, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Get, $"pecas?texto={Uri.EscapeDataString(texto ?? "")}", null, ct);
        await VerificarAsync(r, ct);
        return await r.Content.ReadFromJsonAsync<ResultadoPecas>(ct) ?? throw new HttpRequestException();
    }

    public async Task<Peca?> ObterPorCodigoAsync(string codigo, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Get, $"pecas/codigo/{Uri.EscapeDataString(codigo)}", null, ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        await VerificarAsync(r, ct);
        return await r.Content.ReadFromJsonAsync<Peca>(ct);
    }

    public async Task<IReadOnlyList<EntradaPeca>> EntradasDeHojeAsync(CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Get, "pecas/entradas/hoje", null, ct);
        await VerificarAsync(r, ct);
        return await r.Content.ReadFromJsonAsync<List<EntradaPeca>>(ct) ?? [];
    }

    public async Task<EntradaPeca> DarEntradaAsync(string refPeca, decimal qtt, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Post, "pecas/entradas", new PedidoEntrada(refPeca, qtt), ct);
        await VerificarAsync(r, ct);
        return await r.Content.ReadFromJsonAsync<EntradaPeca>(ct) ?? throw new HttpRequestException();
    }

    // 404 = a linha já não existe (alguém a anulou): para o ecrã o resultado é o desejado.
    public async Task AnularEntradaAsync(string id, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Delete, $"pecas/entradas/{Uri.EscapeDataString(id)}", null, ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return;
        await VerificarAsync(r, ct);
    }

    // 400 e 409 trazem {erro} já escrito para o utilizador; o resto fica como erro genérico.
    private static async Task VerificarAsync(HttpResponseMessage r, CancellationToken ct)
    {
        if (r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
            throw new InvalidOperationException(await LerErroAsync(r, ct) ?? "Não foi possível gravar o movimento.");
        r.EnsureSuccessStatusCode();
    }

    private static async Task<string?> LerErroAsync(HttpResponseMessage r, CancellationToken ct)
    {
        try { return (await r.Content.ReadFromJsonAsync<CorpoErro>(ct))?.Erro; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, object? corpo, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(metodo, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", sessao.Utilizador?.Token) },
            Content = corpo is null ? null : JsonContent.Create(corpo)
        };
        try { return await http.SendAsync(pedido, ct); }
        catch (Exception e) when (ClassificacaoErros.FalhaDeRede(e, ct)) { throw new InvalidOperationException(MensagensIntervencao.SemRede); }
    }
}
