using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

// Guarda as notificações em ficheiros na pasta privada da app (sem limite de tamanho, ao contrário das Preferences no Windows).
public sealed class ArmazemAvisosFicheiro : IArmazemAvisos
{
	private static string Caminho(string chave) =>
		Path.Combine(FileSystem.AppDataDirectory, Path.GetFileName(chave) + ".json");

	public async Task<string?> LerAsync(string chave)
	{
		var caminho = Caminho(chave);
		return File.Exists(caminho) ? await File.ReadAllTextAsync(caminho) : null;
	}

	public async Task GravarAsync(string chave, string valor)
	{
		var caminho = Caminho(chave);
		var temporario = caminho + ".tmp";
		await File.WriteAllTextAsync(temporario, valor);
		File.Move(temporario, caminho, overwrite: true);
	}
}
