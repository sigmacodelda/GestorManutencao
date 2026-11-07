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
		builder.Services.AddSingleton<IArmazemAvisos, ArmazemAvisosFicheiro>();
#if ANDROID
		builder.Services.AddSingleton<IAvisoSistema, AvisoSistemaAndroid>();
#elif WINDOWS
		builder.Services.AddSingleton<IAvisoSistema, AvisoSistemaWindows>();
#endif

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		if (!string.IsNullOrWhiteSpace(ApiUrl))
		{
			if (!ApiUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("ApiUrl tem de ser https (o PIN não pode ir em claro).");
			var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(15) };
			builder.Services.AddSingleton<IAutenticacaoService>(new AutenticacaoApiService(http));
			builder.Services.AddSingleton<IEquipaService>(new EquipaApiService(http));
			builder.Services.AddScoped<IntervencoesApiService>(sp => new IntervencoesApiService(http, sp.GetRequiredService<SessaoService>()));
			builder.Services.AddScoped<IIntervencoesService>(sp => sp.GetRequiredService<IntervencoesApiService>());
			builder.Services.AddScoped<IEquipamentosService>(sp => sp.GetRequiredService<IntervencoesApiService>());
		}
		else
		{
#if DEBUG
			builder.Services.AddSingleton<IAutenticacaoService, DevAutenticacaoService>();
			builder.Services.AddSingleton<IEquipaService, DevEquipaService>();
			builder.Services.AddSingleton<DevIntervencoesService>();
			builder.Services.AddSingleton<IIntervencoesService>(sp => sp.GetRequiredService<DevIntervencoesService>());
			builder.Services.AddSingleton<IEquipamentosService>(sp => sp.GetRequiredService<DevIntervencoesService>());
#else
			throw new InvalidOperationException("ApiUrl por configurar.");
#endif
		}

		return builder.Build();
	}
}
