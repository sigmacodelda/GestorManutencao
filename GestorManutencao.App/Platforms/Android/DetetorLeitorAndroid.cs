using Android.Content.PM;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

// Coletores (Zebra, Honeywell, Urovo...) trazem um serviço de leitura que escreve no campo focado.
public sealed class DetetorLeitorAndroid : IDetetorLeitor
{
	private readonly Lazy<(bool Parece, string Descricao)> _visto = new(Ver);

	public bool PareceLeitorIndustrial => _visto.Value.Parece;
	public string Descricao => _visto.Value.Descricao;

	private static (bool, string) Ver()
	{
		var fabricante = Android.OS.Build.Manufacturer ?? "";
		var modelo = Android.OS.Build.Model ?? "";
		var pacotes = new List<string>();
		try
		{
			// Só os pacotes da lista são visíveis (<queries> no manifesto); com versões antigas do Android vê-se tudo.
			var instalados = Android.App.Application.Context.PackageManager?.GetInstalledPackages((PackageInfoFlags)0);
			foreach (var p in instalados ?? [])
				if (p.PackageName is { } nome && (nome is "com.symbol.datawedge" or "com.honeywell.decode.service" || nome.StartsWith("com.urovo.", StringComparison.Ordinal)))
					pacotes.Add(nome);
		}
		catch (Exception) { /* sem acesso à lista: fica só pelo fabricante e modelo */ }

		var descricao = $"{fabricante} {modelo}".Trim();
		if (pacotes.Count > 0) descricao += $" (serviço de leitura: {string.Join(", ", pacotes)})";
		return (LogicaLeitura.PareceLeitorIndustrial(fabricante, modelo, pacotes), descricao.Length > 0 ? descricao : "Aparelho desconhecido");
	}
}
