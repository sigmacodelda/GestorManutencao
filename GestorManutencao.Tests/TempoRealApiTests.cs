using System.Net.Http.Json;
using GestorManutencao.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GestorManutencao.Tests;

// Canal em tempo real: uma escrita nas fichas chega logo a quem está ligado; sem sessão não se liga.
public class TempoRealApiTests : IDisposable
{
    private const string Pepper = "pepper-de-teste-com-mais-de-32-caracteres!"; // sigma-ok
    private const string PinChefe = "603918"; // sigma-ok
    private static readonly PinHasher Hasher = new(Options.Create(new PinOpcoes { Pepper = Pepper }));
    private readonly WebApplicationFactory<Program> _fabrica;

    public TempoRealApiTests()
    {
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Pin:Pepper", Pepper)
            .ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IRepositorioUtilizadores>(new RepositorioUtilizadoresMemoria(
                [(new UtilizadorApp("chefe", "Chefe de Turno", Perfil.Supervisor, true, false, Tecno: 100), Hasher.Hash(PinChefe))])))));
    }

    public void Dispose() => _fabrica.Dispose();

    private HubConnection Ligacao(string? token) => new HubConnectionBuilder()
        .WithUrl(new Uri(_fabrica.Server.BaseAddress, AvisosHub.Rota), o =>
        {
            o.HttpMessageHandlerFactory = _ => _fabrica.Server.CreateHandler();
            o.Transports = HttpTransportType.LongPolling;
            o.AccessTokenProvider = () => Task.FromResult(token);
        }).Build();

    [Fact]
    public async Task Escrita_nas_fichas_avisa_quem_esta_ligado()
    {
        var c = _fabrica.CreateClient();
        var login = await (await c.PostAsJsonAsync("/auth/login", new PedidoLogin(PinChefe))).Content.ReadFromJsonAsync<RespostaLogin>();
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login!.Token);

        await using var ligacao = Ligacao(login.Token);
        var recebido = new TaskCompletionSource();
        ligacao.On(AvisosHub.Evento, () => recebido.TrySetResult());
        await ligacao.StartAsync();

        await c.PostAsJsonAsync("/intervencoes/F1/cancelar", new PedidoCancelamento("Teste do tempo real"));

        Assert.Same(recebido.Task, await Task.WhenAny(recebido.Task, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task Sem_token_nao_liga()
    {
        await using var ligacao = Ligacao(null);
        await Assert.ThrowsAnyAsync<Exception>(() => ligacao.StartAsync());
    }
}
