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

// Cada pessoa escreve a sua assinatura (campo memo do seu utilizador); ninguém lê nem altera a de outra.
public class AssinaturaApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
        [
            (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
            (new UtilizadorApp("qual", "Marta Qualidade", Perfil.Qualidade, true, false), Hasher.Hash("715329"))
        ]))))
    );

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAsync(string pin)
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    [Fact]
    public async Task Sem_assinatura_devolve_vazio_e_guarda_so_a_da_propria_pessoa()
    {
        var ana = await ComoAsync("482913");
        Assert.Equal("", (await ana.GetFromJsonAsync<PedidoAssinatura>("/auth/assinatura"))!.Assinatura);

        Assert.Equal(HttpStatusCode.NoContent, (await ana.PutAsJsonAsync("/auth/assinatura", new PedidoAssinatura("  Ana Silva, técnica  "))).StatusCode);
        Assert.Equal("Ana Silva, técnica", (await ana.GetFromJsonAsync<PedidoAssinatura>("/auth/assinatura"))!.Assinatura);

        var qual = await ComoAsync("715329");
        Assert.Equal("", (await qual.GetFromJsonAsync<PedidoAssinatura>("/auth/assinatura"))!.Assinatura);
        Assert.Equal(HttpStatusCode.NoContent, (await qual.PutAsJsonAsync("/auth/assinatura", new PedidoAssinatura("Marta"))).StatusCode);
        Assert.Equal("Ana Silva, técnica", (await ana.GetFromJsonAsync<PedidoAssinatura>("/auth/assinatura"))!.Assinatura);
    }

    [Fact]
    public async Task Assinatura_demasiado_longa_e_recusada_e_sem_token_da_401()
    {
        var ana = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync("/auth/assinatura", new PedidoAssinatura(new string('a', PedidoAssinatura.Max + 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ana.PutAsJsonAsync("/auth/assinatura", new PedidoAssinatura(new string('a', PedidoAssinatura.Max)))).StatusCode);

        var anonimo = _fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/auth/assinatura")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PutAsJsonAsync("/auth/assinatura", new PedidoAssinatura("x"))).StatusCode);
    }
}
