namespace GestorManutencao.UI.Servicos;

// Tempo limite por pedido: 15 s para tudo, 60 s para os pedidos de fotos (o envio por rede móvel demora mais).
// Usar com HttpClient.Timeout = Timeout.InfiniteTimeSpan.
public sealed class TempoLimitePorPedidoHandler(HttpMessageHandler interno, TimeSpan? normal = null, TimeSpan? fotos = null) : DelegatingHandler(interno)
{
    public static readonly TimeSpan LimiteNormal = TimeSpan.FromSeconds(15), LimiteFotos = TimeSpan.FromSeconds(60);

    private readonly TimeSpan _normal = normal ?? LimiteNormal, _fotos = fotos ?? LimiteFotos;

    public static bool PedidoDeFoto(HttpRequestMessage pedido) =>
        pedido.RequestUri?.AbsolutePath.Contains("/fotos", StringComparison.OrdinalIgnoreCase) == true;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var limite = PedidoDeFoto(request) ? _fotos : _normal;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(limite);
        // A resposta é lida depois do handler e o limite deve cobri-la: o token só é libertado depois de o tempo passar.
        _ = Task.Delay(limite + TimeSpan.FromSeconds(1), CancellationToken.None).ContinueWith(_ => cts.Dispose(), TaskScheduler.Default);
        try { return await base.SendAsync(request, cts.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException("Tempo limite do pedido.");
        }
    }
}
