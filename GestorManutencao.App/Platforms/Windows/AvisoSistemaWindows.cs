using GestorManutencao.UI.Servicos;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace GestorManutencao.App;

// Notificações do Windows App SDK. A app corre sem pacote (WindowsPackageType None) e aí o registo pode falhar:
// nesse caso devolve false e a app mostra a faixa por dentro. O toque abre a página do aviso (como no Android).
public sealed class AvisoSistemaWindows : IAvisoSistema
{
	private const string ArgTipo = "tipo", ArgFicha = "ficha";
	private readonly CicloVidaApp _ciclo;
	private bool _registado, _indisponivel;

	public AvisoSistemaWindows(CicloVidaApp ciclo)
	{
		_ciclo = ciclo;
		// O registo e o ouvinte têm de existir logo no arranque: o toque pode ter acordado a app (arranque a frio) ou chegar com ela aberta.
		try
		{
			AppNotificationManager.Default.NotificationInvoked += (_, e) => AoTocar(e.Arguments);
			AppNotificationManager.Default.Register();
			_registado = true;
			if (AppInstance.GetCurrent().GetActivatedEventArgs() is { Kind: ExtendedActivationKind.AppNotification, Data: AppNotificationActivatedEventArgs a })
				AoTocar(a.Arguments);
		}
		catch (Exception) { /* sem registo as notificações ficam só como faixa dentro da app */ }
	}

	public Task PedirPermissaoAsync() => Task.CompletedTask;

	public Task<bool> MostrarAsync(Aviso aviso)
	{
		if (_indisponivel) return Task.FromResult(false);
		try
		{
			if (!_registado)
			{
				AppNotificationManager.Default.Register();
				_registado = true;
			}
			var construtor = new AppNotificationBuilder().AddText(aviso.Titulo).AddText(aviso.Texto).AddArgument(ArgTipo, aviso.Tipo.ToString());
			if (aviso.FichaId is { Length: > 0 } ficha) construtor.AddArgument(ArgFicha, ficha);
			var notificacao = construtor.BuildNotification();
			AppNotificationManager.Default.Show(notificacao);
			// O Windows só atribui um identificador se aceitou a notificação.
			return Task.FromResult(notificacao.Id != 0);
		}
		catch (Exception)
		{
			_indisponivel = true;
			return Task.FromResult(false);
		}
	}

	private void AoTocar(IDictionary<string, string> argumentos)
	{
		if (!argumentos.TryGetValue(ArgTipo, out var tipo) || !Enum.TryParse<TipoAviso>(tipo, out var t)) return;
		argumentos.TryGetValue(ArgFicha, out var ficha);
		MainThread.BeginInvokeOnMainThread(() =>
		{
			_ciclo.PedirAbrir(t, ficha);
			TrazerJanelaParaAFrente();
		});
	}

	private static void TrazerJanelaParaAFrente()
	{
		try
		{
			if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not MauiWinUIWindow janela) return;
			if (janela.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } p) p.Restore();
			janela.Activate();
		}
		catch (Exception) { /* a navegação já foi pedida; só falhou trazer a janela à frente */ }
	}
}
