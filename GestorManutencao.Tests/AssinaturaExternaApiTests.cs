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

// Ficha de manutenção externa: a empresa assina (desenho + nome) antes de a ficha se concluir; a assinatura é imutável e vê-se na ficha completa.
public class AssinaturaExternaApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!";
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));

    // PNG de 1x1 pixel (cabeçalho válido) e outro diferente, também válido.
    private const string Png = RegrasAssinatura.Prefixo + "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
    private const string OutroPng = RegrasAssinatura.Prefixo + "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP9z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Development")
        .UseSetting("Pin:Pepper", Pepper)
        .UseSetting("Limites:LoginPorMinuto", "1000")
        .ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
        [
            (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash("482913")),
            (new UtilizadorApp("rui", "Rui Costa", Perfil.Operador, true, false, Tecno: 102), Hasher.Hash("159357")),
            (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash("603918")),
            (new UtilizadorApp("qual", "Marta Qualidade", Perfil.Qualidade, true, false), Hasher.Hash("715329"))
        ]))))
    );

    public void Dispose() => _fabrica.Dispose();

    private async Task<HttpClient> ComoAsync(string pin)
    {
        var c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    // A Ana abre uma avaria e o supervisor passa-a a manutenção externa (o técnico só cria internas).
    private async Task<string> FichaDaAnaAsync(bool externa)
    {
        var ana = await ComoAsync("482913");
        var criada = await ana.PostAsJsonAsync("/intervencoes/avaria", new PedidoAvaria("E1", "Fuga de óleo"));
        Assert.Equal(HttpStatusCode.OK, criada.StatusCode);
        var id = (await criada.Content.ReadFromJsonAsync<IntervencaoDto>())!.Id;
        if (externa)
        {
            var chefe = await ComoAsync("603918");
            Assert.Equal(HttpStatusCode.OK, (await chefe.PutAsJsonAsync($"/intervencoes/{id}", new PedidoFicha(TipoManut: "Externa"))).StatusCode);
        }
        return id;
    }

    private static async Task<string?> ErroAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("erro").GetString();

    private static PedidoConclusao Conclusao => new("Reparação feita pela empresa", new TimeOnly(8, 0), new TimeOnly(9, 0));

    [Fact]
    public async Task Ficha_externa_diz_que_e_externa_e_ainda_nao_esta_assinada()
    {
        var id = await FichaDaAnaAsync(externa: true);
        var ana = await ComoAsync("482913");

        var ficha = (await ana.GetFromJsonAsync<IntervencaoDto>($"/intervencoes/{id}"))!;
        Assert.True(ficha.Externa);
        Assert.False(ficha.TemAssinaturaExterna);

        var interna = await FichaDaAnaAsync(externa: false);
        var outra = (await ana.GetFromJsonAsync<IntervencaoDto>($"/intervencoes/{interna}"))!;
        Assert.False(outra.Externa);
    }

    [Fact]
    public async Task Concluir_externa_sem_assinatura_da_409_e_a_interna_conclui_normalmente()
    {
        var id = await FichaDaAnaAsync(externa: true);
        var ana = await ComoAsync("482913");

        var r = await ana.PostAsJsonAsync($"/intervencoes/{id}/concluir", Conclusao);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("externa_sem_assinatura", await ErroAsync(r));
        Assert.Equal(EstadoIntervencao.EmCurso, (await ana.GetFromJsonAsync<IntervencaoDto>($"/intervencoes/{id}"))!.Estado);

        var interna = await FichaDaAnaAsync(externa: false);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync($"/intervencoes/{interna}/concluir", Conclusao)).StatusCode);
    }

    [Fact]
    public async Task Assinar_com_nome_ou_desenho_invalidos_da_400_e_nada_fica_gravado()
    {
        var id = await FichaDaAnaAsync(externa: true);
        var ana = await ComoAsync("482913");

        var maus = new[]
        {
            new PedidoAssinaturaExterna("A", Png), new PedidoAssinaturaExterna("   ", Png), new PedidoAssinaturaExterna(null, Png),
            new PedidoAssinaturaExterna(new string('x', 61), Png),
            new PedidoAssinaturaExterna("Frio Norte", null), new PedidoAssinaturaExterna("Frio Norte", ""), new PedidoAssinaturaExterna("Frio Norte", "Frio Norte"),
            new PedidoAssinaturaExterna("Frio Norte", "data:image/png;base64,SGVsbG8gV29ybGQ="), new PedidoAssinaturaExterna("Frio Norte", RegrasAssinatura.Prefixo + new string('A', RegrasAssinatura.Max))
        };
        foreach (var mau in maus)
            Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync($"/intervencoes/{id}/assinatura-externa", mau)).StatusCode);

        Assert.False((await ana.GetFromJsonAsync<IntervencaoDto>($"/intervencoes/{id}"))!.TemAssinaturaExterna);
    }

    [Fact]
    public async Task Assinar_ficha_interna_da_409_nao_externa()
    {
        var id = await FichaDaAnaAsync(externa: false);
        var ana = await ComoAsync("482913");

        var r = await ana.PostAsJsonAsync($"/intervencoes/{id}/assinatura-externa", new PedidoAssinaturaExterna("Frio Norte", Png));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("nao_externa", await ErroAsync(r));
    }

    [Fact]
    public async Task Assinar_e_concluir_funciona_e_a_ficha_completa_mostra_assinatura_nome_e_data()
    {
        var id = await FichaDaAnaAsync(externa: true);
        var ana = await ComoAsync("482913");

        var assinada = await ana.PostAsJsonAsync($"/intervencoes/{id}/assinatura-externa", new PedidoAssinaturaExterna("  Frio Norte, Lda ", Png));
        Assert.Equal(HttpStatusCode.OK, assinada.StatusCode);
        var dto = (await assinada.Content.ReadFromJsonAsync<IntervencaoDto>())!;
        Assert.True(dto.Externa);
        Assert.True(dto.TemAssinaturaExterna);

        var concluida = await ana.PostAsJsonAsync($"/intervencoes/{id}/concluir", Conclusao);
        Assert.Equal(HttpStatusCode.OK, concluida.StatusCode);
        Assert.Equal(EstadoIntervencao.Concluida, (await concluida.Content.ReadFromJsonAsync<IntervencaoDto>())!.Estado);

        // O supervisor e o próprio técnico vêm a mesma assinatura na ficha completa.
        var chefe = await ComoAsync("603918");
        foreach (var cliente in new[] { chefe, ana })
        {
            var d = (await cliente.GetFromJsonAsync<FichaDetalheDto>($"/intervencoes/{id}/detalhe"))!;
            Assert.Equal(Png, d.AssinaturaExt);
            Assert.Equal("Frio Norte, Lda", d.AssinaturaExtNome);
            Assert.NotNull(d.AssinaturaExtData);
            Assert.Equal("Externa", d.TipoManut);
        }
    }

    [Fact]
    public async Task Ficha_sem_assinatura_vem_com_a_data_nula_na_ficha_completa()
    {
        var id = await FichaDaAnaAsync(externa: true);
        var ana = await ComoAsync("482913");

        var d = (await ana.GetFromJsonAsync<FichaDetalheDto>($"/intervencoes/{id}/detalhe"))!;
        Assert.Equal("", d.AssinaturaExt);
        Assert.Equal("", d.AssinaturaExtNome);
        Assert.Null(d.AssinaturaExtData);
    }

    [Fact]
    public async Task A_assinatura_e_imutavel_repetir_igual_e_ok_e_diferente_da_409_ja_assinada()
    {
        var id = await FichaDaAnaAsync(externa: true);
        var ana = await ComoAsync("482913");
        var url = $"/intervencoes/{id}/assinatura-externa";

        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync(url, new PedidoAssinaturaExterna("Frio Norte", Png))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync(url, new PedidoAssinaturaExterna("Frio Norte", Png))).StatusCode);

        var outroDesenho = await ana.PostAsJsonAsync(url, new PedidoAssinaturaExterna("Frio Norte", OutroPng));
        Assert.Equal(HttpStatusCode.Conflict, outroDesenho.StatusCode);
        Assert.Equal("ja_assinada", await ErroAsync(outroDesenho));
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsJsonAsync(url, new PedidoAssinaturaExterna("Outra Empresa", Png))).StatusCode);

        // Nem concluída a ficha a assinatura muda; repetir o pedido igual continua a ser inofensivo.
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync($"/intervencoes/{id}/concluir", Conclusao)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync(url, new PedidoAssinaturaExterna("Frio Norte", Png))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsJsonAsync(url, new PedidoAssinaturaExterna("Frio Norte", OutroPng))).StatusCode);
        var d = (await (await ComoAsync("603918")).GetFromJsonAsync<FichaDetalheDto>($"/intervencoes/{id}/detalhe"))!;
        Assert.Equal(Png, d.AssinaturaExt);
    }

    [Fact]
    public async Task Concluir_outra_vez_uma_externa_ja_concluida_continua_idempotente()
    {
        var id = await FichaDaAnaAsync(externa: true);
        var ana = await ComoAsync("482913");
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync($"/intervencoes/{id}/assinatura-externa", new PedidoAssinaturaExterna("Frio Norte", Png))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync($"/intervencoes/{id}/concluir", Conclusao)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync($"/intervencoes/{id}/concluir", Conclusao)).StatusCode);
    }

    [Fact]
    public async Task So_o_tecnico_da_ficha_e_o_supervisor_assinam_a_Qualidade_e_os_anonimos_nao()
    {
        var id = await FichaDaAnaAsync(externa: true);
        var url = $"/intervencoes/{id}/assinatura-externa";
        var pedido = new PedidoAssinaturaExterna("Frio Norte", Png);

        Assert.Equal(HttpStatusCode.Forbidden, (await (await ComoAsync("715329")).PostAsJsonAsync(url, pedido)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().PostAsJsonAsync(url, pedido)).StatusCode);
        // Outro técnico não vê a ficha da Ana.
        Assert.Equal(HttpStatusCode.NotFound, (await (await ComoAsync("159357")).PostAsJsonAsync(url, pedido)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await ComoAsync("482913")).PostAsJsonAsync("/intervencoes/nao-existe/assinatura-externa", pedido)).StatusCode);

        var chefe = await ComoAsync("603918");
        Assert.Equal(HttpStatusCode.OK, (await chefe.PostAsJsonAsync(url, pedido)).StatusCode);
        Assert.True((await (await ComoAsync("482913")).GetFromJsonAsync<IntervencaoDto>($"/intervencoes/{id}"))!.TemAssinaturaExterna);
    }
}
