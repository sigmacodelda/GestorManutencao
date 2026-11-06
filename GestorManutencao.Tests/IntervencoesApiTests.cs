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

public class IntervencoesApiTests : IDisposable
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
                (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false), Hasher.Hash("482913")),
                (new UtilizadorApp("rui", "Rui Costa", Perfil.Operador, true, true), Hasher.Hash("357159"))
            ])));
        }));

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAsync(string pin)
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    [Fact]
    public async Task Sem_token_da_401()
    {
        var r = await _fabrica.CreateClient().GetAsync("/intervencoes");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Com_pin_provisorio_nao_ve_intervencoes()
    {
        var c = await ComoAsync("357159");

        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/intervencoes")).StatusCode);
    }

    [Fact]
    public async Task Lista_so_o_que_e_do_tecnico_ou_nao_tem_dono()
    {
        var c = await ComoAsync("482913");

        var lista = (await c.GetFromJsonAsync<List<IntervencaoDto>>("/intervencoes"))!;

        Assert.Single(lista);
        Assert.Equal(EstadoIntervencao.Planeada, lista[0].Estado);
    }

    [Fact]
    public async Task Iniciar_e_concluir_segue_o_circuito()
    {
        var c = await ComoAsync("482913");
        var id = (await c.GetFromJsonAsync<List<IntervencaoDto>>("/intervencoes"))![0].Id;

        var inicio = await c.PostAsync($"/intervencoes/{id}/iniciar", null);
        Assert.Equal(HttpStatusCode.OK, inicio.StatusCode);
        Assert.Equal(EstadoIntervencao.EmCurso, (await inicio.Content.ReadFromJsonAsync<IntervencaoDto>())!.Estado);

        var fim = await c.PostAsJsonAsync($"/intervencoes/{id}/concluir", new PedidoConclusao("Limpeza feita", new TimeOnly(8, 0), new TimeOnly(9, 0)));
        Assert.Equal(EstadoIntervencao.Concluida, (await fim.Content.ReadFromJsonAsync<IntervencaoDto>())!.Estado);
    }

    [Fact]
    public async Task Iniciar_duas_vezes_da_409()
    {
        var c = await ComoAsync("482913");
        var id = (await c.GetFromJsonAsync<List<IntervencaoDto>>("/intervencoes"))![0].Id;
        await c.PostAsync($"/intervencoes/{id}/iniciar", null);

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/intervencoes/{id}/iniciar", null)).StatusCode);
    }

    [Fact]
    public async Task Ficha_de_outra_pessoa_nao_aparece_nem_se_mexe()
    {
        var c = await ComoAsync("482913");

        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/intervencoes/F2/iniciar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/intervencoes/F2")).StatusCode);
    }

    [Theory]
    [InlineData("", "Não liga")]
    [InlineData("E2", "")]
    public async Task Avaria_incompleta_da_400(string equipamento, string anomalia)
    {
        var c = await ComoAsync("482913");

        var r = await c.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria(equipamento, anomalia));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Avaria_nasce_em_curso_e_fica_na_lista()
    {
        var c = await ComoAsync("482913");

        var r = await c.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria("E2", "Trava a meio da subida"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var nova = (await r.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(EstadoIntervencao.EmCurso, nova.Estado);
        Assert.Equal(TipoIntervencao.Curativa, nova.Tipo);
        Assert.Contains(await c.GetFromJsonAsync<List<IntervencaoDto>>("/intervencoes") ?? [], i => i.Id == nova.Id);
    }

    [Fact]
    public async Task Conclusao_com_texto_demasiado_longo_da_400()
    {
        var c = await ComoAsync("482913");
        var id = (await c.GetFromJsonAsync<List<IntervencaoDto>>("/intervencoes"))![0].Id;
        await c.PostAsync($"/intervencoes/{id}/iniciar", null);

        var r = await c.PostAsJsonAsync($"/intervencoes/{id}/concluir", new PedidoConclusao(new string('x', 255), new TimeOnly(8, 0), new TimeOnly(9, 0)));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Pesquisa_de_equipamentos_pede_pelo_menos_duas_letras()
    {
        var c = await ComoAsync("482913");

        Assert.Empty((await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?q=e"))!);
        Assert.NotEmpty((await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?q=emp"))!);
    }
}
