using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestorManutencao.Api;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

public class RearrumarRegrasTests
{
    [Theory]
    [InlineData(3, 3, null)]
    [InlineData(3, 1.5, null)]
    [InlineData(0, 5, null)] // 0 = quantidade não indicada: qualquer pedido move a linha toda
    public void Quantidade_dentro_do_que_la_esta_e_aceite(double existente, double qtt, string? esperado)
        => Assert.Equal(esperado, RegrasLocalizacao.ErroQuantidadeMover((decimal)existente, (decimal)qtt));

    [Fact]
    public void Quantidade_acima_do_que_la_esta_diz_quanto_ha()
        => Assert.Equal("Só há 3 nesse lugar.", RegrasLocalizacao.ErroQuantidadeMover(3, 3.5m));

    [Fact]
    public void Quantidade_movida_sem_numero_inventado_quando_a_origem_e_zero()
    {
        Assert.Equal(2m, RegrasLocalizacao.QuantidadeMovida(5, 2));
        Assert.Equal(0m, RegrasLocalizacao.QuantidadeMovida(0, 2));
    }

    [Fact]
    public void Mesmo_lugar_ignora_a_forma_como_foi_escrito()
        => Assert.True(RegrasLocalizacao.MesmoLugar(RegrasLocalizacao.LerLugar("A-2-1-1")!, RegrasLocalizacao.LerLugar("LOC-a-02-1-01")!));

    [Fact]
    public void Lugares_diferentes_nao_sao_o_mesmo()
        => Assert.False(RegrasLocalizacao.MesmoLugar(RegrasLocalizacao.LerLugar("A-02-1-01")!, RegrasLocalizacao.LerLugar("A-02-1-02")!));

    [Fact]
    public void Lugar_sem_posicao_nao_serve_para_mover()
        => Assert.Null(RegrasLocalizacao.LerLugar("A-02-1"));

