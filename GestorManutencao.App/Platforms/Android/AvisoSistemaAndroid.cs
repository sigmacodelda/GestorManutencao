using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

// Avisos locais com o NotificationManager do Android; o toque abre a app.
public sealed class AvisoSistemaAndroid : IAvisoSistema
{
	private const string Canal = "manutencao";

	public async Task PedirPermissaoAsync()
	{
		// Só o Android 13 ou superior pede a permissão em execução.
		if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu) return;
		var estado = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
		if (estado != PermissionStatus.Granted)
			await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.PostNotifications>());
	}

	public Task<bool> MostrarAsync(Aviso aviso)
	{
		if (Android.App.Application.Context is not { } contexto) return Task.FromResult(false);
		if (contexto.GetSystemService(Context.NotificationService) is not NotificationManager gestor) return Task.FromResult(false);
		if (NotificationManagerCompat.From(contexto)?.AreNotificationsEnabled() != true) return Task.FromResult(false);

		if (OperatingSystem.IsAndroidVersionAtLeast(26) && gestor.GetNotificationChannel(Canal) is null)
			gestor.CreateNotificationChannel(new NotificationChannel(Canal, "Manutenção", NotificationImportance.High)
			{
				Description = "Avisos sobre intervenções e fichas"
			});

		// O identificador é estável por aviso: o mesmo aviso nunca fica duplicado na barra.
		var id = Convert.ToInt32(CentroAvisos.Hash(aviso.Chave), 16) & 0x7fffffff;

		// O toque abre a MainActivity já com o destino (tipo e ficha). O pedido tem de ser único por aviso, senão o Android troca os extras dos outros.
		var abrir = new Intent(contexto, typeof(MainActivity))
			.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop)
			.PutExtra(MainActivity.ExtraTipo, aviso.Tipo.ToString())
			.PutExtra(MainActivity.ExtraFicha, aviso.FichaId);
		var toque = PendingIntent.GetActivity(contexto, id, abrir, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

		var notificacao = new NotificationCompat.Builder(contexto, Canal)
			.SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)!
			.SetContentTitle(aviso.Titulo)!
			.SetContentText(aviso.Texto)!
			.SetStyle(new NotificationCompat.BigTextStyle().BigText(aviso.Texto))!
			.SetAutoCancel(true)!
			.SetPriority(NotificationCompat.PriorityHigh)!
			.SetContentIntent(toque)!
			.Build();

		gestor.Notify(id, notificacao);
		return Task.FromResult(true);
	}
}
