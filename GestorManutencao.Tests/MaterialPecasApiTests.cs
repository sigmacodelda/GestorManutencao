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

// Material da ficha ligado a uma peça: saída automática, anulação ao editar/apagar e repetição da saída.
public class MaterialPecasApiTests : IDisposable
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
                (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
                (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918"))
            ])));
            s.Replace(ServiceDescriptor.Singleton<IRepositorioIntervencoes>(sp =>
                new RepositorioIntervencoesMemoria(sp.GetRequiredService<TimeProvider>(), new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false))));
            s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(new RepositorioPecasMemoria(
            [
                new PecaDto("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
                new PecaDto("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
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

    // Ficha F1 em curso da Ana.
    private async Task<HttpClient> ComoAnaAsync()
    {
        var c = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/intervencoes/F1/iniciar", null)).StatusCode);
        return c;
    }

    private static async Task<decimal> StockAsync(HttpClient c, string codigo = "P001") =>
        (await c.GetFromJsonAsync<PecaDto>($"/pecas/codigo/{codigo}"))!.Stock;

    private static async Task<MaterialDto> JuntarAsync(HttpClient c, string design, decimal qtt, decimal epv, string? refPeca = null) =>
        (await (await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial(design, qtt, epv, refPeca))).Content.ReadFromJsonAsync<MaterialDto>())!;

    [Fact]
    public async Task Material_com_peca_da_saida_e_apagar_devolve_o_stock()
    {
        var c = await ComoAnaAsync();
        var r = await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Parafuso M8", 4, 0.12m, "P001"));
        var m = await r.Content.ReadFromJsonAsync<MaterialDto>();
        Assert.True(m!.SaidaDada);
        Assert.Equal("P001", m.Ref);
        Assert.Equal(6, await StockAsync(c));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}")).StatusCode);
        Assert.Equal(10, await StockAsync(c));
    }

    [Fact]
    public async Task Editar_a_quantidade_desfaz_a_saida_antiga_e_da_a_nova()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        var e = await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Parafuso M8", 7, 0.12m, "P001"));
        Assert.True((await e.Content.ReadFromJsonAsync<MaterialDto>())!.SaidaDada);
        Assert.Equal(3, await StockAsync(c));
    }

    [Fact]
    public async Task Editar_sem_referencia_desliga_a_peca_e_devolve_o_stock()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        var e = await (await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Parafuso M8", 4, 0.12m))).Content.ReadFromJsonAsync<MaterialDto>();
        Assert.False(e!.SaidaDada);
        Assert.Equal("", e.Ref);
        Assert.Equal(10, await StockAsync(c));
    }

    [Fact]
    public async Task Sem_stock_avisa_mas_deixa_continuar()
    {
        var c = await ComoAnaAsync();
        var r = await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Rolamento 6204", 1, 5.5m, "P002")); // stock 0
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True((await r.Content.ReadFromJsonAsync<MaterialDto>())!.SaidaDada);
    }

    [Fact]
    public async Task Material_sem_peca_nao_mexe_no_stock_e_lista_traz_ref()
    {
        var c = await ComoAnaAsync();
        await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Fita isoladora", 1, 1m));
        await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Parafuso M8", 2, 0.12m, "P001"));
        var lista = await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais");
        Assert.Equal(["", "P001"], lista!.Select(x => x.Ref).ToArray());
        Assert.Equal([false, true], lista!.Select(x => x.SaidaDada).ToArray());
        Assert.Equal(8, await StockAsync(c));
    }

    [Fact]
    public async Task Referencia_inexistente_recusa_com_400_e_nao_cria_material()
    {
        var c = await ComoAnaAsync();
        var r = await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("X", 1, 1m, "NAOEXISTE"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("pedido_invalido", await r.Content.ReadAsStringAsync());
        Assert.Empty((await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais"))!);
    }

    [Fact]
    public async Task Referencia_inexistente_ao_editar_recusa_e_nao_mexe_na_saida()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        var r = await c.PutAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}", new PedidoMaterial("Parafuso M8", 9, 0.12m, "NAOEXISTE"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(6, await StockAsync(c));
    }

    [Fact]
    public async Task Ficha_concluida_recusa_apagar_e_o_stock_nao_muda()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("Feito", new TimeOnly(8, 0), new TimeOnly(9, 0)))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}")).StatusCode);

        Assert.Equal(6, await StockAsync(c));
        var lista = await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais");
        Assert.True(Assert.Single(lista!).SaidaDada);
    }

    [Fact]
    public async Task Apagar_material_inexistente_ou_de_ficha_alheia_da_404()
    {
        var c = await ComoAnaAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/intervencoes/F1/materiais/NAOEXISTE")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/intervencoes/F2/materiais/X")).StatusCode);
    }

    [Fact]
    public async Task Supervisor_apaga_material_de_ficha_alheia_e_o_stock_volta()
    {
        var ana = await ComoAnaAsync();
        var chefe = await ComoAsync("603918");
        var m = await JuntarAsync(ana, "Parafuso M8", 4, 0.12m, "P001");

        Assert.Equal(HttpStatusCode.NoContent, (await chefe.DeleteAsync($"/intervencoes/F1/materiais/{m.Id}")).StatusCode);
        Assert.Equal(10, await StockAsync(ana));
    }

    [Fact]
    public async Task Repetir_saida_de_material_ja_com_saida_da_409_e_nao_duplica()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m, "P001");

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/intervencoes/F1/materiais/{m.Id}/saida", null)).StatusCode);
        Assert.Equal(6, await StockAsync(c));
    }

    [Fact]
    public async Task Repetir_saida_sem_peca_ligada_exige_a_referencia()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/intervencoes/F1/materiais/{m.Id}/saida", null)).StatusCode);
        var r = await c.PostAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}/saida", new PedidoSaida("P001"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var ok = await r.Content.ReadFromJsonAsync<MaterialDto>();
        Assert.True(ok!.SaidaDada);
        Assert.Equal("P001", ok.Ref);
        Assert.Equal(6, await StockAsync(c));
    }

    [Fact]
    public async Task Repetir_saida_segue_o_acesso_de_edicao_da_ficha()
    {
        var c = await ComoAnaAsync();
        var m = await JuntarAsync(c, "Parafuso M8", 4, 0.12m);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/intervencoes/F2/materiais/X/saida", new PedidoSaida("P001"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/intervencoes/F1/materiais/NAOEXISTE/saida", new PedidoSaida("P001"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("Feito", new TimeOnly(8, 0), new TimeOnly(9, 0)))).StatusCode);
        // Ficha já não editável: responde como o POST do material (409).
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/intervencoes/F1/materiais/{m.Id}/saida", new PedidoSaida("P001"))).StatusCode);
        Assert.Equal(10, await StockAsync(c));
    }

    // Repositório de peças que falha a dar saída (recusa ou base em baixo); o resto é o de memória.
    private sealed class SaidaQueFalha(RepositorioPecasMemoria memoria, bool indisponivel) : IRepositorioPecas
    {
        public Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct) => memoria.ListarAsync(texto, maximo, ct);
        public Task<PecaDto?> ObterPorCodigoAsync(string codigo, CancellationToken ct) => memoria.ObterPorCodigoAsync(codigo, ct);
        public Task<ResultadoMovimento> DarEntradaAsync(string refPeca, decimal qtt, string iniciais, CancellationToken ct) => memoria.DarEntradaAsync(refPeca, qtt, iniciais, ct);
        public Task<IReadOnlyList<EntradaPecaDto>> EntradasDeHojeAsync(CancellationToken ct) => memoria.EntradasDeHojeAsync(ct);
        public Task<ResultadoMovimento> AnularEntradaAsync(string id, CancellationToken ct) => memoria.AnularEntradaAsync(id, ct);
        public Task<ResultadoMovimento> DarSaidaAsync(string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct) =>
            indisponivel ? throw new RepositorioIndisponivelException() : Task.FromResult(ResultadoMovimento.Falha(RegrasPecas.ErroOcupado));
        public Task<ResultadoMovimento> AnularSaidaAsync(string materialId, CancellationToken ct) => memoria.AnularSaidaAsync(materialId, ct);
        public Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct) => memoria.EnriquecerMateriaisAsync(materiais, ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Falha_ao_dar_saida_nao_desfaz_o_material_e_diz_saida_por_dar(bool indisponivel)
    {
        var memoria = new RepositorioPecasMemoria();
        var f = _fabrica.WithWebHostBuilder(b => b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(new SaidaQueFalha(memoria, indisponivel)))));
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin("482913"))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/intervencoes/F1/iniciar", null)).StatusCode);

        var r = await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Parafuso M8", 4, 0.12m, "P001"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var m = await r.Content.ReadFromJsonAsync<MaterialDto>();
        Assert.False(m!.SaidaDada);
        Assert.Equal("P001", m.Ref);
        Assert.Single((await c.GetFromJsonAsync<List<MaterialDto>>("/intervencoes/F1/materiais"))!);
        Assert.Equal(10, (await memoria.ObterPorCodigoAsync("P001", default))!.Stock);
    }
}
