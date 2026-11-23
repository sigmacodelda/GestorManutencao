namespace GestorManutencao.UI.Offline;

// Bytes das fotos tiradas sem rede, por id local da foto. Na fila só fica a referência. Sobrevive à limpeza da cache.
public interface IArmazemFotosPendentes
{
    Task GuardarAsync(string fotoId, byte[] bytes);
    Task<byte[]?> LerAsync(string fotoId);
    Task RemoverAsync(string fotoId);
    // Apaga tudo o que não está em manter (inclui restos de gravações interrompidas).
    Task RemoverExcetoAsync(IReadOnlySet<string> manter);
}

public sealed class ArmazemFotosPendentesMemoria : IArmazemFotosPendentes
{
    private readonly object _trinco = new();
    private readonly Dictionary<string, byte[]> _fotos = [];

    public Task GuardarAsync(string fotoId, byte[] bytes) { lock (_trinco) _fotos[fotoId] = bytes; return Task.CompletedTask; }

    public Task<byte[]?> LerAsync(string fotoId) { lock (_trinco) return Task.FromResult(_fotos.GetValueOrDefault(fotoId)); }

    public Task RemoverAsync(string fotoId) { lock (_trinco) _fotos.Remove(fotoId); return Task.CompletedTask; }

    public Task RemoverExcetoAsync(IReadOnlySet<string> manter)
    {
        lock (_trinco) foreach (var id in _fotos.Keys.Where(k => !manter.Contains(k)).ToList()) _fotos.Remove(id);
        return Task.CompletedTask;
    }
}

// Conteúdo (JSON) das operações da fila.
internal sealed record CargaAvaria(string EquipamentoId, string Anomalia);
internal sealed record CargaConclusao(string Feito, TimeOnly Inicio, TimeOnly Fim);
// Local = lugar da peça (etiqueta LOC-... ou A-02-1-01); fila antiga sem o campo lê-se como "" (a API decide ou pede o lugar).
internal sealed record CargaMaterial(string MaterialId, string Design, decimal Qtt, decimal Epv, string Ref = "", string Local = "");
internal sealed record CargaApagar(string ItemId);
// Validar, Devolver (Texto = motivo) e ConcluirQualidade (Texto = quem higienizou); o equipamento serve ao aviso de conflito.
internal sealed record CargaDecisao(string Equipamento, string? Texto);
// Assinatura da empresa externa: PNG em data URL (até ~200 KB) e nome de quem assinou; a fila guarda-os em JSON sem limite de tamanho.
internal sealed record CargaAssinatura(string Nome, string Assinatura);
internal sealed record CargaFoto(string FotoId, string Nome, int Bytes);

internal static class CargasFila
{
    public static string Escrever<T>(T carga) => System.Text.Json.JsonSerializer.Serialize(carga);
    public static T Ler<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Operação inválida.");
}
