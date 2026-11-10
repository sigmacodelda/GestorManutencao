using GestorManutencao.UI.Modelos;

namespace GestorManutencao.UI.Offline;

// Onde se guarda o dia da última geração das planeadas, por dispositivo e utilizador (a geração em si é só online; isto lê-se sem rede).
public interface IArmazemGeracaoDiaria
{
    Task<DateOnly?> LerAsync(string chave);
    Task GuardarAsync(string chave, DateOnly dia);
}

public sealed class ArmazemGeracaoDiariaMemoria : IArmazemGeracaoDiaria
{
    private readonly Dictionary<string, DateOnly> _dias = [];

    public Task<DateOnly?> LerAsync(string chave)
    {
        lock (_dias) return Task.FromResult(_dias.TryGetValue(chave, out var d) ? d : (DateOnly?)null);
    }

    public Task GuardarAsync(string chave, DateOnly dia)
    {
        lock (_dias) _dias[chave] = dia;
        return Task.CompletedTask;
    }
}

// Guarda o dia na meta da cópia local (SQLite na app).
public sealed class ArmazemGeracaoDiariaCache(ICacheLocal cache) : IArmazemGeracaoDiaria
{
    public async Task<DateOnly?> LerAsync(string chave) =>
        DateOnly.TryParseExact(await cache.LerMetaAsync(chave), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    public Task GuardarAsync(string chave, DateOnly dia) => cache.GuardarMetaAsync(chave, dia.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
}

public enum EstadoGeracaoDiaria { JaFeitaHoje, Desligada, Gerou, ComErros, Falhou }

public sealed record ResultadoGeracaoDiaria(EstadoGeracaoDiaria Estado, RelatorioGeracao? Relatorio = null, string? Aviso = null)
{
    public int Criadas => Relatorio?.Criadas ?? 0;
}

// Mesma regra da LandeiroMaui: a geração corre no máximo uma vez por dia, só se a configuração gera planeadas, e se houver erros volta a tentar na próxima abertura.
// Sem rede ou com qualquer falha não incomoda: devolve só um aviso discreto e não marca o dia.
public sealed class GeracaoDiaria(IArmazemGeracaoDiaria armazem, TimeProvider tempo)
{
    public static string Chave(string utilizador) => "geracao-diaria:" + utilizador.Trim().ToLowerInvariant();

    public async Task<bool> JaFoiHojeAsync(string utilizador) => await armazem.LerAsync(Chave(utilizador)) == Hoje;

    private DateOnly Hoje => DateOnly.FromDateTime(tempo.GetLocalNow().DateTime);

    public async Task<ResultadoGeracaoDiaria> ExecutarAsync(string utilizador, bool gerarPlaneadas, Func<Task<RelatorioGeracao>> gerar)
    {
        if (!gerarPlaneadas) return new(EstadoGeracaoDiaria.Desligada);
        if (await JaFoiHojeAsync(utilizador)) return new(EstadoGeracaoDiaria.JaFeitaHoje);
        RelatorioGeracao relatorio;
        try { relatorio = await gerar(); }
        catch (Exception) { return new(EstadoGeracaoDiaria.Falhou, null, "Não foi possível gerar as planeadas de hoje. Tenta-se outra vez na próxima abertura."); }
        if (relatorio.Erros > 0) return new(EstadoGeracaoDiaria.ComErros, relatorio, $"{relatorio.Erros} planeadas não foram geradas. Tenta-se outra vez na próxima abertura.");
        try { await armazem.GuardarAsync(Chave(utilizador), Hoje); } catch (Exception) { /* sem o dia gravado, volta a gerar (é idempotente) */ }
        return new(EstadoGeracaoDiaria.Gerou, relatorio);
    }
}
