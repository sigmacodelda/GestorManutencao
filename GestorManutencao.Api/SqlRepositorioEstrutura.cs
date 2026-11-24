using System.Data;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Só leitura: desenho do armazém em u_armazemlay (JSON da LandeiroMaui) e, sem ele, as localizações já usadas em u_pecaloc.
public sealed class SqlRepositorioEstrutura(IConfiguration configuracao, ILogger<SqlRepositorioEstrutura> log) : IRepositorioEstrutura
{
    private static string Texto(string col) => $"LTRIM(RTRIM(ISNULL({col}, '')))";
    private static string Inteiro(string col) =>
        $"CASE WHEN {Texto(col)} LIKE '[0-9]%' AND {Texto(col)} NOT LIKE '%[^0-9]%' AND LEN({Texto(col)}) <= 9 THEN CAST({Texto(col)} AS int) ELSE 0 END";

    public async Task<IReadOnlyList<EstanteDto>> ListarAsync(CancellationToken ct)
    {
        var ligacao = LigacaoPhc.Obter(configuracao);
        if (string.IsNullOrWhiteSpace(ligacao)) throw new RepositorioIndisponivelException();
        try
        {
            await using var conn = new SqlConnection(ligacao);
            await conn.OpenAsync(ct);
            var json = await LerDesenhoAsync(conn, ct);
            var buffer = await LerBufferAsync(conn, ct);
            // u_pecaloc só se lê quando não há desenho utilizável.
            var lugares = RegrasEstrutura.LerDesenho(json) is null ? await LerLugaresAsync(conn, ct) : [];
            return RegrasEstrutura.Montar(json, lugares, buffer);
        }
        catch (SqlException ex)
        {
            log.LogError("Falha SQL na estrutura do armazém (erro {Numero}).", ex.Number);
            throw new RepositorioIndisponivelException(ex);
        }
    }

    private static async Task<bool> ExisteAsync(SqlConnection conn, string tabela, string[] colunas, CancellationToken ct)
    {
        await using (var t = new SqlCommand("SELECT OBJECT_ID(@t)", conn))
        {
            t.Parameters.Add("@t", SqlDbType.VarChar, 60).Value = tabela;
            if (await t.ExecuteScalarAsync(ct) is null or DBNull) return false;
        }
        foreach (var c in colunas)
        {
            await using var cmd = new SqlCommand("SELECT COL_LENGTH(@t, @c)", conn);
            cmd.Parameters.Add("@t", SqlDbType.VarChar, 60).Value = tabela;
            cmd.Parameters.Add("@c", SqlDbType.VarChar, 60).Value = c;
            if (await cmd.ExecuteScalarAsync(ct) is null or DBNull) return false;
        }
        return true;
    }

    private static async Task<string?> LerDesenhoAsync(SqlConnection conn, CancellationToken ct)
    {
        if (!await ExisteAsync(conn, "u_armazemlay", ["config", "ativo", "usrdata", "usrhora"], ct)) return null;
        await using var cmd = new SqlCommand("SELECT TOP 1 CAST(config AS varchar(max)) FROM u_armazemlay WITH (NOLOCK) WHERE ativo = 1 ORDER BY usrdata DESC, usrhora DESC", conn);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    private static async Task<List<LugarDto>> LerLugaresAsync(SqlConnection conn, CancellationToken ct)
    {
        var l = new List<LugarDto>();
        if (!await ExisteAsync(conn, "u_pecaloc", ["armazem", "corredor", "estante", "nivel", "posicao"], ct)) return l;
        await using var cmd = new SqlCommand($@"
SELECT DISTINCT {Texto("corredor")}, {Texto("estante")}, {Inteiro("nivel")}, {Inteiro("posicao")}
FROM u_pecaloc WITH (NOLOCK) WHERE armazem = @a", conn);
        cmd.Parameters.AddWithValue("@a", RegrasPecas.Armazem);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) l.Add(new LugarDto(rd.GetString(0), rd.GetString(1), rd.GetInt32(2), rd.GetInt32(3), 0));
        return l;
    }

    // u_configman.localentrada, como em SqlRepositorioLocalizacoes; sem o campo ou vazia = sem buffer.
    private static async Task<LugarDto?> LerBufferAsync(SqlConnection conn, CancellationToken ct)
    {
        if (!await ExisteAsync(conn, "u_configman", ["localentrada"], ct)) return null;
        await using var cmd = new SqlCommand("SELECT TOP 1 LTRIM(RTRIM(ISNULL(localentrada, ''))) FROM u_configman WITH (NOLOCK)", conn);
        return RegrasLocalizacao.LerBuffer(await cmd.ExecuteScalarAsync(ct) as string);
    }
}
