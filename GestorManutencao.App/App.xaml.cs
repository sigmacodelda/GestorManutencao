using GestorManutencao.UI.Servicos;

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
		// Ao voltar ao primeiro plano as notificações são verificadas logo, sem esperar pelos 60 s.
		var ciclo = activationState?.Context.Services.GetService<CicloVidaApp>();
		if (ciclo is not null)
		{
			janela.Activated += (_, _) => ciclo.NotificarPrimeiroPlano();
			janela.Resumed += (_, _) => ciclo.NotificarPrimeiroPlano();
		}
#if WINDOWS
		janela.Width = 1100;
		janela.Height = 760;
		janela.MinimumWidth = 420;
		janela.MinimumHeight = 560;
#endif
		return janela;
	}
}
