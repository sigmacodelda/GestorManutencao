#if ANDROID
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

public sealed class LeitorCamaraMaui : ILeitorCamara
{
	public async Task<string?> LerAsync(CancellationToken ct = default)
	{
		var pagina = await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			var estado = await Permissions.CheckStatusAsync<Permissions.Camera>();
			if (estado != PermissionStatus.Granted) estado = await Permissions.RequestAsync<Permissions.Camera>();
			if (estado != PermissionStatus.Granted)
				throw new InvalidOperationException("Sem permissão para usar a câmara. Ative-a nas definições da app.");

			var janela = Application.Current?.Windows.FirstOrDefault()?.Page
				?? throw new InvalidOperationException("A câmara não está disponível neste momento.");
			var p = new PaginaLeituraCamara();
			await janela.Navigation.PushModalAsync(p);
			return p;
		});

		using var _ = ct.Register(() => MainThread.BeginInvokeOnMainThread(() => pagina.Fechar(null)));
		return await pagina.Resultado;
	}
}
#endif
