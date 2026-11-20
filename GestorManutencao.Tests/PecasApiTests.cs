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

public class PecasApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly RepositorioUtilizadoresMemoria _utilizadores = new(
    [
        (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
        (new UtilizadorApp("qual", "Marta Qualidade", Perfil.Qualidade, true, false), Hasher.Hash("715329"))
    ]);

    private readonly WebApplicationFactory<Program> _fabrica;

    public PecasApiTests() => _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(_utilizadores));
            s.Replace(ServiceDescriptor.Singleton<IRepositorioPecas>(new RepositorioPecasMemoria(
            [
                new PecaDto("P001", "Parafuso M8", "560001", "A-01-1-01", 10, 4, 0.12m),
                new PecaDto("P002", "Rolamento 6204", "560002", "A-01-2-03", 0, 2, 5.5m)
            ])));
        }));

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAnaAsync(string pin = "482913")
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    [Fact]
    public async Task Sem_sessao_recusa()
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/pecas")).StatusCode);
    }

    [Fact]
    public async Task Pesquisa_por_texto_e_por_codigo_exato()
    {
        var c = await ComoAnaAsync();
        var r = await c.GetFromJsonAsync<ResultadoPecasDto>("/pecas?texto=rolamento");
        Assert.Equal("P002", Assert.Single(r!.Pecas).Ref);
        var p = await c.GetFromJsonAsync<PecaDto>("/pecas/codigo/560001");
        Assert.Equal("P001", p!.Ref);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/pecas/codigo/XXXX")).StatusCode);
    }

    [Fact]
    public async Task Perfil_sem_acesso_e_recusado_em_todas_as_rotas()
    {
        var c = await ComoAnaAsync("715329");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/pecas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/pecas/codigo/560001")).StatusCode);
    }

    [Fact]
    public async Task Sem_texto_devolve_as_pecas()
    {
        var c = await ComoAnaAsync();
        var r = await c.GetFromJsonAsync<ResultadoPecasDto>("/pecas");
        Assert.Equal(2, r!.Pecas.Count);
    }

    private sealed class RepositorioFalso(bool indisponivel = false) : IRepositorioPecas
    {
        private static Exception Indisp() => new RepositorioIndisponivelException();
        public Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct) => indisponivel ? throw Indisp() : Task.FromResult(new ResultadoPecasDto(true, [], false));
        public Task<PecaDto?> ObterPorCodigoAsync(string codigo, CancellationToken ct) => indisponivel ? throw Indisp() : Task.FromResult<PecaDto?>(null);
        public Task<ResultadoMovimento> AbrirConferenciaAsync(string fichaId, string iniciais, CancellationToken ct) => throw new NotSupportedException();
        public Task<ResultadoMovimento> DarSaidaAsync(string fichaId, string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct) => throw new NotSupportedException();
        public Task<ResultadoMovimento> ConfirmarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct) => throw new NotSupportedException();
        public Task<ResultadoMovimento> CancelarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, decimal>> EmConferenciaPorPecaAsync(IReadOnlyCollection<string> refs, string? excetoFichaId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ResultadoMovimento> AnularSaidaAsync(string materialId, string iniciais, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> LigarPecaAsync(string stampMaterial, string refPeca, string usrinis, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct) => throw new NotSupportedException();
    }

    private async Task<HttpClient> ComoAnaComAsync(IRepositorioPecas repo)
    {
        var f = _fabrica.WithWebHostBuilder(b => b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton(repo))));
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin("482913"))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    [Fact]
    public async Task Repositorio_indisponivel_da_503()
    {
        var c = await ComoAnaComAsync(new RepositorioFalso(indisponivel: true));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.GetAsync("/pecas")).StatusCode);
    }

    [Fact]
    public async Task Utilizador_inativado_e_recusado()
    {
        var c = await ComoAnaAsync();
        Assert.Equal(ResultadoPin.Ok, await _utilizadores.DefinirAtivoAsync("ana", false, "chefe", CancellationToken.None));
        var r = await c.GetAsync("/pecas");
        Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized });
    }

}
