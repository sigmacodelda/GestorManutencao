using Microsoft.AspNetCore.SignalR.Client;

namespace GestorManutencao.UI.Servicos;

// Canal em tempo real com a API: a API diz "as fichas mudaram" no instante em que alguém grava e a app reage logo.
public interface ITempoReal
{
    bool Ligado { get; }
    // Chamado em cada mudança, e também ao (re)ligar, para apanhar o que se perdeu sem ligação.
    event Action? FichasMudaram;
    Task IniciarAsync(Func<string?> token);
    Task PararAsync();
}

public sealed class TempoRealNenhum : ITempoReal
{
    public bool Ligado => false;
    public event Action? FichasMudaram { add { } remove { } }
    public Task IniciarAsync(Func<string?> token) => Task.CompletedTask;
    public Task PararAsync() => Task.CompletedTask;
}

// Manipulador próprio só para testes/depuração (certificado de desenvolvimento): nesse caso usa long polling, que passa por ele.
public sealed record EnderecoTempoReal(Uri Url, Func<HttpMessageHandler>? Manipulador = null);

public sealed class TempoRealSignalR(EnderecoTempoReal endereco) : ITempoReal, IAsyncDisposable
{
    private HubConnection? _ligacao;

    public bool Ligado => _ligacao?.State == HubConnectionState.Connected;
    public event Action? FichasMudaram;

    public async Task IniciarAsync(Func<string?> token)
    {
        if (_ligacao is not null) return;
        var ligacao = new HubConnectionBuilder()
            .WithUrl(endereco.Url, o =>
            {
                o.AccessTokenProvider = () => Task.FromResult(token());
                if (endereco.Manipulador is { } fabrica)
                {
                    o.HttpMessageHandlerFactory = _ => fabrica();
                    o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                }
            })
            .WithAutomaticReconnect(new TentarSempre())
            .Build();
        ligacao.On("fichasMudaram", () => FichasMudaram?.Invoke());
        ligacao.Reconnected += _ => { FichasMudaram?.Invoke(); return Task.CompletedTask; };
        _ligacao = ligacao;
        // Sem rede ao arrancar não é erro: volta a tentar enquanto a sessão durar.
        _ = LigarComRepeticaoAsync(ligacao);
    }

    private async Task LigarComRepeticaoAsync(HubConnection ligacao)
    {
        while (ReferenceEquals(_ligacao, ligacao))
        {
            try
            {
                await ligacao.StartAsync();
                FichasMudaram?.Invoke();
                return;
            }
            catch (Exception)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(10)); } catch (Exception) { return; }
            }
        }
    }

    public async Task PararAsync()
    {
        var ligacao = _ligacao;
        _ligacao = null;
        if (ligacao is null) return;
        try { await ligacao.StopAsync(); } catch (Exception) { }
        await ligacao.DisposeAsync();
    }

    public ValueTask DisposeAsync() => new(PararAsync());

    // Tenta de novo para sempre (0, 1, 2, 5, 10 s e depois de 15 em 15 s): um telemóvel sem rede volta sozinho.
    private sealed class TentarSempre : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext c) => c.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero, 1 => TimeSpan.FromSeconds(1), 2 => TimeSpan.FromSeconds(2), 3 => TimeSpan.FromSeconds(5), 4 => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(15)
        };
    }
}
