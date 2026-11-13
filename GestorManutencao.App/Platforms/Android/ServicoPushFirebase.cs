using Android.App;
using Android.Gms.Extensions;
using Firebase.Messaging;
using GestorManutencao.UI.Servicos;

// O binding marca estes membros como obsoletos por engano ("deprecated" sem alternativa); são os que o FCM chama.
#pragma warning disable CS0618, CS0672
namespace GestorManutencao.App;

// Recebe o push de dados do servidor ({ "t": "fichasMudaram" }): só acorda a app, que verifica as fichas e mostra ela própria os avisos locais.
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public sealed class ServicoPushFirebase : FirebaseMessagingService
{
	public override void OnNewToken(string token)
	{
		base.OnNewToken(token);
		try
		{
			if (IPlatformApplication.Current?.Services is not { } servicos) return;
			Executar(ct => PushFichas.RegistarTokenAsync(servicos, token, ct));
		}
		catch (Exception) { /* o token volta a ser registado no próximo início de sessão */ }
	}

	public override void OnMessageReceived(RemoteMessage message)
	{
		base.OnMessageReceived(message);
		try
		{
			if (message.Data is not { } dados || !dados.TryGetValue("t", out var tipo) || tipo != "fichasMudaram") return;
			if (IPlatformApplication.Current?.Services is not { } servicos) return;
			Executar(ct => PushFichas.VerificarAsync(servicos, ct));
		}
		catch (Exception) { /* sem rede ou sem sessão não há nada a fazer; o ciclo normal apanha-o mais tarde */ }
	}

	// O serviço do FCM só tem alguns segundos: bloqueia até acabar, mas nunca além do limite.
	private static void Executar(Func<CancellationToken, Task> trabalho)
	{
		using var cts = new CancellationTokenSource(PushFichas.Limite);
		try { Task.Run(() => trabalho(cts.Token)).Wait(PushFichas.Limite + TimeSpan.FromSeconds(1)); }
		catch (Exception) { }
	}
}

public sealed class PushTokenAndroid : IPushToken
{
	public async Task<string?> ObterAsync(CancellationToken ct = default)
	{
		try
		{
			var token = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>(); // sigma-ok: variável, não é um valor
			return token?.ToString();
		}
		catch (Exception) { return null; }
	}
}
