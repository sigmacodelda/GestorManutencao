using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace GestorManutencao.UI.Servicos;

public enum ResultadoAssinatura { Ok, Longa, Rede }

public sealed record RespostaAssinatura(ResultadoAssinatura Resultado, string Texto = "");

// A assinatura que cada pessoa escreve para si (fica no seu utilizador; por agora só se guarda).
public interface IAssinaturaService
{
    public const int Max = 1000;
    Task<RespostaAssinatura> LerAsync(string? token, CancellationToken ct = default);
    Task<RespostaAssinatura> GuardarAsync(string? token, string texto, CancellationToken ct = default);
}

public sealed class AssinaturaApiService(HttpClient http) : IAssinaturaService
{
    private sealed record Corpo(string? Assinatura);

    public async Task<RespostaAssinatura> LerAsync(string? token, CancellationToken ct = default)
    {
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Get, "auth/assinatura") { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } };
            using var r = await http.SendAsync(pedido, ct);
            if (!r.IsSuccessStatusCode) return new(ResultadoAssinatura.Rede);
            return new(ResultadoAssinatura.Ok, (await r.Content.ReadFromJsonAsync<Corpo>(ct))?.Assinatura ?? "");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return new(ResultadoAssinatura.Rede);
        }
    }

    public async Task<RespostaAssinatura> GuardarAsync(string? token, string texto, CancellationToken ct = default)
    {
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Put, "auth/assinatura")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
                Content = JsonContent.Create(new Corpo(texto))
            };
            using var r = await http.SendAsync(pedido, ct);
            if (r.StatusCode == System.Net.HttpStatusCode.BadRequest) return new(ResultadoAssinatura.Longa);
            return r.IsSuccessStatusCode ? new(ResultadoAssinatura.Ok, texto.Trim()) : new(ResultadoAssinatura.Rede);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return new(ResultadoAssinatura.Rede);
        }
    }
}

// Para o desenvolvimento sem API.
public sealed class DevAssinaturaService : IAssinaturaService
{
    private string _texto = "";
    public Task<RespostaAssinatura> LerAsync(string? token, CancellationToken ct = default) => Task.FromResult(new RespostaAssinatura(ResultadoAssinatura.Ok, _texto));
    public Task<RespostaAssinatura> GuardarAsync(string? token, string texto, CancellationToken ct = default)
    {
        if (texto.Trim().Length > IAssinaturaService.Max) return Task.FromResult(new RespostaAssinatura(ResultadoAssinatura.Longa));
        _texto = texto.Trim();
        return Task.FromResult(new RespostaAssinatura(ResultadoAssinatura.Ok, _texto));
    }
}
