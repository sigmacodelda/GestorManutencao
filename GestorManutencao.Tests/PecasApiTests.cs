using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

public class PecasApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
            [
                (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
                (new UtilizadorApp("qual", "Marta Qualidade", Perfil.Qualidade, true, false), Hasher.Hash("715329"))
            ])));
            s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(new RepositorioPecasMemoria(
            [
                new PecaDto("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
                new PecaDto("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
            ])));
        }));

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAnaAsync(string pin = "482913")
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    [Fact]
    public async Task Sem_sessao_recusa()
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/pecas")).StatusCode);
    }

    [Fact]
    public async Task Pesquisa_por_texto_e_por_codigo_exato()
    {
        var c = await ComoAnaAsync();
        var r = await c.GetFromJsonAsync<ResultadoPecasDto>("/pecas?texto=rolamento");
        Assert.Equal("P002", Assert.Single(r!.Pecas).Ref);
        var p = await c.GetFromJsonAsync<PecaDto>("/pecas/codigo/560001");
        Assert.Equal("P001", p!.Ref);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/pecas/codigo/XXXX")).StatusCode);
    }

    [Fact]
    public async Task Entrada_sobe_o_stock_lista_hoje_e_anula()
    {
        var c = await ComoAnaAsync();
        var r = await c.PostAsJsonAsync("/pecas/entradas", new PedidoEntrada("P002", 3));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var entrada = await r.Content.ReadFromJsonAsync<EntradaPecaDto>();
        Assert.Equal(3, (await c.GetFromJsonAsync<PecaDto>("/pecas/codigo/P002"))!.Stock);
        Assert.Single((await c.GetFromJsonAsync<List<EntradaPecaDto>>("/pecas/entradas/hoje"))!);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/pecas/entradas/{entrada!.Id}")).StatusCode);
        Assert.Equal(0, (await c.GetFromJsonAsync<PecaDto>("/pecas/codigo/P002"))!.Stock);
        Assert.Empty((await c.GetFromJsonAsync<List<EntradaPecaDto>>("/pecas/entradas/hoje"))!);
    }

    [Theory]
    [InlineData("P001", 0)]
    [InlineData("P001", -2)]
    [InlineData("", 1)]
    [InlineData("INEXISTENTE", 1)]
    public async Task Entrada_invalida_recusa(string refPeca, int qtt)
    {
        var c = await ComoAnaAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/pecas/entradas", new PedidoEntrada(refPeca, qtt))).StatusCode);
    }

    [Fact]
    public async Task Entrada_e_anulacao_sem_sessao_recusam()
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/pecas/entradas", new PedidoEntrada("P001", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.DeleteAsync("/pecas/entradas/abc")).StatusCode);
    }

    [Fact]
    public async Task Anular_entrada_inexistente_da_404()
    {
        var c = await ComoAnaAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/pecas/entradas/naoexiste")).StatusCode);
    }

    [Fact]
    public async Task Quantidade_nula_ou_acima_do_limite_recusa()
    {
        var c = await ComoAnaAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/pecas/entradas", new PedidoEntrada("P001", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/pecas/entradas", new PedidoEntrada("P001", 1000000))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/pecas/entradas", new PedidoEntrada(new string('X', 19), 1))).StatusCode);
    }

    [Fact]
    public async Task Perfil_sem_acesso_e_recusado_em_todas_as_rotas()
    {
        var c = await ComoAnaAsync("715329");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/pecas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/pecas/codigo/560001")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/pecas/entradas/hoje")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/pecas/entradas", new PedidoEntrada("P001", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.DeleteAsync("/pecas/entradas/abc")).StatusCode);
    }

    [Fact]
    public async Task Sem_texto_devolve_as_pecas()
    {
        var c = await ComoAnaAsync();
        var r = await c.GetFromJsonAsync<ResultadoPecasDto>("/pecas");
        Assert.Equal(2, r!.Pecas.Count);
    }
}
