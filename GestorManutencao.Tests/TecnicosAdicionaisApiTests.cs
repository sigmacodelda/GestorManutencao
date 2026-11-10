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

public class TecnicosAdicionaisHelperTests
{
    [Fact]
    public void Le_normaliza_ordena_e_tira_duplicados()
    {
        Assert.Equal([12, 45], RegrasIntervencao.LerTecnos(";45;12;12;"));
        Assert.Equal(";12;45;", RegrasIntervencao.EscreverTecnos([45, 12, 12]));
        Assert.Equal("", RegrasIntervencao.EscreverTecnos([]));
    }

    [Fact]
    public void Escrever_nunca_inclui_o_principal_nem_numeros_fora_de_1_a_99999()
    {
        Assert.Equal(";12;", RegrasIntervencao.EscreverTecnos([45, 12, 0, -3, 100000], principal: 45));
        Assert.Equal(";99999;", RegrasIntervencao.EscreverTecnos([99999, 100000]));
        Assert.Equal("", RegrasIntervencao.EscreverTecnos([7], principal: 7));
    }

    [Fact]
    public void Ler_e_tolerante_a_lixo()
    {
        Assert.Empty(RegrasIntervencao.LerTecnos(null));
        Assert.Empty(RegrasIntervencao.LerTecnos(""));
        Assert.Empty(RegrasIntervencao.LerTecnos(";"));
        Assert.Equal([7, 99999], RegrasIntervencao.LerTecnos("abc;;-5;0;99999;100000;1e3; 7 ;;3.5;"));
    }

    [Fact]
    public void O_12_nao_apanha_o_123()
    {
        Assert.False(RegrasIntervencao.EstaNaLista(";123;", 12));
        Assert.True(RegrasIntervencao.EstaNaLista(";12;123;", 12));
        Assert.True(RegrasIntervencao.EstaNaLista(";123;", 123));
        Assert.Equal("%;12;%", RegrasIntervencao.PadraoTecno(12));
        Assert.DoesNotContain(RegrasIntervencao.PadraoTecno(12).Trim('%'), ";123;");
        Assert.False(RegrasIntervencao.EstaNaLista(";12;", 0));
    }

    [Fact]
    public void Principal_adicional_e_sem_dono()
    {
        Assert.True(RegrasIntervencao.PertenceAFicha(5, ";12;", 5));
        Assert.True(RegrasIntervencao.PertenceAFicha(5, ";12;", 12));
        Assert.False(RegrasIntervencao.PertenceAFicha(5, ";12;", 13));
        Assert.False(RegrasIntervencao.PertenceAFicha(0, "", 13));
        Assert.True(RegrasIntervencao.PodeMexer(0, "", 13));
        Assert.False(RegrasIntervencao.PodeMexer(5, ";12;", 13));
        Assert.Null(RegrasIntervencao.AdicionaisDe(";5;", 5));
        Assert.Equal([12], RegrasIntervencao.AdicionaisDe(";5;12;", 5)!.Select(a => a.Tecno));
    }
}

