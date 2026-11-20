using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.Tests;

public class PecasUiTests
{
    [Theory]
    [InlineData("5600012345", true)]
    [InlineData("56001", false)]
    [InlineData("Parafuso", false)]
    [InlineData("56001a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PareceCodigo_so_digitos_com_pelo_menos_6(string? texto, bool esperado)
        => Assert.Equal(esperado, LogicaEcraPecas.PareceCodigoDeBarras(texto));

    [Fact]
    public void Avisos_de_stock_usam_a_regra_unica()
    {
        Assert.Equal("Stock insuficiente: há 2 e pede 3.", LogicaEcraPecas.AvisoStock(3, 2));
        Assert.Equal("Sem stock desta peça.", LogicaEcraPecas.AvisoStock(1, 0));
        Assert.Null(LogicaEcraPecas.AvisoStock(2, 2));
        Assert.Null(LogicaEcraPecas.AvisoStock(0, 0));
    }

    [Fact]
    public async Task Dev_pesquisa_por_texto_e_codigo_inexistente()
    {
        var s = new PecasDevService();

        var r = await s.PesquisarAsync("rol");

        Assert.Equal(["P002"], r.Pecas.Select(p => p.Ref));
        Assert.Null(await s.ObterPorCodigoAsync("999999"));
    }

    private sealed class Fixo(System.Net.HttpStatusCode codigo, string corpo = "") : HttpMessageHandler
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

    [Fact]
    public async Task Api_codigo_inexistente_404_da_null()
        => Assert.Null(await Api(new Fixo(System.Net.HttpStatusCode.NotFound)).ObterPorCodigoAsync("999999"));

    [Fact]
    public async Task Api_400_le_a_mensagem_do_corpo_real()
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.BadRequest,
            "{\"erro\":\"pedido_invalido\",\"mensagem\":\"Peça inexistente.\"}")).PesquisarAsync("x"));

        Assert.Equal("Peça inexistente.", e.Message);
    }

    [Fact]
    public async Task Api_409_le_o_campo_erro()
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.Conflict,
            "{\"erro\":\"Outro movimento em curso, tente outra vez.\"}")).PesquisarAsync("x"));

        Assert.Equal("Outro movimento em curso, tente outra vez.", e.Message);
    }

    [Fact]
    public async Task Api_401_sai_como_sessao_terminada_403_e_503_com_mensagem()
    {
        var u = await Assert.ThrowsAsync<HttpRequestException>(() => Api(new Fixo(System.Net.HttpStatusCode.Unauthorized)).PesquisarAsync("x"));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, u.StatusCode);
        Assert.Equal("A sessão terminou. Entre outra vez.", MensagensGestao.Para(u, "x"));

        var f = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.Forbidden)).PesquisarAsync("x"));
        Assert.Equal("Não tem permissão para as peças.", f.Message);

        var p = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.ServiceUnavailable)).PesquisarAsync("x"));
        Assert.Equal("O PHC não está a responder. Tente outra vez daqui a pouco.", p.Message);
    }

    private sealed class Falha(Exception e) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct) => Task.FromException<HttpResponseMessage>(e);
    }

    private static PecasApiService Api(HttpMessageHandler h) =>
        new(new HttpClient(h) { BaseAddress = new Uri("https://exemplo.invalid/") }, new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria()));

    [Fact]
    public async Task Api_sem_resposta_e_timeout_dao_a_mensagem_de_sem_rede()
    {
        var rede = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Falha(new HttpRequestException("sem rede"))).PesquisarAsync("x"));
        var tempo = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Falha(new TaskCanceledException())).PesquisarAsync("x"));

        Assert.Equal(MensagensIntervencao.SemRede, rede.Message);
        Assert.Equal(MensagensIntervencao.SemRede, tempo.Message);
    }

    [Fact]
    public async Task Api_400_com_corpo_nao_json_usa_a_mensagem_geral()
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.BadRequest, "<html>")).PesquisarAsync("x"));

        Assert.Equal("Não foi possível gravar o movimento.", e.Message);
    }

    [Fact]
    public async Task Api_envia_o_token_da_sessao_em_Bearer()
    {
        var sessao = new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria());
        Assert.True((await sessao.EntrarAsync("7953")).Sucesso);
        var h = new Fixo(System.Net.HttpStatusCode.OK, "{\"campoExiste\":true,\"pecas\":[],\"haMais\":false}");

        await new PecasApiService(new HttpClient(h) { BaseAddress = new Uri("https://exemplo.invalid/") }, sessao).PesquisarAsync("x");

        Assert.Equal("Bearer", h.Ultimo!.Headers.Authorization!.Scheme);
        Assert.Equal(sessao.Utilizador!.Token, h.Ultimo.Headers.Authorization.Parameter);
    }

    [Theory]
    [InlineData("5600123456", "Código não encontrado: 5600123456")]
    [InlineData(" RL-6204 ", "Código não encontrado: RL-6204")]
    [InlineData("rolamento 6204", "Nenhuma peça encontrada para rolamento 6204")]
    public void Sem_resultado_distingue_codigo_de_pesquisa_livre(string texto, string esperado)
        => Assert.Equal(esperado, LogicaEcraPecas.MensagemSemResultado(texto));

}
