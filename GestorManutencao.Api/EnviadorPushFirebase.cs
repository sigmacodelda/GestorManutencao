using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace GestorManutencao.Api;

// A credencial lê-se do ficheiro indicado em Push:CaminhoChaveFirebase; o conteúdo nunca passa por código nem logs.
public sealed class EnviadorPushFirebase(string caminhoChave, IRepositorioDispositivos dispositivos, ILogger<EnviadorPushFirebase> log) : IEnviadorPush
{
    private FirebaseMessaging? _mensagens;

    public async Task AcordarAsync(IReadOnlyCollection<string> tokens, CancellationToken ct)
    {
        _mensagens ??= FirebaseMessaging.GetMessaging(FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions
        {
            Credential = CredentialFactory.FromFile<ServiceAccountCredential>(caminhoChave).ToGoogleCredential()
        }));

        foreach (var lote in tokens.Chunk(500))
        {
            // Tokens está marcado como obsoleto (a favor de Fids), mas o contrato com a app são tokens FCM.
#pragma warning disable CS0618
            var mensagem = new MulticastMessage
            {
                Tokens = lote,
                Data = new Dictionary<string, string> { ["t"] = "fichasMudaram" },
                Android = new AndroidConfig { Priority = Priority.High, TimeToLive = TimeSpan.FromSeconds(60), CollapseKey = "fichas" }
            };
#pragma warning restore CS0618
            var resposta = await _mensagens.SendEachForMulticastAsync(mensagem, ct);

            var invalidos = new List<string>();
            for (var i = 0; i < resposta.Responses.Count; i++)
            {
                var codigo = resposta.Responses[i].Exception?.MessagingErrorCode;
                // InvalidArgument também vem de erros da mensagem (não só do token): só se remove o que o FCM diz que já não existe.
                if (codigo is MessagingErrorCode.Unregistered) invalidos.Add(lote[i]);
            }
            if (invalidos.Count > 0)
            {
                try { dispositivos.RemoverTokens(invalidos); }
                catch (Exception e) { log.LogWarning("Não consegui remover tokens inválidos ({Tipo}).", e.GetType().Name); }
            }
            log.LogInformation("Push enviado: {Ok} com sucesso, {Falhas} falhas, {Removidos} tokens removidos.", resposta.SuccessCount, resposta.FailureCount, invalidos.Count);
        }
    }
}
