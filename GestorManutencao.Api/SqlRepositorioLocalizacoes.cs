using System.Data;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Lugares das peças em u_pecaloc (tabela criada no PHC, como a LandeiroMaui). Escritas por lugar sob applock.
public sealed class SqlRepositorioLocalizacoes(IConfiguration configuracao, IRepositorioPecas pecas, ILogger<SqlRepositorioLocalizacoes> log) : IRepositorioLocalizacoes
{
    private const string Hoje = "CAST(CONVERT(date, GETDATE()) AS datetime)";
    private const string Hora = "CONVERT(varchar(8), GETDATE(), 108)";
    private static readonly string[] Campos = ["u_pecalocstamp", "ref", "armazem", "corredor", "estante", "nivel", "posicao", "qtt", "marcada",
        "ousrinis", "ousrdata", "ousrhora", "usrinis", "usrdata", "usrhora"];
    private static bool _tabelaOk;

    private async Task<T> Executar<T>(Func<SqlConnection, Task<T>> trabalho)
    {
        var ligacao = LigacaoPhc.Obter(configuracao);
        if (string.IsNullOrWhiteSpace(ligacao)) throw new RepositorioIndisponivelException();
        try
        {
            await using var conn = new SqlConnection(ligacao);
            await conn.OpenAsync();
            return await trabalho(conn);
        }
        catch (SqlException ex)
        {
            log.LogError("Falha SQL nas localizações (erro {Numero}).", ex.Number);
            throw new RepositorioIndisponivelException(ex);
        }
    }

    private static SqlCommand Cmd(SqlConnection conn, SqlTransaction? tx, string sql, params (string Nome, object? Valor)[] ps)
    {
        var cmd = new SqlCommand(sql, conn, tx);
        foreach (var (nome, valor) in ps) Parametro(cmd, nome, valor);
        return cmd;
    }

    // Texto como varchar com tamanho (escalões) e quantidade como decimal(16,3), como o repositório de peças.
    private static void Parametro(SqlCommand cmd, string nome, object? valor)
    {
        switch (valor)
        {
            case string t:
                cmd.Parameters.Add(nome, SqlDbType.VarChar, t.Length <= 25 ? 25 : t.Length <= 60 ? 60 : t.Length <= 200 ? 200 : 8000).Value = t;
                break;
            case decimal d:
                var p = cmd.Parameters.Add(nome, SqlDbType.Decimal);
                p.Precision = 16; p.Scale = 3; p.Value = d;
                break;
            default:
                cmd.Parameters.AddWithValue(nome, valor ?? DBNull.Value);
                break;
        }
    }

    private static async Task RollbackSeguroAsync(SqlTransaction tx)
    {
        try { await tx.RollbackAsync(CancellationToken.None); }
        catch (Exception) { }
    }

    private static async Task<string> VerificarAsync(SqlConnection conn, CancellationToken ct)
    {
        if (_tabelaOk) return "";
        await using (var existe = Cmd(conn, null, "SELECT OBJECT_ID('u_pecaloc')"))
            if (await existe.ExecuteScalarAsync(ct) is null or DBNull) return "Falta criar a tabela u_pecaloc no PHC.";
        var falta = new List<string>();
        foreach (var c in Campos)
        {
            await using var cmd = Cmd(conn, null, "SELECT COL_LENGTH('u_pecaloc', @c)", ("@c", c));
            if (await cmd.ExecuteScalarAsync(ct) is null or DBNull) falta.Add(c);
        }
        if (falta.Count > 0) return "A tabela u_pecaloc não tem o(s) campo(s): " + string.Join(", ", falta) + ".";
        _tabelaOk = true;
        return "";
    }

    public Task<string> VerificarTabelaAsync(CancellationToken ct) => Executar(conn => VerificarAsync(conn, ct));

