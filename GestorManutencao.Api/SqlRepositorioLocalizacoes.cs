using System.Data;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Lugares das peças: só se lê a vista v_sc_pecaloc_disponivel (saldo da u_pecaloc menos o reservado em conferência). A API nunca escreve na u_pecaloc:
// arrumar e mover criam um dossier 66 (origem em bi.lobs, destino em bi.lobs2) e o trigger sc_bi_pecaloc move o saldo; o 63 também é com o trigger.
public sealed partial class SqlRepositorioLocalizacoes(IConfiguration configuracao, IRepositorioPecas pecas, ILogger<SqlRepositorioLocalizacoes> log) : IRepositorioLocalizacoes
{
    private const string Hoje = "CAST(CONVERT(date, GETDATE()) AS datetime)";
    private const string Hora = "CONVERT(varchar(8), GETDATE(), 108)";
    private const string Vista = "v_sc_pecaloc_disponivel";
    private const string Trigger = "sc_bi_pecaloc";
    private static readonly string[] Campos = ["ref", "armazem", "corredor", "estante", "nivel", "posicao", "qtt", "reservado", "disponivel"];
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

    // Sem a vista, os campos dela e o trigger sc_bi_pecaloc, arrumar e mover não fazem nada (o dossier 66 ficava sem efeito): por isso também se exige o trigger.
    private static async Task<string> VerificarAsync(SqlConnection conn, CancellationToken ct)
    {
        if (_tabelaOk) return "";
        await using (var existe = Cmd(conn, null, "SELECT OBJECT_ID(@v)", ("@v", Vista)))
            if (await existe.ExecuteScalarAsync(ct) is null or DBNull) return $"Falta instalar a vista {Vista} no PHC (saldo por lugar).";
        var falta = new List<string>();
        foreach (var c in Campos)
        {
            await using var cmd = Cmd(conn, null, "SELECT COL_LENGTH(@v, @c)", ("@v", Vista), ("@c", c));
            if (await cmd.ExecuteScalarAsync(ct) is null or DBNull) falta.Add(c);
        }
        if (falta.Count > 0) return $"A vista {Vista} não tem o(s) campo(s): " + string.Join(", ", falta) + ".";
        await using (var gatilho = Cmd(conn, null, "SELECT is_disabled FROM sys.triggers WHERE name = @t AND parent_id = OBJECT_ID('dbo.bi')", ("@t", Trigger)))
        {
            var desativado = await gatilho.ExecuteScalarAsync(ct);
            if (desativado is null or DBNull) return $"Falta instalar o trigger {Trigger} no PHC (move o saldo por lugar).";
            if (Convert.ToInt32(desativado) == 1) return $"O trigger {Trigger} está desativado no PHC (o saldo por lugar não se move).";
        }
        _tabelaOk = true;
        return "";
    }

    public Task<string> VerificarTabelaAsync(CancellationToken ct) => Executar(conn => VerificarAsync(conn, ct));

