using GestorManutencao.UI.Offline;

namespace GestorManutencao.Armazenamento;

// Uma foto por ficheiro na pasta privada da app; o nome é o id local da foto (só letras, números, hífen e sublinhado).
public sealed class ArmazemFotosPendentesFicheiro(string pasta) : IArmazemFotosPendentes
{
    private string Caminho(string fotoId)
    {
        if (fotoId.Length is 0 or > 64 || !fotoId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ArgumentException("Identificador de foto inválido.", nameof(fotoId));
        return Path.Combine(pasta, fotoId + ".jpg");
    }

    public async Task GuardarAsync(string fotoId, byte[] bytes)
    {
        var caminho = Caminho(fotoId);
        Directory.CreateDirectory(pasta);
        var temporario = caminho + ".tmp";
        await File.WriteAllBytesAsync(temporario, bytes);
        File.Move(temporario, caminho, overwrite: true);
    }

    public async Task<byte[]?> LerAsync(string fotoId)
    {
        var caminho = Caminho(fotoId);
        return File.Exists(caminho) ? await File.ReadAllBytesAsync(caminho) : null;
    }

    public Task RemoverAsync(string fotoId)
    {
        File.Delete(Caminho(fotoId));
        return Task.CompletedTask;
    }

    public Task RemoverExcetoAsync(IReadOnlySet<string> manter)
    {
        if (!Directory.Exists(pasta)) return Task.CompletedTask;
        foreach (var ficheiro in Directory.EnumerateFiles(pasta))
        {
            var nome = Path.GetFileName(ficheiro);
            var id = nome.EndsWith(".jpg.tmp", StringComparison.Ordinal) ? nome[..^8] : nome.EndsWith(".jpg", StringComparison.Ordinal) ? nome[..^4] : null;
            if (id is null || manter.Contains(id)) continue;
            try { File.Delete(ficheiro); }
            catch (IOException) { }
        }
        return Task.CompletedTask;
    }
}
