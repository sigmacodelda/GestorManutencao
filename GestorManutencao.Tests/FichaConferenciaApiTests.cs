using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

// Ficha com conferência: abrir ao iniciar, material com lugar, validar (stock real), devolver e cancelar.
public class FichaConferenciaApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly PecasEspia _pecas = new(new RepositorioPecasMemoria(
    [
        new PecaDto("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
        new PecaDto("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
    ]));

    private readonly LugaresEspia _lugares;
    private RecusaProxy _recusa = null!;
    private RepositorioIntervencoesMemoria? _interno;

    private readonly WebApplicationFactory<Program> _fabrica;

    public FichaConferenciaApiTests()
    {
        _lugares = new LugaresEspia(new RepositorioLocalizacoesMemoria(_pecas));
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
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
                    _interno = new RepositorioIntervencoesMemoria(sp.GetRequiredService<TimeProvider>(), new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false));
                    var proxy = DispatchProxy.Create<IRepositorioIntervencoes, RecusaProxy>();
                    _recusa = (RecusaProxy)(object)proxy;
                    _recusa.Alvo = _interno;
                    return proxy;
                }));
                s.Replace(ServiceDescriptor.Singleton<IRepositorioGestao>(sp => { sp.GetRequiredService<IRepositorioIntervencoes>(); return _interno!; }));
                s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(_pecas));
                s.Replace(ServiceDescriptor.Singleton<IRepositorioLocalizacoes>(_lugares));
            }));
    }

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAsync(string pin)
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    private async Task<HttpClient> ComoAnaAsync()
    {
        var c = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/intervencoes/F1/iniciar", null)).StatusCode);
        return c;
    }

    private static async Task<PecaDto> PecaAsync(HttpClient c, string codigo = "P001") => (await c.GetFromJsonAsync<PecaDto>($"/pecas/codigo/{codigo}"))!;

    private static async Task<LocalizacoesPecaDto> LugaresAsync(HttpClient c, string codigo = "P001") =>
        (await c.GetFromJsonAsync<LocalizacoesPecaDto>($"/pecas/{codigo}/localizacoes"))!;

    private static async Task<decimal> NoLugarAsync(HttpClient c, string texto, string codigo = "P001") =>
        (await LugaresAsync(c, codigo)).Lugares.Single(l => l.Texto == texto).Qtt;

    private static Task<HttpResponseMessage> JuntarAsync(HttpClient c, decimal qtt, string? local = null, string peca = "P001") =>
        c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Peça", qtt, 1m, peca, local));

    private static async Task<List<MaterialDto>> ListaAsync(HttpClient c) => (await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais"))!;

    private static async Task Arrumar(HttpClient c, string local, decimal qtt) =>
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", local, qtt))).StatusCode);

    private static async Task<HttpResponseMessage> ConcluirAsync(HttpClient c) =>
        await c.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("Feito", new TimeOnly(8, 0), new TimeOnly(9, 0)));

    [Fact]
    public async Task Iniciar_abre_a_conferencia_da_ficha()
    {
        await ComoAnaAsync();
        Assert.Equal(["F1"], _pecas.Abertas);
    }

    [Fact]
    public async Task Iniciar_com_falha_ao_abrir_a_conferencia_responde_200_e_o_material_abre_depois()
    {
        _pecas.FalharAbrir = true;
        var c = await ComoAnaAsync();
        _pecas.FalharAbrir = false;

        var r = await JuntarAsync(c, 2);

        Assert.True((await r.Content.ReadFromJsonAsync<MaterialDto>())!.SaidaDada);
        Assert.Equal(8, (await PecaAsync(c)).Disponivel);
    }

    [Fact]
    public async Task Material_com_lugar_unico_desconta_do_lugar_e_so_o_disponivel_desce()
    {
        var c = await ComoAnaAsync();

        var m = (await (await JuntarAsync(c, 2)).Content.ReadFromJsonAsync<MaterialDto>())!;

        Assert.True(m.SaidaDada);
        Assert.Equal("A-02-1-01", m.Local);
        Assert.Null(m.AvisoStock);
        var p = await PecaAsync(c);
        Assert.Equal(10, p.Stock);
        Assert.Equal(8, p.Disponivel);
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01"));
        Assert.Equal("A-02-1-01", Assert.Single(await ListaAsync(c)).Local);
    }

    [Fact]
    public async Task Local_como_etiqueta_LOC_e_aceite()
    {
        var c = await ComoAnaAsync();
        var m = (await (await JuntarAsync(c, 1, "LOC-A-02-1-01")).Content.ReadFromJsonAsync<MaterialDto>())!;
        Assert.Equal("A-02-1-01", m.Local);
        Assert.Equal(2, await NoLugarAsync(c, "A-02-1-01"));
    }

    [Fact]
    public async Task Varios_lugares_sem_local_da_409_com_os_lugares_e_nada_se_grava()
    {
        var c = await ComoAnaAsync();
        await Arrumar(c, "A-03-2-01", 2);

        var r = await JuntarAsync(c, 1);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var corpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("escolha_o_lugar", corpo.GetProperty("erro").GetString());
        Assert.Equal(2, corpo.GetProperty("lugares").GetArrayLength());
        Assert.Empty(await ListaAsync(c));
        Assert.Equal(10, (await PecaAsync(c)).Disponivel);

        var ok = (await (await JuntarAsync(c, 1, "A-03-2-01")).Content.ReadFromJsonAsync<MaterialDto>())!;
        Assert.Equal("A-03-2-01", ok.Local);
        Assert.Equal(1, await NoLugarAsync(c, "A-03-2-01"));
        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));
    }

    [Fact]
    public async Task Lugar_sem_quantidade_suficiente_grava_na_mesma_e_avisa()
    {
        var c = await ComoAnaAsync();

        var r = await JuntarAsync(c, 5, "A-02-1-01");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var m = (await r.Content.ReadFromJsonAsync<MaterialDto>())!;
        Assert.True(m.SaidaDada);
        Assert.Contains("A-02-1-01", m.AvisoStock);
        Assert.Null(m.Local);
        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));
        Assert.Equal(5, (await PecaAsync(c)).Disponivel);
    }

    [Fact]
    public async Task Peca_sem_lugar_grava_e_avisa_que_nao_tem_localizacao()
    {
        var c = await ComoAnaAsync();

        var r = await JuntarAsync(c, 1, peca: "P002");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var m = (await r.Content.ReadFromJsonAsync<MaterialDto>())!;
        Assert.True(m.SaidaDada);
        Assert.Contains("sem localização", m.AvisoStock);
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("A-02-1")] // falta a posição
    public async Task Local_invalido_ou_incompleto_da_400_e_nao_cria_material(string local)
    {
        var c = await ComoAnaAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await JuntarAsync(c, 1, local)).StatusCode);
        Assert.Empty(await ListaAsync(c));
    }

    [Fact]
    public async Task Editar_a_quantidade_repoe_e_desconta_o_mesmo_lugar()
    {
        var c = await ComoAnaAsync();
        var m = (await (await JuntarAsync(c, 2)).Content.ReadFromJsonAsync<MaterialDto>())!;

        var e = await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Peça", 3, 1m, "P001"));

        var novo = (await e.Content.ReadFromJsonAsync<MaterialDto>())!;
        Assert.Equal("A-02-1-01", novo.Local);
        Assert.Equal(0, await NoLugarAsync(c, "A-02-1-01"));
        Assert.Equal(7, (await PecaAsync(c)).Disponivel);
    }

    [Fact]
    public async Task Editar_o_lugar_repoe_o_antigo_e_desconta_o_novo()
    {
        var c = await ComoAnaAsync();
        await Arrumar(c, "A-03-2-01", 2);
        var m = (await (await JuntarAsync(c, 2, "A-03-2-01")).Content.ReadFromJsonAsync<MaterialDto>())!;
        Assert.Equal(0, await NoLugarAsync(c, "A-03-2-01"));

        var e = await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Peça", 2, 1m, "P001", "A-02-1-01"));

        Assert.Equal("A-02-1-01", (await e.Content.ReadFromJsonAsync<MaterialDto>())!.Local);
        Assert.Equal(2, await NoLugarAsync(c, "A-03-2-01"));
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01"));
        Assert.Equal(8, (await PecaAsync(c)).Disponivel);
    }

    [Fact]
    public async Task Editar_sem_mudar_peca_nem_quantidade_nem_lugar_nao_mexe_no_lugar()
    {
        var c = await ComoAnaAsync();
        var m = (await (await JuntarAsync(c, 2)).Content.ReadFromJsonAsync<MaterialDto>())!;

        var e = await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Outro nome", 2, 1m, "P001"));

        var novo = (await e.Content.ReadFromJsonAsync<MaterialDto>())!;
        Assert.True(novo.SaidaDada);
        Assert.Equal("A-02-1-01", novo.Local);
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01"));
    }

    [Fact]
    public async Task Apagar_repoe_o_lugar()
    {
        var c = await ComoAnaAsync();
        var m = (await (await JuntarAsync(c, 2)).Content.ReadFromJsonAsync<MaterialDto>())!;

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}")).StatusCode);

        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));
        Assert.Equal(10, (await PecaAsync(c)).Disponivel);
    }

    [Fact]
    public async Task Falha_a_dar_saida_repoe_o_lugar_e_deixa_a_peca_por_dar_repetivel()
    {
        var c = await ComoAnaAsync();
        _pecas.FalharSaida = true;

        var m = (await (await JuntarAsync(c, 2)).Content.ReadFromJsonAsync<MaterialDto>())!;

        Assert.False(m.SaidaDada);
        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));

        _pecas.FalharSaida = false;
        var r = await c.PostAsync($"/intervencoes/F1/materiais/{m.Id}/saida", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("A-02-1-01", (await r.Content.ReadFromJsonAsync<MaterialDto>())!.Local);
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01"));
    }

    [Fact]
    public async Task Material_de_ficha_alheia_da_404_e_nao_mexe_nos_lugares()
    {
        var c = await ComoAnaAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/intervencoes/F2/materiais", new PedidoMaterial("X", 1, 1m, "P001", "A-02-1-01"))).StatusCode);
        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));
    }

    [Fact]
    public async Task Validar_confirma_a_conferencia_e_o_stock_real_desce()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        await JuntarAsync(ana, 2);
        Assert.Equal(10, (await PecaAsync(ana)).Stock);
        Assert.Equal(HttpStatusCode.OK, (await ConcluirAsync(ana)).StatusCode);

        var r = await chefe.PostAsync("/validacao/F1", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(EstadoIntervencao.Validada, (await r.Content.ReadFromJsonAsync<IntervencaoDto>())!.Estado);
        Assert.Equal(["F1"], _pecas.Confirmadas);
        var p = await PecaAsync(chefe);
        Assert.Equal(8, p.Stock);
        Assert.Equal(8, p.Disponivel);
    }

    [Fact]
    public async Task Validar_com_confirmacao_a_falhar_avisa_e_uma_segunda_chamada_so_confirma()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        await JuntarAsync(ana, 2);
        await ConcluirAsync(ana);
        _pecas.FalharConfirmar = true;

        var r = await chefe.PostAsync("/validacao/F1", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var corpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Conferência por confirmar", corpo.GetProperty("avisoStock").GetString());
        Assert.Equal("F1", corpo.GetProperty("id").GetString());
        Assert.Equal(10, (await PecaAsync(chefe)).Stock);

        // Repetir com a confirmação ainda a falhar devolve a mesma ficha, com o aviso.
        var repetida = (await (await chefe.PostAsync("/validacao/F1", null)).Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal("F1", repetida.Id);
        Assert.Equal(EstadoIntervencao.Validada, repetida.Estado);
        Assert.Equal("Conferência por confirmar", repetida.AvisoStock);

        _pecas.FalharConfirmar = false;
        var segunda = await chefe.PostAsync("/validacao/F1", null);

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var ficha = (await segunda.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal("F1", ficha.Id);
        Assert.Equal(EstadoIntervencao.Validada, ficha.Estado);
        Assert.Null(ficha.AvisoStock);
        Assert.Equal(8, (await PecaAsync(chefe)).Stock);
    }

    [Fact]
    public async Task Devolver_mantem_a_conferencia_e_validar_depois_confirma()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        await JuntarAsync(ana, 2);
        await ConcluirAsync(ana);

        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsJsonAsync("/validacao/F1/devolver", new PedidoDevolucao("Falta tempo"))).StatusCode);

        Assert.Empty(_pecas.Confirmadas);
        Assert.Empty(_pecas.Canceladas);
        var p = await PecaAsync(ana);
        Assert.Equal(10, p.Stock);
        Assert.Equal(8, p.Disponivel);
        Assert.Single(await ListaAsync(ana));
    }

    [Fact]
    public async Task Cancelar_apaga_a_conferencia_e_repoe_os_lugares()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        await JuntarAsync(ana, 2);
        Assert.Equal(1, await NoLugarAsync(ana, "A-02-1-01"));

        var r = await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Duplicada"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(["F1"], _pecas.Canceladas);
        Assert.Equal(3, await NoLugarAsync(chefe, "A-02-1-01"));
        var p = await PecaAsync(chefe);
        Assert.Equal(10, p.Stock);
        Assert.Equal(10, p.Disponivel);
    }

    [Fact]
    public async Task Cancelar_com_a_conferencia_a_falhar_cancela_na_mesma_a_ficha()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        await JuntarAsync(ana, 2);
        _pecas.FalharCancelar = true;

        var r = await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Duplicada"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var ficha = (await r.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(EstadoIntervencao.Cancelada, ficha.Estado);
        Assert.Equal("Conferência por cancelar", ficha.AvisoStock);
        Assert.Equal(1, await NoLugarAsync(chefe, "A-02-1-01"));
    }

    [Fact]
    public async Task Memoria_guarda_e_devolve_o_lugar_da_saida()
    {
        var r = new RepositorioPecasMemoria();
        Assert.True((await r.DarSaidaAsync("F1", "M1", "P001", 1, "AA", default, "A-02-1-01")).Ok);
        Assert.True((await r.DarSaidaAsync("F1", "M2", "P001", 1, "AA", default)).Ok);

        var lista = await r.EnriquecerMateriaisAsync([new MaterialDto("M1", "x", 1, 0, 0), new MaterialDto("M2", "x", 1, 0, 0), new MaterialDto("M3", "x", 1, 0, 0)], default);

        Assert.Equal("A-02-1-01", lista[0].Local);
        Assert.Null(lista[1].Local);
        Assert.False(lista[2].SaidaDada);
    }

    [Fact]
    public void Lobs_leva_o_stamp_e_so_com_lugar_o_separador_e_o_lugar()
    {
        var stamp = new string('S', 25);
        Assert.Equal(stamp, RegrasPecas.MontarLobs(stamp, null));
        Assert.Equal(stamp, RegrasPecas.MontarLobs(stamp, "  "));
        Assert.Equal($"{stamp}|A-02-1-01", RegrasPecas.MontarLobs(stamp, "A-02-1-01"));
        Assert.Equal($"{stamp}|AAA-999-99-99", RegrasPecas.MontarLobs(stamp, "AAA-999-99-99")); // 13 caracteres: o máximo
        Assert.Null(RegrasPecas.MontarLobs(stamp, "AAAA-999-99-99")); // 14
        Assert.Null(RegrasPecas.MontarLobs(new string('S', 30), "AAA-999-99-99")); // 44 > 40
        Assert.True(RegrasPecas.MontarLobs(stamp, "AAA-999-99-99")!.Length <= RegrasPecas.TamanhoLobs);
    }

    [Fact]
    public async Task Memoria_recusa_a_saida_com_lugar_que_nao_cabe_em_lobs()
    {
        var r = new RepositorioPecasMemoria();

        var f = await r.DarSaidaAsync("F1", "M1", "P001", 1, "AA", default, "AAAA-999-99-99");

        Assert.False(f.Ok);
        Assert.Contains("bi.lobs", f.Erro);
    }

    // ---------- Editar, apagar e repetir a saída: o lugar só se repõe se foi descontado

    private static async Task<MaterialDto> MaterialAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<MaterialDto>())!;

    private static Task<HttpResponseMessage> EditarAsync(HttpClient c, string id, decimal qtt, string peca = "P001", string? local = null, string ficha = "F1") =>
        c.PutAsJsonAsync($"/intervencoes/{ficha}/materiais/{id}", new PedidoMaterial("Peça", qtt, 1m, peca, local));

    [Fact]
    public async Task Editar_material_que_saiu_com_lugar_insuficiente_nunca_repoe_o_que_nao_descontou()
    {
        var c = await ComoAnaAsync();
        var m = await MaterialAsync(await JuntarAsync(c, 5, "A-02-1-01")); // o lugar só tem 3: sai sem descontar
        Assert.Null(m.Local);
        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));

        var e = await MaterialAsync(await EditarAsync(c, m.Id, 2));

        Assert.Equal("A-02-1-01", e.Local);
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01")); // 3 - 2; repor 5 daria 6
    }

    [Fact]
    public async Task Apagar_material_que_saiu_com_lugar_insuficiente_nunca_repoe_o_que_nao_descontou()
    {
        var c = await ComoAnaAsync();
        var m = await MaterialAsync(await JuntarAsync(c, 5, "A-02-1-01"));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}")).StatusCode);

        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));
        Assert.Equal(10, (await PecaAsync(c)).Disponivel);
    }

    [Fact]
    public async Task Editar_mudando_de_peca_repoe_o_lugar_da_antiga_e_a_nova_sem_lugar_avisa()
    {
        var c = await ComoAnaAsync();
        var m = await MaterialAsync(await JuntarAsync(c, 2));
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01"));

        var e = await MaterialAsync(await EditarAsync(c, m.Id, 1, "P002"));

        Assert.Equal("P002", e.Ref);
        Assert.True(e.SaidaDada);
        Assert.Null(e.Local);
        Assert.Contains("sem localização", e.AvisoStock);
        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));
        Assert.Equal(10, (await PecaAsync(c)).Disponivel);
    }

    [Fact]
    public async Task Editar_recusado_pela_ficha_deixa_a_saida_e_o_lugar_como_estavam()
    {
        var c = await ComoAnaAsync();
        var m = await MaterialAsync(await JuntarAsync(c, 2));
        _recusa.RecusarMaterial = true;

        var r = await EditarAsync(c, m.Id, 3);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01"));
        var depois = Assert.Single(await ListaAsync(c));
        Assert.True(depois.SaidaDada);
        Assert.Equal("A-02-1-01", depois.Local);
        Assert.Equal(8, (await PecaAsync(c)).Disponivel);
    }

    [Fact]
    public async Task Apagar_recusado_pela_ficha_deixa_a_saida_e_o_lugar_como_estavam()
    {
        var c = await ComoAnaAsync();
        var m = await MaterialAsync(await JuntarAsync(c, 2));
        _recusa.RecusarMaterial = true;

        var r = await c.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01"));
        Assert.True(Assert.Single(await ListaAsync(c)).SaidaDada);
    }

    [Fact]
    public async Task Editar_recusado_com_a_reposicao_a_falhar_nao_desconta_duas_vezes()
    {
        var c = await ComoAnaAsync();
        var m = await MaterialAsync(await JuntarAsync(c, 2));
        _recusa.RecusarMaterial = true;
        _lugares.FalharRepor = true;

        var r = await EditarAsync(c, m.Id, 3);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01")); // continuou descontado: nada a repor nem a descontar de novo
        var depois = Assert.Single(await ListaAsync(c));
        Assert.True(depois.SaidaDada);
        Assert.Equal("A-02-1-01", depois.Local);
    }

    [Fact]
    public async Task Editar_com_a_reposicao_a_falhar_avisa_lugares_por_repor()
    {
        var c = await ComoAnaAsync();
        var m = await MaterialAsync(await JuntarAsync(c, 2));
        _lugares.FalharRepor = true;

        var e = await MaterialAsync(await EditarAsync(c, m.Id, 1));

        Assert.True(e.SaidaDada);
        Assert.Contains("Lugares por repor", e.AvisoStock);
    }

    [Fact]
    public async Task Apagar_com_a_reposicao_a_falhar_apaga_e_avisa_que_saiu_sem_repor()
    {
        var c = await ComoAnaAsync();
        var m = await MaterialAsync(await JuntarAsync(c, 2));
        _lugares.FalharRepor = true;

        var r = await c.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("sem repor", JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("avisoStock").GetString());
        Assert.Empty(await ListaAsync(c));
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01"));
    }

    [Fact]
    public async Task Editar_com_local_invalido_da_400_mesmo_sem_lugar_antigo_e_nao_mexe_em_nada()
    {
        var c = await ComoAnaAsync();
        var semLugar = await MaterialAsync(await JuntarAsync(c, 1, peca: "P002"));
        var comLugar = await MaterialAsync(await JuntarAsync(c, 2));

        Assert.Equal(HttpStatusCode.BadRequest, (await EditarAsync(c, semLugar.Id, 1, "P002", "xx")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await EditarAsync(c, comLugar.Id, 2, "P001", "A-02-1")).StatusCode);

        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01"));
        Assert.All(await ListaAsync(c), x => Assert.True(x.SaidaDada));
    }

    [Fact]
    public async Task Local_sem_peca_da_400_ao_juntar_e_ao_editar()
    {
        var c = await ComoAnaAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await JuntarAsync(c, 1, "A-02-1-01", peca: "")).StatusCode);
        Assert.Empty(await ListaAsync(c));

        var m = await MaterialAsync(await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Fita", 1, 1m)));
        Assert.Equal(HttpStatusCode.BadRequest, (await EditarAsync(c, m.Id, 1, "", "A-02-1-01")).StatusCode);
    }

    [Fact]
    public async Task Saida_repetida_com_local_desconta_desse_lugar()
    {
        var c = await ComoAnaAsync();
        _pecas.FalharSaida = true;
        var m = await MaterialAsync(await JuntarAsync(c, 2, "A-02-1-01"));
        Assert.False(m.SaidaDada);
        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));
        _pecas.FalharSaida = false;

        var r = await c.PostAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}/saida", new PedidoSaida(null, "LOC-A-02-1-01"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("A-02-1-01", (await MaterialAsync(r)).Local);
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01"));
    }

    [Fact]
    public async Task Saida_repetida_com_varios_lugares_pede_a_escolha_e_com_local_invalido_da_400()
    {
        var c = await ComoAnaAsync();
        await Arrumar(c, "A-03-2-01", 2);
        _pecas.FalharSaida = true;
        var m = await MaterialAsync(await JuntarAsync(c, 1, "A-02-1-01"));
        _pecas.FalharSaida = false;

        var sem = await c.PostAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}/saida", new PedidoSaida(null));
        Assert.Equal(HttpStatusCode.Conflict, sem.StatusCode);
        var corpo = JsonDocument.Parse(await sem.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("escolha_o_lugar", corpo.GetProperty("erro").GetString());
        Assert.Equal(2, corpo.GetProperty("lugares").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}/saida", new PedidoSaida(null, "xx"))).StatusCode);
        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));

        var ok = await c.PostAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}/saida", new PedidoSaida(null, "A-03-2-01"));
        Assert.Equal("A-03-2-01", (await MaterialAsync(ok)).Local);
        Assert.Equal(1, await NoLugarAsync(c, "A-03-2-01"));
    }

    [Fact]
    public async Task Editar_apagar_e_saida_por_tecnico_sem_acesso_da_404_e_nao_mexem_nos_lugares()
    {
        var c = await ComoAnaAsync(); // a F2 é de outro técnico

        Assert.Equal(HttpStatusCode.NotFound, (await EditarAsync(c, "M1", 1, "P001", "A-02-1-01", "F2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/intervencoes/F2/materiais/M1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/intervencoes/F2/materiais/M1/saida", new PedidoSaida("P001", "A-02-1-01"))).StatusCode);

        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));
        Assert.Equal(10, (await PecaAsync(c)).Disponivel);
    }

    // ---------- Falhas das localizações

    [Fact]
    public async Task Localizacoes_indisponiveis_a_saida_da_se_e_avisa_que_nao_descontou()
    {
        var c = await ComoAnaAsync();
        _lugares.Indisponivel = true;

        var semLocal = await MaterialAsync(await JuntarAsync(c, 1));
        var comLocal = await MaterialAsync(await JuntarAsync(c, 1, "A-02-1-01"));

        Assert.True(semLocal.SaidaDada);
        Assert.Contains("sem descontar de nenhum lugar", semLocal.AvisoStock);
        Assert.True(comLocal.SaidaDada);
        Assert.Null(comLocal.Local);
        Assert.Contains("sem descontar do lugar", comLocal.AvisoStock);
        Assert.Equal(8, (await PecaAsync(c)).Disponivel);
        _lugares.Indisponivel = false;
        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));
    }

    [Fact]
    public async Task Lugar_ocupado_nao_da_a_saida_e_a_peca_fica_por_dar_ate_repetir()
    {
        var c = await ComoAnaAsync();
        _lugares.Ocupado = true;

        var m = await MaterialAsync(await JuntarAsync(c, 2, "A-02-1-01"));

        Assert.False(m.SaidaDada);
        Assert.Equal("P001", m.Ref);
        Assert.Contains("ocupado", m.AvisoStock);
        Assert.Equal(10, (await PecaAsync(c)).Disponivel);
        Assert.Equal(3, await NoLugarAsync(c, "A-02-1-01"));
        var lido = Assert.Single(await ListaAsync(c));
        Assert.False(lido.SaidaDada);
        Assert.Equal("P001", lido.Ref);

        _lugares.Ocupado = false;
        var r = await c.PostAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}/saida", new PedidoSaida(null, "A-02-1-01"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1, await NoLugarAsync(c, "A-02-1-01"));
        Assert.Equal(8, (await PecaAsync(c)).Disponivel);
    }

    // ---------- Cancelar

    [Fact]
    public async Task Cancelar_com_a_conferencia_a_falhar_avisa_e_repetir_limpa_e_repoe_os_lugares()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        await JuntarAsync(ana, 2);
        _pecas.FalharCancelar = true;

        var primeira = (await (await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Duplicada"))).Content.ReadFromJsonAsync<IntervencaoDto>())!;

        Assert.Equal(EstadoIntervencao.Cancelada, primeira.Estado);
        Assert.Equal("Conferência por cancelar", primeira.AvisoStock);
        Assert.Equal(1, await NoLugarAsync(chefe, "A-02-1-01"));
        Assert.Empty(_pecas.Canceladas);

        _pecas.FalharCancelar = false;
        var segunda = await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Duplicada"));

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var ficha = (await segunda.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(EstadoIntervencao.Cancelada, ficha.Estado);
        Assert.Null(ficha.AvisoStock);
        Assert.Equal(["F1"], _pecas.Canceladas);
        Assert.Equal(3, await NoLugarAsync(chefe, "A-02-1-01"));

        // Com a limpeza feita, cancelar outra vez é recusado e não repõe de novo.
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Duplicada"))).StatusCode);
        Assert.Equal(3, await NoLugarAsync(chefe, "A-02-1-01"));
    }

    [Fact]
    public async Task Cancelar_com_a_reposicao_a_falhar_avisa_lugares_por_repor()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        await JuntarAsync(ana, 2);
        _lugares.FalharRepor = true;

        var r = (await (await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Duplicada"))).Content.ReadFromJsonAsync<IntervencaoDto>())!;

        Assert.Equal(EstadoIntervencao.Cancelada, r.Estado);
        Assert.Equal("Lugares por repor", r.AvisoStock);
        Assert.Equal(["F1"], _pecas.Canceladas);
    }

    // ---------- Supervisor: detalhe com peças, Ref omitida e validar com peças por dar

    private static async Task<FichaDetalheDto> DetalheAsync(HttpClient c) => (await c.GetFromJsonAsync<FichaDetalheDto>("/intervencoes/F1/detalhe"))!;

    // Corpo sem o campo "ref" (como as APKs antigas) ou com ele, à escolha; JSON à mão para não depender da serialização do record.
    private static Task<HttpResponseMessage> PutBrutoAsync(HttpClient c, string id, string corpo) =>
        c.PutAsync($"/intervencoes/F1/materiais/{id}", new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Detalhe_do_supervisor_traz_a_peca_a_saida_e_o_lugar_dos_materiais()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        await JuntarAsync(ana, 2);
        await JuntarSemPecaAsync(ana);

        var m = (await DetalheAsync(chefe)).Materiais;

        var comPeca = Assert.Single(m, x => x.Ref == "P001");
        Assert.True(comPeca.SaidaDada);
        Assert.Equal("A-02-1-01", comPeca.Local);
        var semPeca = Assert.Single(m, x => x.Ref == "");
        Assert.False(semPeca.SaidaDada);
        Assert.Null(semPeca.Local);
    }

    private static Task<HttpResponseMessage> JuntarSemPecaAsync(HttpClient c) =>
        c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Fita", 1, 1m));

    [Fact]
    public async Task Supervisor_corrige_so_o_preco_pelo_detalhe_e_a_saida_e_o_lugar_mantem_se()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        var m = await MaterialAsync(await JuntarAsync(ana, 2));
        var lido = (await DetalheAsync(chefe)).Materiais.Single();

        // O ecrã do supervisor repete o que leu do detalhe (peça e lugar) e só muda o preço.
        var r = await chefe.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Peça", lido.Qtt, 9m, lido.Ref));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var e = await MaterialAsync(r);
        Assert.True(e.SaidaDada);
        Assert.Equal("P001", e.Ref);
        Assert.Equal("A-02-1-01", e.Local);
        Assert.Equal(1, await NoLugarAsync(chefe, "A-02-1-01"));
        Assert.Equal(8, (await PecaAsync(chefe)).Disponivel);
        var depois = Assert.Single((await DetalheAsync(chefe)).Materiais);
        Assert.Equal(9m, depois.Epv);
        Assert.True(depois.SaidaDada);
    }

    [Fact]
    public async Task Put_sem_ref_mantem_a_peca_a_saida_e_o_lugar()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        var m = await MaterialAsync(await JuntarAsync(ana, 2));

        var r = await PutBrutoAsync(chefe, m.Id, """{"design":"Peça","qtt":2,"epv":7}""");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var e = await MaterialAsync(r);
        Assert.True(e.SaidaDada);
        Assert.Equal("P001", e.Ref);
        Assert.Equal("A-02-1-01", e.Local);
        Assert.Equal(1, await NoLugarAsync(chefe, "A-02-1-01"));
        Assert.Equal(8, (await PecaAsync(chefe)).Disponivel);
        // "ref": null é o mesmo que omitir.
        Assert.True((await MaterialAsync(await PutBrutoAsync(ana, m.Id, """{"design":"Peça","qtt":2,"epv":8,"ref":null}"""))).SaidaDada);
        Assert.Equal(1, await NoLugarAsync(chefe, "A-02-1-01"));
    }

    [Fact]
    public async Task Put_sem_ref_e_com_outra_quantidade_refaz_a_saida_da_mesma_peca_no_mesmo_lugar()
    {
        var ana = await ComoAnaAsync();
        var m = await MaterialAsync(await JuntarAsync(ana, 2));

        var e = await MaterialAsync(await PutBrutoAsync(ana, m.Id, """{"design":"Peça","qtt":1,"epv":1}"""));

        Assert.True(e.SaidaDada);
        Assert.Equal("P001", e.Ref);
        Assert.Equal("A-02-1-01", e.Local);
        Assert.Equal(2, await NoLugarAsync(ana, "A-02-1-01"));
        Assert.Equal(9, (await PecaAsync(ana)).Disponivel);
    }

    [Fact]
    public async Task Put_sem_ref_num_material_por_dar_mantem_a_ligacao_sem_tentar_a_saida()
    {
        var ana = await ComoAnaAsync();
        _lugares.Ocupado = true;
        var m = await MaterialAsync(await JuntarAsync(ana, 2, "A-02-1-01"));
        Assert.False(m.SaidaDada);
        _lugares.Ocupado = false;

        var e = await MaterialAsync(await PutBrutoAsync(ana, m.Id, """{"design":"Peça","qtt":2,"epv":5}"""));

        Assert.False(e.SaidaDada);
        Assert.Equal("P001", e.Ref);
        Assert.Equal(3, await NoLugarAsync(ana, "A-02-1-01"));
        Assert.Equal(10, (await PecaAsync(ana)).Disponivel);
    }

    [Fact]
    public async Task Put_com_ref_vazia_explicita_desliga_a_peca_e_repoe_o_lugar()
    {
        var ana = await ComoAnaAsync();
        var m = await MaterialAsync(await JuntarAsync(ana, 2));
        Assert.Equal(1, await NoLugarAsync(ana, "A-02-1-01"));

        var e = await MaterialAsync(await PutBrutoAsync(ana, m.Id, """{"design":"Peça","qtt":2,"epv":1,"ref":""}"""));

        Assert.False(e.SaidaDada);
        Assert.Equal("", e.Ref);
        Assert.Equal(3, await NoLugarAsync(ana, "A-02-1-01"));
        Assert.Equal(10, (await PecaAsync(ana)).Disponivel);
        Assert.Equal("", Assert.Single(await ListaAsync(ana)).Ref);
    }

    [Fact]
    public async Task Validar_com_peca_por_dar_da_409_sem_efeitos_e_depois_de_repetir_a_saida_valida()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        _lugares.Ocupado = true;
        var m = await MaterialAsync(await JuntarAsync(ana, 2, "A-02-1-01"));
        _lugares.Ocupado = false;
        Assert.Equal(HttpStatusCode.OK, (await ConcluirAsync(ana)).StatusCode);

        var r = await chefe.PostAsync("/validacao/F1", null);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var corpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("pecas_por_dar", corpo.GetProperty("erro").GetString());
        Assert.Equal("Há 1 peça(s) por dar saída; repita a saída ou desligue a peça antes de validar.", corpo.GetProperty("mensagem").GetString());
        Assert.Equal(EstadoIntervencao.Concluida, (await DetalheAsync(chefe)).Estado);
        Assert.Empty(_pecas.Confirmadas);

        // O supervisor repete a saída na ficha Concluída e a validação passa.
        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}/saida", new PedidoSaida(null, "A-02-1-01"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsync("/validacao/F1", null)).StatusCode);
        Assert.Equal(["F1"], _pecas.Confirmadas);
        Assert.Equal(8, (await PecaAsync(chefe)).Stock);
    }

    [Fact]
    public async Task Validar_com_peca_por_dar_desligada_pelo_supervisor_valida()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        _lugares.Ocupado = true;
        var m = await MaterialAsync(await JuntarAsync(ana, 2, "A-02-1-01"));
        _lugares.Ocupado = false;
        await ConcluirAsync(ana);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsync("/validacao/F1", null)).StatusCode);

        var e = await PutBrutoAsync(chefe, m.Id, """{"design":"Peça","qtt":2,"epv":1,"ref":""}""");

        Assert.Equal(HttpStatusCode.OK, e.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsync("/validacao/F1", null)).StatusCode);
        Assert.Equal(10, (await PecaAsync(chefe)).Stock);
    }

    // ---------- Conferência a meio vista no detalhe

    [Fact]
    public async Task Detalhe_de_ficha_validada_com_a_conferencia_a_meio_traz_o_aviso_e_repetir_limpa_o()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        await JuntarAsync(ana, 2);
        await ConcluirAsync(ana);
        _pecas.FalharConfirmar = true;
        await chefe.PostAsync("/validacao/F1", null);

        var d = await DetalheAsync(chefe);

        Assert.Equal(EstadoIntervencao.Validada, d.Estado);
        Assert.Equal("Conferência por confirmar", d.AvisoStock);
        // O técnico não repete a confirmação: não recebe o aviso.
        Assert.Null((await DetalheAsync(ana)).AvisoStock);

        _pecas.FalharConfirmar = false;
        Assert.Null((await (await chefe.PostAsync("/validacao/F1", null)).Content.ReadFromJsonAsync<IntervencaoDto>())!.AvisoStock);
        Assert.Null((await DetalheAsync(chefe)).AvisoStock);
    }

    [Fact]
    public async Task Detalhe_de_ficha_cancelada_com_a_conferencia_a_meio_traz_o_aviso_e_repetir_limpa_o()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        await JuntarAsync(ana, 2);
        _pecas.FalharCancelar = true;
        await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Duplicada"));

        var d = await DetalheAsync(chefe);

        Assert.Equal(EstadoIntervencao.Cancelada, d.Estado);
        Assert.Equal("Conferência por cancelar", d.AvisoStock);

        _pecas.FalharCancelar = false;
        Assert.Null((await (await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento(d.Motivo ?? "Duplicada"))).Content.ReadFromJsonAsync<IntervencaoDto>())!.AvisoStock);
        Assert.Null((await DetalheAsync(chefe)).AvisoStock);
    }

    [Fact]
    public async Task Detalhe_de_ficha_validada_sem_nada_a_meio_nao_traz_aviso()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        await JuntarAsync(ana, 2);
        await ConcluirAsync(ana);
        await chefe.PostAsync("/validacao/F1", null);

        Assert.Null((await DetalheAsync(chefe)).AvisoStock);
    }

    // ---------- Cancelar com peças por dar

    [Fact]
    public async Task Cancelar_limpa_tambem_a_ligacao_das_pecas_por_dar_e_repetir_ja_e_recusado()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        _lugares.Ocupado = true;
        await JuntarAsync(ana, 2, "A-02-1-01");
        _lugares.Ocupado = false;

        var r = (await (await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Duplicada"))).Content.ReadFromJsonAsync<IntervencaoDto>())!;

        Assert.Null(r.AvisoStock);
        Assert.Equal("", Assert.Single((await DetalheAsync(chefe)).Materiais).Ref);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Duplicada"))).StatusCode);
    }

    // ---------- Reposição da saída que falha, lugares fora do formato

    [Fact]
    public async Task Editar_recusado_com_a_saida_a_nao_repor_responde_stock_desfasado()
    {
        var c = await ComoAnaAsync();
        var m = await MaterialAsync(await JuntarAsync(c, 2));
        _recusa.RecusarMaterial = true;
        _pecas.FalharSaida = true;

        var r = await EditarAsync(c, m.Id, 3);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("desfasado", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Lugar_com_formato_nao_reconhecido_nao_se_desconta_e_a_peca_sai_com_aviso()
    {
        var c = await ComoAnaAsync();
        await _lugares.ArrumarAsync("P002", "ABCDE", 5, 1, 1, 3, "XX", CancellationToken.None); // corredor com mais de 3 letras: não se sabe repor

        var m = await MaterialAsync(await JuntarAsync(c, 1, null, "P002"));

        Assert.True(m.SaidaDada);
        Assert.Null(m.Local);
        Assert.Contains("formato", m.AvisoStock);
        Assert.Equal(3, Assert.Single((await LugaresAsync(c, "P002")).Lugares).Qtt);
    }

    // Repositório de peças que regista as chamadas e falha onde se pedir; o resto é o de memória.
    private sealed class PecasEspia(RepositorioPecasMemoria memoria) : IRepositorioPecas
    {
        public List<string> Abertas = [], Confirmadas = [], Canceladas = [];
        public bool FalharAbrir, FalharConfirmar, FalharCancelar, FalharSaida;

        public Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct) => memoria.ListarAsync(texto, maximo, ct);
        public Task<PecaDto?> ObterPorCodigoAsync(string codigo, CancellationToken ct) => memoria.ObterPorCodigoAsync(codigo, ct);

        public Task<ResultadoMovimento> AbrirConferenciaAsync(string fichaId, string iniciais, CancellationToken ct)
        {
            if (FalharAbrir) return Task.FromResult(ResultadoMovimento.Falha(RegrasPecas.ErroOcupado));
            Abertas.Add(fichaId);
            return memoria.AbrirConferenciaAsync(fichaId, iniciais, ct);
        }

        public Task<ResultadoMovimento> DarSaidaAsync(string fichaId, string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct, string? local = null) =>
            FalharSaida ? Task.FromResult(ResultadoMovimento.Falha(RegrasPecas.ErroOcupado)) : memoria.DarSaidaAsync(fichaId, materialId, refPeca, qtt, iniciais, ct, local);

        public Task<ResultadoMovimento> AnularSaidaAsync(string materialId, string iniciais, CancellationToken ct) => memoria.AnularSaidaAsync(materialId, iniciais, ct);

        public Task<ResultadoMovimento> ConfirmarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct)
        {
            if (FalharConfirmar) return Task.FromResult(ResultadoMovimento.Falha(RegrasPecas.ErroOcupado));
            Confirmadas.Add(fichaId);
            return memoria.ConfirmarConferenciaAsync(fichaId, iniciais, ct);
        }

        public Task<ResultadoMovimento> CancelarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct)
        {
            if (FalharCancelar) return Task.FromResult(ResultadoMovimento.Falha(RegrasPecas.ErroOcupado));
            Canceladas.Add(fichaId);
            return memoria.CancelarConferenciaAsync(fichaId, iniciais, ct);
        }

        public Task<IReadOnlyDictionary<string, decimal>> EmConferenciaPorPecaAsync(IReadOnlyCollection<string> refs, string? excetoFichaId, CancellationToken ct) =>
            memoria.EmConferenciaPorPecaAsync(refs, excetoFichaId, ct);
        public Task<bool> LigarPecaAsync(string stampMaterial, string refPeca, string usrinis, CancellationToken ct) => memoria.LigarPecaAsync(stampMaterial, refPeca, usrinis, ct);
        public Task<bool> ConferenciaPendenteAsync(string fichaId, CancellationToken ct) => memoria.ConferenciaPendenteAsync(fichaId, ct);
        public Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct) => memoria.EnriquecerMateriaisAsync(materiais, ct);
    }

    // Localizações de memória que falham onde se pedir.
    private sealed class LugaresEspia(IRepositorioLocalizacoes interno) : IRepositorioLocalizacoes
    {
        public bool Indisponivel, Ocupado, FalharRepor;

        private void Verificar() { if (Indisponivel) throw new RepositorioIndisponivelException(); }

        public Task<LocalizacoesPecaDto> ListarAsync(string refPeca, CancellationToken ct) { Verificar(); return interno.ListarAsync(refPeca, ct); }
        public Task<ResultadoMovimento> ArrumarAsync(string refPeca, string corredor, int estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct) =>
            interno.ArrumarAsync(refPeca, corredor, estante, nivel, posicao, qtt, iniciais, ct);
        public Task<ResultadoMovimento> DescontarAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct)
        {
            Verificar();
            return Ocupado ? Task.FromResult(ResultadoMovimento.Falha(RegrasPecas.ErroOcupado)) : interno.DescontarAsync(refPeca, corredor, estante, nivel, posicao, qtt, iniciais, ct);
        }
        public Task<ResultadoMovimento> ReporAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct) =>
            FalharRepor ? Task.FromResult(ResultadoMovimento.Falha("Não foi possível repor.")) : interno.ReporAsync(refPeca, corredor, estante, nivel, posicao, qtt, iniciais, ct);
        public Task<string> VerificarTabelaAsync(CancellationToken ct) { Verificar(); return interno.VerificarTabelaAsync(ct); }
    }

    // Faz o repositório de fichas recusar a edição e a remoção de material (a ficha mudou entretanto); o resto é o de memória.
    public class RecusaProxy : DispatchProxy
    {
        public IRepositorioIntervencoes Alvo = null!;
        public bool RecusarMaterial;

        protected override object? Invoke(MethodInfo? metodo, object?[]? args)
        {
            if (RecusarMaterial && metodo!.Name == nameof(IRepositorioIntervencoes.EditarMaterialAsync))
                return Task.FromResult<(ResultadoIntervencao, MaterialDto?)>((ResultadoIntervencao.Conflito, null));
            if (RecusarMaterial && metodo!.Name == nameof(IRepositorioIntervencoes.ApagarMaterialAsync))
                return Task.FromResult(ResultadoIntervencao.Conflito);
            try { return metodo!.Invoke(Alvo, args); }
            catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
        }
    }
}
