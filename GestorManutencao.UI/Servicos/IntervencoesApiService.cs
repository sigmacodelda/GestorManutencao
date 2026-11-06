using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Intervenções e equipamentos através da API; o token vem da sessão atual.
public sealed class IntervencoesApiService(HttpClient http, SessaoService sessao) : IIntervencoesService, IEquipamentosService
{
    public async Task<IReadOnlyList<Intervencao>> ListarMinhasAsync(CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, "intervencoes", null, ct);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<List<Intervencao>>(ct) ?? [];
    }

    public async Task<Intervencao?> ObterAsync(string id, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"intervencoes/{Uri.EscapeDataString(id)}", null, ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<Intervencao>(ct);
    }

    public Task<Intervencao> RegistarAvariaAsync(string equipamentoId, string anomalia, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Post, "intervencoes/avaria", new { equipamentoId, anomalia }, ct);

    public Task<Intervencao> IniciarAsync(string id, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Post, $"intervencoes/{Uri.EscapeDataString(id)}/iniciar", null, ct);

    public Task<Intervencao> ConcluirAsync(string id, string feito, TimeOnly inicio, TimeOnly fim, CancellationToken ct = default) =>
        AlterarAsync(HttpMethod.Post, $"intervencoes/{Uri.EscapeDataString(id)}/concluir", new { feito, inicio, fim }, ct);

    public async Task<IReadOnlyList<Equipamento>> PesquisarAsync(string texto, CancellationToken ct = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"equipamentos?q={Uri.EscapeDataString(texto)}", null, ct);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<List<Equipamento>>(ct) ?? [];
    }

    // 404 e 409 querem dizer que a ficha já mudou ou não é desta pessoa.
    private async Task<Intervencao> AlterarAsync(HttpMethod metodo, string url, object? corpo, CancellationToken ct)
    {
        using var resposta = await EnviarAsync(metodo, url, corpo, ct);
        if (resposta.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound) throw new InvalidOperationException();
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<Intervencao>(ct) ?? throw new HttpRequestException();
    }

    private Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, object? corpo, CancellationToken ct)
    {
        var pedido = new HttpRequestMessage(metodo, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", sessao.Utilizador?.Token) },
            Content = corpo is null ? null : JsonContent.Create(corpo)
        };
        return http.SendAsync(pedido, ct);
    }
}
