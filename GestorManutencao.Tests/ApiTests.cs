using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GestorManutencao.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _fabrica;

    public ApiTests(WebApplicationFactory<Program> fabrica) =>
        _fabrica = fabrica.WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Limites:LoginPorMinuto", "1000")
            .ConfigureServices(s => s.Replace(ServiceDescriptor.Scoped<IValidadorCredenciais, ValidadorFalso>())));

    private sealed class ValidadorFalso : IValidadorCredenciais
    {
        public Task<ResultadoValidacao> ValidarAsync(string utilizador, string password, CancellationToken ct) =>
            Task.FromResult((utilizador, password) switch
            {
                ("ana", "ok") => new ResultadoValidacao(new UtilizadorValidado("ana", "Ana Silva", ["DES_PEDIDOS"]), null),
                ("chefe", "ok") => new ResultadoValidacao(new UtilizadorValidado("chefe", "Chefe Turno", ["DES_PEDIDOS", "DES_MANUT_SUPERVISOR"]), null),
                ("sem-sql", _) => new ResultadoValidacao(null, FalhaValidacao.Indisponivel),
                _ => new ResultadoValidacao(null, FalhaValidacao.Credenciais)
            });
    }

    private HttpClient Cliente() => _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Login_valido_devolve_token_e_perfil_operador()
    {
        var r = await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin("ana", "ok"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var corpo = await r.Content.ReadFromJsonAsync<RespostaLogin>();
        Assert.False(string.IsNullOrEmpty(corpo!.Token));
        Assert.Equal("Ana Silva", corpo.Nome);
        Assert.Equal(Perfil.Operador, corpo.Perfil);
    }

    [Fact]
    public async Task Perfil_supervisor_vem_do_perfil_do_PHC()
    {
        var r = await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin("chefe", "ok"));

        Assert.Equal(Perfil.Supervisor, (await r.Content.ReadFromJsonAsync<RespostaLogin>())!.Perfil);
    }

    [Fact]
    public async Task Credenciais_erradas_dao_401_sem_token()
    {
        var r = await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin("ana", "errada"));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.DoesNotContain("token", await r.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sql_indisponivel_da_503_e_nao_401()
    {
        var r = await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin("sem-sql", "x"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("ana", "")]
    [InlineData(null, null)]
    public async Task Pedido_incompleto_da_400(string? utilizador, string? password)
    {
        var r = await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin(utilizador, password));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Rota_protegida_exige_token_e_aceita_o_emitido()
    {
        var cliente = Cliente();
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/auth/eu")).StatusCode);

        var login = await (await cliente.PostAsJsonAsync("/auth/login", new PedidoLogin("chefe", "ok"))).Content.ReadFromJsonAsync<RespostaLogin>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        var eu = await cliente.GetAsync("/auth/eu");
        Assert.Equal(HttpStatusCode.OK, eu.StatusCode);
        Assert.Contains("Supervisor", await eu.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Token_adulterado_e_rejeitado()
    {
        var cliente = Cliente();
        var login = await (await cliente.PostAsJsonAsync("/auth/login", new PedidoLogin("ana", "ok"))).Content.ReadFromJsonAsync<RespostaLogin>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token[..^3] + "abc");

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/auth/eu")).StatusCode);
    }

    [Fact]
    public async Task Excesso_de_tentativas_e_travado()
    {
        using var estrita = _fabrica.WithWebHostBuilder(b => b.UseSetting("Limites:LoginPorMinuto", "5"));
        var cliente = estrita.CreateClient();
        var estados = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
            estados.Add((await cliente.PostAsJsonAsync("/auth/login", new PedidoLogin("ana", "errada"))).StatusCode);

        Assert.Equal(5, estados.Count(e => e == HttpStatusCode.Unauthorized));
        Assert.Equal(3, estados.Count(e => e == HttpStatusCode.TooManyRequests));
    }
}
