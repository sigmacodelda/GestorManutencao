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

public class EstruturaArmazemRegrasTests
{
    private const string Desenho = """
        {"sala":{"largura":20},"estantes":[
          {"id":"A-03","colunas":5,"niveis":3,"x":1},
          {"id":"a-1","colunas":2,"niveis":2},
          {"id":"B-02","colunas":4,"niveis":4},
          {"id":"semformato","colunas":4,"niveis":4},
          {"id":"C-04","colunas":0,"niveis":4}]}
        """;

    [Fact]
    public void Desenho_vira_estantes_ordenadas_e_ignora_as_invalidas()
    {
        var l = RegrasEstrutura.LerDesenho(Desenho)!;
        Assert.Equal([new EstanteDto("A", 1, 2, 2), new EstanteDto("A", 3, 3, 5), new EstanteDto("B", 2, 4, 4)], l);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{isto não é json")]
    [InlineData("{}")]
    [InlineData("""{"estantes":[]}""")]
    [InlineData("""{"estantes":[{"id":"??","colunas":3,"niveis":3}]}""")]
    public void Sem_desenho_utilizavel_devolve_null(string? json) => Assert.Null(RegrasEstrutura.LerDesenho(json));

    [Fact]
    public void Json_danificado_usa_as_localizacoes_existentes()
    {
        var lugares = new[]
        {
            new LugarDto("A", "A-02", 1, 1, 0), new LugarDto("A", "A-02", 3, 6, 0), new LugarDto("A", "A-02", 2, 2, 0),
            new LugarDto("B", "B-01", 1, 4, 0), new LugarDto("BUFFER", "", 0, 0, 0)
        };
        var l = RegrasEstrutura.Montar("{danificado", lugares, null);
        Assert.Equal([new EstanteDto("A", 2, 3, 6), new EstanteDto("B", 1, 1, 4)], l);
    }

    [Fact]
    public void Buffer_fica_de_fora_no_desenho_e_nas_localizacoes()
    {
        // Texto livre: o corredor com esse nome não é opção.
        Assert.DoesNotContain(RegrasEstrutura.Montar(Desenho, [], RegrasLocalizacao.LerBuffer("B")), e => e.Corredor == "B");
        // Lugar completo: a estante que o contém sai da escolha.
        var semB = RegrasEstrutura.Montar(Desenho, [], RegrasLocalizacao.LerBuffer("A-03-1-01"));
        Assert.DoesNotContain(semB, e => e is { Corredor: "A", Estante: 3 });
        Assert.Contains(semB, e => e is { Corredor: "A", Estante: 1 });
        // Sem desenho: só a linha do buffer sai (a estante mantém as outras posições).
        var lugares = new[] { new LugarDto("A", "A-01", 1, 1, 0), new LugarDto("A", "A-01", 4, 9, 0) };
        Assert.Equal([new EstanteDto("A", 1, 1, 1)], RegrasEstrutura.Montar(null, lugares, RegrasLocalizacao.LerBuffer("A-01-4-09")));
    }

    private static readonly IReadOnlyList<EstanteArmazem> Estrutura = [new("A", 1, 2, 3), new("A", 2, 4, 6), new("B", 1, 1, 2)];

    [Fact]
    public void Cascata_so_oferece_opcoes_depois_do_passo_anterior()
    {
        Assert.Equal(["A", "B"], LogicaEstrutura.Corredores(Estrutura));
        Assert.Equal([1, 2], LogicaEstrutura.Estantes(Estrutura, "A"));
        Assert.Empty(LogicaEstrutura.Estantes(Estrutura, ""));
        Assert.Equal([1, 2, 3, 4], LogicaEstrutura.Niveis(Estrutura, "A", 2));
        Assert.Equal([1, 2, 3, 4, 5, 6], LogicaEstrutura.Posicoes(Estrutura, "A", 2));
        Assert.Empty(LogicaEstrutura.Niveis(Estrutura, "A", null));
        Assert.Empty(LogicaEstrutura.Posicoes(Estrutura, "Z", 1));
    }

    [Fact]
    public void Etiqueta_do_lugar_escolhido_e_lida_pela_picada_e_pela_api()
    {
        var e = LogicaFichaPeca.EtiquetaDeLugar("a", 2, 1, 1);
        Assert.Equal("LOC-A-2-1-1", e);
        Assert.True(LogicaLeitura.EEtiquetaLocal(e));
        Assert.Equal(("A", 2, 1, 1), RegrasLocalizacao.LerEtiqueta(e));
    }
}

public class EstruturaArmazemApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    private readonly WebApplicationFactory<Program> _fabrica;

    public EstruturaArmazemApiTests()
    {
        var repo = new RepositorioEstruturaMemoria([new("A", 1, 3, 4), new("A", 2, 4, 6), new("B", 1, 2, 3)]);
        repo.DefinirBuffer("B-01-1-01");
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Pin:Pepper", Pepper)
            .UseSetting("Limites:LoginPorMinuto", "1000")
            .ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
                [
                    (new UtilizadorApp("ana", "Ana Silva", Api.Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
                    (new UtilizadorApp("qual", "Marta Qualidade", Api.Perfil.Qualidade, true, false), Hasher.Hash("715329")),
                    (new UtilizadorApp("sup", "Rui Supervisor", Api.Perfil.Supervisor, true, false), Hasher.Hash("246813"))
                ])));
                s.Replace(ServiceDescriptor.Singleton<IRepositorioEstrutura>(repo));
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

    [Theory]
    [InlineData("482913")]
    [InlineData("246813")]
    public async Task Tecnico_e_supervisor_recebem_as_estantes_sem_o_buffer(string pin)
    {
        var r = await (await ComoAsync(pin)).GetAsync("/pecas/estrutura-armazem");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var l = await r.Content.ReadFromJsonAsync<List<EstanteDto>>();
        Assert.Equal([new EstanteDto("A", 1, 3, 4), new EstanteDto("A", 2, 4, 6)], l);
    }

    [Fact]
    public async Task Qualidade_nao_tem_acesso_e_sem_sessao_nao_entra()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ComoAsync("715329")).GetAsync("/pecas/estrutura-armazem")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await ComoAsync(null)).GetAsync("/pecas/estrutura-armazem")).StatusCode);
    }
}
