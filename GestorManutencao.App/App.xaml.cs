namespace GestorManutencao.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var janela = new Window(new MainPage()) { Title = "Gestor de Manutenção" };
#if WINDOWS
		janela.Width = 1100;
		janela.Height = 760;
		janela.MinimumWidth = 420;
		janela.MinimumHeight = 560;
#endif
		return janela;
	}
}