public class TecnicosAdicionaisApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly WebApplicationFactory<Program> _fabrica = Criar(null);

    // ana (101) e joao (103) são técnicos; chefe (100) e anasup (102) são supervisores; velho (104) está inativo.
    private static WebApplicationFactory<Program> Criar(ConfigManutencao? configuracao) => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
            [
                (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
                (new UtilizadorApp("joao", "João Mota", Perfil.Operador, true, false, Tecno: 103), Hasher.Hash("715938")),
                (new UtilizadorApp("velho", "Velho Antigo", Perfil.Operador, false, false, Tecno: 104), Hasher.Hash("264813")),
                (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918")),
                (new UtilizadorApp("anasup", "Ana Silva", Perfil.Supervisor, true, false, Tecno: 102), Hasher.Hash("846213"))
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
    private Task<HttpClient> Chefe() => ComoAsync("603918");
    private Task<HttpClient> Ana() => ComoAsync("482913");
    private Task<HttpClient> Joao() => ComoAsync("715938");

    private static Task<List<IntervencaoDto>> Lista(HttpClient c, string url) => c.GetFromJsonAsync<List<IntervencaoDto>>(url)!;

    private static Task<HttpResponseMessage> Adicionais(HttpClient c, string id, params int[] tecnos) =>
        c.PutAsJsonAsync($"/planeadas/{id}/tecnicos", new PedidoAdicionais(tecnos));

    private static ByteArrayContent Jpeg() => new([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4]) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } };

    [Fact]
    public async Task Adicional_ve_inicia_mexe_em_material_e_fotos_e_conclui()
    {
        var chefe = await Chefe();
        var resposta = await Adicionais(chefe, "F2", 101);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var ficha = (await resposta.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(999, ficha.Tecno);
        var adicional = Assert.Single(ficha.Adicionais!);
        Assert.Equal((101, "Ana Silva"), (adicional.Tecno, adicional.Nome));

        var ana = await Ana();
        Assert.Contains(await Lista(ana, "/intervencoes"), i => i.Id == "F2");
        var vista = (await ana.GetFromJsonAsync<IntervencaoDto>("/intervencoes/F2"))!;
        Assert.Equal("Ana Silva", Assert.Single(vista.Adicionais!).Nome);

        var iniciada = (await (await ana.PostAsync("/intervencoes/F2/iniciar", null)).Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(EstadoIntervencao.EmCurso, iniciada.Estado);
        Assert.Equal(999, iniciada.Tecno);

        var material = await ana.PostAsJsonAsync("/intervencoes/F2/materiais", new PedidoMaterial("Filtro", 2, 5.5m));
        Assert.Equal(HttpStatusCode.OK, material.StatusCode);
        var m = (await material.Content.ReadFromJsonAsync<MaterialDto>())!;
        Assert.Single((await ana.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F2/materiais"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await ana.DeleteAsync($"/intervencoes/F2/materiais/{m.Id}")).StatusCode);

        var foto = await ana.PostAsync("/intervencoes/F2/fotos?nome=a", Jpeg());
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        var f = (await foto.Content.ReadFromJsonAsync<FotoDto>())!;
        Assert.Single((await ana.GetFromJsonAsync<List<FotoDto>>("/intervencoes/F2/fotos"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await ana.DeleteAsync($"/intervencoes/F2/fotos/{f.Id}")).StatusCode);

        var fim = await ana.PostAsJsonAsync("/intervencoes/F2/concluir", new PedidoConclusao("Feito em equipa", new TimeOnly(8, 0), new TimeOnly(9, 0)));
        Assert.Equal(EstadoIntervencao.Concluida, (await fim.Content.ReadFromJsonAsync<IntervencaoDto>())!.Estado);
    }

    [Fact]
    public async Task Principal_e_adicional_podem_repetir_o_iniciar_sem_erro()
    {
        await Adicionais(await Chefe(), "F2", 101);
        var ana = await Ana();
        await ana.PostAsync("/intervencoes/F2/iniciar", null);

        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsync("/intervencoes/F2/iniciar", null)).StatusCode);
    }

    [Fact]
    public async Task Tecnico_fora_da_lista_fica_bloqueado()
    {
        var chefe = await Chefe();
        await Adicionais(chefe, "F2", 101);
        await (await Ana()).PostAsync("/intervencoes/F2/iniciar", null);

        var joao = await Joao();
        Assert.DoesNotContain(await Lista(joao, "/intervencoes"), i => i.Id == "F2");
        Assert.Equal(HttpStatusCode.NotFound, (await joao.GetAsync("/intervencoes/F2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await joao.PostAsync("/intervencoes/F2/iniciar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await joao.PostAsJsonAsync("/intervencoes/F2/concluir", new PedidoConclusao("x", new TimeOnly(8, 0), new TimeOnly(9, 0)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await joao.PostAsJsonAsync("/intervencoes/F2/materiais", new PedidoMaterial("Filtro", 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await joao.PostAsync("/intervencoes/F2/fotos?nome=a", Jpeg())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await joao.GetAsync("/intervencoes/F2/fotos")).StatusCode);
    }

    [Fact]
    public async Task Retirado_da_lista_o_tecnico_perde_o_acesso()
    {
        var chefe = await Chefe();
        await Adicionais(chefe, "F2", 101);
        var ana = await Ana();
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/intervencoes/F2")).StatusCode);

        var livre = (await (await Adicionais(chefe, "F2")).Content.ReadFromJsonAsync<IntervencaoDto>())!;

        Assert.Null(livre.Adicionais);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync("/intervencoes/F2")).StatusCode);
    }

    [Fact]
    public async Task Endpoint_so_para_supervisor()
    {
        var ana = await Ana();

        Assert.Equal(HttpStatusCode.Forbidden, (await Adicionais(ana, "F2", 103)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().PutAsJsonAsync("/planeadas/F2/tecnicos", new PedidoAdicionais([103]))).StatusCode);
    }

    [Fact]
    public async Task Tecnico_inexistente_ou_inativo_da_404_e_valores_invalidos_da_400()
    {
        var chefe = await Chefe();

        Assert.Equal(HttpStatusCode.NotFound, (await Adicionais(chefe, "F2", 555)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Adicionais(chefe, "F2", 101, 104)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Adicionais(chefe, "NAOEXISTE", 101)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Adicionais(chefe, "F2", 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Adicionais(chefe, "F2", 100000)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PutAsJsonAsync("/planeadas/F2/tecnicos", new { tecnos = (int[]?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Adicionais(chefe, "F2", Enumerable.Range(1, RegrasIntervencao.MaxAdicionais + 1).ToArray())).StatusCode);
    }

    [Fact]
    public async Task O_principal_na_lista_e_ignorado_e_os_duplicados_caem()
    {
        var chefe = await Chefe();
        await chefe.PutAsJsonAsync("/planeadas/F1/tecnico", new PedidoAtribuicao(103));

        var ficha = (await (await Adicionais(chefe, "F1", 103, 101, 101, 100)).Content.ReadFromJsonAsync<IntervencaoDto>())!;

        Assert.Equal(103, ficha.Tecno);
        Assert.Equal([100, 101], ficha.Adicionais!.Select(a => a.Tecno));
    }

    [Fact]
    public async Task Ficha_fechada_ou_sem_tecnico_principal_da_409()
    {
        var chefe = await Chefe();

        Assert.Equal(HttpStatusCode.Conflict, (await Adicionais(chefe, "F3", 103)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Adicionais(chefe, "F1", 103)).StatusCode);
    }

    [Fact]
    public async Task Mudar_o_principal_tira_o_novo_principal_dos_adicionais_e_sem_dono_limpa_tudo()
    {
        var chefe = await Chefe();
        await Adicionais(chefe, "F2", 101, 103);

        var trocada = (await (await chefe.PutAsJsonAsync("/planeadas/F2/tecnico", new PedidoAtribuicao(101))).Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(101, trocada.Tecno);
        Assert.Equal([103], trocada.Adicionais!.Select(a => a.Tecno));

        var livre = (await (await chefe.PutAsJsonAsync("/planeadas/F2/tecnico", new PedidoAtribuicao(0))).Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Null(livre.Adicionais);
    }

    [Fact]
    public async Task Supervisor_adicional_nao_valida_a_propria_ficha_por_defeito()
    {
        var chefe = await Chefe();
        await Adicionais(chefe, "F2", 100);
        await chefe.PostAsync("/intervencoes/F2/iniciar", null);
        await chefe.PostAsJsonAsync("/intervencoes/F2/concluir", new PedidoConclusao("Feito", new TimeOnly(8, 0), new TimeOnly(9, 0)));

        var r = await chefe.PostAsync("/validacao/F2", null);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("propria", await r.Content.ReadAsStringAsync());
        Assert.False((await Lista(chefe, "/validacao")).Single(i => i.Id == "F2").PodeValidar);

        var outro = await ComoAsync("846213");
        Assert.Equal(HttpStatusCode.OK, (await outro.PostAsync("/validacao/F2", null)).StatusCode);
    }

    [Fact]
    public async Task Com_validar_propria_ligado_o_supervisor_adicional_valida()
    {
        using var fabrica = Criar(new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false, ValidarPropria: true));
        var chefe = await ComoAsync(fabrica, "603918");
        await Adicionais(chefe, "F2", 100);
        await chefe.PostAsync("/intervencoes/F2/iniciar", null);
        await chefe.PostAsJsonAsync("/intervencoes/F2/concluir", new PedidoConclusao("Feito", new TimeOnly(8, 0), new TimeOnly(9, 0)));

        Assert.True((await Lista(chefe, "/validacao")).Single(i => i.Id == "F2").PodeValidar);
        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsync("/validacao/F2", null)).StatusCode);
    }

    [Fact]
    public async Task Filtro_por_tecnico_apanha_principal_e_adicionais_mas_a_pesquisa_de_texto_so_o_principal()
    {
        var chefe = await Chefe();
        await Adicionais(chefe, "F2", 101);

        var da101 = (await chefe.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas?tecno=101"))!.Itens.Select(i => i.Id).ToList();
        Assert.Contains("F3", da101);
        Assert.Contains("F2", da101);

        var da999 = (await chefe.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas?tecno=999"))!.Itens.Select(i => i.Id).ToList();
        Assert.Equal(["F2"], da999);

        Assert.Empty((await chefe.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas?tecno=103"))!.Itens);

        var porNome = (await chefe.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas?q=Ana"))!.Itens.Select(i => i.Id).ToList();
        Assert.Contains("F3", porNome);
        Assert.DoesNotContain("F2", porNome);
    }

    [Fact]
    public async Task Planeadas_do_supervisor_trazem_os_adicionais_com_nome()
    {
        var chefe = await Chefe();
        await Adicionais(chefe, "F2", 103, 101);

        var f2 = (await Lista(chefe, "/planeadas")).Single(i => i.Id == "F2");

        Assert.Equal(["Ana Silva", "João Mota"], f2.Adicionais!.Select(a => a.Nome));
    }

    [Fact]
    public async Task Recentes_do_adicional_incluem_as_fichas_fechadas_onde_participou()
    {
        var chefe = await Chefe();
        var ana = await Ana();
        await Adicionais(chefe, "F2", 103);
        var joao = await Joao();
        await joao.PostAsync("/intervencoes/F2/iniciar", null);
        await joao.PostAsJsonAsync("/intervencoes/F2/concluir", new PedidoConclusao("Feito", new TimeOnly(8, 0), new TimeOnly(9, 0)));

        Assert.Contains(await Lista(joao, "/intervencoes/recentes"), i => i.Id == "F2");
        Assert.DoesNotContain(await Lista(ana, "/intervencoes/recentes"), i => i.Id == "F2");
    }

    [Fact]
    public async Task Adicional_que_ficou_inativo_pode_ser_mantido_e_retirado_mas_nao_acrescentado()
    {
        var chefe = await Chefe();
        Assert.Equal(HttpStatusCode.OK, (await Adicionais(chefe, "F2", 103)).StatusCode);
        ((RepositorioUtilizadoresMemoria)_fabrica.Services.GetRequiredService<IRepositorioUtilizadores>()).Desativar("joao");

        Assert.Equal(HttpStatusCode.OK, (await Adicionais(chefe, "F2", 103, 101)).StatusCode);
        var retirado = (await (await Adicionais(chefe, "F2", 101)).Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal([101], retirado.Adicionais!.Select(a => a.Tecno));

        var voltar = await Adicionais(chefe, "F2", 101, 103);
        Assert.Equal(HttpStatusCode.NotFound, voltar.StatusCode);
        Assert.Contains("tecnico_invalido", await voltar.Content.ReadAsStringAsync());
        var semFicha = await Adicionais(chefe, "NAOEXISTE", 101);
        Assert.Equal(HttpStatusCode.NotFound, semFicha.StatusCode);
        Assert.DoesNotContain("tecnico_invalido", await semFicha.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Historico_do_equipamento_mostra_o_conteudo_ao_adicional_e_esconde_ao_terceiro()
    {
        await Adicionais(await Chefe(), "F2", 101);
        var ana = await Ana();
        await ana.PostAsync("/intervencoes/F2/iniciar", null);

        var daAna = (await ana.GetFromJsonAsync<FichaEquipamentoDto>("/equipamentos/E3"))!.Historico.Single(h => h.Id == "F2");
        var doJoao = (await (await Joao()).GetFromJsonAsync<FichaEquipamentoDto>("/equipamentos/E3"))!.Historico.Single(h => h.Id == "F2");

        Assert.Equal("Outra Pessoa", daAna.Tecnico);
        Assert.Equal("Ana Silva", Assert.Single(daAna.Adicionais!).Nome);
        Assert.True(daAna.PodeAbrir);
        Assert.Null(doJoao.Tecnico);
        Assert.Null(doJoao.Adicionais);
        Assert.False(doJoao.PodeAbrir);
    }

    [Fact]
    public async Task Atribuir_a_um_adicional_numa_ficha_em_curso_troca_o_principal_e_mantem_o_resto()
    {
        var chefe = await Chefe();
        await Adicionais(chefe, "F2", 101, 103);
        await (await Ana()).PostAsync("/intervencoes/F2/iniciar", null);

        var r = (await (await chefe.PutAsJsonAsync("/planeadas/F2/tecnico", new PedidoAtribuicao(103))).Content.ReadFromJsonAsync<IntervencaoDto>())!;

        Assert.Equal(EstadoIntervencao.EmCurso, r.Estado);
        Assert.Equal(103, r.Tecno);
        Assert.Equal([101], r.Adicionais!.Select(a => a.Tecno));
        Assert.Equal(HttpStatusCode.OK, (await (await Joao()).GetAsync("/intervencoes/F2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await Ana()).GetAsync("/intervencoes/F2")).StatusCode);
    }
}