    [Theory]
    [InlineData("LOC-A-1-1-1", "A-01-1-01")]
    [InlineData("A-01-1-01", "A-01-1-01")]
    [InlineData("loc'b'12'3'4", "B-12-3-04")]
    [InlineData("A-01-1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Texto_normal_do_lugar_da_UI(string? local, string? esperado)
        => Assert.Equal(esperado, LogicaFichaPeca.TextoNormalDoLugar(local));
}

public class RearrumarApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly RepositorioLocalizacoesMemoria _repo;

    // P001: stock 10, 3 arrumadas em A-02-1-01 (amostra do repositório); P002 stock 0.
    public RearrumarApiTests() : this(null) { }

    private RearrumarApiTests(Func<RepositorioLocalizacoesMemoria, IRepositorioLocalizacoes>? decorar)
    {
        var pecas = new RepositorioPecasMemoria(
        [
            new PecaDto("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
            new PecaDto("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
        ]);
        _repo = new RepositorioLocalizacoesMemoria(pecas);
        IRepositorioLocalizacoes usado = decorar is null ? _repo : decorar(_repo);
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Pin:Pepper", Pepper)
            .UseSetting("Limites:LoginPorMinuto", "1000")
            .ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
                [
                    (new UtilizadorApp("ana", "Ana Silva", GestorManutencao.Api.Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
                    (new UtilizadorApp("qual", "Marta Qualidade", GestorManutencao.Api.Perfil.Qualidade, true, false), Hasher.Hash("715329")),
                    (new UtilizadorApp("sup", "Rui Supervisor", GestorManutencao.Api.Perfil.Supervisor, true, false), Hasher.Hash("246813"))
                ])));
                s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(pecas));
                s.Replace(ServiceDescriptor.Singleton(usado));
            }));
    }

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAsync(string? pin)
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (pin is null) return c;
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    private static Task<HttpResponseMessage> Mover(HttpClient c, string? origem, string? destino, decimal? qtt, string peca = "P001") =>
        c.PostAsJsonAsync($"/pecas/{peca}/mover", new PedidoMover(origem, destino, qtt));

    private static async Task<LocalizacoesPecaDto> Lugares(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<LocalizacoesPecaDto>())!;
    }

    [Fact]
    public async Task Sem_token_401_e_Qualidade_403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Mover(await ComoAsync(null), "A-02-1-01", "B-01-1-01", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Mover(await ComoAsync("715329"), "A-02-1-01", "B-01-1-01", 1)).StatusCode);
    }

    [Fact]
    public async Task Tecnico_e_supervisor_podem_mover()
    {
        Assert.Equal(HttpStatusCode.OK, (await Mover(await ComoAsync("482913"), "A-02-1-01", "B-01-1-01", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Mover(await ComoAsync("246813"), "B-01-1-01", "C-01-1-01", 1)).StatusCode);
    }

    [Fact]
    public async Task Mover_parte_deixa_o_resto_na_origem_e_cria_o_destino()
    {
        var d = await Lugares(await Mover(await ComoAsync("482913"), "LOC-A-02-1-01", "B-1-2-03", 1.5m));
        Assert.Equal([("A-02-1-01", 1.5m), ("B-01-2-03", 1.5m)], d.Lugares.OrderBy(l => l.Texto).Select(l => (l.Texto, l.Qtt)));
        Assert.Equal(3, d.Localizado);
        Assert.Equal(7, d.PorArrumar); // o stock do PHC não muda
    }

    [Fact]
    public async Task Mover_tudo_apaga_a_linha_da_origem()
    {
        var d = await Lugares(await Mover(await ComoAsync("482913"), "A-02-1-01", "B-01-1-01", 3));
        Assert.Equal("B-01-1-01", Assert.Single(d.Lugares).Texto);
        Assert.Equal(3, d.Lugares[0].Qtt);
        Assert.Empty(await _repo.ListarPecasDoLugarAsync("A", "A-02", 1, 1, Ct));
    }

    [Fact]
    public async Task Destino_que_ja_tem_stock_soma()
    {
        Assert.True((await _repo.ReporAsync("P001", "B", "B-01", 1, 1, 2, "XX", Ct)).Ok);
        var d = await Lugares(await Mover(await ComoAsync("482913"), "A-02-1-01", "B-01-1-01", 3));
        Assert.Equal(5, Assert.Single(d.Lugares).Qtt);
    }

    [Fact]
    public async Task Origem_com_quantidade_nao_indicada_move_a_linha_sem_inventar_numeros()
    {
        Assert.True((await _repo.ReporAsync("P002", "A", "A-05", 1, 1, 0, "XX", Ct)).Ok); // qtt 0 = não indicada
        var d = await Lugares(await Mover(await ComoAsync("482913"), "A-05-1-01", "B-01-1-01", 1, "P002"));
        var lugar = Assert.Single(d.Lugares);
        Assert.Equal("B-01-1-01", lugar.Texto);
        Assert.Equal(0, lugar.Qtt);
    }

    [Theory]
    [InlineData("A-02-1-01", "A-02-1-01")]
    [InlineData("A-02-1-01", "LOC-A-2-1-1")]
    public async Task Origem_igual_ao_destino_400(string origem, string destino)
    {
        var r = await Mover(await ComoAsync("482913"), origem, destino, 1);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("mesmo lugar", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Quantidade_acima_da_origem_400_e_nada_muda()
    {
        var c = await ComoAsync("482913");
        var r = await Mover(c, "A-02-1-01", "B-01-1-01", 3.5m);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Só há 3", await r.Content.ReadAsStringAsync());
        var d = (await c.GetFromJsonAsync<LocalizacoesPecaDto>("/pecas/P001/localizacoes"))!;
        Assert.Equal("A-02-1-01", Assert.Single(d.Lugares).Texto);
        Assert.Equal(3, d.Lugares[0].Qtt);
    }

    [Fact]
    public async Task Origem_sem_linha_400()
    {
        var r = await Mover(await ComoAsync("482913"), "C-09-9-09", "B-01-1-01", 1);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("não tem stock nesse lugar", await r.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null, "B-01-1-01", 1.0)]
    [InlineData("A-02-1-01", null, 1.0)]
    [InlineData("lixo", "B-01-1-01", 1.0)]
    [InlineData("A-02-1-01", "B-01-1", 1.0)]  // destino sem posição
    [InlineData("A-02-1", "B-01-1-01", 1.0)]  // origem sem posição
    [InlineData("A-02-1-01", "B-01-1-01", null)]
    [InlineData("A-02-1-01", "B-01-1-01", 0.0)]
    [InlineData("A-02-1-01", "B-01-1-01", -1.0)]
    [InlineData("A-02-1-01", "B-01-1-01", 1.2345)]
    public async Task Pedido_invalido_400(string? origem, string? destino, double? qtt)
        => Assert.Equal(HttpStatusCode.BadRequest, (await Mover(await ComoAsync("482913"), origem, destino, (decimal?)qtt)).StatusCode);

    [Fact]
    public async Task Peca_inexistente_404()
        => Assert.Equal(HttpStatusCode.NotFound, (await Mover(await ComoAsync("482913"), "A-02-1-01", "B-01-1-01", 1, "XXX")).StatusCode);

    [Fact]
    public async Task Buffer_nunca_e_destino_nem_origem()
    {
        _repo.DefinirBuffer("Z-09-9-09");
        Assert.True((await _repo.ReporAsync("P001", "Z", "Z-09", 9, 9, 7, "XX", Ct)).Ok); // 7 à espera no buffer
        var c = await ComoAsync("482913");

        var paraBuffer = await Mover(c, "A-02-1-01", "Z-09-9-09", 1);
        Assert.Equal(HttpStatusCode.BadRequest, paraBuffer.StatusCode);
        Assert.Contains("buffer", await paraBuffer.Content.ReadAsStringAsync());

        var doBuffer = await Mover(c, "Z-09-9-09", "B-01-1-01", 1);
        Assert.Equal(HttpStatusCode.BadRequest, doBuffer.StatusCode);
        Assert.Contains("Arrumação", await doBuffer.Content.ReadAsStringAsync());

        Assert.Equal(7m, Assert.Single(await _repo.ListarPecasDoLugarAsync("Z", "Z-09", 9, 9, Ct)).Qtt); // buffer intacto
    }

    // Repositório que devolve o que lhe mandarem em MoverAsync.
    private sealed class RepoMover(IRepositorioLocalizacoes interno, ResultadoMovimento? resultado, string tabela = "") : IRepositorioLocalizacoes
    {
        public Task<LocalizacoesPecaDto> ListarAsync(string r, CancellationToken ct) => interno.ListarAsync(r, ct);
        public Task<ResultadoMovimento> ArrumarAsync(string r, string c, int e, int n, int p, decimal q, string i, CancellationToken ct) => interno.ArrumarAsync(r, c, e, n, p, q, i, ct);
        public Task<ResultadoMovimento> DescontarAsync(string r, string c, string e, int n, int p, decimal q, string i, CancellationToken ct) => interno.DescontarAsync(r, c, e, n, p, q, i, ct);
        public Task<ResultadoMovimento> ReporAsync(string r, string c, string e, int n, int p, decimal q, string i, CancellationToken ct) => interno.ReporAsync(r, c, e, n, p, q, i, ct);
        public Task<string> VerificarTabelaAsync(CancellationToken ct) => Task.FromResult(tabela);
        public Task<IReadOnlyList<(string Ref, decimal Qtt)>> ListarPecasDoLugarAsync(string c, string e, int n, int p, CancellationToken ct) => interno.ListarPecasDoLugarAsync(c, e, n, p, ct);
        public Task<ResultadoMovimento> MoverAsync(string r, LugarDto o, LugarDto d, decimal q, string i, CancellationToken ct) => Task.FromResult(resultado!);
    }

    [Fact]
    public async Task Lugar_ocupado_409()
    {
        using var f = new RearrumarApiTests(r => new RepoMover(r, ResultadoMovimento.Falha(RegrasPecas.ErroOcupado)));
        Assert.Equal(HttpStatusCode.Conflict, (await Mover(await f.ComoAsync("482913"), "A-02-1-01", "B-01-1-01", 1)).StatusCode);
    }

    [Fact]
    public async Task Tabela_em_falta_409_com_o_texto()
    {
        using var f = new RearrumarApiTests(r => new RepoMover(r, null, "Falta criar a tabela u_pecaloc no PHC."));
        var r = await Mover(await f.ComoAsync("482913"), "A-02-1-01", "B-01-1-01", 1);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("Falta criar a tabela u_pecaloc no PHC.", await r.Content.ReadAsStringAsync());
    }
}

public class RearrumarUiTests
{
    private sealed class Fixo(HttpStatusCode codigo, string corpo) : HttpMessageHandler
    {
        public HttpRequestMessage? Ultimo { get; private set; }
        public string? Corpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            Ultimo = pedido;
            Corpo = pedido.Content is null ? null : await pedido.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(codigo) { Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private static PecasApiService Api(Fixo h) =>
        new(new HttpClient(h) { BaseAddress = new Uri("https://exemplo.invalid/") }, new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria()));

    private const string Corpo = "{\"ref\":\"P001\",\"stock\":10,\"localizado\":4,\"porArrumar\":6,\"lugares\":[{\"corredor\":\"B\",\"estante\":\"B-01\",\"nivel\":1,\"posicao\":1,\"qtt\":4,\"texto\":\"B-01-1-01\"}]}";

    [Fact]
    public async Task Api_mover_envia_origem_destino_e_quantidade_e_le_os_lugares()
    {
        var h = new Fixo(HttpStatusCode.OK, Corpo);

        var r = await Api(h).MoverAsync("P001", "A-01-1-01", "LOC-B-1-1-1", 2.5m);

        Assert.Equal(HttpMethod.Post, h.Ultimo!.Method);
        Assert.Equal("/pecas/P001/mover", h.Ultimo.RequestUri!.AbsolutePath);
        using var doc = System.Text.Json.JsonDocument.Parse(h.Corpo!);
        Assert.Equal("A-01-1-01", doc.RootElement.GetProperty("origem").GetString());
        Assert.Equal("LOC-B-1-1-1", doc.RootElement.GetProperty("destino").GetString());
        Assert.Equal(2.5m, doc.RootElement.GetProperty("qtt").GetDecimal());
        Assert.Equal("B-01-1-01", Assert.Single(r.Lugares).Texto);
    }

    [Fact]
    public async Task Api_mover_400_e_409_chegam_com_a_mensagem()
    {
        var a = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(HttpStatusCode.BadRequest,
            "{\"erro\":\"pedido_invalido\",\"mensagem\":\"Só há 3 nesse lugar.\"}")).MoverAsync("P001", "A-01-1-01", "B-01-1-01", 9));
        Assert.Equal("Só há 3 nesse lugar.", a.Message);
        var b = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(HttpStatusCode.Conflict,
            "{\"erro\":\"Outro movimento em curso, tente outra vez.\"}")).MoverAsync("P001", "A-01-1-01", "B-01-1-01", 1));
        Assert.Equal("Outro movimento em curso, tente outra vez.", b.Message);
    }

    [Fact]
    public async Task Dev_mover_parte_e_depois_tudo_apaga_a_origem()
    {
        var s = new PecasDevService();

        var r = await s.MoverAsync("P001", "A-01-1-01", "LOC-B-1-2-3", 1.5m);
        Assert.Equal([("A-01-1-01", 2.5m), ("B-01-2-03", 1.5m)], r.Lugares.OrderBy(l => l.Texto).Select(l => (l.Texto, l.Qtt)));
        Assert.Equal(6m, r.PorArrumar); // stock 10 - 4 localizadas: não muda

        r = await s.MoverAsync("P001", "A-01-1-01", "B-01-2-03", 2.5m);
        Assert.Equal("B-01-2-03", Assert.Single(r.Lugares).Texto);
        Assert.Equal(4m, r.Lugares[0].Qtt);
    }

    [Fact]
    public async Task Dev_mover_recusa_o_que_a_api_recusa()
    {
        var s = new PecasDevService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.MoverAsync("P001", "A-01-1-01", "LOC-A-1-1-1", 1)); // mesmo lugar
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.MoverAsync("P001", "A-01-1-01", "B-01-1-01", 5));    // mais do que há
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.MoverAsync("P001", "C-01-1-01", "B-01-1-01", 1));    // origem sem linha
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.MoverAsync("P001", "A-01-1-01", "B-01-1", 1));       // destino incompleto
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.MoverAsync("P001", "A-01-1-01", "B-01-1-01", 0));    // quantidade
    }
}
