using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

// SingleTop: tocar numa notificação com a app já aberta reutiliza esta janela (OnNewIntent) em vez de criar outra.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	public const string ExtraTipo = "aviso_tipo";
	public const string ExtraFicha = "aviso_ficha";

	// Sem estado guardado: o Android recria a janela e os fragmentos antigos (jumpToStart) já não têm vista, o que rebentava ao reabrir a app.
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(null);
		LerAviso(Intent);
	}

	protected override void OnNewIntent(Intent? intent)
	{
		base.OnNewIntent(intent);
		LerAviso(intent);
	}

	// Toque numa notificação: passa o destino à UI, que navega assim que a sessão estiver pronta.
	private static void LerAviso(Intent? intent)
	{
		if (intent?.GetStringExtra(ExtraTipo) is not { } tipo || !Enum.TryParse<TipoAviso>(tipo, out var t)) return;
		var ciclo = IPlatformApplication.Current?.Services.GetService<CicloVidaApp>();
		ciclo?.PedirAbrir(t, intent.GetStringExtra(ExtraFicha));
		// Evita repetir a navegação se a atividade for recriada com o mesmo intent.
		intent.RemoveExtra(ExtraTipo);
	}
}