    public async Task<LocalizacoesPecaDto> ListarAsync(string refPeca, CancellationToken ct)
    {
        var stock = (await pecas.ObterPorCodigoAsync(refPeca, ct))?.Stock ?? 0;
        var lugares = await Executar(async conn =>
        {
            var l = new List<LugarDto>();
            if (await VerificarAsync(conn, ct) != "") return l;
            await using var cmd = Cmd(conn, null, @"
SELECT ISNULL(LTRIM(RTRIM(corredor)), ''), ISNULL(LTRIM(RTRIM(estante)), ''), CAST(ISNULL(NULLIF(LTRIM(RTRIM(nivel)), ''), '0') AS int), CAST(ISNULL(NULLIF(LTRIM(RTRIM(posicao)), ''), '0') AS int),
       CAST(ISNULL(NULLIF(LTRIM(RTRIM(qtt)), ''), '0') AS decimal(13, 3)) AS q  -- qtt, nivel e posicao são varchar no PHC
FROM u_pecaloc WITH (NOLOCK) WHERE LTRIM(RTRIM(ref)) = @r AND armazem = @a
ORDER BY q DESC, corredor, estante, nivel, posicao",
                ("@r", refPeca.Trim()), ("@a", RegrasPecas.Armazem));
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) l.Add(new LugarDto(rd.GetString(0), rd.GetString(1), rd.GetInt32(2), rd.GetInt32(3), rd.GetDecimal(4)));
            return l;
        });
        var localizado = lugares.Sum(l => l.Qtt);
        return new LocalizacoesPecaDto(refPeca, stock, localizado, RegrasLocalizacao.Calcular(stock, localizado), lugares);
    }

    public Task<ResultadoMovimento> ArrumarAsync(string refPeca, string corredor, int estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct)
        => SomarAsync(refPeca, corredor, RegrasLocalizacao.NormalizarEstante(corredor, estante), nivel, posicao, qtt, iniciais, ct);

    public Task<ResultadoMovimento> ReporAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct)
        => SomarAsync(refPeca, corredor, estante, nivel, posicao, qtt, iniciais, ct);

    // Um só comando: soma ao lugar se existir, senão cria.
    private Task<ResultadoMovimento> SomarAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct)
        => Escrever(refPeca, estante, nivel, posicao, ct, async (conn, tx) =>
        {
            await using var cmd = Cmd(conn, tx, $@"
UPDATE u_pecaloc SET qtt = ISNULL(qtt, 0) + @q, usrinis = @ini, usrdata = {Hoje}, usrhora = {Hora}
WHERE LTRIM(RTRIM(ref)) = @r AND armazem = @a AND LTRIM(RTRIM(corredor)) = @c AND LTRIM(RTRIM(estante)) = @e AND nivel = @n AND posicao = @po;
IF @@ROWCOUNT = 0
INSERT INTO u_pecaloc (u_pecalocstamp, ref, armazem, corredor, estante, nivel, posicao, qtt, marcada, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
VALUES (@s, @r, @a, @c, @e, @n, @po, @q, 0, @ini, {Hoje}, {Hora}, @ini, {Hoje}, {Hora})",
                Parametros(refPeca, corredor, estante, nivel, posicao, qtt, iniciais, Guid.NewGuid().ToString("N")[..25]));
            await cmd.ExecuteNonQueryAsync(ct);
            return ResultadoMovimento.Sucesso("");
        });

    public Task<ResultadoMovimento> DescontarAsync(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct)
        => Escrever(refPeca, estante, nivel, posicao, ct, async (conn, tx) =>
        {
            await using var cmd = Cmd(conn, tx, $@"
UPDATE u_pecaloc SET qtt = ISNULL(qtt, 0) - @q, usrinis = @ini, usrdata = {Hoje}, usrhora = {Hora}
WHERE LTRIM(RTRIM(ref)) = @r AND armazem = @a AND LTRIM(RTRIM(corredor)) = @c AND LTRIM(RTRIM(estante)) = @e AND nivel = @n AND posicao = @po AND ISNULL(qtt, 0) >= @q;
SELECT @@ROWCOUNT",
                Parametros(refPeca, corredor, estante, nivel, posicao, qtt, iniciais, ""));
            return (int)(await cmd.ExecuteScalarAsync(ct))! == 0
                ? ResultadoMovimento.Falha("Não há quantidade suficiente neste lugar.")
                : ResultadoMovimento.Sucesso("");
        });

    private static (string, object?)[] Parametros(string refPeca, string corredor, string estante, int nivel, int posicao, decimal qtt, string iniciais, string stamp) =>
    [
        ("@r", refPeca.Trim()), ("@a", RegrasPecas.Armazem), ("@c", corredor.Trim().ToUpperInvariant()), ("@e", estante.Trim().ToUpperInvariant()),
        ("@n", nivel), ("@po", posicao), ("@q", qtt), ("@ini", iniciais ?? ""), ("@s", stamp)
    ];

    // Transação com applock por lugar; o lock liberta-se no commit/rollback.
    private async Task<ResultadoMovimento> Escrever(string refPeca, string estante, int nivel, int posicao, CancellationToken ct, Func<SqlConnection, SqlTransaction, Task<ResultadoMovimento>> trabalho)
    {
        // Parâmetros não usados pelo comando (@s no desconto) são aceites pelo SQL Server sem erro.
        return await Executar(async conn =>
        {
            var falta = await VerificarAsync(conn, ct);
            if (falta != "") return ResultadoMovimento.Falha(falta);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                await using (var bloqueio = Cmd(conn, tx, "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; SELECT @r;",
                    ("@res", $"pecas-lugar-{refPeca.Trim()}-{estante.Trim().ToUpperInvariant()}-{nivel}-{posicao}")))
                    if ((int)(await bloqueio.ExecuteScalarAsync(ct))! < 0) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroOcupado); }
                var r = await trabalho(conn, tx);
                if (!r.Ok) { await RollbackSeguroAsync(tx); return r; }
                await tx.CommitAsync(CancellationToken.None);
                return r;
            }
            catch (SqlException ex) when (ex.Number == 50000)
            {
                await RollbackSeguroAsync(tx);
                return ResultadoMovimento.Falha(ex.Message);
            }
            // Erros de dados (valor grande demais, conversão) são do pedido, não indisponibilidade: 400 e não 503.
            catch (SqlException ex) when (ex.Number is 8152 or 2628 or 8115 or 220 or 245)
            {
                await RollbackSeguroAsync(tx);
                log.LogWarning("Dados recusados pela tabela u_pecaloc (erro {Numero}).", ex.Number);
                return ResultadoMovimento.Falha(ex.Number is 8152 or 2628
                    ? "O local ou a peça excede o tamanho permitido na tabela u_pecaloc."
                    : "Quantidade ou posição fora dos limites permitidos na tabela u_pecaloc.");
            }
            catch (Exception)
            {
                await RollbackSeguroAsync(tx);
                throw;
            }
        });
    }
}
