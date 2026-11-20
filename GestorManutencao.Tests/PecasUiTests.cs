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
    public async Task Dev_entrada_sobe_o_stock_aparece_nas_de_hoje_e_anular_desfaz()
    {
        var s = new PecasDevService();
        var antes = (await s.ObterPorCodigoAsync("P001"))!.Stock;

        var e = await s.DarEntradaAsync("P001", 3);

        Assert.Equal(antes + 3, (await s.ObterPorCodigoAsync("560001"))!.Stock);
        Assert.Contains(await s.EntradasDeHojeAsync(), x => x.Id == e.Id);

        await s.AnularEntradaAsync(e.Id);

        Assert.Equal(antes, (await s.ObterPorCodigoAsync("P001"))!.Stock);
        Assert.Empty(await s.EntradasDeHojeAsync());
    }

    [Fact]
    public async Task Dev_pesquisa_por_texto_e_codigo_inexistente()
    {
        var s = new PecasDevService();

        var r = await s.PesquisarAsync("rol");

        Assert.Equal(["P002"], r.Pecas.Select(p => p.Ref));
        Assert.Null(await s.ObterPorCodigoAsync("999999"));
    }

    [Fact]
    public async Task Dev_recusa_referencia_inexistente_e_anular_duas_vezes_nao_falha()
    {
        var s = new PecasDevService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.DarEntradaAsync("XXX", 1));

        var e = await s.DarEntradaAsync("P002", 2);
        await s.AnularEntradaAsync(e.Id);
        await s.AnularEntradaAsync(e.Id);
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
    public async Task Api_envia_ref_e_qtt_como_o_contrato_e_le_a_entrada()
    {
        var h = new Fixo(System.Net.HttpStatusCode.OK, "{\"id\":\"1\",\"ref\":\"P001\",\"design\":\"Parafuso\",\"qtt\":3,\"quem\":\"AB\",\"hora\":\"10:00:00\"}");

        var e = await Api(h).DarEntradaAsync("P001", 3);

        Assert.Equal("pecas/entradas", h.Ultimo!.RequestUri!.AbsolutePath.TrimStart('/'));
        Assert.Contains("\"ref\":\"P001\"", h.Corpo);
        Assert.Contains("\"qtt\":3", h.Corpo);
        Assert.Equal(("1", 3m), (e.Id, e.Qtt));
    }

    [Fact]
    public async Task Api_codigo_inexistente_404_da_null()
        => Assert.Null(await Api(new Fixo(System.Net.HttpStatusCode.NotFound)).ObterPorCodigoAsync("999999"));

    [Fact]
    public async Task Api_400_le_a_mensagem_do_corpo_real()
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.BadRequest,
            "{\"erro\":\"pedido_invalido\",\"mensagem\":\"Peça inexistente.\"}")).DarEntradaAsync("P001", 1));

        Assert.Equal("Peça inexistente.", e.Message);
    }

    [Fact]
    public async Task Api_409_le_o_campo_erro()
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.Conflict,
            "{\"erro\":\"Outro movimento em curso, tente outra vez.\"}")).DarEntradaAsync("P001", 1));

        Assert.Equal("Outro movimento em curso, tente outra vez.", e.Message);
    }

    [Fact]
    public async Task Api_401_sai_como_sessao_terminada_403_e_503_com_mensagem()
    {
        var u = await Assert.ThrowsAsync<HttpRequestException>(() => Api(new Fixo(System.Net.HttpStatusCode.Unauthorized)).EntradasDeHojeAsync());
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, u.StatusCode);
        Assert.Equal("A sessão terminou. Entre outra vez.", MensagensGestao.Para(u, "x"));

        var f = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.Forbidden)).EntradasDeHojeAsync());
        Assert.Equal("Não tem permissão para as peças.", f.Message);

        var p = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.ServiceUnavailable)).EntradasDeHojeAsync());
        Assert.Equal("O PHC não está a responder. Tente outra vez daqui a pouco.", p.Message);
    }

    private sealed class Falha(Exception e) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct) => Task.FromException<HttpResponseMessage>(e);
    }

    private static PecasApiService Api(HttpMessageHandler h) =>
        new(new HttpClient(h) { BaseAddress = new Uri("https://exemplo.invalid/") }, new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria()));

    [Fact]
    public async Task Api_sem_resposta_e_timeout_dao_falha_de_rede_e_contam_como_incertos()
    {
        var rede = await Assert.ThrowsAsync<FalhaDeRedePecasException>(() => Api(new Falha(new HttpRequestException("sem rede"))).DarEntradaAsync("P001", 1));
        var tempo = await Assert.ThrowsAsync<FalhaDeRedePecasException>(() => Api(new Falha(new TaskCanceledException())).DarEntradaAsync("P001", 1));

        Assert.True(LogicaEcraPecas.ResultadoIncerto(rede));
        Assert.True(LogicaEcraPecas.ResultadoIncerto(tempo));
    }

    [Fact]
    public async Task Api_503_no_post_da_entrada_e_incerto_mas_nas_outras_chamadas_nao()
    {
        var post = await Assert.ThrowsAsync<EntradaIncertaPecasException>(() => Api(new Fixo(System.Net.HttpStatusCode.ServiceUnavailable)).DarEntradaAsync("P001", 1));
        var lista = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.ServiceUnavailable)).EntradasDeHojeAsync());
        var pesq = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.ServiceUnavailable)).PesquisarAsync("x"));

        Assert.True(LogicaEcraPecas.ResultadoIncerto(post));
        Assert.False(LogicaEcraPecas.ResultadoIncerto(lista));
        Assert.False(LogicaEcraPecas.ResultadoIncerto(pesq));
    }

    [Fact]
    public async Task Api_200_com_corpo_ilegivel_no_post_e_incerto()
    {
        var e = await Assert.ThrowsAsync<EntradaIncertaPecasException>(() => Api(new Fixo(System.Net.HttpStatusCode.OK, "<html>proxy</html>")).DarEntradaAsync("P001", 1));

        Assert.True(LogicaEcraPecas.ResultadoIncerto(e));
    }

    [Fact]
    public async Task Api_400_com_corpo_nao_json_usa_a_mensagem_geral()
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Api(new Fixo(System.Net.HttpStatusCode.BadRequest, "<html>")).DarEntradaAsync("P001", 1));

        Assert.Equal("Não foi possível gravar o movimento.", e.Message);
    }

    [Fact]
    public void Resultado_incerto_exclui_validacao_e_sessao_terminada_e_o_aviso_depende_da_recarga()
    {
        Assert.False(LogicaEcraPecas.ResultadoIncerto(new InvalidOperationException("Peça inexistente.")));
        Assert.False(LogicaEcraPecas.ResultadoIncerto(new HttpRequestException("x", null, System.Net.HttpStatusCode.Unauthorized)));
        Assert.True(LogicaEcraPecas.ResultadoIncerto(new HttpRequestException("x", null, System.Net.HttpStatusCode.InternalServerError)));
        Assert.Equal("Confirme na lista de hoje antes de repetir a entrada.", LogicaEcraPecas.AvisoResultadoIncerto(true));
        Assert.Equal("Não foi possível confirmar; verifique a lista antes de repetir.", LogicaEcraPecas.AvisoResultadoIncerto(false));
    }

    [Fact]
    public async Task Api_envia_o_token_da_sessao_em_Bearer()
    {
        var sessao = new SessaoService(new DevAutenticacaoService(), new ArmazemSessaoMemoria());
        Assert.True((await sessao.EntrarAsync("7953")).Sucesso);
        var h = new Fixo(System.Net.HttpStatusCode.OK, "[]");

        await new PecasApiService(new HttpClient(h) { BaseAddress = new Uri("https://exemplo.invalid/") }, sessao).EntradasDeHojeAsync();

        Assert.Equal("Bearer", h.Ultimo!.Headers.Authorization!.Scheme);
        Assert.Equal(sessao.Utilizador!.Token, h.Ultimo.Headers.Authorization.Parameter);
    }

    [Theory]
    [InlineData(1.0, null)]
    [InlineData(0.001, null)]
    [InlineData(999999.0, null)]
    [InlineData(999999.999, null)]
    [InlineData(1000000.0, "A quantidade máxima é 999999.")]
    [InlineData(0.0001, "A quantidade tem no máximo 3 casas decimais.")]
    [InlineData(0.0, "Indique uma quantidade positiva.")]
    [InlineData(null, "Indique uma quantidade positiva.")]
    public void Quantidade_valida_no_cliente_como_a_api(double? qtt, string? esperado)
        => Assert.Equal(esperado, LogicaEcraPecas.ValidarQuantidade(qtt is null ? null : (decimal)qtt));

    [Theory]
    [InlineData("5600123456", "Código não encontrado: 5600123456")]
    [InlineData(" RL-6204 ", "Código não encontrado: RL-6204")]
    [InlineData("rolamento 6204", "Nenhuma peça encontrada para rolamento 6204")]
    public void Sem_resultado_distingue_codigo_de_pesquisa_livre(string texto, string esperado)
        => Assert.Equal(esperado, LogicaEcraPecas.MensagemSemResultado(texto));

    [Fact]
    public async Task Api_anular_404_nao_falha_e_500_falha()
    {
        await Api(new Fixo(System.Net.HttpStatusCode.NotFound)).AnularEntradaAsync("x");
        await Assert.ThrowsAsync<HttpRequestException>(() => Api(new Fixo(System.Net.HttpStatusCode.InternalServerError)).AnularEntradaAsync("x"));
    }
}
