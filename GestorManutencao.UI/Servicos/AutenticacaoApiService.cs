using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Entrada na GestorManutencao.Api: a app nunca fala com o SQL Server nem guarda segredos dele.
public sealed class AutenticacaoApiService(HttpClient http) : IAutenticacaoService
{
    private sealed record RespostaLogin(string Token, DateTimeOffset ExpiraEm, string Nome, Perfil Perfil, bool PinProvisorio, bool Configuracao = false);

    public Task<ResultadoLogin> EntrarAsync(string pin, CancellationToken ct = default) =>
        Enviar(() => new HttpRequestMessage(HttpMethod.Post, "auth/login") { Content = JsonContent.Create(new { pin }) }, ct);

    public Task<ResultadoLogin> MudarPinAsync(string? token, string pinAtual, string pinNovo, CancellationToken ct = default) =>
        Enviar(() => new HttpRequestMessage(HttpMethod.Post, "auth/pin")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new { pinAtual, pinNovo })
        }, ct);

    private async Task<ResultadoLogin> Enviar(Func<HttpRequestMessage> pedido, CancellationToken ct)
    {
        try
        {
            using var mensagem = pedido();
            using var resposta = await http.SendAsync(mensagem, ct);

            switch (resposta.StatusCode)
            {
                case HttpStatusCode.Unauthorized: return ResultadoLogin.Falha(ErroLogin.Credenciais);
                case HttpStatusCode.TooManyRequests: return ResultadoLogin.Falha(ErroLogin.DemasiadasTentativas);
                case HttpStatusCode.Conflict: return ResultadoLogin.Falha(ErroLogin.PinEmUso);
                case HttpStatusCode.BadRequest: return ResultadoLogin.Falha(ErroLogin.PinFraco);
            }
            if (!resposta.IsSuccessStatusCode) return ResultadoLogin.Falha(ErroLogin.Rede);

            var corpo = await resposta.Content.ReadFromJsonAsync<RespostaLogin>(ct);
            return corpo is null
                ? ResultadoLogin.Falha(ErroLogin.Rede)
                : ResultadoLogin.Ok(new Utilizador(corpo.Nome, corpo.Perfil, corpo.Token, corpo.ExpiraEm, corpo.PinProvisorio, corpo.Configuracao));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return ResultadoLogin.Falha(ErroLogin.Rede);
        }
    }
}
