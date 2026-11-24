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

public class InventarioTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly RepositorioInventariosMemoria _inventarios;
    private readonly string _daAna, _doBruno, _concluidoDaAna, _lancadoDaAna, _antigoDaAna;

    public InventarioTests()
    {
        var pecas = new RepositorioPecasMemoria(
        [
            new PecaDto("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
            new PecaDto("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
        ]);
        _inventarios = new RepositorioInventariosMemoria(pecas, [(101, "Ana Silva"), (102, "Bruno Costa")]);
        _daAna = _inventarios.Criar(101, "Inventário da Ana");
        _doBruno = _inventarios.Criar(102, "Inventário do Bruno");
        _concluidoDaAna = _inventarios.Criar(101, "Concluído da Ana", concluido: true);
        _lancadoDaAna = _inventarios.Criar(101, "Lançado da Ana", concluido: true, lancado: true);
        _antigoDaAna = _inventarios.Criar(101, "Concluído há muito", concluido: true, data: DateOnly.FromDateTime(DateTime.Today.AddDays(-200)));

        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Pin:Pepper", Pepper)
            .UseSetting("Limites:LoginPorMinuto", "1000")
            .ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
                [
                    (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
                    (new UtilizadorApp("bruno", "Bruno Costa", Perfil.Operador, true, false, Tecno: 102), Hasher.Hash("159357")),
                    (new UtilizadorApp("qual", "Marta Qualidade", Perfil.Qualidade, true, false), Hasher.Hash("715329")),
                    (new UtilizadorApp("sup", "Rui Supervisor", Perfil.Supervisor, true, false), Hasher.Hash("246813"))
                ])));
                s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(pecas));
                s.Replace(ServiceDescriptor.Singleton<IRepositorioInventarios>(_inventarios));
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

    private static Task<HttpResponseMessage> Contar(HttpClient c, string id, string refPeca, string local, decimal qtt) =>
        c.PostAsJsonAsync($"/inventarios/{id}/linhas", new PedidoLinhaInventario(refPeca, local, qtt));

    private static async Task<InventarioDetalheDto> Detalhe(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<InventarioDetalheDto>())!;

    [Fact]
    public async Task Lista_so_os_do_tecnico_e_esconde_os_concluidos_antigos()
    {
        var c = await ComoAsync("482913");
        var lista = await c.GetFromJsonAsync<List<InventarioDto>>("/inventarios");
        var ids = lista!.Select(i => i.Id).ToHashSet();
        Assert.Contains(_daAna, ids);
        Assert.Contains(_concluidoDaAna, ids);
        Assert.Contains(_lancadoDaAna, ids);
        Assert.DoesNotContain(_doBruno, ids);
        Assert.DoesNotContain(_antigoDaAna, ids);
        Assert.All(lista!, i => Assert.Equal(101, i.Tecno));
        Assert.True(lista!.Single(i => i.Id == _daAna).PodeEditar);
        Assert.False(lista!.Single(i => i.Id == _concluidoDaAna).PodeEditar);
    }

    [Fact]
    public async Task Supervisor_ve_todos_mas_nao_edita_os_dos_outros()
    {
        var c = await ComoAsync("246813");
        var lista = await c.GetFromJsonAsync<List<InventarioDto>>("/inventarios");
        Assert.Contains(lista!, i => i.Id == _daAna);
        Assert.Contains(lista!, i => i.Id == _doBruno);
        Assert.All(lista!, i => Assert.False(i.PodeEditar));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/inventarios/{_daAna}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Contar(c, _daAna, "P001", "A-03-2-04", 1)).StatusCode);
    }

    [Fact]
    public async Task Qualidade_nao_acede()
    {
        var c = await ComoAsync("715329");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/inventarios")).StatusCode);
    }

    [Fact]
    public async Task Sem_sessao_recusa()
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/inventarios")).StatusCode);
    }

    [Fact]
    public async Task Mesma_peca_e_lugar_soma_na_linha_existente_mesmo_com_etiqueta_diferente()
    {
        var c = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.OK, (await Contar(c, _daAna, "P001", "LOC-A-03-2-04", 3)).StatusCode);
        var r = await Contar(c, _daAna, "560001", "a-03-2-4", 2.5m);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var d = await Detalhe(r);
        var linha = Assert.Single(d.Linhas);
        Assert.Equal("P001", linha.Ref);
        Assert.Equal("A-03-2-04", linha.Lugar);
        Assert.Equal(5.5m, linha.Qtt);
        Assert.Equal(1, d.Inventario.Linhas);

        var outra = await Detalhe(await Contar(c, _daAna, "P001", "A-03-2-05", 1));
        Assert.Equal(2, outra.Linhas.Count);
    }

    [Theory]
    [InlineData("A-03-2")]
    [InlineData("LOC-A-03-2")]
    [InlineData("A-03")]
    [InlineData("")]
    [InlineData("qualquer coisa")]
    public async Task Recusa_lugar_sem_posicao(string local)
    {
        var c = await ComoAsync("482913");
        var r = await Contar(c, _daAna, "P001", local, 1);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<InventarioDetalheDto>($"/inventarios/{_daAna}"))!.Linhas);
    }

    [Fact]
    public async Task Recusa_quantidade_invalida_e_peca_inexistente()
    {
        var c = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.BadRequest, (await Contar(c, _daAna, "P001", "A-03-2-04", -1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Contar(c, _daAna, "P001", "A-03-2-04", 1.2345m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Contar(c, _daAna, "NAOEXISTE", "A-03-2-04", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Contar(c, _daAna, "P001", "A-03-2-04", 0)).StatusCode);
    }

    [Fact]
    public async Task Recusa_o_dono_errado_em_tudo()
    {
        var c = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/inventarios/{_doBruno}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Contar(c, _doBruno, "P001", "A-03-2-04", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/inventarios/{_doBruno}/concluir", null)).StatusCode);

        var bruno = await ComoAsync("159357");
        var linha = (await Detalhe(await Contar(bruno, _doBruno, "P001", "A-03-2-04", 4))).Linhas.Single();
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync($"/inventarios/{_doBruno}/linhas/{linha.Id}", new PedidoQuantidadeInventario(9))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.DeleteAsync($"/inventarios/{_doBruno}/linhas/{linha.Id}")).StatusCode);
        Assert.Equal(4, (await bruno.GetFromJsonAsync<InventarioDetalheDto>($"/inventarios/{_doBruno}"))!.Linhas.Single().Qtt);
    }

    [Fact]
    public async Task Edita_a_quantidade_e_apaga_a_linha()
    {
        var c = await ComoAsync("482913");
        var linha = (await Detalhe(await Contar(c, _daAna, "P001", "A-03-2-04", 4))).Linhas.Single();
        var editado = await Detalhe(await c.PutAsJsonAsync($"/inventarios/{_daAna}/linhas/{linha.Id}", new PedidoQuantidadeInventario(7)));
        Assert.Equal(7, editado.Linhas.Single().Qtt);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/inventarios/{_daAna}/linhas/{linha.Id}", new PedidoQuantidadeInventario(-2))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync($"/inventarios/{_daAna}/linhas/naoexiste", new PedidoQuantidadeInventario(1))).StatusCode);

        var apagado = await Detalhe(await c.DeleteAsync($"/inventarios/{_daAna}/linhas/{linha.Id}"));
        Assert.Empty(apagado.Linhas);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/inventarios/{_daAna}/linhas/{linha.Id}")).StatusCode);
    }

    [Fact]
    public async Task Concluir_fecha_o_inventario_e_recusa_alteracoes()
    {
        var c = await ComoAsync("482913");
        var linha = (await Detalhe(await Contar(c, _daAna, "P001", "A-03-2-04", 4))).Linhas.Single();

        var concluido = await Detalhe(await c.PostAsync($"/inventarios/{_daAna}/concluir", null));
        Assert.True(concluido.Inventario.Concluido);
        Assert.False(concluido.Inventario.Lancado);
        Assert.False(concluido.Inventario.PodeEditar);

        Assert.Equal(HttpStatusCode.Conflict, (await Contar(c, _daAna, "P001", "A-03-2-05", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/inventarios/{_daAna}/linhas/{linha.Id}", new PedidoQuantidadeInventario(9))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/inventarios/{_daAna}/linhas/{linha.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/inventarios/{_daAna}/concluir", null)).StatusCode);
        var final = await c.GetFromJsonAsync<InventarioDetalheDto>($"/inventarios/{_daAna}");
        Assert.Equal(4, final!.Linhas.Single().Qtt);
    }

    [Fact]
    public async Task Recusa_editar_inventario_ja_lancado_ou_concluido_antes()
    {
        var c = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.Conflict, (await Contar(c, _concluidoDaAna, "P001", "A-03-2-04", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Contar(c, _lancadoDaAna, "P001", "A-03-2-04", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/inventarios/{_lancadoDaAna}/concluir", null)).StatusCode);
    }

    [Fact]
    public async Task Nao_conclui_sem_linhas()
    {
        var c = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/inventarios/{_daAna}/concluir", null)).StatusCode);
        Assert.False((await c.GetFromJsonAsync<InventarioDetalheDto>($"/inventarios/{_daAna}"))!.Inventario.Concluido);
    }

    [Fact]
    public async Task Inventario_inexistente_da_404()
    {
        var c = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/inventarios/naoexiste")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Contar(c, "naoexiste", "P001", "A-03-2-04", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/inventarios/naoexiste/concluir", null)).StatusCode);
    }

    [Fact]
    public async Task A_resposta_nao_traz_precos()
    {
        var c = await ComoAsync("482913");
        var corpo = await (await Contar(c, _daAna, "P001", "A-03-2-04", 1)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("preco", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("epv", corpo, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("LOC-A-03-2-04", "A-03-2-04")]
    [InlineData("A-03-2-04", "A-03-2-04")]
    [InlineData("a'03'2'4", "A-03-2-04")]
    [InlineData("A-03-2", null)]
    [InlineData("LOC-A-03", null)]
    [InlineData(null, null)]
    public void Lugar_normalizado(string? texto, string? esperado) => Assert.Equal(esperado, RegrasInventario.LugarNormalizado(texto));

    [Theory]
    [InlineData(0, true)]
    [InlineData(12.345, true)]
    [InlineData(-0.001, false)]
    [InlineData(1000000, false)]
    [InlineData(1.0001, false)]
    public void Quantidade_valida(double q, bool esperado) => Assert.Equal(esperado, RegrasInventario.QuantidadeValida((decimal)q));
}

public class InventarioUiTests
{
    [Theory]
    [InlineData("LOC-A-03-2-04", true)]
    [InlineData("A-03-2-04", true)]
    [InlineData("LOC-A-03-2", false)]
    [InlineData("A-03", false)]
    [InlineData("", false)]
    public void Lugar_completo(string local, bool esperado) => Assert.Equal(esperado, GestorManutencao.UI.Modelos.LogicaInventario.LugarCompleto(local));

    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("0,0", true, 0)]
    [InlineData("2,5", true, 2.5)]
    [InlineData("-1", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData(".", false, 0)]
    public void Quantidade_aceita_zero(string texto, bool ok, double esperado)
    {
        Assert.Equal(ok, GestorManutencao.UI.Modelos.LogicaInventario.LerQuantidade(texto, out var q));
        if (ok) Assert.Equal((decimal)esperado, q);
    }

    [Fact]
    public async Task Servico_dev_soma_recusa_lugar_incompleto_e_fecha_ao_concluir()
    {
        var s = new GestorManutencao.UI.Servicos.InventarioDevService();
        await s.ContarAsync("inv-dev-1", "P001", "LOC-A-03-2-04", 2);
        var d = await s.ContarAsync("inv-dev-1", "P001", "A-03-2-04", 3);
        Assert.Equal(5, Assert.Single(d.Linhas).Qtt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.ContarAsync("inv-dev-1", "P001", "A-03-2", 1));
        Assert.True((await s.ConcluirAsync("inv-dev-1")).Inventario.Concluido);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.ContarAsync("inv-dev-1", "P001", "A-03-2-05", 1));
    }
}
