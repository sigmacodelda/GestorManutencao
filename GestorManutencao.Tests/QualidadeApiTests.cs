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

// Circuito: técnico conclui, supervisor valida ou devolve (com motivo), Qualidade indica quem higienizou e conclui.
public class QualidadeApiTests : IDisposable
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
            (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918")),
            (new UtilizadorApp("qual", "Marta Qualidade", Perfil.Qualidade, true, false), Hasher.Hash("715329")),
            (new UtilizadorApp("qual2", "Qualidade Com Numero", Perfil.Qualidade, true, false, Tecno: 150), Hasher.Hash("951372")),
            (new UtilizadorApp("chefe2", "Outro Chefe", Perfil.Supervisor, true, false, Tecno: 160), Hasher.Hash("268413"))
        ]))))
    );

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAsync(string pin)
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    // F3 já vem concluída pela Ana na amostra em memória.
    private async Task ValidarF3Async()
    {
        var chefe = await ComoAsync("603918");
        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsync("/validacao/F3", null)).StatusCode);
    }

    [Fact]
    public async Task Validada_fica_aberta_e_so_a_Qualidade_a_ve()
    {
        await ValidarF3Async();
        var qual = await ComoAsync("715329");

        var fila = await qual.GetFromJsonAsync<List<IntervencaoDto>>("/qualidade");
        Assert.Equal("F3", Assert.Single(fila!).Id);

        var chefe = await ComoAsync("603918");
        Assert.Equal(HttpStatusCode.Forbidden, (await chefe.GetAsync("/qualidade")).StatusCode);
        var ana = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/qualidade")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsJsonAsync("/qualidade/F3/concluir", new PedidoConclusaoQualidade("Fulano"))).StatusCode);
    }

    [Fact]
    public async Task Qualidade_nao_conclui_sem_nome_de_quem_higienizou()
    {
        await ValidarF3Async();
        var qual = await ComoAsync("715329");

        Assert.Equal(HttpStatusCode.BadRequest, (await qual.PostAsJsonAsync("/qualidade/F3/concluir", new PedidoConclusaoQualidade("  "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await qual.PostAsJsonAsync("/qualidade/F3/concluir", new PedidoConclusaoQualidade(null))).StatusCode);
        Assert.Single((await qual.GetFromJsonAsync<List<IntervencaoDto>>("/qualidade"))!);
    }

    [Fact]
    public async Task Qualidade_conclui_uma_so_vez_e_o_historico_fica_completo()
    {
        await ValidarF3Async();
        var qual = await ComoAsync("715329");

        var ok = await qual.PostAsJsonAsync("/qualidade/F3/concluir", new PedidoConclusaoQualidade("Limpezas Silva"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Empty((await qual.GetFromJsonAsync<List<IntervencaoDto>>("/qualidade"))!);
        // A higienização não se altera: concluir outra vez já não é possível.
        Assert.Equal(HttpStatusCode.Conflict, (await qual.PostAsJsonAsync("/qualidade/F3/concluir", new PedidoConclusaoQualidade("Outro"))).StatusCode);

        var chefe = await ComoAsync("603918");
        var d = (await chefe.GetFromJsonAsync<FichaDetalheDto>("/intervencoes/F3/detalhe"))!;
        Assert.True(d.Fechada);
        Assert.True(d.LimRem);
        Assert.Equal(EstadoIntervencao.Validada, d.Estado);
        var etapas = d.Historico!.ToDictionary(e => e.Etapa);
        Assert.Equal("Ana Silva", etapas[RegrasQualidade.EtapaExecucao].Quem);
        Assert.Equal("Chefe de Turno", etapas[RegrasQualidade.EtapaValidacao].Quem);
        Assert.Equal("Limpezas Silva", etapas[RegrasQualidade.EtapaHigienizacao].Quem);
        Assert.Equal("Marta Qualidade", etapas[RegrasQualidade.EtapaConclusao].Quem);
        Assert.All(new[] { RegrasQualidade.EtapaValidacao, RegrasQualidade.EtapaHigienizacao, RegrasQualidade.EtapaConclusao }, e => Assert.NotNull(etapas[e].Quando));
    }

    [Fact]
    public async Task Ficha_que_nao_esta_validada_da_409_e_inexistente_404()
    {
        var qual = await ComoAsync("715329");

        Assert.Equal(HttpStatusCode.Conflict, (await qual.PostAsJsonAsync("/qualidade/F3/concluir", new PedidoConclusaoQualidade("X"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await qual.PostAsJsonAsync("/qualidade/nao-existe/concluir", new PedidoConclusaoQualidade("X"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await qual.GetAsync("/qualidade/F3/detalhe")).StatusCode);
    }

    [Fact]
    public async Task Devolucao_fica_no_historico_com_o_motivo_e_nao_vai_para_a_Qualidade()
    {
        var chefe = await ComoAsync("603918");
        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsJsonAsync("/validacao/F3/devolver", new PedidoDevolucao("Falta apertar a correia"))).StatusCode);
        var qual = await ComoAsync("715329");
        Assert.Empty((await qual.GetFromJsonAsync<List<IntervencaoDto>>("/qualidade"))!);

        var d = (await chefe.GetFromJsonAsync<FichaDetalheDto>("/intervencoes/F3/detalhe"))!;
        var dev = Assert.Single(d.Historico!, e => e.Etapa == RegrasQualidade.EtapaDevolucao);
        Assert.Equal("Falta apertar a correia", dev.Texto);
        Assert.Equal("Chefe de Turno", dev.Quem);
    }

    [Fact]
    public async Task Qualidade_nao_usa_as_rotas_do_tecnico_nem_do_supervisor_mesmo_com_numero()
    {
        var qual = await ComoAsync("951372");

        Assert.Equal(HttpStatusCode.Forbidden, (await qual.GetAsync("/intervencoes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await qual.GetAsync("/intervencoes/F3/detalhe")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await qual.GetAsync("/validacao")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await qual.PostAsync("/validacao/F3", null)).StatusCode);
    }

    [Fact]
    public void Regras_do_historico_de_devolucoes()
    {
        var h = RegrasQualidade.AcrescentarDevolucao(null, new DateTime(2026, 11, 18, 9, 30, 0), "chefe", "Falta | limpar\nlinha");
        h = RegrasQualidade.AcrescentarDevolucao(h, new DateTime(2026, 11, 19, 10, 0, 0), "chefe", "Segunda");
        var lidas = RegrasQualidade.LerDevolucoes(h).ToList();

        Assert.Equal(2, lidas.Count);
        Assert.Equal("Falta / limpar linha", lidas[0].Texto);
        Assert.Equal(new DateTime(2026, 11, 19, 10, 0, 0), lidas[1].Quando);
        Assert.Empty(RegrasQualidade.LerDevolucoes("lixo\n||"));
        Assert.True(RegrasQualidade.AguardaQualidade("Validada", false));
        Assert.False(RegrasQualidade.AguardaQualidade("Validada", true));
        Assert.NotNull(RegrasQualidade.ValidarHigienizacao(new string('a', 61)));
    }

    private static Task<HttpResponseMessage> Ativo(HttpClient c, string codigo, bool ativo) => c.PutAsJsonAsync($"/utilizadores/{codigo}/ativo", new PedidoAtivo(ativo));

    [Fact]
    public async Task Supervisor_inativa_utilizador_que_deixa_de_entrar_e_pode_reativar()
    {
        var chefe = await ComoAsync("603918");

        Assert.Equal(HttpStatusCode.NoContent, (await Ativo(chefe, "qual2", false)).StatusCode);
        var c = _fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/auth/login", new PedidoLogin("951372"))).StatusCode);
        // Continua na lista (nunca se apaga).
        Assert.Contains("qual2", await chefe.GetStringAsync("/utilizadores"));

        Assert.Equal(HttpStatusCode.NoContent, (await Ativo(chefe, "qual2", true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/auth/login", new PedidoLogin("951372"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Ativo(chefe, "nao.existe", false)).StatusCode);
        Assert.NotEqual(HttpStatusCode.NoContent, (await chefe.DeleteAsync("/utilizadores/qual2")).StatusCode); // apagar já não existe
    }

    [Fact]
    public async Task Nao_se_inativa_a_propria_conta_nem_outro_supervisor_e_operador_nao_inativa()
    {
        var chefe = await ComoAsync("603918");
        var propria = await Ativo(chefe, "chefe", false);
        Assert.Equal(HttpStatusCode.Conflict, propria.StatusCode);
        Assert.Contains("propria_conta", await propria.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await Ativo(chefe, "chefe2", false)).StatusCode);

        var ana = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.Forbidden, (await Ativo(ana, "qual", false)).StatusCode);
        var qual = await ComoAsync("715329");
        Assert.Equal(HttpStatusCode.Forbidden, (await Ativo(qual, "ana", false)).StatusCode);
    }
}
