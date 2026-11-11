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

// PIN de configuração (Pin:Dev): só no servidor, só cria utilizadores.
public class PinConfiguracaoTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!"; // sigma-ok: valor de teste fictício
    private const string PinDev = "7391"; // sigma-ok: valor de teste fictício
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly RepositorioUtilizadoresMemoria _repo = new(
    [
        (new UtilizadorApp("chefe", "Chefe Turno", Perfil.Supervisor, true, false), Hasher.Hash("795316")), // sigma-ok: PIN de teste fictício
        (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false), Hasher.Hash("482913")) // sigma-ok: PIN de teste fictício
    ]);

    private readonly List<WebApplicationFactory<Program>> _fabricas = [];

    private HttpClient Cliente(string? pinDev)
    {
        var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development").UseSetting("Pin:Pepper", Pepper).UseSetting("Limites:LoginPorMinuto", "100"); // sigma-ok: PIN de teste fictício
            if (pinDev is not null) b.UseSetting("Pin:Dev", pinDev);
            b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(_repo)));
        });
        _fabricas.Add(f);
        return f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose() { foreach (var f in _fabricas) f.Dispose(); }

    private static async Task<RespostaLogin?> EntrarAsync(HttpClient c, string pin)
    {
        var r = await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin));
        if (r.StatusCode != HttpStatusCode.OK) return null;
        var login = await r.Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return login;
    }

    [Fact]
    public async Task Sem_pinDev_configurado_o_pin_nao_entra()
    {
        Assert.Null(await EntrarAsync(Cliente(null), PinDev));
        Assert.Null(await EntrarAsync(Cliente(""), PinDev));
    }

    [Fact]
    public async Task Com_pinDev_entra_e_cria_um_supervisor_e_um_operador()
    {
        var c = Cliente(PinDev);
        var login = await EntrarAsync(c, PinDev);
        Assert.NotNull(login);
        Assert.True(login.Configuracao);

        var lista = await c.GetAsync("/utilizadores");
        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);

        var sup = await c.PostAsJsonAsync("/utilizadores", new { utilizador = "novo.sup", nome = "Novo Supervisor", perfil = "Supervisor" });
        Assert.Equal(HttpStatusCode.Created, sup.StatusCode);
        var pinSup = (await sup.Content.ReadFromJsonAsync<RespostaPin>())!.Pin;
        var comoSup = await EntrarAsync(Cliente(PinDev), pinSup);
        Assert.Equal(Perfil.Supervisor, comoSup!.Perfil);
        Assert.True(comoSup.PinProvisorio);
        Assert.False(comoSup.Configuracao);

        var op = await c.PostAsJsonAsync("/utilizadores", new { utilizador = "novo.op", nome = "Novo Operador" });
        Assert.Equal(HttpStatusCode.Created, op.StatusCode);
        Assert.Equal(Perfil.Operador, (await _repo.ObterAsync("novo.op", default))!.Perfil);
    }

    [Fact]
    public async Task Com_pinDev_so_se_criam_utilizadores_o_resto_fica_fechado()
    {
        var c = Cliente(PinDev);
        Assert.NotNull(await EntrarAsync(c, PinDev));

        foreach (var url in new[] { "/intervencoes", "/equipamentos", "/configuracao", "/gestao/opcoes", "/auth/eu" })
        {
            var estado = (await c.GetAsync(url)).StatusCode;
            Assert.True(estado is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"GET {url} respondeu {estado}");
        }
        var avaria = (await c.PostAsJsonAsync("/intervencoes/avaria", new { })).StatusCode;
        Assert.True(avaria is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"POST avaria respondeu {avaria}");
        var gestao = (await c.PostAsJsonAsync("/gestao/equipamentos", new { })).StatusCode;
        Assert.True(gestao is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"POST gestao respondeu {gestao}");
        var pin = (await c.PostAsJsonAsync("/auth/pin", new { pinAtual = PinDev, pinNovo = "8264" })).StatusCode; // sigma-ok: PIN de teste fictício
        Assert.True(pin is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"POST /auth/pin respondeu {pin}");
    }

    [Fact]
    public async Task Pedidos_invalidos_ao_criar_sao_recusados()
    {
        var c = Cliente(PinDev);
        Assert.NotNull(await EntrarAsync(c, PinDev));

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/utilizadores", new { utilizador = "configuracao", nome = "Falso Acesso" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/utilizadores", new { utilizador = "x.y", nome = "Perfil Inventado", perfil = 5 })).StatusCode);
    }

    [Fact]
    public async Task Um_pin_novo_igual_ao_pinDev_e_recusado()
    {
        var c = Cliente(PinDev);
        Assert.NotNull(await EntrarAsync(c, "482913")); // sigma-ok: PIN de teste fictício

        var r = await c.PostAsJsonAsync("/auth/pin", new PedidoMudarPin("482913", PinDev)); // sigma-ok: PIN de teste fictício
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Um_supervisor_normal_continua_sem_poder_criar_supervisores()
    {
        var c = Cliente(PinDev);
        Assert.NotNull(await EntrarAsync(c, "795316")); // sigma-ok: PIN de teste fictício

        var r = await c.PostAsJsonAsync("/utilizadores", new { utilizador = "outro", nome = "Outro Chefe", perfil = "Supervisor" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/utilizadores", new { utilizador = "outro", nome = "Outro Operador" })).StatusCode);
    }

    [Fact]
    public async Task Token_do_pinDev_deixa_de_valer_se_o_pin_for_desligado()
    {
        var c = Cliente(PinDev);
        Assert.NotNull(await EntrarAsync(c, PinDev));
        var jwt = c.DefaultRequestHeaders.Authorization!.Parameter;

        var outro = Cliente("");
        outro.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await outro.GetAsync("/utilizadores")).StatusCode);
    }

    [Theory]
    [InlineData("1234")] // sigma-ok: PIN de teste fictício
    [InlineData("0000")] // sigma-ok: PIN de teste fictício
    [InlineData("123")] // sigma-ok: PIN de teste fictício
    [InlineData("73910")] // sigma-ok: PIN de teste fictício
    [InlineData("abcd")] // sigma-ok: PIN de teste fictício
    public void PinDev_fraco_ou_mal_formado_impede_o_arranque(string pin) =>
        Assert.ThrowsAny<Exception>(() => Cliente(pin).GetAsync("/saude").GetAwaiter().GetResult());
}
