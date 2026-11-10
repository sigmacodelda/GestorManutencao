using GestorManutencao.UI.Servicos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GestorManutencao.UI.Offline;

public static class OfflineExtensions
{
    // A app regista antes o IIntervencoesRemoto (IntervencoesApiService) e, se quiser persistência, as suas ICacheLocal, IFilaSaida,
    // IArmazemFotosPendentes, IRedeEstado e IDonoDados (por defeito ficam em memória). Isto substitui IIntervencoesService e IEquipamentosService
    // pelo decorator offline. Pensado para MAUI, onde há um único âmbito: o trinco e o sincronizador têm de ser únicos.
    public static IServiceCollection AddManutencaoOffline(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICacheLocal, CacheLocalMemoria>();
        services.TryAddSingleton<IFilaSaida, FilaSaidaMemoria>();
        services.TryAddSingleton<IArmazemGeracaoDiaria>(sp => new ArmazemGeracaoDiariaCache(sp.GetRequiredService<ICacheLocal>()));
        services.TryAddSingleton<GeracaoDiaria>();
        services.TryAddSingleton<IArmazemFotosPendentes, ArmazemFotosPendentesMemoria>();
        services.TryAddSingleton<IRedeEstado, RedeEstadoManual>();
        services.TryAddScoped<IDonoDados, DonoDadosSessao>();
        services.AddScoped<ManutencaoCache>().AddScoped<SincronizadorFila>().AddScoped<AtualizadorCache>().AddScoped<OfflineIntervencoesService>();
        services.AddScoped<IIntervencoesService>(sp => sp.GetRequiredService<OfflineIntervencoesService>());
        services.AddScoped<IEquipamentosService>(sp => sp.GetRequiredService<OfflineIntervencoesService>());
        services.AddScoped<IEstadoLeitura>(sp => sp.GetRequiredService<OfflineIntervencoesService>());
        services.AddScoped<IGestorOffline, GestorOffline>();
        return services;
    }
}
