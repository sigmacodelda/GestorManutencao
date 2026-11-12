using System.Net;
using System.Net.Sockets;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.UI.Offline;

// Falha ao ler ou gravar um ficheiro do próprio telemóvel (não é falha de rede, apesar de ser IOException).
internal sealed class ErroLocalException(Exception interna) : Exception("Erro no armazenamento local.", interna);

internal static class ClassificacaoErros
{
    public const string FotoJaNaoExiste = "A foto já não está guardada no telemóvel.";
    public const string OperacaoInvalida = "Operação inválida.";
    public const string FotoDemasiadoGrande = "A foto é demasiado grande. Tente outra vez.";
    public const string NaoAceitou = "O servidor não aceitou os dados enviados.";
    public const string AvisoSemEnvio = "Não foi possível enviar. O texto que escreveu está guardado aqui.";

    // Só texto em português escrito por nós; as mensagens das exceções do .NET (inglês) nunca chegam ao técnico.
    private static readonly HashSet<string> MensagensProprias =
    [
        MensagensIntervencao.Mudou, MensagensIntervencao.NaoPausa, MensagensIntervencao.NaoRetoma, FotoJaNaoExiste, OperacaoInvalida, FotoDemasiadoGrande, "A foto é demasiado grande.",
        .. new[] { "fora_de_ordem", "externa_sem_documento", "propria", "limite_fotos" }.Select(MensagensIntervencao.Para)
    ];

    // Sem resposta do servidor: sem rede, timeout ou ligação cortada. 401, 403 e 5xx não são "offline".
    public static bool FalhaDeRede(Exception e, CancellationToken ct = default) =>
        !ct.IsCancellationRequested && e switch
        {
            ErroLocalException => false,
            HttpRequestException h => h.StatusCode is null,
            TaskCanceledException => true,
            SocketException => true,
            IOException => true,
            _ => false
        };

    // 401: a sessão deixou de valer; pára tudo e retoma depois de entrar outra vez.
    public static bool SemSessao(Exception e) => e is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized };

    // O servidor percebeu o pedido e recusou-o: repetir não adianta.
    public static bool Recusa(Exception e) =>
        e is InvalidOperationException and not ObjectDisposedException
        || e is HttpRequestException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnprocessableEntity };

    public static string Motivo(Exception e) => e switch
    {
        InvalidOperationException i when MensagensProprias.Contains(i.Message) => i.Message,
        HttpRequestException { StatusCode: HttpStatusCode.RequestEntityTooLarge } => "A foto é demasiado grande.",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => MensagensIntervencao.Mudou,
        _ => NaoAceitou
    };

    public static bool IdLocal(string id) => id.StartsWith("L-", StringComparison.Ordinal);

    public static string NovoIdLocal() => "L-" + Guid.NewGuid().ToString("N")[..22];

    public static string NovoOpId() => Guid.NewGuid().ToString("N")[..22];
}
