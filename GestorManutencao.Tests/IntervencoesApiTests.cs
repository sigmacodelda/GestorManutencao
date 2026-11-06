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

    private readonly WebApplicationFactory<Program> _fabrica = Criar(null);

    // Ana (101) é técnica; "anasup" é a mesma pessoa (número 101) com perfil de supervisor; chefe (100) é outro supervisor.
    private static WebApplicationFactory<Program> Criar(ConfigManutencao? configuracao) => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
            [
                (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
                (new UtilizadorApp("rui", "Rui Costa", Perfil.Operador, true, true, Tecno: 102), Hasher.Hash("357159")),
                (new UtilizadorApp("novo", "Sem Numero", Perfil.Operador, true, false), Hasher.Hash("264813")),
                (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918")),
                (new UtilizadorApp("anasup", "Ana Silva", Perfil.Supervisor, true, false, Tecno: 101), Hasher.Hash("846213"))
            ])));
            if (configuracao is not null)
                s.Replace(ServiceDescriptor.Singleton<IRepositorioIntervencoes>(sp =>
                    new RepositorioIntervencoesMemoria(sp.GetRequiredService<TimeProvider>(), configuracao)));
        }));

    public void Dispose() => _fabrica.Dispose();

    private static async Task<HttpClient> ComoAsync(WebApplicationFactory<Program> fabrica, string pin)
    {
        var c = fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    private Task<HttpClient> ComoAsync(string pin) => ComoAsync(_fabrica, pin);

    private static Task<List<IntervencaoDto>> Lista(HttpClient c, string url) => c.GetFromJsonAsync<List<IntervencaoDto>>(url)!;

    [Fact]
    public async Task Sem_token_da_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().GetAsync("/intervencoes")).StatusCode);
    }

    [Fact]
    public async Task Com_pin_provisorio_nao_ve_intervencoes()
    {
        var c = await ComoAsync("357159");

        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/intervencoes")).StatusCode);
    }

    [Fact]
    public async Task Sem_numero_de_tecnico_nao_ha_fichas_e_a_resposta_explica()
    {
        var c = await ComoAsync("264813");

        var r = await c.GetAsync("/intervencoes");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("sem_tecno", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Lista_so_o_que_e_do_tecnico_ou_nao_tem_dono_dentro_da_antecedencia()
    {
        var c = await ComoAsync("482913");

        var lista = await Lista(c, "/intervencoes");

        Assert.Equal("F1", Assert.Single(lista).Id);
        Assert.Equal(EstadoIntervencao.Planeada, lista[0].Estado);
    }

    [Fact]
    public async Task Iniciar_e_concluir_segue_o_circuito_e_gera_a_seguinte()
    {
        var c = await ComoAsync("482913");

        var inicio = await c.PostAsync("/intervencoes/F1/iniciar", null);
        var iniciada = (await inicio.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(EstadoIntervencao.EmCurso, iniciada.Estado);
        Assert.Equal(101, iniciada.Tecno);

        var fim = await c.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("Limpeza feita", new TimeOnly(8, 0), new TimeOnly(9, 0)));
        Assert.Equal(EstadoIntervencao.Concluida, (await fim.Content.ReadFromJsonAsync<IntervencaoDto>())!.Estado);

        var chefe = await ComoAsync("603918");
        var planeadas = await Lista(chefe, "/planeadas");
        Assert.Contains(planeadas, i => i.Equipamento == "Compressor de ar 02" && i.Estado == EstadoIntervencao.Planeada && i.Id != "F1");
    }

    [Fact]
    public async Task Iniciar_duas_vezes_da_409_e_concluir_sem_iniciar_tambem()
    {
        var c = await ComoAsync("482913");

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("x", new TimeOnly(8, 0), new TimeOnly(9, 0)))).StatusCode);
        await c.PostAsync("/intervencoes/F1/iniciar", null);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/intervencoes/F1/iniciar", null)).StatusCode);
    }

    [Fact]
    public async Task Ficha_de_outra_pessoa_nao_aparece_nem_se_mexe()
    {
        var c = await ComoAsync("482913");

        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/intervencoes/F2/iniciar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/intervencoes/F2")).StatusCode);
    }

    [Fact]
    public async Task Fora_de_ordem_so_se_a_configuracao_nao_deixar_avancar()
    {
        using var restrita = Criar(new ConfigManutencao(AvancaOrdem: false, ExtExigeDoc: false));
        var c = await ComoAsync(restrita, "482913");

        var r = await c.PostAsync("/intervencoes/F4/iniciar", null);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("fora_de_ordem", await r.Content.ReadAsStringAsync());

        var livre = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.OK, (await livre.PostAsync("/intervencoes/F4/iniciar", null)).StatusCode);
    }

    [Theory]
    [InlineData("", "Não liga")]
    [InlineData("E2", "")]
    public async Task Avaria_incompleta_da_400(string equipamento, string anomalia)
    {
        var c = await ComoAsync("482913");

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria(equipamento, anomalia))).StatusCode);
    }

    [Fact]
    public async Task Avaria_nasce_em_curso_com_o_tecnico_e_fica_na_lista()
    {
        var c = await ComoAsync("482913");

        var r = await c.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria("E2", "Trava a meio da subida"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var nova = (await r.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(EstadoIntervencao.EmCurso, nova.Estado);
        Assert.Equal(TipoIntervencao.Curativa, nova.Tipo);
        Assert.Equal(101, nova.Tecno);
        Assert.Contains(await Lista(c, "/intervencoes"), i => i.Id == nova.Id);
    }

    [Fact]
    public async Task Conclusao_com_texto_demasiado_longo_da_400()
    {
        var c = await ComoAsync("482913");
        await c.PostAsync("/intervencoes/F1/iniciar", null);

        var r = await c.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao(new string('x', 255), new TimeOnly(8, 0), new TimeOnly(9, 0)));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Pesquisa_de_equipamentos_pede_pelo_menos_duas_letras()
    {
        var c = await ComoAsync("482913");

        Assert.Empty((await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?q=e"))!);
        Assert.NotEmpty((await c.GetFromJsonAsync<List<EquipamentoDto>>("/equipamentos?q=emp"))!);
    }

    [Fact]
    public async Task Operador_nao_ve_nem_valida_fichas_nem_atribui()
    {
        var c = await ComoAsync("482913");

        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/validacao")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/validacao/F3", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/planeadas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync("/planeadas/F1/tecnico", new PedidoAtribuicao(101))).StatusCode);
    }

    [Fact]
    public async Task Supervisor_valida_ficha_concluida_uma_so_vez()
    {
        var c = await ComoAsync("603918");

        var lista = await Lista(c, "/validacao");
        var ficha = Assert.Single(lista);
        Assert.Equal("F3", ficha.Id);
        Assert.Equal("Ana Silva", ficha.Tecnico);
        Assert.True(ficha.PodeValidar);

        var ok = await c.PostAsync("/validacao/F3", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(EstadoIntervencao.Validada, (await ok.Content.ReadFromJsonAsync<IntervencaoDto>())!.Estado);
        Assert.Empty(await Lista(c, "/validacao"));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/validacao/F3", null)).StatusCode);
    }

    [Fact]
    public async Task Quem_concluiu_nao_valida_a_propria_ficha_pelo_numero_do_PHC()
    {
        var c = await ComoAsync("846213");

        Assert.False(Assert.Single(await Lista(c, "/validacao")).PodeValidar);
        var r = await c.PostAsync("/validacao/F3", null);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("propria", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Validar_ficha_que_nao_esta_concluida_da_409_e_inexistente_404()
    {
        var c = await ComoAsync("603918");

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/validacao/F1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/validacao/nao-existe", null)).StatusCode);
    }

    [Fact]
    public async Task Devolver_exige_motivo_e_poe_a_ficha_outra_vez_em_curso()
    {
        var c = await ComoAsync("603918");

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/validacao/F3/devolver", new PedidoDevolucao(" "))).StatusCode);
        var r = await c.PostAsJsonAsync("/validacao/F3/devolver", new PedidoDevolucao("Falta registar o tempo"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(EstadoIntervencao.EmCurso, (await r.Content.ReadFromJsonAsync<IntervencaoDto>())!.Estado);
        Assert.Empty(await Lista(c, "/validacao"));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/validacao/F3/devolver", new PedidoDevolucao("outra vez"))).StatusCode);
    }

    [Fact]
    public async Task Supervisor_ve_as_planeadas_de_todos_e_atribui_tecnico()
    {
        var c = await ComoAsync("603918");

        var todas = await Lista(c, "/planeadas");
        Assert.Contains(todas, i => i.Id == "F2" && i.Tecno == 999);
        Assert.DoesNotContain(todas, i => i.Id == "F3");

        var atribuida = await c.PutAsJsonAsync("/planeadas/F1/tecnico", new PedidoAtribuicao(101));
        Assert.Equal(HttpStatusCode.OK, atribuida.StatusCode);
        var ficha = (await atribuida.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(101, ficha.Tecno);
        Assert.Equal("Ana Silva", ficha.Tecnico);

        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/planeadas/F1/tecnico", new PedidoAtribuicao(555))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/planeadas/F3/tecnico", new PedidoAtribuicao(101))).StatusCode);

        var livre = await c.PutAsJsonAsync("/planeadas/F1/tecnico", new PedidoAtribuicao(0));
        Assert.Equal(0, (await livre.Content.ReadFromJsonAsync<IntervencaoDto>())!.Tecno);
    }

    [Fact]
    public async Task Atribuir_a_outro_tira_a_ficha_ao_tecnico_anterior()
    {
        var chefe = await ComoAsync("603918");
        var ana = await ComoAsync("482913");
        Assert.Single(await Lista(ana, "/intervencoes"));

        await chefe.PutAsJsonAsync("/planeadas/F1/tecnico", new PedidoAtribuicao(102));

        Assert.Empty(await Lista(ana, "/intervencoes"));
    }

    [Fact]
    public async Task Materiais_so_se_lancam_com_a_ficha_em_curso_e_somam_o_custo()
    {
        var c = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Filtro", 2, 5.5m))).StatusCode);
        await c.PostAsync("/intervencoes/F1/iniciar", null);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("", 2, 5.5m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Filtro", 0, 5.5m))).StatusCode);

        var r = await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Filtro de ar", 2, 5.5m));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var material = (await r.Content.ReadFromJsonAsync<MaterialDto>())!;
        Assert.Equal(11m, material.Custo);
        Assert.Single((await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais"))!);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/intervencoes/F1/materiais/{material.Id}")).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais"))!);
    }

    [Fact]
    public async Task Materiais_de_ficha_alheia_nao_se_veem()
    {
        var c = await ComoAsync("482913");

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/intervencoes/F2/materiais")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/intervencoes/F2/materiais", new PedidoMaterial("Filtro", 1, 1))).StatusCode);
    }

    [Fact]
    public async Task Supervisor_cria_operador_com_numero_do_PHC_que_nao_se_repete()
    {
        var c = await ComoAsync("603918");

        var criado = await c.PostAsJsonAsync("/utilizadores", new PedidoNovoUtilizador("joao", "João Pereira", 19));
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);

        var repetido = await c.PostAsJsonAsync("/utilizadores", new PedidoNovoUtilizador("maria", "Maria Lopes", 19));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Contains("tecno_em_uso", await repetido.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/utilizadores/joao/tecno", new PedidoTecno(20))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/utilizadores/joao/tecno", new PedidoTecno(101))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/utilizadores/ninguem/tecno", new PedidoTecno(20))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/utilizadores/joao/tecno", new PedidoTecno(-1))).StatusCode);

        var lista = (await c.GetFromJsonAsync<List<UtilizadorApp>>("/utilizadores"))!;
        Assert.Equal(20, lista.Single(u => u.Codigo == "joao").Tecno);
    }
}
