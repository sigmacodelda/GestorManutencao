using Android.Content.PM;
using GestorManutencao.UI.Servicos;

namespace GestorManutencao.App;

// Coletores (Zebra, Honeywell, Urovo...) trazem um serviço de leitura que escreve no campo focado.
public sealed class DetetorLeitorAndroid : IDetetorLeitor
{
	private static readonly string[] Servicos = ["com.symbol.datawedge", "com.honeywell.decode.service"];

	private readonly Lazy<(bool Parece, string Descricao)> _visto = new(Ver);

	public bool TemCamara => true;
	public bool PareceLeitorIndustrial => _visto.Value.Parece;
	public string Descricao => _visto.Value.Descricao;

	private static (bool, string) Ver()
	{
		var fabricante = Android.OS.Build.Manufacturer ?? "";
		var modelo = Android.OS.Build.Model ?? "";
		var pacotes = new List<string>();
		// Só os pacotes declarados em <queries> no manifesto são visíveis; um pacote invisível ou ausente dá NameNotFoundException.
		var gestor = Android.App.Application.Context.PackageManager;
		foreach (var nome in Servicos)
		{
			try
			{
				if (gestor?.GetPackageInfo(nome, (PackageInfoFlags)0) is not null) pacotes.Add(nome);
			}
			catch (PackageManager.NameNotFoundException) { }
			catch (Exception) { /* sem acesso: fica só pelo fabricante e modelo */ }
		}

		var descricao = $"{fabricante} {modelo}".Trim();
		if (pacotes.Count > 0) descricao += $" (serviço de leitura: {string.Join(", ", pacotes)})";
		return (LogicaLeitura.PareceLeitorIndustrial(fabricante, modelo, pacotes), descricao.Length > 0 ? descricao : "Aparelho desconhecido");
	}
}
