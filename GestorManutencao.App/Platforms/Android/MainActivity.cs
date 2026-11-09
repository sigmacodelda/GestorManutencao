using Android.App;
using Android.Content.PM;
using Android.OS;

namespace GestorManutencao.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	// Sem estado guardado: o Android recria a janela e os fragmentos antigos (jumpToStart) já não têm vista, o que rebentava ao reabrir a app.
	protected override void OnCreate(Bundle? savedInstanceState) => base.OnCreate(null);
}
