using GestorManutencao.UI.Servicos;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace GestorManutencao.App;

// Notificações do Windows App SDK. A app corre sem pacote (WindowsPackageType None) e aí o registo pode falhar:
// nesse caso devolve false e a app mostra a faixa por dentro.
public sealed class AvisoSistemaWindows : IAvisoSistema
{
	private bool _registado, _indisponivel;

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
			var notificacao = new AppNotificationBuilder().AddText(aviso.Titulo).AddText(aviso.Texto).BuildNotification();
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
}
