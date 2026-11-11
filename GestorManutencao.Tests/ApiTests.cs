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

public class ApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly RepositorioUtilizadoresMemoria _repo = new(
    [
        (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false), Hasher.Hash("482913")),
        (new UtilizadorApp("rui", "Rui Costa", Perfil.Operador, true, true), Hasher.Hash("357159")),
        (new UtilizadorApp("inativo", "Inativo", Perfil.Operador, false, false), Hasher.Hash("246802")),
        (new UtilizadorApp("chefe", "Chefe Turno", Perfil.Supervisor, true, false), Hasher.Hash("795316")),
        (new UtilizadorApp("chefe2", "Outro Chefe", Perfil.Supervisor, true, false), Hasher.Hash("8135"))
    ]);

    private readonly WebApplicationFactory<Program> _fabrica;

    public ApiTests() => _fabrica = Criar(100);

    private WebApplicationFactory<Program> Criar(int limiteIp, int limiteGlobal = 1000) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Pin:Pepper", Pepper)
            .UseSetting("Limites:LoginPorMinuto", limiteIp.ToString())
            .UseSetting("Limites:LoginGlobalPorMinuto", limiteGlobal.ToString())
            .ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(_repo))));

    public void Dispose() => _fabrica.Dispose();

    private HttpClient Cliente(WebApplicationFactory<Program>? fabrica = null) =>
        (fabrica ?? _fabrica).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpClient> ComoAsync(HttpClient c, string pin)
    {
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    [Fact]
    public async Task Pin_valido_devolve_token_e_perfil()
    {
        var r = await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin("482913"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var corpo = (await r.Content.ReadFromJsonAsync<RespostaLogin>())!;
        Assert.False(string.IsNullOrEmpty(corpo.Token));
        Assert.Equal("Ana Silva", corpo.Nome);
        Assert.Equal(Perfil.Operador, corpo.Perfil);
        Assert.False(corpo.PinProvisorio);
    }

    [Fact]
    public async Task Perfil_supervisor_vem_da_tabela()
    {
        var r = await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin("795316"));

        Assert.Equal(Perfil.Supervisor, (await r.Content.ReadFromJsonAsync<RespostaLogin>())!.Perfil);
    }

    [Theory]
    [InlineData("000001")]
    [InlineData("246802")] // utilizador inativo
    public async Task Pin_errado_ou_de_inativo_da_401_sem_token(string pin)
    {
        var r = await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin(pin));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.DoesNotContain("token", await r.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData(null)]
    public async Task Pin_mal_formado_da_400(string? pin) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).StatusCode);

    [Fact]
    public async Task Base_indisponivel_da_503_e_nao_401()
    {
        using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Development").UseSetting("Pin:Pepper", Pepper)
            .ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioQueFalha()))));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Cliente(f).PostAsJsonAsync("/auth/login", new PedidoLogin("482913"))).StatusCode);
    }

    private sealed class RepositorioQueFalha : IRepositorioUtilizadores
    {
        public Task<UtilizadorApp?> ObterPorPinAsync(string pinHash, CancellationToken ct) => throw new RepositorioIndisponivelException();
        public Task<UtilizadorApp?> ObterAsync(string codigo, CancellationToken ct) => throw new RepositorioIndisponivelException();
        public Task<IReadOnlyList<UtilizadorApp>> ListarAsync(CancellationToken ct) => throw new RepositorioIndisponivelException();
        public Task<ResultadoPin> CriarUtilizadorAsync(string c, string n, Perfil perfil, int tecno, string h, string p, CancellationToken ct) => throw new RepositorioIndisponivelException();
        public Task<ResultadoPin> DefinirTecnoAsync(string c, int tecno, string p, CancellationToken ct) => throw new RepositorioIndisponivelException();
        public Task<ResultadoPin> DefinirPinAsync(string c, string h, bool p, string q, CancellationToken ct, string? a = null) => throw new RepositorioIndisponivelException();
    }

    [Fact]
    public async Task Rota_protegida_exige_token_e_aceita_o_emitido()
    {
        var cliente = Cliente();
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/auth/eu")).StatusCode);

        await ComoAsync(cliente, "795316");
        var eu = await cliente.GetAsync("/auth/eu");
        Assert.Equal(HttpStatusCode.OK, eu.StatusCode);
        Assert.Contains("Supervisor", await eu.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Token_adulterado_e_rejeitado()
    {
        var cliente = Cliente();
        var login = (await (await cliente.PostAsJsonAsync("/auth/login", new PedidoLogin("482913"))).Content.ReadFromJsonAsync<RespostaLogin>())!;
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token[..^3] + "abc");

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/auth/eu")).StatusCode);
    }

    [Fact]
    public async Task Excesso_de_tentativas_por_endereco_e_travado()
    {
        using var f = Criar(limiteIp: 5);
        var cliente = Cliente(f);
        var estados = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
            estados.Add((await cliente.PostAsJsonAsync("/auth/login", new PedidoLogin("000001"))).StatusCode);

        Assert.Equal(5, estados.Count(e => e == HttpStatusCode.Unauthorized));
        Assert.Equal(3, estados.Count(e => e == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task Limite_total_trava_mesmo_com_enderecos_diferentes()
    {
        using var f = Criar(limiteIp: 1000, limiteGlobal: 4);
        var cliente = Cliente(f);
        var estados = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            estados.Add((await cliente.PostAsJsonAsync("/auth/login", new PedidoLogin("000001"))).StatusCode);

        Assert.Equal(2, estados.Count(e => e == HttpStatusCode.TooManyRequests));
    }

    // ---- PIN provisório e mudança de PIN ----

    [Fact]
    public async Task Pin_provisorio_so_serve_para_mudar_o_pin()
    {
        var cliente = Cliente();
        var login = (await (await cliente.PostAsJsonAsync("/auth/login", new PedidoLogin("357159"))).Content.ReadFromJsonAsync<RespostaLogin>())!;
        Assert.True(login.PinProvisorio);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        var nova = await cliente.PostAsJsonAsync("/auth/pin", new PedidoMudarPin("357159", "9137"));
        Assert.Equal(HttpStatusCode.OK, nova.StatusCode);
        Assert.False((await nova.Content.ReadFromJsonAsync<RespostaLogin>())!.PinProvisorio);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin("357159"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin("9137"))).StatusCode);
    }

    [Theory]
    [InlineData("482913", "1111", "pin_fraco")]
    [InlineData("482913", "1234", "pin_fraco")]
    [InlineData("8135", "8135", "pin_igual")]
    [InlineData("482913", "8135", "pin_em_uso")]
    public async Task Mudar_pin_recusa_pins_fracos_repetidos_ou_de_outra_pessoa(string atual, string novo, string erro)
    {
        var cliente = await ComoAsync(Cliente(), atual);
        var r = await cliente.PostAsJsonAsync("/auth/pin", new PedidoMudarPin(atual, novo));

        Assert.Contains(erro, await r.Content.ReadAsStringAsync());
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Mudar_pin_exige_o_pin_atual_certo()
    {
        var cliente = await ComoAsync(Cliente(), "482913");

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await cliente.PostAsJsonAsync("/auth/pin", new PedidoMudarPin("795316", "9137"))).StatusCode);
    }

    // ---- Equipa (supervisor) ----

    [Fact]
    public async Task Operador_nao_ve_nem_gere_a_equipa()
    {
        var cliente = await ComoAsync(Cliente(), "482913");

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/utilizadores")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsync("/utilizadores/rui/pin", null)).StatusCode);
    }

    [Fact]
    public async Task Supervisor_com_pin_provisorio_nao_gere_a_equipa()
    {
        await _repo.DefinirPinAsync("chefe", Hasher.Hash("795316"), true, "teste", default);
        var cliente = await ComoAsync(Cliente(), "795316");

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/utilizadores")).StatusCode);
    }

    [Fact]
    public async Task Supervisor_cria_operador_e_o_pin_provisorio_funciona_uma_vez()
    {
        var chefe = await ComoAsync(Cliente(), "795316");
        var criado = await chefe.PostAsJsonAsync("/utilizadores", new PedidoNovoUtilizador("Novo.User", "Novo Utilizador"));

        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var pin = (await criado.Content.ReadFromJsonAsync<RespostaPin>())!.Pin;
        Assert.Matches(@"^\d{4}$", pin);

        var entrada = (await (await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>())!;
        Assert.Equal("Novo Utilizador", entrada.Nome);
        Assert.Equal(Perfil.Operador, entrada.Perfil);
        Assert.True(entrada.PinProvisorio);

        Assert.Equal(HttpStatusCode.Conflict,
            (await chefe.PostAsJsonAsync("/utilizadores", new PedidoNovoUtilizador("novo.user", "Outra vez"))).StatusCode);
    }

    [Theory]
    [InlineData("a", "Nome Ok")]
    [InlineData("Com Espaço", "Nome Ok")]
    [InlineData("valido", "x")]
    public async Task Criar_utilizador_valida_codigo_e_nome(string codigo, string nome)
    {
        var chefe = await ComoAsync(Cliente(), "795316");

        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/utilizadores", new PedidoNovoUtilizador(codigo, nome))).StatusCode);
    }

    [Fact]
    public async Task Supervisor_repoe_pin_de_operador_e_a_pessoa_volta_a_ter_de_o_mudar()
    {
        var chefe = await ComoAsync(Cliente(), "795316");
        var r = await chefe.PostAsync("/utilizadores/ana/pin", null);
        var pin = (await r.Content.ReadFromJsonAsync<RespostaPin>())!.Pin;

        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin("482913"))).StatusCode);
        var entrada = (await (await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>())!;
        Assert.True(entrada.PinProvisorio);
    }

    [Fact]
    public async Task Supervisor_nao_repoe_o_pin_de_outro_supervisor_nem_de_quem_nao_existe()
    {
        var chefe = await ComoAsync(Cliente(), "795316");

        Assert.Equal(HttpStatusCode.Forbidden, (await chefe.PostAsync("/utilizadores/chefe2/pin", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PostAsync("/utilizadores/ninguem/pin", null)).StatusCode);
    }

    [Fact]
    public async Task Utilizador_desativado_perde_logo_o_acesso_mesmo_com_token_valido()
    {
        var chefe = await ComoAsync(Cliente(), "795316");
        Assert.Equal(HttpStatusCode.OK, (await chefe.GetAsync("/utilizadores")).StatusCode);

        _repo.Desativar("chefe");

        Assert.Equal(HttpStatusCode.Unauthorized, (await chefe.GetAsync("/utilizadores")).StatusCode);
    }

    [Fact]
    public async Task Lista_da_equipa_nunca_traz_hashes_nem_pins()
    {
        var chefe = await ComoAsync(Cliente(), "795316");
        var corpo = await (await chefe.GetAsync("/utilizadores")).Content.ReadAsStringAsync();

        Assert.Contains("Ana Silva", corpo);
        Assert.DoesNotContain("482913", corpo);
        Assert.DoesNotContain(Hasher.Hash("482913"), corpo);
        Assert.DoesNotContain("hash", corpo, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Revisão de segurança ----

    [Theory]
    [InlineData("/auth/login/")]
    [InlineData("/auth/Login")]
    public async Task Limite_de_tentativas_nao_se_contorna_com_variantes_do_caminho(string caminho)
    {
        using var f = Criar(limiteIp: 3);
        var cliente = Cliente(f);
        var estados = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            estados.Add((await cliente.PostAsJsonAsync(caminho, new PedidoLogin("000001"))).StatusCode);

        Assert.Contains(HttpStatusCode.TooManyRequests, estados);
    }

    [Fact]
    public async Task Token_provisorio_nao_serve_em_rotas_normais()
    {
        var cliente = await ComoAsync(Cliente(), "357159");

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/auth/eu")).StatusCode);
    }

    [Fact]
    public async Task Token_antigo_morre_quando_o_pin_muda()
    {
        var antigo = await ComoAsync(Cliente(), "482913");
        Assert.Equal(HttpStatusCode.OK, (await antigo.GetAsync("/auth/eu")).StatusCode);

        var chefe = await ComoAsync(Cliente(), "795316");
        await chefe.PostAsync("/utilizadores/ana/pin", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await antigo.GetAsync("/auth/eu")).StatusCode);
    }

    [Fact]
    public async Task Token_antigo_morre_quando_a_propria_pessoa_muda_o_pin_e_o_novo_funciona()
    {
        var antigo = await ComoAsync(Cliente(), "482913");
        var troca = await antigo.PostAsJsonAsync("/auth/pin", new PedidoMudarPin("482913", "9137"));
        var novo = (await troca.Content.ReadFromJsonAsync<RespostaLogin>())!;

        Assert.Equal(HttpStatusCode.Unauthorized, (await antigo.GetAsync("/auth/eu")).StatusCode);
        var cliente = Cliente();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", novo.Token);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/auth/eu")).StatusCode);
    }

    [Fact]
    public async Task Troca_de_pin_nao_atropela_uma_reposicao_feita_entretanto()
    {
        var cliente = await ComoAsync(Cliente(), "482913");
        // o supervisor repõe o PIN depois de a pessoa já se ter autenticado
        await _repo.DefinirPinAsync("ana", Hasher.Hash("864209"), true, "chefe", default);

        var r = await cliente.PostAsJsonAsync("/auth/pin", new PedidoMudarPin("482913", "9137"));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Cliente().PostAsJsonAsync("/auth/login", new PedidoLogin("864209"))).StatusCode);
    }

    [Fact]
    public void Records_com_pin_ou_token_nao_se_imprimem()
    {
        Assert.DoesNotContain("482913", new PedidoLogin("482913").ToString());
        Assert.DoesNotContain("482913", new PedidoMudarPin("482913", "9137").ToString());
        Assert.DoesNotContain("9137", new RespostaPin("9137").ToString());
        Assert.DoesNotContain("tok", new RespostaLogin("tok-secreto", DateTimeOffset.UtcNow, "Ana", Perfil.Operador, false).ToString());
    }
}
