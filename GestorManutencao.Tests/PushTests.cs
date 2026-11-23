using System.Net;
using System.Net.Http.Json;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

public sealed class EnviadorPushFalso : IEnviadorPush
{
    public List<string[]> Envios { get; } = [];

    public Task AcordarAsync(IReadOnlyCollection<string> tokens, CancellationToken ct)
    {
        lock (Envios) Envios.Add(tokens.ToArray());
        return Task.CompletedTask;
    }
}

public sealed class NotificadorFalso : INotificadorPush
{
    public List<string?> Autores { get; } = [];
    public void Notificar(string? autor) { lock (Autores) Autores.Add(autor); }
}

public class RepositorioDispositivosTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "gm-push-" + Guid.NewGuid().ToString("N"));
    private string Caminho => Path.Combine(_pasta, "d.json");

    private RepositorioDispositivosFicheiro Novo() => new(Caminho, TimeProvider.System, NullLogger<RepositorioDispositivosFicheiro>.Instance);

    public void Dispose() { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); }

    [Fact]
    public void Upsert_passa_o_token_para_o_utilizador_atual()
    {
        var r = Novo();
        r.Registar("ana", "t1");
        r.Registar("chefe", "t1");
        Assert.Empty(r.Tokens().Except(["t1"]));
        Assert.Single(r.Tokens());
        Assert.Empty(r.Tokens("chefe"));
        Assert.Single(r.Tokens("ana"));
    }

    [Fact]
    public void Limite_de_cinco_tokens_remove_o_mais_antigo()
    {
        var r = Novo();
        for (var i = 1; i <= 6; i++) r.Registar("ana", "t" + i);
        var tokens = r.Tokens();
        Assert.Equal(5, tokens.Count);
        Assert.DoesNotContain("t1", tokens);
        Assert.Contains("t6", tokens);
    }

    [Fact]
    public void Remover_so_funciona_para_o_dono()
    {
        var r = Novo();
        r.Registar("ana", "t1");
        Assert.False(r.Remover("chefe", "t1"));
        Assert.True(r.Remover("ana", "t1"));
        Assert.Empty(r.Tokens());
    }

    [Fact]
    public void Persiste_em_ficheiro_e_remove_tokens_invalidos()
    {
        var r = Novo();
        r.Registar("ana", "t1");
        r.Registar("ana", "t2");
        r.RemoverTokens(["t1"]);
        Assert.Equal(["t2"], Novo().Tokens());
        Assert.False(File.Exists(Caminho + ".tmp"));
    }

    [Fact]
    public void Ficheiro_corrompido_comeca_vazio()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Caminho, "{isto não é json");
        Assert.Empty(Novo().Tokens());
    }
}

public class NotificadorPushTests
{
    private static readonly TimeSpan Atraso = TimeSpan.FromMilliseconds(150);

    private static async Task<(NotificadorPush, EnviadorPushFalso, IRepositorioDispositivos)> Iniciar()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "gm-push-" + Guid.NewGuid().ToString("N"));
        var repo = new RepositorioDispositivosFicheiro(Path.Combine(pasta, "d.json"), TimeProvider.System, NullLogger<RepositorioDispositivosFicheiro>.Instance);
        repo.Registar("ana", "tA");
        repo.Registar("chefe", "tC");
        var enviador = new EnviadorPushFalso();
        var n = new NotificadorPush(repo, enviador, NullLogger<NotificadorPush>.Instance, Atraso);
        await n.StartAsync(CancellationToken.None);
        return (n, enviador, repo);
    }

    [Fact]
    public async Task Escritas_seguidas_do_mesmo_autor_dao_um_so_envio_sem_o_autor()
    {
        var (n, enviador, _) = await Iniciar();
        n.Notificar("ana"); n.Notificar("ana"); n.Notificar("ana");
        await Task.Delay(Atraso * 5);
        await n.StopAsync(CancellationToken.None);

        var envio = Assert.Single(enviador.Envios);
        Assert.Equal(["tC"], envio);
    }

    [Fact]
    public async Task Autores_diferentes_no_mesmo_lote_acordam_todos()
    {
        var (n, enviador, _) = await Iniciar();
        n.Notificar("ana"); n.Notificar("chefe");
        await Task.Delay(Atraso * 5);
        await n.StopAsync(CancellationToken.None);

        var envio = Assert.Single(enviador.Envios);
        Assert.Equal(["tA", "tC"], envio.OrderBy(x => x));
    }

    [Fact]
    public async Task Sem_outros_dispositivos_nao_envia()
    {
        var (n, enviador, repo) = await Iniciar();
        repo.RemoverTokens(["tC"]);
        n.Notificar("ana");
        await Task.Delay(Atraso * 5);
        await n.StopAsync(CancellationToken.None);
        Assert.Empty(enviador.Envios);
    }
}

