using System.Text.Json;
using GestorManutencao.UI.Modelos;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

// Guarda a sessão no armazenamento seguro do sistema (Keystore no Android, DPAPI no Windows).
public sealed class ArmazemSessaoSeguro : IArmazemSessao
{
	private const string Chave = "sessao";

	public async Task<Utilizador?> LerAsync()
	{
		try
		{
			var json = await SecureStorage.Default.GetAsync(Chave);
			return json is null ? null : JsonSerializer.Deserialize<Utilizador>(json);
		}
		catch (Exception)
		{
			SecureStorage.Default.Remove(Chave);
			return null;
		}
	}

	public Task GuardarAsync(Utilizador utilizador) =>
		SecureStorage.Default.SetAsync(Chave, JsonSerializer.Serialize(utilizador));

	public Task LimparAsync()
	{
		SecureStorage.Default.Remove(Chave);
		return Task.CompletedTask;
	}
}
