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

    private readonly WebApplicationFactory<Program> _fabrica;

    public FichaConferenciaApiTests()
    {
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
                    new RepositorioIntervencoesMemoria(sp.GetRequiredService<TimeProvider>(), new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false))));
                s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(_pecas));
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

        _pecas.FalharConfirmar = false;
        var segunda = await chefe.PostAsync("/validacao/F1", null);

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Null(JsonDocument.Parse(await segunda.Content.ReadAsStringAsync()).RootElement.GetProperty("avisoStock").GetString());
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
        Assert.Equal(EstadoIntervencao.Cancelada, (await r.Content.ReadFromJsonAsync<IntervencaoDto>())!.Estado);
        Assert.Equal(1, await NoLugarAsync(chefe, "A-02-1-01"));
    }

    [Fact]
    public async Task Memoria_guarda_e_devolve_o_lugar_da_saida()
    {
        var r = new RepositorioPecasMemoria();
        Assert.True((await r.DarSaidaAsync("F1", "M1", "P001", 1, "AA", default, "A-02-1-01")).Ok);
        Assert.True((await r.DarSaidaAsync("F1", "M2", "P001", 1, "AA", default)).Ok);

        Assert.Equal("A-02-1-01", await r.LugarDaSaidaAsync("M1", default));
        Assert.Null(await r.LugarDaSaidaAsync("M2", default));
        Assert.Null(await r.LugarDaSaidaAsync("NAOEXISTE", default));
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
        public Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct) => memoria.EnriquecerMateriaisAsync(materiais, ct);
        public Task<string?> LugarDaSaidaAsync(string materialId, CancellationToken ct) => memoria.LugarDaSaidaAsync(materialId, ct);
    }
}
