using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

// Serviço em primeiro plano: mantém o processo vivo (e o CPU acordado) com o ecrã apagado, para o ciclo de 5 s continuar a verificar as fichas.
// Declarado à mão no AndroidManifest.xml (o tipo specialUse exige a propriedade com o motivo).
[Register("pt.sigmacode.gestormanutencao.ServicoManutencao")]
public sealed class ServicoManutencao : Service
{
	private const string Canal = "segundo-plano";
	private const int IdNotificacao = 1001;
	private PowerManager.WakeLock? _trinco;

	public override IBinder? OnBind(Intent? intent) => null;

	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		var gestor = (NotificationManager?)GetSystemService(NotificationService);
		if (OperatingSystem.IsAndroidVersionAtLeast(26) && gestor?.GetNotificationChannel(Canal) is null)
			gestor?.CreateNotificationChannel(new NotificationChannel(Canal, "A receber avisos", NotificationImportance.Min)
			{
				Description = "Mantém a app a verificar fichas com o ecrã apagado"
			});

		var abrir = PackageManager?.GetLaunchIntentForPackage(PackageName ?? "");
		var toque = abrir is null ? null : PendingIntent.GetActivity(this, 0, abrir, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
		var notificacao = new NotificationCompat.Builder(this, Canal)
			.SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)!
			.SetContentTitle("Gestor de Manutenção")!
			.SetContentText("A receber avisos")!
			.SetOngoing(true)!
			.SetPriority(NotificationCompat.PriorityMin)!
			.SetContentIntent(toque)!
			.Build();

		if (OperatingSystem.IsAndroidVersionAtLeast(34))
			StartForeground(IdNotificacao, notificacao, ForegroundService.TypeSpecialUse);
		else
			StartForeground(IdNotificacao, notificacao);

		if (_trinco is null && GetSystemService(PowerService) is PowerManager energia)
		{
			_trinco = energia.NewWakeLock(WakeLockFlags.Partial, "GestorManutencao:avisos");
			_trinco?.Acquire();
		}
		// Se o sistema matar o processo, só volta quando o utilizador abrir a app (a sessão é que arranca o ciclo).
		return StartCommandResult.NotSticky;
	}

	public override void OnDestroy()
	{
		if (_trinco?.IsHeld == true) _trinco.Release();
		_trinco = null;
		base.OnDestroy();
	}
}

public sealed class ServicoSegundoPlanoAndroid : IServicoSegundoPlano
{
	private const string ChaveBateria = "bateria-pedida";

	public Task IniciarAsync()
	{
		if (Android.App.Application.Context is not { } contexto) return Task.CompletedTask;
		ContextCompat.StartForegroundService(contexto, new Intent(contexto, typeof(ServicoManutencao)));
		PedirSemRestricaoDeBateria(contexto);
		return Task.CompletedTask;
	}

	public void Parar()
	{
		if (Android.App.Application.Context is not { } contexto) return;
		contexto.StopService(new Intent(contexto, typeof(ServicoManutencao)));
	}

	// Sem isto o Doze atrasa a rede com o ecrã apagado. Pede-se uma só vez; se o utilizador recusar, não se insiste.
	private static void PedirSemRestricaoDeBateria(Context contexto)
	{
		if (Preferences.Get(ChaveBateria, false)) return;
		if (contexto.GetSystemService(Context.PowerService) is not PowerManager energia) return;
		if (energia.IsIgnoringBatteryOptimizations(contexto.PackageName)) return;
		Preferences.Set(ChaveBateria, true);
		var pedido = new Intent(Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations)
			.SetData(Android.Net.Uri.Parse("package:" + contexto.PackageName))
			.AddFlags(ActivityFlags.NewTask);
		contexto.StartActivity(pedido);
	}
}
