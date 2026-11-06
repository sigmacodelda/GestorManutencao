using System.Net;
using System.Net.Http.Json;
using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Servicos;

// Login contra a GestorManutencao.Api: a app nunca fala com o SQL Server nem guarda credenciais dele.
public sealed class AutenticacaoApiService(HttpClient http) : IAutenticacaoService
{
    private sealed record RespostaLogin(string Token, DateTimeOffset ExpiraEm, string Nome, Perfil Perfil);

    public async Task<ResultadoLogin> EntrarAsync(string utilizador, string password, CancellationToken ct = default)
    {
        try
        {
            using var resposta = await http.PostAsJsonAsync("auth/login", new { utilizador, password }, ct);

            if (resposta.StatusCode == HttpStatusCode.Unauthorized)
                return ResultadoLogin.Falha(ErroLogin.Credenciais);
            if (resposta.StatusCode == HttpStatusCode.TooManyRequests)
                return ResultadoLogin.Falha(ErroLogin.DemasiadasTentativas);
            if (!resposta.IsSuccessStatusCode)
                return ResultadoLogin.Falha(ErroLogin.Rede);

            var corpo = await resposta.Content.ReadFromJsonAsync<RespostaLogin>(ct);
            return corpo is null
                ? ResultadoLogin.Falha(ErroLogin.Rede)
                : ResultadoLogin.Ok(new Utilizador(corpo.Nome, corpo.Perfil, corpo.Token, corpo.ExpiraEm));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return ResultadoLogin.Falha(ErroLogin.Rede);
        }
    }
}
