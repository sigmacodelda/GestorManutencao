using GestorManutencao.Armazenamento;
using GestorManutencao.UI.Offline;
using GestorManutencao.UI.Servicos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GestorManutencao.App;

public static class MauiProgram
{
	// Cliente desta compilação (ver Temas/*.json no projeto UI).
	private const string TemaCliente = "landeiro";

	// https sempre; http só para um IP privado (10.x, 172.16-31.x, 192.168.x) ou loopback, ou seja, rede interna.
	internal static bool UrlPermitida(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
		if (uri.Scheme == Uri.UriSchemeHttps) return true;
		if (uri.Scheme != Uri.UriSchemeHttp || !System.Net.IPAddress.TryParse(uri.Host, out var ip)) return false;
		if (System.Net.IPAddress.IsLoopback(ip)) return true;
		var b = ip.GetAddressBytes();
		return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168));
	}

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>();

		// Endereço (https, ou http numa rede interna) da GestorManutencao.Api deste cliente, em Resources/Raw/appsettings.json ("ApiUrl"). Vazio só serve em Debug (utilizadores fictícios).
		using (var ficheiro = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult())
			builder.Configuration.AddJsonStream(ficheiro);

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddManutencaoUI(TemaCliente);
		builder.Services.AddSingleton<IArmazemSessao, ArmazemSessaoSeguro>();
		builder.Services.AddSingleton<IArmazemAvisos, ArmazemAvisosFicheiro>();
#if ANDROID
		builder.Services.AddSingleton<IAvisoSistema, AvisoSistemaAndroid>();
		builder.Services.AddSingleton<IDitado, DitadoAndroid>();
		builder.Services.AddSingleton<ICamara, CamaraAndroid>();
#elif WINDOWS
		builder.Services.AddSingleton<IAvisoSistema, AvisoSistemaWindows>();
		builder.Services.AddSingleton<IDitado, DitadoWindows>();
#endif

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var apiUrl = builder.Configuration["ApiUrl"];
		HttpMessageHandler? manipulador = null;
#if DEBUG && WINDOWS
		// Circuito de testes (Playwright): GESTOR_API_URL aponta à API local; só aceita o certificado de desenvolvimento para localhost.
		if (Environment.GetEnvironmentVariable("GESTOR_API_URL") is { Length: > 0 } apiTeste && Uri.TryCreate(apiTeste, UriKind.Absolute, out var uriTeste) && uriTeste.IsLoopback)
		{
			apiUrl = apiTeste;
			manipulador = new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback = (pedido, _, _, erros) =>
					erros == System.Net.Security.SslPolicyErrors.None || pedido.RequestUri?.IsLoopback == true
			};
		}
#endif
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
			if (!UrlPermitida(apiUrl))
				throw new InvalidOperationException("ApiUrl tem de ser https (http só para um IP da rede interna: o PIN não pode ir em claro pela internet).");
			var http = new HttpClient(new TempoLimitePorPedidoHandler(manipulador ?? new HttpClientHandler())) { BaseAddress = new Uri(apiUrl), Timeout = Timeout.InfiniteTimeSpan };
			builder.Services.AddSingleton<IAutenticacaoService>(new AutenticacaoApiService(http));
			builder.Services.AddSingleton<IEquipaService>(new EquipaApiService(http));
			builder.Services.AddScoped<IntervencoesApiService>(sp => new IntervencoesApiService(http, sp.GetRequiredService<SessaoService>()));
			builder.Services.AddScoped<IIntervencoesRemoto>(sp => sp.GetRequiredService<IntervencoesApiService>());
			// Gestão (só supervisor): sempre online, nunca na fila de saída; sem rede recusa com uma mensagem clara.
			builder.Services.AddScoped(sp => new GestaoApiService(http, sp.GetRequiredService<SessaoService>()));
			builder.Services.AddScoped<IGestaoService>(sp => new GestaoSoOnlineService(sp.GetRequiredService<GestaoApiService>(), sp.GetRequiredService<IRedeEstado>(),
				() => sp.GetRequiredService<OfflineIntervencoesService>().AposEscritaDeGestaoAsync()));

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
			builder.Services.AddSingleton<IGestaoService, GestaoDevService>();
			builder.Services.AddSingleton<IArmazemGeracaoDiaria, ArmazemGeracaoDiariaMemoria>();
			builder.Services.AddSingleton<GeracaoDiaria>(sp => new GeracaoDiaria(sp.GetRequiredService<IArmazemGeracaoDiaria>(), TimeProvider.System));
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
