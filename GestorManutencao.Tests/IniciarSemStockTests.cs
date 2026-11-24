using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

public class IniciarSemStockTests
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    // ana (101) é técnica; chefe (100) é supervisor. F1 é uma planeada sem dono.
    private static WebApplicationFactory<Program> Criar(ConfigManutencao cfg, Action<RepositorioIntervencoesMemoria>? preparar = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Pin:Pepper", Pepper)
            .UseSetting("Limites:LoginPorMinuto", "1000")
            .ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
                [
                    (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
                    (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918"))
                ])));
                s.Replace(ServiceDescriptor.Singleton<IRepositorioIntervencoes>(sp =>
                {
                    var repo = new RepositorioIntervencoesMemoria(sp.GetRequiredService<TimeProvider>(), cfg);
                    preparar?.Invoke(repo);
                    return repo;
                }));
            }));

    private static async Task<HttpClient> ComoAsync(WebApplicationFactory<Program> fabrica, string pin)
    {
        var c = fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    private static void SemPeca(RepositorioIntervencoesMemoria r)
    {
        r.DefinirNecessidades("F1", ("P-01", "Filtro de ar", 2), ("P-02", "Correia", 1));
        r.DefinirStock("P-01", 1);
        r.DefinirStock("P-02", 5);
    }

    [Fact]
    public async Task Campo_ligado_permite_iniciar_mesmo_sem_stock()
    {
        using var fabrica = Criar(new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false, IniciarSemStock: true), SemPeca);
        var ana = await ComoAsync(fabrica, "482913");
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsync("/intervencoes/F1/iniciar", null)).StatusCode);
    }

    [Fact]
    public async Task Campo_ausente_vale_desligado_e_recusa_sem_stock()
    {
        using var fabrica = Criar(new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false), SemPeca);
        var ana = await ComoAsync(fabrica, "482913");
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsync("/intervencoes/F1/iniciar", null)).StatusCode);
    }

    [Fact]
    public async Task Desligado_e_falta_de_stock_recusa_com_a_lista_e_nada_fica_iniciado()
    {
        using var fabrica = Criar(new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false, IniciarSemStock: false), SemPeca);
        var ana = await ComoAsync(fabrica, "482913");
        var resposta = await ana.PostAsync("/intervencoes/F1/iniciar", null);
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("sem_stock", corpo.GetProperty("erro").GetString());
        var pecas = corpo.GetProperty("pecas").EnumerateArray().Select(p => p.GetString()!).ToList();
        var uma = Assert.Single(pecas);
        Assert.Contains("P-01", uma);
        Assert.Contains("precisa 2, há 1", uma);

        var ficha = (await ana.GetFromJsonAsync<IntervencaoDto>("/intervencoes/F1"))!;
        Assert.Equal(EstadoIntervencao.Planeada, ficha.Estado);
    }

    [Fact]
    public async Task Supervisor_esta_sujeito_a_mesma_regra()
    {
        using var fabrica = Criar(new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false, IniciarSemStock: false), SemPeca);
        var chefe = await ComoAsync(fabrica, "603918");
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsync("/intervencoes/F1/iniciar", null)).StatusCode);
    }

    [Fact]
    public async Task Desligado_com_stock_suficiente_permite_iniciar()
    {
        using var fabrica = Criar(new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false, IniciarSemStock: false), r =>
        {
            SemPeca(r);
            r.DefinirStock("P-01", 2);
        });
        var ana = await ComoAsync(fabrica, "482913");
        var resposta = await ana.PostAsync("/intervencoes/F1/iniciar", null);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(EstadoIntervencao.EmCurso, (await resposta.Content.ReadFromJsonAsync<IntervencaoDto>())!.Estado);
    }

    [Fact]
    public void Regra_soma_repetidas_e_formata_como_o_desktop()
    {
        var precisa = new Dictionary<string, (string Design, decimal Qtt)>();
        RegrasSemStock.Somar(precisa, "A", "Peça A", 1.5m);
        RegrasSemStock.Somar(precisa, "A", "Peça A", 1m);
        var falta = RegrasSemStock.EmFalta(precisa, _ => 2m);
        var linha = Assert.Single(falta);
        Assert.StartsWith("A · Peça A (precisa 2", linha);
        Assert.EndsWith("há 2)", linha);
    }
}
