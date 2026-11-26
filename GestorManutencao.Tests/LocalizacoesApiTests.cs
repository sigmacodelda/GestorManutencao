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

public class LocalizacoesApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly WebApplicationFactory<Program> _fabrica;

    public LocalizacoesApiTests() : this(null) { }

    private LocalizacoesApiTests(IRepositorioLocalizacoes? repo)
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
                    (new UtilizadorApp("qual", "Marta Qualidade", Perfil.Qualidade, true, false), Hasher.Hash("715329")),
                    (new UtilizadorApp("sup", "Rui Supervisor", Perfil.Supervisor, true, false), Hasher.Hash("246813"))
                ])));
                s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(new RepositorioPecasMemoria(
                [
                    new PecaDto("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
                    new PecaDto("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
                ])));
                if (repo is not null) s.Replace(ServiceDescriptor.Singleton(repo));
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

    [Fact]
    public async Task No_lugar_devolve_as_pecas_com_quantidade_e_aceita_a_etiqueta()
    {
        var c = await ComoAsync("482913");
        foreach (var local in new[] { "A-02-1-01", "LOC-A-02-1-01" })
        {
            var r = await c.GetAsync($"/pecas/no-lugar?local={Uri.EscapeDataString(local)}");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var pecas = (await r.Content.ReadFromJsonAsync<List<PecaNoLugarDto>>())!;
            Assert.Equal(new PecaNoLugarDto("P001", "Parafuso M8", 3), Assert.Single(pecas));
        }
    }

    [Fact]
    public async Task No_lugar_junta_varias_pecas_e_ignora_as_sem_quantidade()
    {
        var c = await ComoAsync("482913");
        // P002 tem stock 0 (nada no buffer): a quantidade põe-se direto no lugar, sem passar pela arrumação.
        ((RepositorioLocalizacoesMemoria)_fabrica.Services.GetRequiredService<IRepositorioLocalizacoes>()).Semear("P002", "A", "A-02", 1, 1, 5);
        var pecas = (await c.GetFromJsonAsync<List<PecaNoLugarDto>>("/pecas/no-lugar?local=A-02-1-01"))!;
        Assert.Equal(["P002", "P001"], pecas.Select(p => p.Ref));
        Assert.Empty((await c.GetFromJsonAsync<List<PecaNoLugarDto>>("/pecas/no-lugar?local=B-09-9-09"))!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A-02")]
    [InlineData("lixo")]
    public async Task No_lugar_local_invalido_400(string local) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await (await ComoAsync("482913")).GetAsync($"/pecas/no-lugar?local={Uri.EscapeDataString(local)}")).StatusCode);

    [Fact]
    public async Task No_lugar_sem_token_401_e_Qualidade_403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await ComoAsync(null)).GetAsync("/pecas/no-lugar?local=A-02-1-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ComoAsync("715329")).GetAsync("/pecas/no-lugar?local=A-02-1-01")).StatusCode);
    }

    [Fact]
    public async Task Sem_token_401_e_Qualidade_403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await ComoAsync(null)).GetAsync("/pecas/P001/localizacoes")).StatusCode);
        var q = await ComoAsync("715329");
        Assert.Equal(HttpStatusCode.Forbidden, (await q.GetAsync("/pecas/P001/localizacoes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await q.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "A-02-1-01", 1))).StatusCode);
    }

    [Fact]
    public async Task Get_devolve_lugares_ordenados_e_por_arrumar()
    {
        var c = await ComoAsync("482913");
        var d = await c.GetFromJsonAsync<LocalizacoesPecaDto>("/pecas/P001/localizacoes");
        Assert.Equal(10, d!.Stock);
        Assert.Equal(3, d.Localizado);
        Assert.Equal(7, d.PorArrumar);
        Assert.Equal("A-02-1-01", Assert.Single(d.Lugares).Texto);
    }

    [Fact]
    public async Task Post_soma_ao_lugar_existente_e_cria_um_novo_ordenado_por_qtt()
    {
        var c = await ComoAsync("482913");
        var r = await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "LOC-A-02-1-01", 2));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var d = (await r.Content.ReadFromJsonAsync<LocalizacoesPecaDto>())!;
        Assert.Equal(5, Assert.Single(d.Lugares).Qtt);
        Assert.Equal(5, d.PorArrumar);

        d = (await (await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "B-1-2-03", 4.5m))).Content.ReadFromJsonAsync<LocalizacoesPecaDto>())!;
        Assert.Equal(["A-02-1-01", "B-01-2-03"], d.Lugares.Select(l => l.Texto));
        Assert.Equal(9.5m, d.Localizado);
        Assert.Equal(0.5m, d.PorArrumar);
    }

    [Fact]
    public async Task So_se_arruma_o_que_esta_no_buffer()
    {
        var c = await ComoAsync("482913"); // P001: stock 10, 3 já arrumadas
        var r = await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "B-01-1-01", 8));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("buffer", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "B-01-1-01", 7))).StatusCode);

        // Buffer vazio: nada mais a arrumar. P002 tem stock 0 e também não.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "B-01-1-01", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P002", "B-01-1-01", 1))).StatusCode);
    }

    [Fact]
    public async Task Linha_do_buffer_nao_conta_como_arrumada_e_arrumar_tira_do_buffer()
    {
        var pecas = new RepositorioPecasMemoria([new PecaDto("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m)]);
        var repo = new RepositorioLocalizacoesMemoria(pecas);
        repo.DefinirBuffer("Z-09-9-09");
        repo.Semear("P001", "Z", "Z-09", 9, 9, 7); // entrada: 7 à espera no buffer
        using var f = new LocalizacoesApiTests(repo);
        var c = await f.ComoAsync("482913");

        var d = (await c.GetFromJsonAsync<LocalizacoesPecaDto>("/pecas/P001/localizacoes"))!;
        Assert.Equal(7, d.PorArrumar); // com a linha do buffer a contar daria 0 ("já está tudo arrumado")
        Assert.DoesNotContain(d.Lugares, l => l.Texto == "Z-09-9-09");

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "Z-09-9-09", 1))).StatusCode);

        d = (await (await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "B-01-1-01", 7))).Content.ReadFromJsonAsync<LocalizacoesPecaDto>())!;
        Assert.Equal(0, d.PorArrumar);
        Assert.Equal(["A-02-1-01", "B-01-1-01"], d.Lugares.Select(l => l.Texto).Order());
        Assert.Empty(await repo.ListarPecasDoLugarAsync("Z", "Z-09", 9, 9, CancellationToken.None)); // o buffer ficou vazio
    }

    [Fact]
    public async Task Peca_inexistente_404()
    {
        var c = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("XXX", "A-02-1-01", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/pecas/XXX/localizacoes")).StatusCode);
    }

    [Theory]
    [InlineData(null, "A-02-1-01", 1.0)]
    [InlineData("P001", null, 1.0)]
    [InlineData("P001", "lixo", 1.0)]
    [InlineData("P001", "LOC-A-02-1", 1.0)] // sem posição não chega para arrumar
    [InlineData("P001", "A-02-1-01", null)]
    [InlineData("P001", "A-02-1-01", 0.0)]
    [InlineData("P001", "A-02-1-01", -1.0)]
    [InlineData("P001", "A-02-1-01", 1.2345)]
    [InlineData("P001", "A-02-1-01", 1000000.0)]
    [InlineData("0123456789012345678", "A-02-1-01", 1.0)]
    [InlineData("P001", "ABCD-02-1-01", 1.0)] // corredor com 4 letras
    [InlineData("P001", "A-1000-1-01", 1.0)]  // estante 1000
    [InlineData("P001", "A-02-100-01", 1.0)]  // nível 100
    [InlineData("P001", "A-02-1-100", 1.0)]   // posição 100
    public async Task Pedido_invalido_400(string? refPeca, string? local, double? qtt)
    {
        var c = await ComoAsync("482913");
        var r = await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar(refPeca, local, (decimal?)qtt));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    private sealed class RepoComTabelaEmFalta : IRepositorioLocalizacoes
    {
        public Task<LocalizacoesPecaDto> ListarAsync(string r, CancellationToken ct) => Task.FromResult(new LocalizacoesPecaDto(r, 10, 0, 10, []));
        public Task<ResultadoMovimento> ArrumarAsync(string r, string c, int e, int n, int p, decimal q, string i, CancellationToken ct) => throw new InvalidOperationException();
        public Task<string> VerificarTabelaAsync(CancellationToken ct) => Task.FromResult("Falta criar a tabela u_pecaloc no PHC.");
        public Task<IReadOnlyList<(string Ref, decimal Qtt)>> ListarPecasDoLugarAsync(string c, string e, int n, int p, CancellationToken ct) => throw new InvalidOperationException();
    }

    private sealed class RepoIndisponivel : IRepositorioLocalizacoes
    {
        public Task<LocalizacoesPecaDto> ListarAsync(string r, CancellationToken ct) => throw new RepositorioIndisponivelException();
        public Task<ResultadoMovimento> ArrumarAsync(string r, string c, int e, int n, int p, decimal q, string i, CancellationToken ct) => throw new RepositorioIndisponivelException();
        public Task<string> VerificarTabelaAsync(CancellationToken ct) => throw new RepositorioIndisponivelException();
        public Task<IReadOnlyList<(string Ref, decimal Qtt)>> ListarPecasDoLugarAsync(string c, string e, int n, int p, CancellationToken ct) => throw new RepositorioIndisponivelException();
    }

    [Fact]
    public async Task Tabela_em_falta_409_com_o_texto()
    {
        using var f = new LocalizacoesApiTests(new RepoComTabelaEmFalta());
        var r = await (await f.ComoAsync("482913")).PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "A-02-1-01", 1));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("Falta criar a tabela u_pecaloc no PHC.", await r.Content.ReadAsStringAsync());
    }

    private sealed class RepoQueFalha(string erro) : IRepositorioLocalizacoes
    {
        public Task<LocalizacoesPecaDto> ListarAsync(string r, CancellationToken ct) => Task.FromResult(new LocalizacoesPecaDto(r, 10, 0, 10, []));
        public Task<ResultadoMovimento> ArrumarAsync(string r, string c, int e, int n, int p, decimal q, string i, CancellationToken ct) => Task.FromResult(ResultadoMovimento.Falha(erro));
        public Task<string> VerificarTabelaAsync(CancellationToken ct) => Task.FromResult("");
        public Task<IReadOnlyList<(string Ref, decimal Qtt)>> ListarPecasDoLugarAsync(string c, string e, int n, int p, CancellationToken ct) => throw new InvalidOperationException();
    }

    [Fact]
    public async Task Lugar_ocupado_409()
    {
        using var f = new LocalizacoesApiTests(new RepoQueFalha(RegrasPecas.ErroOcupado));
        var r = await (await f.ComoAsync("482913")).PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "A-02-1-01", 1));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Erro_de_dados_do_repositorio_400_e_nao_503()
    {
        using var f = new LocalizacoesApiTests(new RepoQueFalha("O local ou a peça excede o tamanho permitido na tabela u_pecaloc."));
        var r = await (await f.ComoAsync("482913")).PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "A-02-1-01", 1));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("excede", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Supervisor_pode_ler_e_arrumar()
    {
        var c = await ComoAsync("246813");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/pecas/P001/localizacoes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "A-02-1-01", 1))).StatusCode);
    }

    [Fact]
    public async Task Repositorio_indisponivel_503()
    {
        using var f = new LocalizacoesApiTests(new RepoIndisponivel());
        var c = await f.ComoAsync("482913");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.PostAsJsonAsync("/pecas/localizacoes", new PedidoArrumar("P001", "A-02-1-01", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.GetAsync("/pecas/P001/localizacoes")).StatusCode);
    }
}
