using System.Text.Json;

namespace GestorManutencao.Api;

public sealed record Dispositivo(string Token, string Utilizador, DateTimeOffset RegistadoEm);

public interface IRepositorioDispositivos
{
    void Registar(string utilizador, string token);
    bool Remover(string utilizador, string token);
    void RemoverTokens(IEnumerable<string> tokens);
    IReadOnlyList<string> Tokens(string? excetoUtilizador = null);
}

// Poucos utilizadores: a lista vive em memória e grava-se inteira (ficheiro temporário + move) a cada alteração.
public sealed class RepositorioDispositivosFicheiro : IRepositorioDispositivos
{
    public const int MaxPorUtilizador = 5;
    public const int MaxTamanhoToken = 4096; // sigma-ok: tamanho máximo, não é um segredo

    private readonly object _trinco = new();
    private readonly string _caminho;
    private readonly TimeProvider _relogio;
    private readonly ILogger<RepositorioDispositivosFicheiro> _log;
    private List<Dispositivo> _itens = [];

    public RepositorioDispositivosFicheiro(string caminho, TimeProvider relogio, ILogger<RepositorioDispositivosFicheiro> log)
    {
        _caminho = caminho;
        _relogio = relogio;
        _log = log;
        try
        {
            if (File.Exists(caminho))
                // Itens nulos ou com token/utilizador inválidos (ficheiro editado à mão ou corrompido) não entram.
                _itens = (JsonSerializer.Deserialize<List<Dispositivo?>>(File.ReadAllText(caminho)) ?? [])
                    .Where(d => d is not null && TokenValido(d.Token) && !string.IsNullOrWhiteSpace(d.Utilizador)).Select(d => d!).ToList();
        }
        catch (Exception e)
        {
            // Qualquer falha (JSON, E/S, permissões) deixa a API arrancar: o push é um extra.
            _log.LogError("Ficheiro de dispositivos ilegível ({Tipo}); começa vazio.", e.GetType().Name);
        }
    }

    public static bool TokenValido(string? token) => !string.IsNullOrWhiteSpace(token) && token.Length <= MaxTamanhoToken;

    public void Registar(string utilizador, string token)
    {
        lock (_trinco)
        {
            _itens.RemoveAll(d => d.Token == token);
            _itens.Add(new Dispositivo(token, utilizador, _relogio.GetUtcNow()));
            var seus = _itens.Where(d => d.Utilizador == utilizador).ToList();
            foreach (var velho in seus.OrderBy(d => d.RegistadoEm).Take(Math.Max(0, seus.Count - MaxPorUtilizador)))
                _itens.Remove(velho);
            Gravar();
        }
    }

    public bool Remover(string utilizador, string token)
    {
        lock (_trinco)
        {
            if (_itens.RemoveAll(d => d.Token == token && d.Utilizador == utilizador) == 0) return false;
            Gravar();
            return true;
        }
    }

    public void RemoverTokens(IEnumerable<string> tokens)
    {
        var lote = tokens.ToHashSet();
        lock (_trinco)
        {
            if (_itens.RemoveAll(d => lote.Contains(d.Token)) > 0) Gravar();
        }
    }

    public IReadOnlyList<string> Tokens(string? excetoUtilizador = null)
    {
        lock (_trinco)
        {
            return _itens.Where(d => excetoUtilizador is null || d.Utilizador != excetoUtilizador).Select(d => d.Token).ToList();
        }
    }

    private void Gravar()
    {
        // Sem permissão de escrita a memória continua certa até ao próximo reinício; o registo não deve dar erro à app.
        try
        {
            var pasta = Path.GetDirectoryName(_caminho);
            if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
            var temporario = _caminho + ".tmp";
            File.WriteAllText(temporario, JsonSerializer.Serialize(_itens));
            File.Move(temporario, _caminho, overwrite: true);
        }
        catch (Exception e)
        {
            _log.LogError("Não consegui gravar o ficheiro de dispositivos ({Tipo}); confirme a permissão de escrita da pasta.", e.GetType().Name);
        }
    }
}
