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

// Lista de fichas do supervisor: situação, por supervisionar, tipo, texto, ordenação e os campos novos (repositório em memória).
// Fichas semeadas: F1..F4 (nº 1 a 4; F3 é concluída e curativa), H1 e H2 validadas (nº 5 e 6), H3 cancelada (nº 7).
public class ListaFichasApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!"; // sigma-ok
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
        [
            (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
            (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918"))
        ])))));

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAsync(string pin)
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    private static async Task<PaginaFichasDto> Pagina(HttpClient c, string consulta = "") =>
        (await c.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas" + consulta))!;

    private static async Task<string[]> Ids(HttpClient c, string consulta) => (await Pagina(c, consulta)).Itens.Select(i => i.Id).ToArray();

    [Fact]
    public async Task Sem_filtros_novos_continua_a_devolver_tudo_por_data_decrescente()
    {
        var chefe = await ComoAsync("603918");

        var p = await Pagina(chefe);

        Assert.Equal(7, p.Itens.Count);
        Assert.False(p.TemMais);
    }

    [Fact]
    public async Task Abertas_nao_incluem_fechadas_nem_canceladas_e_fechadas_so_as_fechadas()
    {
        var chefe = await ComoAsync("603918");

        Assert.Equal(["F1", "F2", "F3", "F4"], (await Ids(chefe, "?situacao=abertas&ordem=numero&desc=false")));
        Assert.Equal(["H1", "H2"], (await Ids(chefe, "?situacao=fechadas&ordem=numero&desc=false")));
        Assert.Equal(7, (await Ids(chefe, "?situacao=todas")).Length);
    }

    [Fact]
    public async Task Ficha_antiga_aberta_ou_fechada_entra_na_situacao_certa_e_sinaliza_por_supervisionar()
    {
        var repo = (RepositorioIntervencoesMemoria)_fabrica.Services.GetRequiredService<IRepositorioIntervencoes>();
        repo.SemearAntiga("A1");
        repo.SemearAntiga("A2", fechada: true);
        var chefe = await ComoAsync("603918");

        var abertas = (await Pagina(chefe, "?situacao=abertas")).Itens;
        var fechadas = (await Pagina(chefe, "?situacao=fechadas")).Itens;

        var a1 = Assert.Single(abertas, i => i.Id == "A1");
        Assert.True(a1.Antiga);
        Assert.False(a1.Fechada);
        Assert.True(a1.PorSupervisionar);
        Assert.DoesNotContain(abertas, i => i.Id == "A2");
        var a2 = Assert.Single(fechadas, i => i.Id == "A2");
        Assert.True(a2.Fechada);
        Assert.False(a2.PorSupervisionar);
    }

    [Fact]
    public async Task So_por_supervisionar_exclui_as_validadas()
    {
        var chefe = await ComoAsync("603918");

        var ids = await Ids(chefe, "?porSupervisionar=true&ordem=numero&desc=false");

        Assert.Equal(["F1", "F2", "F3", "F4", "H3"], ids);
    }

    [Fact]
    public async Task Tipo_filtra_preventiva_e_curativa()
    {
        var chefe = await ComoAsync("603918");

        Assert.Equal(["F3", "H2"], (await Ids(chefe, "?tipo=curativa&ordem=numero&desc=false")));
        Assert.DoesNotContain("F3", await Ids(chefe, "?tipo=preventiva"));
    }

    [Fact]
    public async Task Texto_encontra_pelo_numero_da_ficha()
    {
        var chefe = await ComoAsync("603918");

        Assert.Equal(["H3"], await Ids(chefe, "?q=7"));
    }

    [Fact]
    public async Task Ordenacao_por_numero_equipamento_e_total_nos_dois_sentidos()
    {
        var chefe = await ComoAsync("603918");
        await chefe.PostAsJsonAsync("/intervencoes/F3/materiais", new PedidoMaterial("Cabo", 2, 5m));

        Assert.Equal("F1", (await Ids(chefe, "?ordem=numero&desc=false"))[0]);
        Assert.Equal("H3", (await Ids(chefe, "?ordem=numero&desc=true"))[0]);
        Assert.Equal("F3", (await Ids(chefe, "?ordem=equipamento&desc=false"))[0]);
        Assert.Equal("H2", (await Ids(chefe, "?ordem=equipamento&desc=true"))[0]);
        Assert.Equal("F3", (await Ids(chefe, "?ordem=total&desc=true"))[0]);
        Assert.Equal(10m, (await Pagina(chefe, "?ordem=total&desc=true")).Itens[0].Total);
        Assert.Equal(0m, (await Pagina(chefe, "?ordem=total&desc=true")).Itens[^1].Total);
    }

    [Fact]
    public async Task Os_itens_trazem_numero_total_e_fechada()
    {
        var chefe = await ComoAsync("603918");

        var h1 = Assert.Single((await Pagina(chefe, "?situacao=fechadas")).Itens, i => i.Id == "H1");
        var f1 = Assert.Single((await Pagina(chefe, "?situacao=abertas")).Itens, i => i.Id == "F1");

        Assert.Equal(5, h1.Numero);
        Assert.True(h1.Fechada);
        Assert.False(h1.PorSupervisionar);
        Assert.Equal(1, f1.Numero);
        Assert.False(f1.Fechada);
        Assert.True(f1.PorSupervisionar);
    }

    [Theory]
    [InlineData("?situacao=nada")]
    [InlineData("?ordem=nada")]
    [InlineData("?tipo=nada")]
    public async Task Valores_desconhecidos_dao_400(string consulta)
    {
        var chefe = await ComoAsync("603918");

        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.GetAsync("/intervencoes/todas" + consulta)).StatusCode);
    }

    [Fact]
    public async Task So_o_supervisor_ve_a_lista()
    {
        var ana = await ComoAsync("482913");

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/intervencoes/todas?situacao=abertas")).StatusCode);
    }

    [Fact]
    public void A_ordem_do_sql_vem_de_uma_lista_fechada()
    {
        foreach (var ordem in Enum.GetValues<OrdemFichas>())
        {
            var asc = SqlRepositorioIntervencoes.OrdenarTodas(ordem, false);
            var desc = SqlRepositorioIntervencoes.OrdenarTodas(ordem, true);

            Assert.StartsWith(" order by ", asc);
            Assert.DoesNotContain("@", asc);
            Assert.Contains(" desc", desc);
            Assert.DoesNotContain(" desc", asc);
        }
    }
}
