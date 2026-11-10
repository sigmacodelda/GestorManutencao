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

// Ficha completa: cancelar, ficha manual, edição, material, mão de obra, totais, compras, fichas antigas e contadores (repositório em memória).
public class FichaCompletaApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!"; // sigma-ok
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));
    // O repositório em memória usa o relógio do sistema; o dia vem do mesmo relógio, lido uma só vez para o conjunto de testes.
    private static readonly DateTime Dia = TimeProvider.System.GetLocalNow().Date;

    private readonly List<WebApplicationFactory<Program>> _fabricas = [];

    // ana (101) e o rui (102, ainda com PIN provisório) são técnicos; chefe (100) e anasup (101) são supervisores; velho (104) está inativo; novo não tem número.
    private WebApplicationFactory<Program> Criar(ConfigManutencao? configuracao = null)
    {
        var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
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
                    (new UtilizadorApp("velho", "Velho Antigo", Perfil.Operador, false, false, Tecno: 104), Hasher.Hash("715938")),
                    (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918")),
                    (new UtilizadorApp("anasup", "Ana Silva", Perfil.Supervisor, true, false, Tecno: 101), Hasher.Hash("846213")),
                    (new UtilizadorApp("chefesem", "Chefe Sem Numero", Perfil.Supervisor, true, false), Hasher.Hash("951357"))
                ])));
                if (configuracao is not null)
                    s.Replace(ServiceDescriptor.Singleton<IRepositorioIntervencoes>(sp => new RepositorioIntervencoesMemoria(sp.GetRequiredService<TimeProvider>(), configuracao)));
            }));
        _fabricas.Add(f);
        return f;
    }

    public void Dispose() { foreach (var f in _fabricas) f.Dispose(); }

    private static async Task<HttpClient> ComoAsync(WebApplicationFactory<Program> fabrica, string pin)
    {
        var c = fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    private const string PinAna = "482913", PinChefe = "603918", PinAnaSup = "846213", PinNovo = "264813"; // sigma-ok

    private static async Task<T> Ler<T>(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<T>())!;

    private static async Task<string> Erro(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("erro", out var e) ? e.GetString() ?? "" : "";
    }

    private static Task<HttpResponseMessage> Detalhe(HttpClient c, string id) => c.GetAsync($"/intervencoes/{id}/detalhe");

    private static async Task<FichaDetalheDto> DetalheDe(HttpClient c, string id) => await Ler<FichaDetalheDto>(await Detalhe(c, id));

    // ---------------------------------------------------------------- Cancelar

    [Fact]
    public async Task Cancelar_planeada_pelo_supervisor_grava_o_motivo_e_gera_a_seguinte_da_tarefa()
    {
        var chefe = await ComoAsync(Criar(), PinChefe);

        var r = await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Equipamento parado"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(EstadoIntervencao.Cancelada, (await Ler<IntervencaoDto>(r)).Estado);
        var detalhe = await DetalheDe(chefe, "F1");
        Assert.Equal(EstadoIntervencao.Cancelada, detalhe.Estado);
        Assert.Equal("Cancelada: Equipamento parado", detalhe.Motivo);
        Assert.False(detalhe.PodeCancelar);
        Assert.False(detalhe.PodeEditar);
        var planeadas = await Ler<List<IntervencaoDto>>(await chefe.GetAsync("/planeadas"));
        Assert.Contains(planeadas, i => i.Equipamento == "Compressor de ar 02" && i.Estado == EstadoIntervencao.Planeada && i.Id != "F1");
    }

    [Fact]
    public async Task Cancelar_ficha_em_curso_de_um_tecnico()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);

        var r = await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Já não é preciso"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(EstadoIntervencao.Cancelada, (await Ler<IntervencaoDto>(r)).Estado);
    }

    [Fact]
    public async Task Cancelar_recusa_operador_motivo_invalido_e_fichas_fechadas()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento(null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("  "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento(new string('a', 241)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsJsonAsync("/intervencoes/H1/cancelar", new PedidoCancelamento("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsJsonAsync("/intervencoes/H3/cancelar", new PedidoCancelamento("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsJsonAsync("/intervencoes/F3/cancelar", new PedidoCancelamento("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PostAsJsonAsync("/intervencoes/NAOEXISTE/cancelar", new PedidoCancelamento("x"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento(new string('a', 240)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("outra vez"))).StatusCode);
    }

    // ---------------------------------------------------------------- Ficha manual

    [Fact]
    public async Task Ficha_manual_nasce_em_curso_com_o_tecnico_que_a_cria()
    {
        var ana = await ComoAsync(Criar(), PinAna);

        var r = await ana.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E1", TipoIntervencao.Curativa, "Externa", "Mensal", "Fuga de óleo"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var ficha = await Ler<IntervencaoDto>(r);
        Assert.Equal(EstadoIntervencao.EmCurso, ficha.Estado);
        Assert.Equal(101, ficha.Tecno);
        Assert.Equal(TipoIntervencao.Curativa, ficha.Tipo);
        Assert.NotNull(ficha.HoraInicio);
        var d = await DetalheDe(ana, ficha.Id);
        Assert.Equal("Externa", d.TipoManut);
        Assert.Equal("Mensal", d.Periodicidade);
        Assert.Equal("Fuga de óleo", d.Problema);
        Assert.True(d.Numero > 0);
        Assert.True(d.PodeEditar);
    }

    [Fact]
    public async Task Ficha_manual_omissoes_preventiva_e_interna()
    {
        var ana = await ComoAsync(Criar(), PinAna);

        var ficha = await Ler<IntervencaoDto>(await ana.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E2")));

        Assert.Equal(TipoIntervencao.Preventiva, ficha.Tipo);
        var d = await DetalheDe(ana, ficha.Id);
        Assert.Equal("Interna", d.TipoManut);
        Assert.Equal("", d.Periodicidade);
    }

    [Fact]
    public async Task Ficha_manual_recusa_equipamento_invalido_periodicidade_alheia_e_texto_longo()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);

        Assert.Equal(HttpStatusCode.NotFound, (await ana.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E7"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("NAOEXISTE"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes", new PedidoFichaManual(""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E1", TipoManut: "Mista"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E1", Problema: new string('a', 201)))).StatusCode);

        var alheia = await ana.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E1", Periodicidade: "Trimestral"));
        Assert.Equal(HttpStatusCode.BadRequest, alheia.StatusCode);
        Assert.Equal("periodicidade_invalida", await Erro(alheia));
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E3", Periodicidade: "Trimestral"))).StatusCode);
    }

    [Fact]
    public async Task Ficha_manual_precisa_de_numero_de_tecnico_e_de_sessao()
    {
        var fabrica = Criar();
        var novo = await ComoAsync(fabrica, PinNovo);

        var r = await novo.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E1"));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("sem_tecno", await Erro(r));
        Assert.Equal(HttpStatusCode.Unauthorized, (await fabrica.CreateClient().PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E1"))).StatusCode);
    }

    [Fact]
    public async Task Ficha_manual_com_o_mesmo_opId_devolve_a_mesma_ficha()
    {
        var ana = await ComoAsync(Criar(), PinAna);
        var pedido = new PedidoFichaManual("E1", Problema: "Fuga", OpId: "abcdef123456");

        var a = await Ler<IntervencaoDto>(await ana.PostAsJsonAsync("/intervencoes", pedido));
        var b = await Ler<IntervencaoDto>(await ana.PostAsJsonAsync("/intervencoes", pedido));

        Assert.Equal(a.Id, b.Id);
        Assert.Equal(1, (await Ler<List<IntervencaoDto>>(await ana.GetAsync("/intervencoes"))).Count(i => i.Anomalia == "Fuga"));
    }

    [Fact]
    public async Task Supervisor_com_numero_tambem_cria_ficha_manual()
    {
        var anasup = await ComoAsync(Criar(), PinAnaSup);

        var ficha = await Ler<IntervencaoDto>(await anasup.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E4")));

        Assert.Equal(101, ficha.Tecno);
    }

    // ---------------------------------------------------------------- Detalhe e edição

    [Fact]
    public async Task Detalhe_mostra_a_ficha_completa_e_os_bloqueios_de_quem_pede()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);

        var deAna = await DetalheDe(ana, "F3");
        Assert.Equal("Mostrador apagado", deAna.Problema);
        Assert.Equal("Trocado o cabo", deAna.Resolucao);
        Assert.Equal(EstadoIntervencao.Concluida, deAna.Estado);
        Assert.False(deAna.PodeEditar);
        Assert.False(deAna.PodeCancelar);

        var doChefe = await DetalheDe(chefe, "F3");
        Assert.True(doChefe.PodeEditar);
        Assert.True(doChefe.PodeEditarGestao);
        Assert.False(doChefe.PodeCancelar);
        Assert.True((await DetalheDe(chefe, "F1")).PodeCancelar);
        Assert.False((await DetalheDe(chefe, "H1")).PodeEditar);
    }

    [Fact]
    public async Task Detalhe_de_ficha_de_outra_pessoa_so_para_o_supervisor()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);

        Assert.Equal(HttpStatusCode.NotFound, (await Detalhe(ana, "F2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Detalhe(chefe, "F2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Detalhe(chefe, "NAOEXISTE")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Detalhe(chefe, new string('x', 26))).StatusCode);
    }

    [Fact]
    public async Task Operador_edita_a_execucao_da_sua_ficha_em_curso()
    {
        var ana = await ComoAsync(Criar(), PinAna);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);
        var inicio = Dia.AddHours(8);

        var r = await ana.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(
            HoraInicio: inicio, HoraFim: inicio.AddHours(2), HorasFrio: 2.4m, HorasConf: 1.5m, Problema: "Fuga no vedante", Resolucao: "Vedante trocado", Obs: "Voltar na semana seguinte",
            LimRem: true, DocExterno: "1479/2026", NomeFornecedor: "Frio Industrial", NoFornecedor: 7000004072));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var d = await Ler<FichaDetalheDto>(r);
        Assert.Equal(EstadoIntervencao.EmCurso, d.Estado);
        Assert.Equal(inicio, d.HoraInicio);
        Assert.Equal(inicio.AddHours(2), d.HoraFim);
        Assert.Equal(2.4m, d.HorasFrio);
        Assert.Equal(1.5m, d.HorasConf);
        Assert.Equal("Fuga no vedante", d.Problema);
        Assert.Equal("Vedante trocado", d.Resolucao);
        Assert.Equal("Voltar na semana seguinte", d.Obs);
        Assert.True(d.LimRem);
        Assert.Equal("1479/2026", d.DocExterno);
        Assert.Equal(7000004072, d.NoFornecedor);
        Assert.Equal("Frio Industrial, Lda", d.NomeFornecedor);
        Assert.False(d.PodeEditarGestao);
        var lida = await DetalheDe(ana, "F1");
        Assert.Equal(d.Resolucao, lida.Resolucao);
        Assert.Equal(d.HoraFim, lida.HoraFim);
        Assert.Equal(d.NoFornecedor, lida.NoFornecedor);
    }

    [Fact]
    public async Task Operador_nao_mexe_em_tecnico_data_tipos_nem_periodicidade()
    {
        var ana = await ComoAsync(Criar(), PinAna);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);

        foreach (var pedido in new[]
        {
            new PedidoFicha(Tecno: 102), new PedidoFicha(Data: DateOnly.FromDateTime(Dia).AddDays(2)), new PedidoFicha(Tipo: TipoIntervencao.Curativa),
            new PedidoFicha(TipoManut: "Externa"), new PedidoFicha(Periodicidade: "Anual")
        })
        {
            var r = await ana.PutAsJsonAsync("/intervencoes/F1", pedido);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("sem_permissao", await Erro(r));
        }

        var antes = await DetalheDe(ana, "F1");
        Assert.Equal(TipoIntervencao.Preventiva, antes.Tipo);
        Assert.Equal(101, antes.Tecno);
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(Tipo: TipoIntervencao.Preventiva, Tecno: 101, Resolucao: "ok"))).StatusCode);
    }

    [Fact]
    public async Task Operador_so_edita_as_suas_fichas_em_curso()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);

        Assert.Equal(HttpStatusCode.Conflict, (await ana.PutAsJsonAsync("/intervencoes/F3", new PedidoFicha(Resolucao: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(Resolucao: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.PutAsJsonAsync("/intervencoes/F2", new PedidoFicha(Resolucao: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.PutAsJsonAsync("/intervencoes/NAOEXISTE", new PedidoFicha(Resolucao: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ComoAsync(fabrica, PinNovo)).PutAsJsonAsync("/intervencoes/F1", new PedidoFicha())).StatusCode);
    }

    [Fact]
    public async Task Supervisor_edita_gestao_de_qualquer_ficha_nao_final_sem_mudar_o_estado()
    {
        var chefe = await ComoAsync(Criar(), PinChefe);
        var data = DateOnly.FromDateTime(Dia).AddDays(3);

        var r = await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(Data: data, Tipo: TipoIntervencao.Curativa, TipoManut: "externa", Periodicidade: "Mensal", Tecno: 102));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var d = await Ler<FichaDetalheDto>(r);
        Assert.Equal(EstadoIntervencao.Planeada, d.Estado);
        Assert.Equal(data, d.Data);
        Assert.Equal(TipoIntervencao.Curativa, d.Tipo);
        Assert.Equal("Externa", d.TipoManut);
        Assert.Equal("Mensal", d.Periodicidade);
        Assert.Equal(102, d.Tecno);
        Assert.Equal("Rui Costa", d.Tecnico);
        Assert.True(d.PodeEditarGestao);
        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/intervencoes/F3", new PedidoFicha(Obs: "Confirmado"))).StatusCode);
    }

    [Fact]
    public async Task Supervisor_recusa_tecnico_inexistente_ou_inativo_e_fichas_finais()
    {
        var chefe = await ComoAsync(Criar(), PinChefe);

        var inativo = await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(Tecno: 104));
        Assert.Equal(HttpStatusCode.NotFound, inativo.StatusCode);
        Assert.Equal("tecnico_invalido", await Erro(inativo));
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(Tecno: 777))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PutAsJsonAsync("/intervencoes/H1", new PedidoFicha(Obs: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PutAsJsonAsync("/intervencoes/H3", new PedidoFicha(Obs: "x"))).StatusCode);
    }

    [Fact]
    public async Task Mudar_o_tecnico_principal_tira_o_novo_dos_adicionais()
    {
        var chefe = await ComoAsync(Criar(), PinChefe);
        await chefe.PutAsJsonAsync("/planeadas/F1/tecnico", new PedidoAtribuicao(101));
        await chefe.PutAsJsonAsync("/planeadas/F1/tecnicos", new PedidoAdicionais([102]));
        Assert.Equal(102, Assert.Single((await DetalheDe(chefe, "F1")).Adicionais!).Tecno);

        var d = await Ler<FichaDetalheDto>(await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(Tecno: 102)));

        Assert.Equal(102, d.Tecno);
        Assert.Null(d.Adicionais);
    }

    [Fact]
    public async Task Edicao_valida_limites_e_formatos()
    {
        var chefe = await ComoAsync(Criar(), PinChefe);
        var invalidos = new[]
        {
            new PedidoFicha(HorasConf: 1000m), new PedidoFicha(HorasFrio: -1m), new PedidoFicha(Problema: new string('a', 201)), new PedidoFicha(Resolucao: new string('a', 255)),
            new PedidoFicha(DocExterno: new string('a', 26)), new PedidoFicha(TipoManut: "Mista"), new PedidoFicha(Data: new DateOnly(1800, 1, 1)), new PedidoFicha(Tecno: 0),
            new PedidoFicha(MoExterna: -1m), new PedidoFicha(HoraInicio: Dia.AddHours(9), HoraFim: Dia.AddHours(8))
        };

        foreach (var p in invalidos)
        {
            var r = await chefe.PutAsJsonAsync("/intervencoes/F1", p);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("pedido_invalido", await Erro(r));
        }
        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(HorasConf: 999.99m, Problema: new string('a', 200)))).StatusCode);
    }

    [Fact]
    public async Task Edicao_aceita_limpar_o_fim_com_1900_e_mostra_o_vazio_como_nulo()
    {
        var ana = await ComoAsync(Criar(), PinAna);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);
        await ana.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(HoraInicio: Dia.AddHours(1), HoraFim: Dia.AddHours(2)));
        Assert.NotNull((await DetalheDe(ana, "F1")).HoraFim);

        var d = await Ler<FichaDetalheDto>(await ana.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(HoraFim: new DateTime(1900, 1, 1))));

        Assert.Null(d.HoraFim);
    }

    // ---------------------------------------------------------------- Material

    [Fact]
    public async Task Editar_material_recalcula_o_custo_e_os_totais()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);
        var m = await Ler<MaterialDto>(await ana.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Cabo", 2, 1.5m)));

        var r = await ana.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Cabo novo", 3, 2.5m));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var editado = await Ler<MaterialDto>(r);
        Assert.Equal(m.Id, editado.Id);
        Assert.Equal("Cabo novo", editado.Design);
        Assert.Equal(7.5m, editado.Custo);
        Assert.Equal(7.5m, Assert.Single((await DetalheDe(ana, "F1")).Materiais).Custo);
        var d = await DetalheDe(chefe, "F1");
        Assert.Equal(7.5m, d.MatInterno);
        Assert.Equal(7.5m, d.TotRef);
    }

    [Fact]
    public async Task Editar_material_valida_e_respeita_o_acesso()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);
        var m = await Ler<MaterialDto>(await ana.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Cabo", 2, 1.5m)));

        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Cabo", 0, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.PutAsJsonAsync("/intervencoes/F1/materiais/NAOEXISTE", new PedidoMaterial("Cabo", 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.PutAsJsonAsync("/intervencoes/F2/materiais/X", new PedidoMaterial("Cabo", 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PutAsJsonAsync("/intervencoes/F3/materiais/X", new PedidoMaterial("Cabo", 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PutAsJsonAsync("/intervencoes/H1/materiais/X", new PedidoMaterial("Cabo", 1, 1))).StatusCode);

        // O supervisor edita, junta e apaga material de uma ficha de outra pessoa.
        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Cabo", 4, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsJsonAsync("/intervencoes/F3/materiais", new PedidoMaterial("Fita", 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await chefe.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}")).StatusCode);
    }

    [Fact]
    public async Task Operador_continua_sem_juntar_material_a_ficha_de_outra_pessoa()
    {
        var ana = await ComoAsync(Criar(), PinAna);

        Assert.Equal(HttpStatusCode.NotFound, (await ana.PostAsJsonAsync("/intervencoes/F2/materiais", new PedidoMaterial("Fita", 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsJsonAsync("/intervencoes/F3/materiais", new PedidoMaterial("Fita", 1, 1))).StatusCode);
    }

    // ---------------------------------------------------------------- Mão de obra

    [Fact]
    public async Task Mao_de_obra_junta_edita_e_apaga_com_custo_e_totais_recalculados()
    {
        // O supervisor vê todos os custos; o que o operador vê está no teste seguinte.
        var ana = await ComoAsync(Criar(), PinChefe);

        var criada = await ana.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(102, 2, 10m));
        Assert.Equal(HttpStatusCode.OK, criada.StatusCode);
        var t = await Ler<TempoDto>(criada);
        Assert.Equal(20m, t.Custo);
        Assert.Equal("Rui Costa", t.Responsavel);
        Assert.Equal(10m, t.CustoHora);
        var d = await DetalheDe(ana, "F1");
        Assert.Equal(20m, d.MoInterna);
        Assert.Equal(20m, d.TotMo);

        var editada = await Ler<TempoDto>(await ana.PutAsJsonAsync($"/intervencoes/F1/mao-de-obra/{t.Id}", new PedidoTempo(101, 3, 12.345m)));
        Assert.Equal(37.035m, editada.Custo);
        Assert.Equal("Ana Silva", editada.Responsavel);
        Assert.Equal(37.035m, (await DetalheDe(ana, "F1")).TotMo);
        Assert.Equal(editada, Assert.Single(await Ler<List<TempoDto>>(await ana.GetAsync("/intervencoes/F1/mao-de-obra"))));

        Assert.Equal(HttpStatusCode.NoContent, (await ana.DeleteAsync($"/intervencoes/F1/mao-de-obra/{t.Id}")).StatusCode);
        Assert.Equal(0m, (await DetalheDe(ana, "F1")).TotMo);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.DeleteAsync($"/intervencoes/F1/mao-de-obra/{t.Id}")).StatusCode);
    }

    [Fact]
    public async Task Mao_de_obra_valida_tecnico_horas_e_acesso()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);

        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(102, 0, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(102, null, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(null, 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(0, 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(102, 1, -1))).StatusCode);
        var inativo = await ana.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(104, 1, 1));
        Assert.Equal(HttpStatusCode.NotFound, inativo.StatusCode);
        Assert.Equal("tecnico_invalido", await Erro(inativo));

        Assert.Equal(HttpStatusCode.NotFound, (await ana.PostAsJsonAsync("/intervencoes/F2/mao-de-obra", new PedidoTempo(102, 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsJsonAsync("/intervencoes/F3/mao-de-obra", new PedidoTempo(102, 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync("/intervencoes/F2/mao-de-obra")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.PutAsJsonAsync("/intervencoes/F1/mao-de-obra/NAOEXISTE", new PedidoTempo(102, 1, 1))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsJsonAsync("/intervencoes/F3/mao-de-obra", new PedidoTempo(102, 1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsJsonAsync("/intervencoes/H1/mao-de-obra", new PedidoTempo(102, 1, 1))).StatusCode);
    }

    [Fact]
    public async Task Custo_hora_sugerido_so_do_proprio_tecnico_ou_do_supervisor()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);
        Assert.Equal(0m, await Ler<decimal>(await chefe.GetAsync("/tecnicos/102/custo-hora")));

        await chefe.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(102, 2, 8m));
        await chefe.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(102, 1, 9.5m));
        await chefe.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(101, 1, 4m));

        Assert.Equal(9.5m, await Ler<decimal>(await chefe.GetAsync("/tecnicos/102/custo-hora")));
        Assert.Equal(4m, await Ler<decimal>(await chefe.GetAsync("/tecnicos/101/custo-hora")));
        Assert.Equal(4m, await Ler<decimal>(await ana.GetAsync("/tecnicos/101/custo-hora")));
        Assert.Equal(0m, await Ler<decimal>(await ana.GetAsync("/tecnicos/102/custo-hora")));
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.GetAsync("/tecnicos/0/custo-hora")).StatusCode);
    }

    [Fact]
    public async Task Operador_nao_ve_custos_de_terceiros_externos_totais_nem_compras_e_nao_escreve_externos()
    {
        var fabrica = Criar(new ConfigManutencao(AvancaOrdem: true, ComprasFichaInt: false));
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);
        await chefe.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(102, 2, 10m));
        await chefe.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(101, 1, 4m));
        // O operador não fixa o custo/hora: usa o último do técnico (4 €/h).
        var propria = await Ler<TempoDto>(await ana.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(101, 1, 99m)));
        Assert.Equal(4m, propria.Custo);

        var d = await DetalheDe(ana, "F1");

        Assert.True(d.CustosOcultos);
        Assert.False(d.ExternosAMao);
        Assert.Equal(0m, d.MoInterna);
        Assert.Equal(0m, d.TotMo);
        Assert.Equal(0m, d.TotRef);
        Assert.All(d.MaoDeObra.Where(t => t.Tecno == 101), t => Assert.Equal(4m, t.Custo));
        Assert.Equal(0m, d.MaoDeObra.Single(t => t.Tecno == 102).Custo);
        Assert.Equal(0m, Assert.Single(await Ler<List<TempoDto>>(await ana.GetAsync("/intervencoes/F1/mao-de-obra")), t => t.Tecno == 102).Custo);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/intervencoes/F1/compras")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(MoExterna: 50m, MatExterno: 5m))).StatusCode);
        var doChefe = await DetalheDe(chefe, "F1");
        Assert.Equal(0m, doChefe.MoExterna);
        Assert.Equal(0m, doChefe.MatExterno);
        Assert.False(doChefe.CustosOcultos);
        Assert.Equal(28m, doChefe.MoInterna);
    }

    [Fact]
    public async Task Hora_noutro_dia_so_o_supervisor_aceita_dentro_do_intervalo()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);

        var recusada = await ana.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(HoraInicio: Dia.AddDays(-1).AddHours(8)));
        Assert.Equal(HttpStatusCode.BadRequest, recusada.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(HoraFim: Dia.AddDays(2)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(HoraInicio: Dia.AddDays(-1).AddHours(8)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(HoraFim: Dia.AddDays(5)))).StatusCode);
    }

    [Fact]
    public async Task Operador_nao_fixa_o_custo_hora_e_so_lanca_horas_de_quem_esta_na_ficha()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);
        await chefe.PutAsJsonAsync("/planeadas/F1/tecnicos", new PedidoAdicionais([102]));
        var deColega = await Ler<TempoDto>(await chefe.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(102, 2, 10m)));

        // O custo/hora que o operador envia é ignorado: na criação vale o último do técnico, na edição o gravado na linha.
        var criada = await Ler<TempoDto>(await ana.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(102, 1, 99m)));
        Assert.Equal(0m, criada.Custo);
        Assert.Equal(20m + 10m, (await DetalheDe(chefe, "F1")).TotMo);

        // Ler a ficha (a linha do colega vem sem custos), editar essa linha e gravar não muda o custo que o supervisor vê.
        var vista = (await DetalheDe(ana, "F1")).MaoDeObra.Single(t => t.Id == deColega.Id);
        Assert.Equal(0m, vista.CustoHora);
        var editada = await ana.PutAsJsonAsync($"/intervencoes/F1/mao-de-obra/{deColega.Id}", new PedidoTempo(102, 3, vista.CustoHora));
        Assert.Equal(HttpStatusCode.OK, editada.StatusCode);
        Assert.Equal(30m + 10m, (await DetalheDe(chefe, "F1")).TotMo);
        Assert.Equal(10m, (await DetalheDe(chefe, "F1")).MaoDeObra.Single(t => t.Id == deColega.Id).CustoHora);

        // Quem não está na ficha (o chefe, número 100) não se pode lançar pelo operador.
        var alheio = await ana.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(100, 1, 1));
        Assert.Equal(HttpStatusCode.NotFound, alheio.StatusCode);
        Assert.Equal("tecnico_invalido", await Erro(alheio));
        Assert.Equal(HttpStatusCode.NotFound, (await ana.PutAsJsonAsync($"/intervencoes/F1/mao-de-obra/{deColega.Id}", new PedidoTempo(100, 1, 1))).StatusCode);
    }

    [Fact]
    public async Task Supervisor_junta_material_a_ficha_de_outra_pessoa_e_o_detalhe_mostra_o()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);

        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Filtro", 1, 12m))).StatusCode);

        Assert.Equal("Filtro", Assert.Single((await DetalheDe(chefe, "F1")).Materiais).Design);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ComoAsync(fabrica, "951357")).GetAsync("/intervencoes/F1")).StatusCode);
        Assert.Equal("Filtro", Assert.Single((await DetalheDe(await ComoAsync(fabrica, "951357"), "F1")).Materiais).Design);
    }

    [Fact]
    public async Task Ficha_planeada_para_a_semana_seguinte_e_iniciada_hoje_deixa_o_operador_corrigir_as_horas_de_hoje()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);
        await ana.PostAsync("/intervencoes/F1/iniciar", null);
        await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(Data: DateOnly.FromDateTime(Dia).AddDays(7)));

        var r = await ana.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(HoraInicio: Dia.AddMinutes(1), HoraFim: Dia.AddMinutes(30)));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Dois_editores_com_a_mesma_versao_o_segundo_recebe_409()
    {
        var fabrica = Criar();
        var chefe = await ComoAsync(fabrica, PinChefe);
        var anasup = await ComoAsync(fabrica, PinAnaSup);
        var lida = await DetalheDe(chefe, "F1");
        Assert.False(string.IsNullOrEmpty(lida.Versao));

        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(Obs: "primeiro", Versao: lida.Versao))).StatusCode);
        var segundo = await anasup.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(Obs: "segundo", Versao: lida.Versao));

        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        Assert.Equal("primeiro", (await DetalheDe(chefe, "F1")).Obs);
        var nova = await DetalheDe(chefe, "F1");
        Assert.NotEqual(lida.Versao, nova.Versao);
        Assert.Equal(HttpStatusCode.OK, (await anasup.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(Obs: "terceiro", Versao: nova.Versao))).StatusCode);
    }

    [Fact]
    public async Task Fornecedor_tem_de_existir_e_o_nome_vem_do_numero()
    {
        var chefe = await ComoAsync(Criar(), PinChefe);

        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(NoFornecedor: 123))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(NoFornecedor: RegrasFicha.MaxNoFornecedor + 1))).StatusCode);
        var d = await Ler<FichaDetalheDto>(await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(NoFornecedor: 7000004072, NomeFornecedor: "Nome inventado")));
        Assert.Equal("Frio Industrial, Lda", d.NomeFornecedor);
        var sem = await Ler<FichaDetalheDto>(await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(NoFornecedor: 0)));
        Assert.Equal("", sem.NomeFornecedor);
    }

    [Fact]
    public async Task Supervisor_sem_numero_de_tecnico_nao_cria_ficha_manual()
    {
        var sem = await ComoAsync(Criar(), "951357");

        var r = await sem.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E1"));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("sem_tecno", await Erro(r));
        Assert.Equal(HttpStatusCode.OK, (await sem.GetAsync("/intervencoes/F3/detalhe")).StatusCode);
    }

    [Fact]
    public async Task Periodicidade_da_ficha_manual_nao_distingue_maiusculas()
    {
        var ana = await ComoAsync(Criar(), PinAna);

        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/intervencoes", new PedidoFichaManual("E3", Periodicidade: "trimestral"))).StatusCode);
    }

    // ---------------------------------------------------------------- Totais e compras

    [Fact]
    public async Task Totais_externos_vem_das_compras_quando_ha_linhas_ligadas()
    {
        var fabrica = Criar();
        var repo = (RepositorioIntervencoesMemoria)fabrica.Services.GetRequiredService<IRepositorioIntervencoes>();
        repo.LigarCompra("F1", new CompraLigadaDto(DateOnly.FromDateTime(Dia), "FF 12", "Frio Industrial", "R1", "Mão de obra", 1, 100m, RegrasManutencao.CompraMaoObra));
        repo.LigarCompra("F1", new CompraLigadaDto(DateOnly.FromDateTime(Dia), "FF 12", "Frio Industrial", "R2", "Compressor", 1, 40m, RegrasManutencao.CompraPecas));
        var chefe = await ComoAsync(fabrica, PinChefe);
        await chefe.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(102, 2, 10m));
        await chefe.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Cabo", 2, 1.5m));

        var d = await DetalheDe(chefe, "F1");

        Assert.Equal(20m, d.MoInterna);
        Assert.Equal(100m, d.MoExterna);
        Assert.Equal(120m, d.TotMo);
        Assert.Equal(3m, d.MatInterno);
        Assert.Equal(40m, d.MatExterno);
        Assert.Equal(43m, d.TotRef);
        Assert.False(d.ExternosAMao);
        var compras = await Ler<List<CompraLigadaDto>>(await chefe.GetAsync("/intervencoes/F1/compras"));
        Assert.Equal(2, compras.Count);
    }

    [Fact]
    public async Task Com_compras_na_ficha_e_sem_linhas_ligadas_os_externos_a_mao_nao_se_gravam()
    {
        var chefe = await ComoAsync(Criar(new ConfigManutencao(ComprasFichaInt: true)), PinChefe);

        var d = await Ler<FichaDetalheDto>(await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(MoExterna: 55m, MatExterno: 5m)));

        Assert.Equal(0m, d.MoExterna);
        Assert.Equal(0m, d.MatExterno);
        Assert.False(d.ExternosAMao);
    }

    [Fact]
    public async Task Sem_compras_na_ficha_os_externos_escrevem_se_a_mao_e_entram_nos_totais()
    {
        var chefe = await ComoAsync(Criar(new ConfigManutencao(ComprasFichaInt: false)), PinChefe);
        await chefe.PostAsJsonAsync("/intervencoes/F1/mao-de-obra", new PedidoTempo(102, 2, 10m));

        var d = await Ler<FichaDetalheDto>(await chefe.PutAsJsonAsync("/intervencoes/F1", new PedidoFicha(MoExterna: 55m, MatExterno: 5m)));

        Assert.True(d.ExternosAMao);
        Assert.Equal(55m, d.MoExterna);
        Assert.Equal(5m, d.MatExterno);
        Assert.Equal(75m, d.TotMo);
        Assert.Equal(5m, d.TotRef);
        Assert.Empty(await Ler<List<CompraLigadaDto>>(await chefe.GetAsync("/intervencoes/F1/compras")));
    }

    [Fact]
    public async Task Compras_so_o_supervisor_as_ve()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/intervencoes/F3/compras")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await chefe.GetAsync("/intervencoes/F2/compras")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.GetAsync("/intervencoes/NAOEXISTE/compras")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fabrica.CreateClient().GetAsync("/intervencoes/F3/compras")).StatusCode);
    }

    // ---------------------------------------------------------------- Fichas antigas

    [Fact]
    public async Task Ficha_antiga_fecha_se_a_mao_e_fica_fora_das_transicoes()
    {
        var fabrica = Criar();
        var repo = (RepositorioIntervencoesMemoria)fabrica.Services.GetRequiredService<IRepositorioIntervencoes>();
        repo.SemearAntiga("A1");
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);

        var d = await DetalheDe(chefe, "A1");
        Assert.True(d.Antiga);
        Assert.True(d.PodeFecharAntiga);
        Assert.True(d.PodeEditar);
        Assert.False(d.PodeCancelar);
        Assert.False(d.Fechada);

        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsync("/intervencoes/A1/iniciar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsJsonAsync("/intervencoes/A1/concluir", new PedidoConclusao("x", new TimeOnly(8, 0), new TimeOnly(9, 0)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsync("/validacao/A1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsJsonAsync("/validacao/A1/devolver", new PedidoDevolucao("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PostAsJsonAsync("/intervencoes/A1/cancelar", new PedidoCancelamento("x"))).StatusCode);
        Assert.DoesNotContain(await Ler<List<IntervencaoDto>>(await chefe.GetAsync("/validacao")), i => i.Id == "A1");

        var fechada = await Ler<FichaDetalheDto>(await chefe.PutAsJsonAsync("/intervencoes/A1/antiga", new PedidoFechoAntiga(true, true)));
        Assert.True(fechada.Fechada);
        Assert.True(fechada.Supervisao);
        Assert.Equal(EstadoIntervencao.Validada, fechada.Estado);
        Assert.True(fechada.PodeEditar);

        var reaberta = await Ler<FichaDetalheDto>(await chefe.PutAsJsonAsync("/intervencoes/A1/antiga", new PedidoFechoAntiga(true, false)));
        Assert.False(reaberta.Fechada);
        Assert.Equal(EstadoIntervencao.Concluida, reaberta.Estado);
    }

    [Fact]
    public async Task Fechar_ficha_antiga_so_para_supervisor_e_so_fichas_antigas()
    {
        var fabrica = Criar();
        ((RepositorioIntervencoesMemoria)fabrica.Services.GetRequiredService<IRepositorioIntervencoes>()).SemearAntiga("A1");
        var ana = await ComoAsync(fabrica, PinAna);
        var chefe = await ComoAsync(fabrica, PinChefe);

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PutAsJsonAsync("/intervencoes/A1/antiga", new PedidoFechoAntiga(true, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chefe.PutAsJsonAsync("/intervencoes/F1/antiga", new PedidoFechoAntiga(true, true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PutAsJsonAsync("/intervencoes/NAOEXISTE/antiga", new PedidoFechoAntiga(true, true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chefe.PutAsJsonAsync("/intervencoes/A1/antiga", new PedidoFechoAntiga(null, true))).StatusCode);
    }

    [Fact]
    public async Task Fechar_antiga_externa_exige_fornecedor_e_documento_quando_a_configuracao_pede()
    {
        var fabrica = Criar(new ConfigManutencao(ExtExigeDoc: true));
        ((RepositorioIntervencoesMemoria)fabrica.Services.GetRequiredService<IRepositorioIntervencoes>()).SemearAntiga("A1", tipoManut: "Externa");
        var chefe = await ComoAsync(fabrica, PinChefe);

        var recusada = await chefe.PutAsJsonAsync("/intervencoes/A1/antiga", new PedidoFechoAntiga(true, true));
        Assert.Equal(HttpStatusCode.Conflict, recusada.StatusCode);
        Assert.Equal("externa_sem_documento", await Erro(recusada));
        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/intervencoes/A1/antiga", new PedidoFechoAntiga(true, false))).StatusCode);

        await chefe.PutAsJsonAsync("/intervencoes/A1", new PedidoFicha(NoFornecedor: 7000004072, DocExterno: "1479/2026"));
        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/intervencoes/A1/antiga", new PedidoFechoAntiga(true, true))).StatusCode);
    }

    [Fact]
    public async Task Fechar_antiga_externa_sem_exigencia_na_configuracao_fecha_sem_documento()
    {
        var fabrica = Criar(new ConfigManutencao(ExtExigeDoc: false));
        ((RepositorioIntervencoesMemoria)fabrica.Services.GetRequiredService<IRepositorioIntervencoes>()).SemearAntiga("A1", tipoManut: "Externa");
        var chefe = await ComoAsync(fabrica, PinChefe);

        Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync("/intervencoes/A1/antiga", new PedidoFechoAntiga(true, true))).StatusCode);
    }

    // ---------------------------------------------------------------- Contadores e fornecedores

    [Fact]
    public async Task Contadores_contam_atrasadas_e_fora_de_prazo_como_o_kpi_e_as_fichas_por_validar()
    {
        var fabrica = Criar();
        var chefe = await ComoAsync(fabrica, PinChefe);

        // F1 está para hoje mas a prevista (última feita + 1 mês) já passou: conta como fora de prazo. F3 é a ficha por validar.
        var inicial = await Ler<ContadoresMenuDto>(await chefe.GetAsync("/intervencoes/contadores"));
        Assert.Equal(1, inicial.PlaneadasAtrasadas);
        Assert.Equal(1, inicial.PorValidar);

        // F4 sem tarefa, com a data de ontem: atrasada.
        await chefe.PutAsJsonAsync("/intervencoes/F4", new PedidoFicha(Data: DateOnly.FromDateTime(Dia).AddDays(-1)));
        Assert.Equal(2, (await Ler<ContadoresMenuDto>(await chefe.GetAsync("/intervencoes/contadores"))).PlaneadasAtrasadas);

        // Cancelar salta a ocorrência: a seguinte da tarefa já não está fora de prazo.
        await chefe.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Salta"));
        Assert.Equal(1, (await Ler<ContadoresMenuDto>(await chefe.GetAsync("/intervencoes/contadores"))).PlaneadasAtrasadas);

        await chefe.PostAsync("/validacao/F3", null);
        Assert.Equal(0, (await Ler<ContadoresMenuDto>(await chefe.GetAsync("/intervencoes/contadores"))).PorValidar);
    }

    [Fact]
    public async Task Contadores_so_para_o_supervisor()
    {
        var fabrica = Criar();

        Assert.Equal(HttpStatusCode.Forbidden, (await (await ComoAsync(fabrica, PinAna)).GetAsync("/intervencoes/contadores")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fabrica.CreateClient().GetAsync("/intervencoes/contadores")).StatusCode);
    }

    [Fact]
    public async Task Fornecedores_pesquisam_com_duas_letras_no_minimo()
    {
        var fabrica = Criar();
        var ana = await ComoAsync(fabrica, PinAna);

        Assert.Equal(7000004072, Assert.Single(await Ler<List<FornecedorDto>>(await ana.GetAsync("/fornecedores?q=frio"))).No);
        Assert.Empty(await Ler<List<FornecedorDto>>(await ana.GetAsync("/fornecedores?q=f")));
        Assert.Empty(await Ler<List<FornecedorDto>>(await ana.GetAsync("/fornecedores")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await fabrica.CreateClient().GetAsync("/fornecedores?q=frio")).StatusCode);
    }
}
