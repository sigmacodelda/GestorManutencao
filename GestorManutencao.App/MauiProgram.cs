using GestorManutencao.Armazenamento;
using GestorManutencao.UI.Offline;
using GestorManutencao.UI.Servicos;
using Microsoft.Extensions.Logging;

namespace GestorManutencao.App;

public static class MauiProgram
{
	// Cliente desta compilação (ver Temas/*.json no projeto UI).
	private const string TemaCliente = "landeiro";

	// Endereço (https) da GestorManutencao.Api deste cliente. Vazio só serve em Debug (utilizadores fictícios).
	private const string ApiUrl = "https://192.168.18.168:5055/";

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
		builder.Services.AddSingleton<IDitado, DitadoAndroid>();
#elif WINDOWS
		builder.Services.AddSingleton<IAvisoSistema, AvisoSistemaWindows>();
		builder.Services.AddSingleton<IDitado, DitadoWindows>();
#endif

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var apiUrl = ApiUrl;
		HttpMessageHandler? manipulador = null;
#if DEBUG && ANDROID
		// Telemóvel em depuração: chega à API deste computador por "adb reverse tcp:7257 tcp:7257" e aceita o certificado de desenvolvimento só para localhost.
		if (string.IsNullOrWhiteSpace(apiUrl))
		{
			apiUrl = "https://localhost:7257";
			manipulador = new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback = (pedido, _, _, erros) =>
					erros == System.Net.Security.SslPolicyErrors.None || pedido.RequestUri?.Host == "localhost"
			};
		}
#endif
		if (!string.IsNullOrWhiteSpace(apiUrl))
		{
			if (!apiUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("ApiUrl tem de ser https (o PIN não pode ir em claro).");
			var http = new HttpClient(new TempoLimitePorPedidoHandler(manipulador ?? new HttpClientHandler())) { BaseAddress = new Uri(apiUrl), Timeout = Timeout.InfiniteTimeSpan };
			builder.Services.AddSingleton<IAutenticacaoService>(new AutenticacaoApiService(http));
			builder.Services.AddSingleton<IEquipaService>(new EquipaApiService(http));
			builder.Services.AddScoped<IntervencoesApiService>(sp => new IntervencoesApiService(http, sp.GetRequiredService<SessaoService>()));
			builder.Services.AddScoped<IIntervencoesRemoto>(sp => sp.GetRequiredService<IntervencoesApiService>());

			// Offline: cópia local em SQLite na pasta privada da app. O que AddManutencaoOffline regista por cima substitui IIntervencoesService e IEquipamentosService.
			builder.Services.AddSingleton(_ => new BaseLocalSqlite(Path.Combine(FileSystem.AppDataDirectory, "manutencao-offline.db3")));
			builder.Services.AddSingleton<ICacheLocal, CacheLocalSqlite>();
			builder.Services.AddSingleton<IFilaSaida, FilaSaidaSqlite>();
			builder.Services.AddSingleton<IArmazemFotosPendentes>(_ => new ArmazemFotosPendentesFicheiro(Path.Combine(FileSystem.AppDataDirectory, "fotos-pendentes")));
			builder.Services.AddSingleton<IRedeEstado, RedeEstadoMaui>();
			builder.Services.AddSingleton<IAreaTransferencia, AreaTransferenciaMaui>();
			builder.Services.AddManutencaoOffline();
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
