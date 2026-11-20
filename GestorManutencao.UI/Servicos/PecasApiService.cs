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
    private sealed record CorpoErro(string? Erro, string? Mensagem);

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
        // A entrada já está gravada quando a API devolve 503 (a leitura final falha): resultado incerto.
        if (r.StatusCode == HttpStatusCode.ServiceUnavailable) throw new EntradaIncertaPecasException();
        await VerificarAsync(r, ct);
        try { return await r.Content.ReadFromJsonAsync<EntradaPeca>(ct) ?? throw new EntradaIncertaPecasException(); }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException) { throw new EntradaIncertaPecasException(); }
    }

    // 404 = a linha já não existe (alguém a anulou): para o ecrã o resultado é o desejado.
    public async Task AnularEntradaAsync(string id, CancellationToken ct = default)
    {
        using var r = await EnviarAsync(HttpMethod.Delete, $"pecas/entradas/{Uri.EscapeDataString(id)}", null, ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return;
        await VerificarAsync(r, ct);
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
                throw new InvalidOperationException(texto ?? "Não foi possível gravar o movimento.");
            case HttpStatusCode.Unauthorized: throw new HttpRequestException("A sessão terminou.", null, HttpStatusCode.Unauthorized);
            case HttpStatusCode.Forbidden: throw new InvalidOperationException("Não tem permissão para as peças.");
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
        catch (Exception e) when (ClassificacaoErros.FalhaDeRede(e, ct)) { throw new FalhaDeRedePecasException(); }
    }
}

// Sem resposta do servidor: numa entrada não se sabe se chegou a gravar, por isso o ecrã confirma na lista antes de repetir.
public sealed class FalhaDeRedePecasException() : InvalidOperationException(MensagensIntervencao.SemRede);

// A API respondeu mas não confirma a entrada (503 no POST ou 200 ilegível): também incerto.
public sealed class EntradaIncertaPecasException() : InvalidOperationException("O PHC não está a responder. Tente outra vez daqui a pouco.");