public class DispositivosApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!"; // sigma-ok
    private const string PinChefe = "603918"; // sigma-ok
    private const string PinAna = "482916"; // sigma-ok
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "gm-push-" + Guid.NewGuid().ToString("N"));
    private readonly NotificadorFalso _notificador = new();
    private readonly WebApplicationFactory<Program> _fabrica;

    public DispositivosApiTests()
    {
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Pin:Pepper", Pepper)
            .UseSetting("Push:FicheiroDispositivos", Path.Combine(_pasta, "d.json"))
            .ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
                [
                    (new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash(PinChefe)),
                    (new UtilizadorApp("ana", "Ana Silva", Perfil.Operador, true, false, Tecno: 101), Hasher.Hash(PinAna))
                ])));
                s.Replace(ServiceDescriptor.Singleton<INotificadorPush>(_notificador));
            }));
    }

    public void Dispose()
    {
        _fabrica.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    private async Task<HttpClient> Cliente(string? pin)
    {
        var c = _fabrica.CreateClient();
        if (pin is null) return c;
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(pin))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login!.Token);
        return c;
    }

    private static Task<HttpResponseMessage> Apagar(HttpClient c, string? token) =>
        c.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/dispositivos") { Content = JsonContent.Create(new PedidoDispositivo(token)) });

    [Fact]
    public async Task Sem_sessao_da_401()
    {
        var c = await Cliente(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/dispositivos", new PedidoDispositivo("t1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Apagar(c, "t1")).StatusCode);
    }

    [Fact]
    public async Task Registar_e_remover_da_204_e_guarda_para_o_utilizador()
    {
        var c = await Cliente(PinChefe);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/dispositivos", new PedidoDispositivo("t1"))).StatusCode);
        var repo = _fabrica.Services.GetRequiredService<IRepositorioDispositivos>();
        Assert.Equal(["t1"], repo.Tokens("ana"));

        var ana = await Cliente(PinAna);
        Assert.Equal(HttpStatusCode.NoContent, (await Apagar(ana, "t1")).StatusCode);
        Assert.Single(repo.Tokens());

        Assert.Equal(HttpStatusCode.NoContent, (await Apagar(c, "t1")).StatusCode);
        Assert.Empty(repo.Tokens());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Token_vazio_da_400(string? token)
    {
        var c = await Cliente(PinChefe);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/dispositivos", new PedidoDispositivo(token))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Apagar(c, token)).StatusCode);
    }

    [Fact]
    public async Task Token_demasiado_grande_da_400()
    {
        var c = await Cliente(PinChefe);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/dispositivos", new PedidoDispositivo(new string('x', 4097)))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/dispositivos", new PedidoDispositivo(new string('x', 4096)))).StatusCode);
    }

    [Fact]
    public async Task Escrita_nas_fichas_notifica_com_o_autor()
    {
        var c = await Cliente(PinChefe);
        await c.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Teste do push"));
        Assert.Equal(["chefe"], _notificador.Autores);
    }
}
