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

// Preparação da API para o modo offline: fichas recentes, avaria idempotente (OpId) e repetição segura de iniciar e concluir.
public class ApiOfflineTests : IDisposable
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
            (new UtilizadorApp("rui", "Rui Costa", Perfil.Operador, true, false, Tecno: 102), Hasher.Hash("357159")),
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

    private static Task<List<IntervencaoDto>> Lista(HttpClient c, string url) => c.GetFromJsonAsync<List<IntervencaoDto>>(url)!;

    [Fact]
    public async Task Recentes_sem_token_da_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().GetAsync("/intervencoes/recentes")).StatusCode);
    }

    [Fact]
    public async Task Recentes_so_traz_as_fichas_fechadas_do_tecnico_na_janela_de_dias()
    {
        var ana = await ComoAsync("482913");

        var padrao = await Lista(ana, "/intervencoes/recentes");
        Assert.Equal(["F3"], padrao.Select(i => i.Id));

        // H1 (validada há 35 dias) só entra com janela maior; planeadas e em curso nunca entram.
        await ana.PostAsync("/intervencoes/F1/iniciar", null);
        var larga = await Lista(ana, "/intervencoes/recentes?dias=90");
        Assert.Equal(["F3", "H1"], larga.Select(i => i.Id));
        Assert.All(larga, i => Assert.True(i.Estado is EstadoIntervencao.Concluida or EstadoIntervencao.Validada or EstadoIntervencao.Cancelada));
        Assert.Equal(["F3"], (await Lista(ana, "/intervencoes/recentes?dias=1")).Select(i => i.Id));
    }

    [Fact]
    public async Task Recentes_nao_mostra_fichas_de_outros_tecnicos()
    {
        var rui = await ComoAsync("357159");
        var chefe = await ComoAsync("603918");

        Assert.Empty(await Lista(rui, "/intervencoes/recentes?dias=90"));
        Assert.DoesNotContain(await Lista(chefe, "/intervencoes/recentes?dias=90"), i => i.Id is "F3" or "H1");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("91")]
    [InlineData("-5")]
    public async Task Recentes_com_dias_invalidos_da_400(string dias)
    {
        var ana = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.GetAsync("/intervencoes/recentes?dias=" + dias)).StatusCode);
    }

    [Fact]
    public async Task Recentes_nao_colide_com_id_nem_com_todas()
    {
        var ana = await ComoAsync("482913");
        var chefe = await ComoAsync("603918");

        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/intervencoes/recentes")).StatusCode);
        Assert.Equal("F3", (await ana.GetFromJsonAsync<IntervencaoDto>("/intervencoes/F3"))!.Id);
        var todas = await chefe.GetFromJsonAsync<PaginaFichasDto>("/intervencoes/todas");
        Assert.NotEmpty(todas!.Itens);
    }

    [Fact]
    public async Task Recentes_devolve_no_maximo_500()
    {
        var repo = new RepositorioIntervencoesMemoria(TimeProvider.System);
        var ana = new TecnicoAtual("ana", 101, "Ana Silva");
        for (var i = 0; i < 510; i++)
        {
            var (_, ficha) = await repo.RegistarAvariaAsync("E4", "Avaria " + i, ana, default);
            await repo.ConcluirAsync(ficha!.Id, ana, "Feito", new TimeOnly(8, 0), new TimeOnly(9, 0), default);
        }

        Assert.Equal(RegrasIntervencao.MaxRecentes, (await repo.ListarRecentesAsync(ana, 30, default)).Count);
    }

    [Fact]
    public async Task Avaria_com_opid_cria_e_repetir_devolve_a_mesma_ficha()
    {
        var ana = await ComoAsync("482913");
        var pedido = new PedidoAvaria("E5", "Mostrador apagado", "Abcdef1234");

        var primeira = await ana.PostAsJsonAsync("/intervencoes/avaria", pedido);
        var segunda = await ana.PostAsJsonAsync("/intervencoes/avaria", pedido);

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var a = (await primeira.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        var b = (await segunda.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(a.Id, b.Id);
        Assert.Equal("Mostrador apagado", b.Anomalia);
        Assert.DoesNotContain("[op:", b.Anomalia);
        Assert.Single(await Lista(ana, "/intervencoes"), i => i.Anomalia == "Mostrador apagado");
    }

    [Fact]
    public async Task Avaria_com_opids_diferentes_cria_fichas_diferentes_e_sem_opid_nao_muda()
    {
        var ana = await ComoAsync("482913");

        var a = await (await ana.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria("E5", "Ruído", "AAAAAAAA"))).Content.ReadFromJsonAsync<IntervencaoDto>();
        var b = await (await ana.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria("E5", "Ruído", "BBBBBBBB"))).Content.ReadFromJsonAsync<IntervencaoDto>();
        var c = await (await ana.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria("E5", "Ruído"))).Content.ReadFromJsonAsync<IntervencaoDto>();
        var d = await (await ana.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria("E5", "Ruído"))).Content.ReadFromJsonAsync<IntervencaoDto>();

        Assert.Equal(4, new[] { a!.Id, b!.Id, c!.Id, d!.Id }.Distinct().Count());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("abcdefg h")]
    [InlineData("abcdefgh-1")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("")]
    public async Task Avaria_com_opid_invalido_da_400(string opId)
    {
        var ana = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria("E5", "Ruído", opId))).StatusCode);
    }

    [Fact]
    public async Task Avaria_com_opid_respeita_o_limite_do_texto()
    {
        var ana = await ComoAsync("482913");
        var opId = new string('a', RegrasIntervencao.MaxOpId);

        // O OpId vai num campo próprio: o limite é 200 com ou sem OpId.
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria("E5", new string('x', 200), opId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria("E5", new string('x', 201), opId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria("E5", new string('x', 200)))).StatusCode);
    }

    [Fact]
    public void Marcador_sai_do_texto_lido_e_so_quando_e_valido()
    {
        Assert.Equal("Não liga", RegrasIntervencao.SemMarcador("Não liga" + RegrasIntervencao.Marcador("abcdef12")));
        Assert.Equal("Não liga [op:abc]", RegrasIntervencao.SemMarcador("Não liga [op:abc]"));
        Assert.Equal("Sem marcador", RegrasIntervencao.SemMarcador("Sem marcador"));
        Assert.Equal(38, RegrasIntervencao.Marcador(new string('a', RegrasIntervencao.MaxOpId)).Length);
    }

    [Fact]
    public async Task Iniciar_repetido_pelo_mesmo_tecnico_e_ok_e_por_outro_nao()
    {
        var ana = await ComoAsync("482913");

        var a = await ana.PostAsync("/intervencoes/F1/iniciar", null);
        var b = await ana.PostAsync("/intervencoes/F1/iniciar", null);

        Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        var fichaB = (await b.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.Equal(EstadoIntervencao.EmCurso, fichaB.Estado);
        Assert.Equal((await a.Content.ReadFromJsonAsync<IntervencaoDto>())!.HoraInicio, fichaB.HoraInicio);
        // O Rui não vê a ficha que a Ana já tem.
        Assert.Equal(HttpStatusCode.NotFound, (await (await ComoAsync("357159")).PostAsync("/intervencoes/F1/iniciar", null)).StatusCode);
    }

    [Fact]
    public async Task Concluir_repetido_igual_e_ok_sem_gerar_outra_seguinte()
    {
        var ana = await ComoAsync("482913");
        var chefe = await ComoAsync("603918");
        await ana.PostAsync("/intervencoes/F1/iniciar", null);
        var pedido = new PedidoConclusao("Limpeza feita", new TimeOnly(8, 0, 0), new TimeOnly(9, 0, 0));

        var a = await ana.PostAsJsonAsync("/intervencoes/F1/concluir", pedido);
        var b = await ana.PostAsJsonAsync("/intervencoes/F1/concluir", pedido);

        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        Assert.Equal(EstadoIntervencao.Concluida, (await b.Content.ReadFromJsonAsync<IntervencaoDto>())!.Estado);
        Assert.Single(await Lista(chefe, "/planeadas"), i => i.Equipamento == "Compressor de ar 02" && i.Id != "F1");
    }

    [Fact]
    public async Task Concluir_repetido_com_texto_ou_horas_diferentes_continua_conflito()
    {
        var ana = await ComoAsync("482913");
        await ana.PostAsync("/intervencoes/F1/iniciar", null);
        await ana.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("Limpeza feita", new TimeOnly(8, 0), new TimeOnly(9, 0)));

        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("Outro texto", new TimeOnly(8, 0), new TimeOnly(9, 0)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("Limpeza feita", new TimeOnly(8, 0), new TimeOnly(9, 30)))).StatusCode);
    }
}