    public async Task<LocalizacoesPecaDto> ListarAsync(string refPeca, CancellationToken ct)
    {
        var stock = (await pecas.ObterPorCodigoAsync(refPeca, ct))?.Stock ?? 0;
        var (lugares, localizado) = await Executar(async conn =>
        {
            var l = new List<LugarDto>();
            decimal saldos = 0;
            if (await VerificarAsync(conn, ct) != "") return (l, saldos);
            var buffer = await LerBufferAsync(conn, null, ct);
            // Qtt = disponível (saldo menos o reservado em conferência); o saldo físico de cada lugar vem da coluna qtt da vista.
            await using var cmd = Cmd(conn, null, $@"
SELECT {Texto("corredor")}, {Texto("estante")}, {Inteiro("nivel")}, {Inteiro("posicao")}, {Qtd("disponivel")} AS d, {Qtd("reservado")} AS res, {QtdComSinal("qtt")} AS saldo
FROM {Vista} WHERE LTRIM(RTRIM(ref)) = @r AND armazem = @a
ORDER BY d DESC, corredor, estante, nivel, posicao",
                ("@r", refPeca.Trim()), ("@a", RegrasPecas.Armazem));
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                // A linha do buffer é o que espera lugar, não um lugar: fora da lista e do "localizado".
                var lugar = new LugarDto(rd.GetString(0), rd.GetString(1), rd.GetInt32(2), rd.GetInt32(3), rd.GetDecimal(4), rd.GetDecimal(5));
                if (RegrasLocalizacao.EBuffer(lugar, buffer)) continue;
                l.Add(lugar);
                saldos += Math.Max(0, rd.GetDecimal(6)); // saldo negativo não conta como localizado (a view de divergências mostra-o)
            }
            return (l, saldos);
        });
        return new LocalizacoesPecaDto(refPeca, stock, localizado, RegrasLocalizacao.Calcular(stock, localizado), lugares);
    }

    public Task<IReadOnlyList<(string Ref, decimal Qtt)>> ListarPecasDoLugarAsync(string corredor, string estante, int nivel, int posicao, CancellationToken ct)
        => Executar<IReadOnlyList<(string Ref, decimal Qtt)>>(async conn =>
        {
            var l = new List<(string Ref, decimal Qtt)>();
            if (await VerificarAsync(conn, ct) != "") return l;
            await using var cmd = Cmd(conn, null, $@"
SELECT r, SUM(q) AS q FROM (
    SELECT LTRIM(RTRIM(ISNULL(ref, ''))) AS r, {Qtd("disponivel")} AS q
    FROM {Vista}
    WHERE armazem = @a AND LTRIM(RTRIM(corredor)) = @c AND LTRIM(RTRIM(estante)) = @e AND {Inteiro("nivel")} = @n AND {Inteiro("posicao")} = @po
) t
WHERE r <> ''
GROUP BY r
HAVING SUM(q) > 0
ORDER BY SUM(q) DESC, r",
                ("@a", RegrasPecas.Armazem), ("@c", corredor.Trim().ToUpperInvariant()), ("@e", estante.Trim().ToUpperInvariant()), ("@n", nivel), ("@po", posicao));
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) l.Add((rd.GetString(0), rd.GetDecimal(1)));
            return l;
        });

    // u_configman.localentrada (A-03-2-04 ou texto livre como BUFFER), a mesma da LandeiroMaui; sem o campo ou vazia = sem buffer.
    private static async Task<LugarDto?> LerBufferAsync(SqlConnection conn, SqlTransaction? tx, CancellationToken ct)
    {
        await using (var coluna = Cmd(conn, tx, "SELECT COL_LENGTH('u_configman', 'localentrada')"))
            if (await coluna.ExecuteScalarAsync(ct) is null or DBNull) return null;
        await using var cmd = Cmd(conn, tx, "SELECT TOP 1 LTRIM(RTRIM(ISNULL(localentrada, ''))) FROM u_configman WITH (NOLOCK)");
        return RegrasLocalizacao.LerBuffer(await cmd.ExecuteScalarAsync(ct) as string);
    }

    // Regista o dossier 66 (buffer -> destino); o trigger sc_bi_pecaloc tira do buffer e soma ao destino.
    public Task<ResultadoMovimento> ArrumarAsync(string refPeca, string corredor, int estante, int nivel, int posicao, decimal qtt, string iniciais, CancellationToken ct)
    {
        var estanteNormal = RegrasLocalizacao.NormalizarEstante(corredor, estante);
        return Escrever(refPeca, estanteNormal, nivel, posicao, ct, async (conn, tx) =>
        {
            var buffer = await LerBufferAsync(conn, tx, ct);
            if (RegrasLocalizacao.EBuffer(new LugarDto(corredor.Trim().ToUpperInvariant(), estanteNormal, nivel, posicao, 0), buffer))
                return ResultadoMovimento.Falha("Escolha um lugar diferente do de entrada (buffer).");
            var destino = new LugarDto(corredor.Trim().ToUpperInvariant(), estanteNormal, nivel, posicao, qtt);
            if (await CriarMovimentoAsync(conn, tx, "Arrumação", refPeca, RegrasLocalizacao.TextoLugar(buffer), destino.Texto, qtt, iniciais, ct) is { } falha)
                return ResultadoMovimento.Falha(falha);
            return ResultadoMovimento.Sucesso("");
        });
    }

    // Regista o dossier 66 (origem -> destino) sob applock nos dois lugares; o trigger move o saldo. Só se move o disponível (o reservado em conferência fica).
    // Origem a 0 = "não indicada": o trigger descarta movimentos de 0, por isso recusa-se em vez de criar um dossier sem efeito.
    public Task<ResultadoMovimento> MoverAsync(string refPeca, LugarDto origem, LugarDto destino, decimal qtt, string iniciais, CancellationToken ct)
        => EscreverBloqueando([ChaveLugar(refPeca, origem.Estante, origem.Nivel, origem.Posicao), ChaveLugar(refPeca, destino.Estante, destino.Nivel, destino.Posicao)], ct, async (conn, tx) =>
        {
            var buffer = await LerBufferAsync(conn, tx, ct);
            if (RegrasLocalizacao.EBuffer(destino, buffer)) return ResultadoMovimento.Falha(RegrasLocalizacao.ErroMoverParaBuffer);
            if (RegrasLocalizacao.EBuffer(origem, buffer)) return ResultadoMovimento.Falha(RegrasLocalizacao.ErroMoverDoBuffer);
            var linhaOrigem = $"LTRIM(RTRIM(ref)) = @r AND armazem = @a AND {Texto("corredor")} = @c AND {Texto("estante")} = @e AND {Inteiro("nivel")} = @n AND {Inteiro("posicao")} = @po";
            decimal saldo, atual, reservado;
            await using (var ler = Cmd(conn, tx, $"SELECT TOP 1 {QtdComSinal("qtt")}, {Qtd("disponivel")}, {Qtd("reservado")} FROM {Vista} WHERE {linhaOrigem}",
                ("@r", refPeca.Trim()), ("@a", RegrasPecas.Armazem), ("@c", origem.Corredor.Trim().ToUpperInvariant()), ("@e", origem.Estante.Trim().ToUpperInvariant()),
                ("@n", origem.Nivel), ("@po", origem.Posicao)))
            await using (var rd = await ler.ExecuteReaderAsync(ct))
            {
                if (!await rd.ReadAsync(ct)) return ResultadoMovimento.Falha(RegrasLocalizacao.ErroMoverSemLinha);
                saldo = rd.GetDecimal(0);
                atual = rd.GetDecimal(1);
                reservado = rd.GetDecimal(2);
            }
            if (saldo < 0) return ResultadoMovimento.Falha(RegrasLocalizacao.ErroMoverSaldoNegativo);
            if (atual <= 0 && (saldo > 0 || reservado > 0)) return ResultadoMovimento.Falha(RegrasLocalizacao.ErroMoverReservado);
            if (RegrasLocalizacao.ErroQuantidadeMover(atual, qtt) is { } erro) return ResultadoMovimento.Falha(erro);
            if (await CriarMovimentoAsync(conn, tx, "Rearrumação", refPeca, origem.Texto, destino.Texto, qtt, iniciais, ct) is { } falha)
                return ResultadoMovimento.Falha(falha);
            return ResultadoMovimento.Sucesso("");
        });

    // qtt, nivel e posicao podem ser texto no PHC e ter lixo (outra app): lê-se só texto limpo (dígitos, no máximo um ponto), o resto vale 0,
    // para uma linha estranha nunca fazer falhar a conversão (e não se compara texto com número sem proteção).
    private static string Texto(string col) => $"LTRIM(RTRIM(ISNULL({col}, '')))";
    private static string Qtd(string col) =>
        $"CASE WHEN {Texto(col)} LIKE '%[0-9]%' AND {Texto(col)} NOT LIKE '%[^0-9.]%' AND LEN({Texto(col)}) - LEN(REPLACE({Texto(col)}, '.', '')) <= 1 AND LEN({Texto(col)}) <= 13 THEN CAST({Texto(col)} AS decimal(13, 3)) ELSE CAST(0 AS decimal(13, 3)) END";
    // Saldo físico: pode ser negativo (saída acima do saldo); o sinal só se aceita à frente.
    private static string QtdComSinal(string col)
    {
        var t = Texto(col);
        var u = $"(CASE WHEN LEFT({t}, 1) = '-' THEN SUBSTRING({t}, 2, 50) ELSE {t} END)";
        return $"CASE WHEN {u} LIKE '%[0-9]%' AND {u} NOT LIKE '%[^0-9.]%' AND LEN({u}) - LEN(REPLACE({u}, '.', '')) <= 1 AND LEN({u}) <= 13 THEN CAST({u} AS decimal(13, 3)) * (CASE WHEN LEFT({t}, 1) = '-' THEN -1 ELSE 1 END) ELSE CAST(0 AS decimal(13, 3)) END";
    }
    private static string Inteiro(string col) =>
        $"CASE WHEN {Texto(col)} LIKE '[0-9]%' AND {Texto(col)} NOT LIKE '%[^0-9]%' AND LEN({Texto(col)}) <= 9 THEN CAST({Texto(col)} AS int) ELSE 0 END";

    private static string ChaveLugar(string refPeca, string estante, int nivel, int posicao) =>
        $"pecas-lugar-{refPeca.Trim()}-{estante.Trim().ToUpperInvariant()}-{nivel}-{posicao}";

    private Task<ResultadoMovimento> Escrever(string refPeca, string estante, int nivel, int posicao, CancellationToken ct, Func<SqlConnection, SqlTransaction, Task<ResultadoMovimento>> trabalho)
        => EscreverBloqueando([ChaveLugar(refPeca, estante, nivel, posicao)], ct, trabalho);

    // Transação com applock por lugar, sempre pela mesma ordem (dois movimentos cruzados não se bloqueiam); o lock liberta-se no commit/rollback.
    private async Task<ResultadoMovimento> EscreverBloqueando(string[] chaves, CancellationToken ct, Func<SqlConnection, SqlTransaction, Task<ResultadoMovimento>> trabalho)
    {
        return await Executar(async conn =>
        {
            var falta = await VerificarAsync(conn, ct);
            if (falta != "") return ResultadoMovimento.Falha(falta);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var chave in chaves.Distinct().Order(StringComparer.Ordinal))
                    await using (var bloqueio = Cmd(conn, tx, "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; SELECT @r;",
                        ("@res", chave)))
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
                log.LogWarning("Dados recusados pelo movimento de arrumação (erro {Numero}).", ex.Number);
                return ResultadoMovimento.Falha(ex.Number is 8152 or 2628
                    ? "O local ou a peça excede o tamanho permitido no movimento."
                    : "Quantidade ou posição fora dos limites permitidos no movimento.");
            }
            catch (Exception)
            {
                await RollbackSeguroAsync(tx);
                throw;
            }
        });
    }
}
