using Android.App;
using Android.Content;
using AndroidX.Core.App;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

// Notificação fixa da intervenção a contar tempo: cronómetro do sistema, botão Pausar/Retomar e toque que abre a ficha.
public sealed class AvisoEmCursoAndroid : IAvisoEmCurso
{
	private const string Canal = "em-curso";
	private const int IdNotificacao = 1002;

	public void Mostrar(AvisoEmCurso aviso)
	{
		if (Android.App.Application.Context is not { } contexto) return;
		if (contexto.GetSystemService(Context.NotificationService) is not NotificationManager gestor) return;
		if (NotificationManagerCompat.From(contexto)?.AreNotificationsEnabled() != true) return;

		if (OperatingSystem.IsAndroidVersionAtLeast(26) && gestor.GetNotificationChannel(Canal) is null)
			gestor.CreateNotificationChannel(new NotificationChannel(Canal, "Intervenção em curso", NotificationImportance.Low)
			{
				Description = "Cronómetro da intervenção a decorrer, com Pausar e Retomar"
			});

		var abrir = new Intent(contexto, typeof(MainActivity))
			.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop)
			.PutExtra(MainActivity.ExtraTipo, TipoAviso.Atribuida.ToString())
			.PutExtra(MainActivity.ExtraFicha, aviso.FichaId);
		var toque = PendingIntent.GetActivity(contexto, IdNotificacao, abrir, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

		var pausa = new Intent(contexto, typeof(AcaoEmCursoReceiver)).SetAction(AcaoEmCursoReceiver.AcaoPausa)
			.SetPackage(contexto.PackageName).PutExtra(MainActivity.ExtraFicha, aviso.FichaId);
		var botao = PendingIntent.GetBroadcast(contexto, IdNotificacao, pausa, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

		var construtor = new NotificationCompat.Builder(contexto, Canal)
			.SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)!
			.SetContentTitle(aviso.Titulo)!
			.SetOngoing(true)!
			.SetOnlyAlertOnce(true)!
			.SetPriority(NotificationCompat.PriorityLow)!
			.SetContentIntent(toque)!
			.AddAction(0, aviso.EmPausa ? "Retomar" : "Pausar", botao)!;

		if (aviso.EmPausa)
		{
			// Em pausa o cronómetro pára: mostra o tempo já decorrido como texto.
			var t = TimeSpan.FromMilliseconds(aviso.DecorridoMs);
			construtor.SetContentText($"Em pausa · {(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}");
			construtor.SetUsesChronometer(false);
		}
		else
		{
			construtor.SetContentText("Em curso");
			construtor.SetShowWhen(true);
			construtor.SetWhen(aviso.InicioMs);
			construtor.SetUsesChronometer(true);
		}

		gestor.Notify(IdNotificacao, construtor.Build());
	}

	public void Esconder()
	{
		if (Android.App.Application.Context is not { } contexto) return;
		(contexto.GetSystemService(Context.NotificationService) as NotificationManager)?.Cancel(IdNotificacao);
	}
}

// Botão Pausar/Retomar: passa o pedido à UI (o NotificacaoService trata, com a sessão e a fila offline normais).
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class AcaoEmCursoReceiver : BroadcastReceiver
{
	public const string AcaoPausa = "pt.sigmacode.gestormanutencao.PAUSA";

	public override void OnReceive(Context? context, Intent? intent)
	{
		if (intent?.Action != AcaoPausa || intent.GetStringExtra(MainActivity.ExtraFicha) is not { Length: > 0 } ficha) return;
		IPlatformApplication.Current?.Services.GetService<CicloVidaApp>()?.PedirPausa(ficha);
	}
}
