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

// "Exigir material ao concluir" (configuração) e fotos da intervenção (anexos): regras e rotas com o repositório em memória.
public class MaterialFotosTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    private readonly WebApplicationFactory<Program> _fabrica = Criar(new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false));
    private readonly List<IDisposable> _extra = [];

    private static WebApplicationFactory<Program> Criar(ConfigManutencao configuracao) => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
            [
                (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
                (new UtilizadorApp("novo", "Sem Numero", Perfil.Operador, true, false), Hasher.Hash("264813")),
                (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918"))
            ])));
            s.Replace(ServiceDescriptor.Singleton<IRepositorioIntervencoes>(sp =>
                new RepositorioIntervencoesMemoria(sp.GetRequiredService<TimeProvider>(), configuracao)));
        }));

    public void Dispose()
    {
        _fabrica.Dispose();
        foreach (var d in _extra) d.Dispose();
    }

    private static async Task<HttpClient> ComoAsync(WebApplicationFactory<Program> fabrica, string pin)
    {
        var c = fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    private static HttpContent CorpoJpeg(byte[]? dados = null)
    {
        var corpo = new ByteArrayContent(dados ?? Jpeg);
        corpo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return corpo;
    }

    private static async Task<HttpClient> AnaComFichaEmCursoAsync(WebApplicationFactory<Program> fabrica)
    {
        var c = await ComoAsync(fabrica, "482913");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/intervencoes/F1/iniciar", null)).StatusCode);
        return c;
    }

    private static Task<HttpResponseMessage> Concluir(HttpClient c) =>
        c.PostAsJsonAsync("/intervencoes/F1/concluir", new PedidoConclusao("Feito", new TimeOnly(8, 0), new TimeOnly(9, 0)));

    // ---- Material ao concluir ----

    [Fact]
    public async Task Sem_a_opcao_ligada_conclui_sem_material()
    {
        var c = await AnaComFichaEmCursoAsync(_fabrica);

        Assert.Equal(HttpStatusCode.OK, (await Concluir(c)).StatusCode);
    }

    [Fact]
    public async Task Com_a_opcao_ligada_recusa_sem_material_e_aceita_com_material()
    {
        var fabrica = Criar(new ConfigManutencao(AvancaOrdem: true, ExtExigeDoc: false, ExigeMaterial: true));
        _extra.Add(fabrica);
        var c = await AnaComFichaEmCursoAsync(fabrica);

        var recusada = await Concluir(c);
        Assert.Equal(HttpStatusCode.Conflict, recusada.StatusCode);
        Assert.Contains("sem_material", await recusada.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/intervencoes/F1/materiais", new PedidoMaterial("Rolamento", 2, 5))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Concluir(c)).StatusCode);
    }

    [Fact]
    public async Task Configuracao_guarda_exige_material_e_pedidos_antigos_ficam_desligados()
    {
        var chefe = await ComoAsync(_fabrica, "603918");

        var ligar = await chefe.PutAsJsonAsync("/configuracao", new { antecedencia = 7, gerarPlaneadas = true, avancaOrdem = true, validarPropria = false, extExigeDoc = false, comprasFichaInt = true, exigeMaterial = true });
        Assert.True((await ligar.Content.ReadFromJsonAsync<ConfigManutencao>())!.ExigeMaterial);

        var antigo = await chefe.PutAsJsonAsync("/configuracao", new { antecedencia = 7, gerarPlaneadas = true, avancaOrdem = true, validarPropria = false, extExigeDoc = false, comprasFichaInt = true });
        Assert.False((await antigo.Content.ReadFromJsonAsync<ConfigManutencao>())!.ExigeMaterial);
        Assert.False((await chefe.GetFromJsonAsync<ConfigManutencao>("/configuracao"))!.ExigeMaterial);
    }

    // ---- Fotos ----

    [Fact]
    public async Task Foto_junta_lista_devolve_e_apaga_com_a_ficha_em_curso()
    {
        var c = await AnaComFichaEmCursoAsync(_fabrica);

        var nova = await c.PostAsync("/intervencoes/F1/fotos?nome=Rolamento antes", CorpoJpeg());
        Assert.Equal(HttpStatusCode.OK, nova.StatusCode);
        var foto = (await nova.Content.ReadFromJsonAsync<FotoDto>())!;
        Assert.Equal("Rolamentoantes", foto.Nome);

        Assert.Single((await c.GetFromJsonAsync<List<FotoDto>>("/intervencoes/F1/fotos"))!);
        Assert.Equal(Jpeg, await c.GetByteArrayAsync($"/intervencoes/F1/fotos/{foto.Id}"));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/intervencoes/F1/fotos/{foto.Id}")).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<List<FotoDto>>("/intervencoes/F1/fotos"))!);
    }

    [Fact]
    public async Task Foto_so_com_a_ficha_em_curso()
    {
        var c = await ComoAsync(_fabrica, "482913");

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/intervencoes/F1/fotos", CorpoJpeg())).StatusCode);
    }

    [Fact]
    public async Task Foto_recusa_o_que_nao_e_jpeg_e_o_que_e_grande_demais()
    {
        var c = await AnaComFichaEmCursoAsync(_fabrica);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/intervencoes/F1/fotos", CorpoJpeg([1, 2, 3, 4, 5, 6]))).StatusCode);
        var grande = new byte[RegrasIntervencao.MaxBytesFoto + 1];
        grande[0] = 0xFF; grande[1] = 0xD8; grande[2] = 0xFF;
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.PostAsync("/intervencoes/F1/fotos", CorpoJpeg(grande))).StatusCode);
    }

    [Fact]
    public async Task Foto_tem_limite_por_intervencao()
    {
        var c = await AnaComFichaEmCursoAsync(_fabrica);
        for (var i = 0; i < RegrasIntervencao.MaxFotos; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/intervencoes/F1/fotos", CorpoJpeg())).StatusCode);

        var excesso = await c.PostAsync("/intervencoes/F1/fotos", CorpoJpeg());

        Assert.Equal(HttpStatusCode.Conflict, excesso.StatusCode);
        Assert.Contains("limite_fotos", await excesso.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Supervisor_ve_as_fotos_mas_nao_as_altera_e_sem_token_nao_ha_acesso()
    {
        var ana = await AnaComFichaEmCursoAsync(_fabrica);
        var foto = (await (await ana.PostAsync("/intervencoes/F1/fotos", CorpoJpeg())).Content.ReadFromJsonAsync<FotoDto>())!;
        var chefe = await ComoAsync(_fabrica, "603918");

        Assert.Single((await chefe.GetFromJsonAsync<List<FotoDto>>("/intervencoes/F1/fotos"))!);
        Assert.Equal(Jpeg, await chefe.GetByteArrayAsync($"/intervencoes/F1/fotos/{foto.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.PostAsync("/intervencoes/F1/fotos", CorpoJpeg())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chefe.DeleteAsync($"/intervencoes/F1/fotos/{foto.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().GetAsync("/intervencoes/F1/fotos")).StatusCode);
    }

    [Fact]
    public async Task Tecnico_sem_numero_nao_ve_fotos()
    {
        var c = await ComoAsync(_fabrica, "264813");

        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/intervencoes/F1/fotos")).StatusCode);
    }

    [Theory]
    [InlineData("Rolamento antes", "Rolamentoantes")]
    [InlineData("../../etc/passwd", "etcpasswd")]
    [InlineData("foto.JPG", "foto")]
    [InlineData("", "foto_20261109_101500")]
    [InlineData(null, "foto_20261109_101500")]
    public void Nome_da_foto_so_guarda_caracteres_seguros(string? nome, string esperado)
    {
        Assert.Equal(esperado, RegrasIntervencao.NomeFoto(nome, new DateTime(2026, 11, 9, 10, 15, 0)));
    }

    [Fact]
    public void Nome_da_foto_cabe_na_coluna_com_a_extensao()
    {
        Assert.True(RegrasIntervencao.NomeFoto(new string('a', 400), DateTime.Now).Length + 4 <= RegrasIntervencao.TamanhoNomeFoto);
    }

    [Fact]
    public void So_se_aceita_jpeg()
    {
        Assert.True(RegrasIntervencao.EhJpeg(Jpeg));
        Assert.False(RegrasIntervencao.EhJpeg([0x89, 0x50, 0x4E, 0x47, 0x0D]));
        Assert.False(RegrasIntervencao.EhJpeg([]));
    }
}
