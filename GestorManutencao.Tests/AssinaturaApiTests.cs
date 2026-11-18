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

    // PNG de 1x1 pixel (cabeçalho válido).
    private const string Png = RegrasAssinatura.Prefixo + "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Sem_assinatura_devolve_vazio_e_guarda_so_a_da_propria_pessoa()
    {
        var ana = await ComoAsync("482913");
        Assert.Equal("", (await ana.GetFromJsonAsync<PedidoAssinatura>("/auth/assinatura"))!.Assinatura);

        Assert.Equal(HttpStatusCode.NoContent, (await ana.PutAsJsonAsync("/auth/assinatura", new PedidoAssinatura(Png))).StatusCode);
        Assert.Equal(Png, (await ana.GetFromJsonAsync<PedidoAssinatura>("/auth/assinatura"))!.Assinatura);

        var qual = await ComoAsync("715329");
        Assert.Equal("", (await qual.GetFromJsonAsync<PedidoAssinatura>("/auth/assinatura"))!.Assinatura);
        Assert.Equal(HttpStatusCode.NoContent, (await qual.PutAsJsonAsync("/auth/assinatura", new PedidoAssinatura(Png))).StatusCode);
        Assert.Equal(Png, (await ana.GetFromJsonAsync<PedidoAssinatura>("/auth/assinatura"))!.Assinatura);
    }

    [Fact]
    public async Task Assinatura_obrigatoria_so_aceita_um_desenho_png_valido()
    {
        var ana = await ComoAsync("482913");
        foreach (var mau in new[] { "", "   ", "Ana Silva", "data:image/png;base64,", "data:image/png;base64,naoebase64!!", "data:image/png;base64,SGVsbG8gV29ybGQ=", "data:image/jpeg;base64,/9j/4AAQ", RegrasAssinatura.Prefixo + new string('A', RegrasAssinatura.Max) })
            Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync("/auth/assinatura", new PedidoAssinatura(mau))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync("/auth/assinatura", new PedidoAssinatura(null))).StatusCode);
        Assert.Equal("", (await ana.GetFromJsonAsync<PedidoAssinatura>("/auth/assinatura"))!.Assinatura);

        var anonimo = _fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/auth/assinatura")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PutAsJsonAsync("/auth/assinatura", new PedidoAssinatura(Png))).StatusCode);
    }
}
