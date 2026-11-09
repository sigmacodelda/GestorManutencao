using System.Text.Json;
using System.Text.Json.Serialization;
using SQLite;

namespace GestorManutencao.Armazenamento;

// Uma só base SQLite (ficheiro na pasta privada da app) partilhada pela cache e pela fila. Não guarda token nem PIN.
// A base não está cifrada: ver docs/offline.md.
public sealed class BaseLocalSqlite(string caminho)
{
    private static readonly JsonSerializerOptions Opcoes = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly SemaphoreSlim _arranque = new(1, 1);
    private SQLiteAsyncConnection? _ligacao;

    public static string Serializar<T>(T valor) => JsonSerializer.Serialize(valor, Opcoes);

    public static T Ler<T>(string json) => JsonSerializer.Deserialize<T>(json, Opcoes) ?? throw new InvalidOperationException("Dados locais inválidos.");

    public async Task<SQLiteAsyncConnection> LigarAsync()
    {
        if (_ligacao is not null) return _ligacao;
        await _arranque.WaitAsync();
        try
        {
            if (_ligacao is not null) return _ligacao;
            if (Path.GetDirectoryName(caminho) is { Length: > 0 } pasta) Directory.CreateDirectory(pasta);
            var ligacao = new SQLiteAsyncConnection(caminho, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
            await ligacao.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL");
            await ligacao.CreateTableAsync<LinhaMeta>();
            await ligacao.CreateTableAsync<LinhaFicha>();
            await ligacao.CreateTableAsync<LinhaMateriais>();
            await ligacao.CreateTableAsync<LinhaFotos>();
            await ligacao.CreateTableAsync<LinhaFotoBytes>();
            await ligacao.CreateTableAsync<LinhaEquipamentos>();
            await ligacao.CreateTableAsync<LinhaFichaEquipamento>();
            await ligacao.CreateTableAsync<LinhaOperacao>();
            await ligacao.CreateTableAsync<LinhaAviso>();
            _ligacao = ligacao;
            return ligacao;
        }
        finally { _arranque.Release(); }
    }

    // Fecha a ligação (usado nos testes, para poder apagar o ficheiro).
    public async Task FecharAsync()
    {
        if (_ligacao is null) return;
        await _ligacao.CloseAsync();
        _ligacao = null;
    }
}

[Table("Meta")] public sealed class LinhaMeta { [PrimaryKey] public string Chave { get; set; } = ""; public string Valor { get; set; } = ""; }
[Table("Ficha")] public sealed class LinhaFicha { [PrimaryKey] public string Id { get; set; } = ""; public string Json { get; set; } = ""; }
[Table("Materiais")] public sealed class LinhaMateriais { [PrimaryKey] public string FichaId { get; set; } = ""; public string Json { get; set; } = ""; }
[Table("Fotos")] public sealed class LinhaFotos { [PrimaryKey] public string FichaId { get; set; } = ""; public string Json { get; set; } = ""; }

[Table("FotoBytes")]
public sealed class LinhaFotoBytes
{
    [PrimaryKey] public string Chave { get; set; } = "";
    [Indexed] public string FichaId { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
}

[Table("Equipamentos")] public sealed class LinhaEquipamentos { [PrimaryKey] public string Lista { get; set; } = ""; public string Json { get; set; } = ""; }

[Table("FichaEquipamento")]
public sealed class LinhaFichaEquipamento
{
    [PrimaryKey] public string Id { get; set; } = "";
    public long AbertaTicks { get; set; }
    public string Json { get; set; } = "";
}

[Table("Operacao")]
public sealed class LinhaOperacao
{
    [PrimaryKey, AutoIncrement] public long Seq { get; set; }
    [Indexed(Unique = true)] public string Id { get; set; } = "";
    [Indexed] public string Dono { get; set; } = "";
    public string FichaId { get; set; } = "";
    public int Estado { get; set; }
    public string Json { get; set; } = "";
}

[Table("Aviso")]
public sealed class LinhaAviso
{
    [PrimaryKey, AutoIncrement] public long Seq { get; set; }
    [Indexed(Unique = true)] public string Id { get; set; } = "";
    [Indexed] public string Dono { get; set; } = "";
    public string Json { get; set; } = "";
}
