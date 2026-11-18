using System.Threading.Channels;

namespace GestorManutencao.Api;

public interface IEnviadorPush
{
    // Envia só o sinal (sem texto nem dados pessoais): a app verifica sozinha o que avisar.
    Task AcordarAsync(IReadOnlyCollection<string> tokens, CancellationToken ct);
}

public interface INotificadorPush
{
    // Não espera nem falha: só entrega o pedido ao serviço em segundo plano.
    void Notificar(string? autor);
}

public sealed class EnviadorPushNulo(ILogger<EnviadorPushNulo> log) : IEnviadorPush
{
    private int _avisado;

    public Task AcordarAsync(IReadOnlyCollection<string> tokens, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _avisado, 1) == 0) log.LogWarning("Push:CaminhoChaveFirebase não está configurado: os avisos push estão desligados.");
        return Task.CompletedTask;
    }
}

// Junta as escritas que chegam seguidas (janela a contar da primeira) num só envio e exclui o aparelho de quem escreveu.
public sealed class NotificadorPush : BackgroundService, INotificadorPush
{
    public static readonly TimeSpan AtrasoOmissao = TimeSpan.FromSeconds(3);

    private readonly Channel<string?> _fila = Channel.CreateUnbounded<string?>(new UnboundedChannelOptions { SingleReader = true });
    private readonly IRepositorioDispositivos _dispositivos;
    private readonly IEnviadorPush _enviador;
    private readonly ILogger<NotificadorPush> _log;
    private readonly TimeSpan _atraso;

    public NotificadorPush(IRepositorioDispositivos dispositivos, IEnviadorPush enviador, ILogger<NotificadorPush> log, TimeSpan? atraso = null)
    {
        _dispositivos = dispositivos;
        _enviador = enviador;
        _log = log;
        _atraso = atraso ?? AtrasoOmissao;
    }

    public void Notificar(string? autor) => _fila.Writer.TryWrite(autor);

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        try
        {
            while (await _fila.Reader.WaitToReadAsync(parar))
            {
                var autores = new HashSet<string?>();
                await Task.Delay(_atraso, parar);
                while (_fila.Reader.TryRead(out var autor)) autores.Add(autor);
                if (autores.Count == 0) continue;

                try
                {
                    // Com vários autores no lote, todos têm de ser acordados (cada um não escreveu as dos outros).
                    var excluir = autores.Count == 1 ? autores.First() : null;
                    var tokens = _dispositivos.Tokens(excluir);
                    if (tokens.Count > 0) await _enviador.AcordarAsync(tokens, parar);
                }
                // Um timeout HTTP também é TaskCanceledException: só o encerramento real pára o ciclo.
                catch (Exception e) when (!parar.IsCancellationRequested)
                {
                    _log.LogWarning("Falha ao enviar o push ({Tipo}).", e.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (parar.IsCancellationRequested) { /* encerramento */ }
    }
}
