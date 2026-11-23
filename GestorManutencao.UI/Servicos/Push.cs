using Microsoft.Extensions.DependencyInjection;

namespace GestorManutencao.UI.Servicos;

// Token de push do aparelho (FCM no Android). Nulo quando não há push (Windows) ou o token ainda não está disponível.
public interface IPushToken
{
    Task<string?> ObterAsync(CancellationToken ct = default);
}

public sealed class PushTokenNenhum : IPushToken
{
    public Task<string?> ObterAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
}

// Registo do aparelho na API para receber o push. O JWT vai explícito porque ao terminar a sessão já não está na SessaoService.
public interface IDispositivosRemoto
{
    Task RegistarAsync(string jwt, string token, CancellationToken ct = default);
    Task DesregistarAsync(string jwt, string token, CancellationToken ct = default);
}

// Trabalho feito quando chega um push com a app em segundo plano ou morta (sem janela, sem a UI a correr).
public static class PushFichas
{
    public static readonly TimeSpan Limite = TimeSpan.FromSeconds(9);

    // Com a app viva, quem verifica é o NotificacaoService da sessão; sem ele, abre-se uma sessão própria a partir do armazém seguro.
    public static async Task VerificarAsync(IServiceProvider servicos, CancellationToken ct)
    {
        if (servicos.GetService<CicloVidaApp>()?.Verificador is { } verificador)
        {
            await verificador(ct);
            return;
        }
        using var ambito = servicos.CreateScope();
        var sessao = ambito.ServiceProvider.GetRequiredService<SessaoService>();
        await sessao.RestaurarAsync();
        if (!sessao.TemSessao || sessao.Utilizador?.PinProvisorio == true) return;
        // Só depois de restaurar a sessão, para o serviço não arrancar o ciclo normal (SignalR e serviço em primeiro plano).
        await ambito.ServiceProvider.GetRequiredService<NotificacaoService>().VerificarAsync(ct);
    }

    // Token novo do FCM: volta a registar se houver sessão.
    public static async Task RegistarTokenAsync(IServiceProvider servicos, string token, CancellationToken ct)
    {
        using var ambito = servicos.CreateScope();
        var sessao = ambito.ServiceProvider.GetRequiredService<SessaoService>();
        await sessao.RestaurarAsync();
        if (sessao.Utilizador is not { Token: { Length: > 0 } jwt }) return;
        if (ambito.ServiceProvider.GetService<IDispositivosRemoto>() is not { } dispositivos) return;
        await dispositivos.RegistarAsync(jwt, token, ct);
    }
}
