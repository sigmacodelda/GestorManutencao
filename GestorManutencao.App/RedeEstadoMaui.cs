using GestorManutencao.UI.Offline;

namespace GestorManutencao.App;

// Estado da rede do sistema. "Internet" ou "Local" contam como ligado: se o servidor não responder, o pedido falha e a app trata como sem rede.
public sealed class RedeEstadoMaui : IRedeEstado
{
	public RedeEstadoMaui()
	{
		Disponivel = Ligado(Connectivity.Current.NetworkAccess);
		Connectivity.Current.ConnectivityChanged += AoMudar;
	}

	public bool Disponivel { get; private set; }

	public event Action<bool>? Alterada;

	private static bool Ligado(NetworkAccess acesso) => acesso is NetworkAccess.Internet or NetworkAccess.Local or NetworkAccess.ConstrainedInternet;

	private void AoMudar(object? origem, ConnectivityChangedEventArgs e)
	{
		var ligado = Ligado(e.NetworkAccess);
		if (ligado == Disponivel) return;
		Disponivel = ligado;
		Alterada?.Invoke(ligado);
	}
}

public sealed class AreaTransferenciaMaui : IAreaTransferencia
{
	public async Task<bool> CopiarAsync(string texto)
	{
		try
		{
			await Clipboard.Default.SetTextAsync(texto);
			return true;
		}
		catch (Exception) { return false; }
	}
}
