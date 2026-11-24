using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Offline;

namespace GestorManutencao.UI.Servicos;

// Inventários através da API; o token vem da sessão atual.
public sealed class InventarioApiService(HttpClient http, SessaoService sessao) : IInventarioService
{
    private sealed record CorpoErro(string? Erro, string? Mensagem);

    public async Task<IReadOnlyList<Inventario>> ListarAsync(CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Get, "inventarios", null, ct);
        await VerificarAsync(r, ct);
        return await r.Content.ReadFromJsonAsync<List<Inventario>>(ct) ?? throw new HttpRequestException();
    }

    public async Task<InventarioDetalhe?> ObterAsync(string id, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Get, $"inventarios/{Uri.EscapeDataString(id)}", null, ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        await VerificarAsync(r, ct);
        return await r.Content.ReadFromJsonAsync<InventarioDetalhe>(ct);
    }

    public Task<InventarioDetalhe> ContarAsync(string id, string refPeca, string local, decimal qtt, CancellationToken ct = default) =>
        EscreverAsync(HttpMethod.Post, $"inventarios/{Uri.EscapeDataString(id)}/linhas", new { @ref = refPeca, local, qtt }, ct);

    public Task<InventarioDetalhe> AlterarQuantidadeAsync(string id, string linhaId, decimal qtt, CancellationToken ct = default) =>
        EscreverAsync(HttpMethod.Put, $"inventarios/{Uri.EscapeDataString(id)}/linhas/{Uri.EscapeDataString(linhaId)}", new { qtt }, ct);

    public Task<InventarioDetalhe> ApagarLinhaAsync(string id, string linhaId, CancellationToken ct = default) =>
        EscreverAsync(HttpMethod.Delete, $"inventarios/{Uri.EscapeDataString(id)}/linhas/{Uri.EscapeDataString(linhaId)}", null, ct);

    public Task<InventarioDetalhe> ConcluirAsync(string id, CancellationToken ct = default) =>
        EscreverAsync(HttpMethod.Post, $"inventarios/{Uri.EscapeDataString(id)}/concluir", null, ct);

    private async Task<InventarioDetalhe> EscreverAsync(HttpMethod metodo, string url, object? corpo, CancellationToken ct)
    {
        using var r = await EnviarAsync(metodo, url, corpo, ct);
        await VerificarAsync(r, ct);
        return await r.Content.ReadFromJsonAsync<InventarioDetalhe>(ct) ?? throw new HttpRequestException();
    }

    // 400 traz {erro:"pedido_invalido", mensagem}; 409 traz {erro:"texto"} já escrito para o utilizador.
    private static async Task VerificarAsync(HttpResponseMessage r, CancellationToken ct)
    {
        if (r.IsSuccessStatusCode) return;
        switch (r.StatusCode)
        {
            case HttpStatusCode.BadRequest:
            case HttpStatusCode.Conflict:
                var c = await LerCorpoAsync(r, ct);
                var texto = r.StatusCode == HttpStatusCode.BadRequest
                    ? c?.Mensagem ?? (c?.Erro is not (null or "pedido_invalido") ? c.Erro : null)
                    : c?.Erro;
                throw new InvalidOperationException(texto ?? "Não foi possível gravar o inventário.");
            case HttpStatusCode.NotFound: throw new InvalidOperationException("O inventário ou a linha já não existe.");
            case HttpStatusCode.Unauthorized: throw new HttpRequestException("A sessão terminou.", null, HttpStatusCode.Unauthorized);
            case HttpStatusCode.Forbidden: throw new InvalidOperationException("Este inventário não é seu ou não tem permissão para o inventário.");
            case HttpStatusCode.ServiceUnavailable: throw new InvalidOperationException("O PHC não está a responder. Tente outra vez daqui a pouco.");
        }
        r.EnsureSuccessStatusCode();
    }

    private static async Task<CorpoErro?> LerCorpoAsync(HttpResponseMessage r, CancellationToken ct)
    {
        try { return await r.Content.ReadFromJsonAsync<CorpoErro>(ct); }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException) { return null; }
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
