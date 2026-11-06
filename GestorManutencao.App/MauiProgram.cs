using GestorManutencao.UI.Servicos;
using Microsoft.Extensions.Logging;

namespace GestorManutencao.App;

public static class MauiProgram
{
	// Cliente desta compilação (ver Temas/*.json no projeto UI).
	private const string TemaCliente = "landeiro";

	// Endereço (https) da GestorManutencao.Api deste cliente. Vazio só serve em Debug (utilizadores fictícios).
	private const string ApiUrl = "";

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>();

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddManutencaoUI(TemaCliente);
		builder.Services.AddSingleton<IArmazemSessao, ArmazemSessaoSeguro>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		if (!string.IsNullOrWhiteSpace(ApiUrl))
		{
			if (!ApiUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("ApiUrl tem de ser https (a password não pode ir em claro).");
			var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(15) };
			builder.Services.AddSingleton<IAutenticacaoService>(new AutenticacaoApiService(http));
			// Ligação das intervenções ao PHC por fazer (tabelas por confirmar).
			builder.Services.AddSingleton<IIntervencoesService, IntervencoesIndisponiveis>();
		}
		else
		{
#if DEBUG
			builder.Services.AddSingleton<IAutenticacaoService, DevAutenticacaoService>();
			builder.Services.AddSingleton<IIntervencoesService, DevIntervencoesService>();
#else
			throw new InvalidOperationException("ApiUrl por configurar.");
#endif
		}

		return builder.Build();
	}
}
