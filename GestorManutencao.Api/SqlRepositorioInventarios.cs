using System.Data;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Inventário (dossier 65) contado na app, como o contrato da LandeiroMaui (InventarioService): bo.maquina = nº do técnico, bo.logi1 = concluído,
// bo.logi2 = lançado (só o desktop), bi.ref/bi.qtt/bi.lobs3 = peça, quantidade e lugar (o lugar vai em lobs3, não em lobs). Não cria dossiers, não mexe em stock nem em u_pecaloc.
// SQL ainda por validar contra a base de testes.
public sealed class SqlRepositorioInventarios(IConfiguration configuracao, ILogger<SqlRepositorioInventarios> log) : IRepositorioInventarios
{
    private const string Hoje = "CAST(CONVERT(date, GETDATE()) AS datetime)";
    private const string Hora = "CONVERT(varchar(8), GETDATE(), 108)";

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
            log.LogError("Falha SQL nos inventários (erro {Numero}).", ex.Number);
            throw new RepositorioIndisponivelException(ex);
        }
    }

    private static SqlCommand Cmd(SqlConnection conn, SqlTransaction? tx, string sql, params (string Nome, object? Valor)[] ps)
    {
        var cmd = new SqlCommand(sql, conn, tx);
        foreach (var (nome, valor) in ps)
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
        return cmd;
    }

    private static async Task RollbackSeguroAsync(SqlTransaction tx)
    {
        try { await tx.RollbackAsync(CancellationToken.None); }
        catch (Exception) { }
    }

    // Nº do técnico e nome (como a LandeiroMaui); o filtro por dono compara o texto de bo.maquina.
    private const string SelecionarInventarios = $"""
        SELECT b.bostamp, b.obrano, CAST(b.dataopen AS date), LTRIM(RTRIM(ISNULL(b.obranome, ''))), LTRIM(RTRIM(ISNULL(b.maquina, ''))),
               ISNULL(u.nome, ''), (SELECT COUNT(*) FROM bi i WITH (NOLOCK) WHERE i.bostamp = b.bostamp AND i.ndos = @ndos),
               CAST(ISNULL(b.logi1, 0) AS bit), CAST(ISNULL(b.logi2, 0) AS bit)
        FROM bo b WITH (NOLOCK)
        OUTER APPLY (SELECT TOP 1 LTRIM(RTRIM(m.nome)) AS nome FROM u_manutil m WITH (NOLOCK)
                     WHERE m.tecno = CASE WHEN ISNUMERIC(b.maquina) = 1 THEN CAST(LTRIM(RTRIM(b.maquina)) AS numeric(18, 0)) END) u
        """;

    private static InventarioDto LerInventario(SqlDataReader r, int tecno)
    {
        var dono = int.TryParse(r.GetString(4), out var t) ? t : 0;
        var concluido = r.GetBoolean(7);
        var lancado = r.GetBoolean(8);
        return new InventarioDto(r.GetString(0).Trim(), r.GetDecimal(1), DateOnly.FromDateTime(r.GetDateTime(2)), r.GetString(3), dono, r.GetString(5),
            r.GetInt32(6), concluido, lancado, dono == tecno && dono > 0 && !concluido && !lancado);
    }

    public Task<IReadOnlyList<InventarioDto>> ListarAsync(int tecno, bool supervisor, CancellationToken ct) => Executar<IReadOnlyList<InventarioDto>>(async conn =>
    {
        await using var cmd = Cmd(conn, null, $"""
            {SelecionarInventarios}
            WHERE b.ndos = @ndos
              AND (@todos = 1 OR LTRIM(RTRIM(b.maquina)) = @tec)
              AND ((ISNULL(b.logi1, 0) = 0 AND ISNULL(b.logi2, 0) = 0) OR b.dataopen >= DATEADD(day, -@dias, GETDATE()))
            ORDER BY b.dataopen DESC, b.obrano DESC
            """, ("@ndos", RegrasInventario.Ndos), ("@todos", supervisor ? 1 : 0), ("@tec", tecno.ToString()), ("@dias", RegrasInventario.DiasConcluidosVisiveis));
        var lista = new List<InventarioDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(LerInventario(r, tecno));
        return lista;
    });

    private static async Task<InventarioDetalheDto?> LerDetalheAsync(SqlConnection conn, SqlTransaction? tx, string id, int tecno, CancellationToken ct)
    {
        InventarioDto? inv = null;
        await using (var cmd = Cmd(conn, tx, $"{SelecionarInventarios} WHERE b.bostamp = @id AND b.ndos = @ndos", ("@id", id), ("@ndos", RegrasInventario.Ndos)))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            if (await r.ReadAsync(ct)) inv = LerInventario(r, tecno);
        if (inv is null) return null;

        var linhas = new List<LinhaInventarioDto>();
        await using (var cmd = Cmd(conn, tx, """
            SELECT LTRIM(RTRIM(i.bistamp)), LTRIM(RTRIM(i.ref)), LTRIM(RTRIM(i.design)), LTRIM(RTRIM(ISNULL(i.lobs3, ''))), i.qtt
            FROM bi i WITH (NOLOCK) WHERE i.bostamp = @id AND i.ndos = @ndos ORDER BY i.lordem
            """, ("@id", id), ("@ndos", RegrasInventario.Ndos)))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) linhas.Add(new LinhaInventarioDto(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDecimal(4)));
        return new InventarioDetalheDto(inv, linhas);
    }

    public Task<InventarioDetalheDto?> ObterAsync(string id, int tecno, CancellationToken ct) =>
        Executar(conn => LerDetalheAsync(conn, null, id, tecno, ct));

    // Corre a escrita numa transação com o dossier bloqueado (UPDLOCK): dono e estado verificam-se no mesmo instante em que se grava,
    // para uma conclusão em simultâneo não deixar entrar linhas depois de concluído.
    private async Task<ResultadoInventario> EscreverAsync(string id, int tecno, CancellationToken ct,
        Func<SqlConnection, SqlTransaction, Task<ResultadoInventario?>> escrita) => await Executar(async conn =>
    {
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            bool concluido, lancado;
            string dono;
            await using (var cmd = Cmd(conn, tx, """
                SELECT LTRIM(RTRIM(ISNULL(maquina, ''))), CAST(ISNULL(logi1, 0) AS bit), CAST(ISNULL(logi2, 0) AS bit)
                FROM bo WITH (UPDLOCK, ROWLOCK) WHERE bostamp = @id AND ndos = @ndos
                """, ("@id", id), ("@ndos", RegrasInventario.Ndos)))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) { await RollbackSeguroAsync(tx); return ResultadoInventario.De(EstadoInventario.NaoExiste); }
                dono = r.GetString(0); concluido = r.GetBoolean(1); lancado = r.GetBoolean(2);
            }
            if (dono != tecno.ToString()) { await RollbackSeguroAsync(tx); return ResultadoInventario.De(EstadoInventario.OutroTecnico, RegrasInventario.ErroOutroTecnico); }
            if (concluido || lancado) { await RollbackSeguroAsync(tx); return ResultadoInventario.De(EstadoInventario.Fechado, RegrasInventario.ErroFechado); }

            var recusa = await escrita(conn, tx);
            if (recusa is not null) { await RollbackSeguroAsync(tx); return recusa; }
            var detalhe = await LerDetalheAsync(conn, tx, id, tecno, ct);
            await tx.CommitAsync(ct);
            return ResultadoInventario.Sucesso(detalhe!);
        }
        catch (Exception ex) when (ex is not RepositorioIndisponivelException)
        {
            await RollbackSeguroAsync(tx);
            if (ex is SqlException { Number: 50000 } negocio) return ResultadoInventario.De(EstadoInventario.Invalido, negocio.Message);
            throw;
        }
    });

    // Tamanhos reais das colunas (lobs3 e ref da linha, maquina do dossier) antes de gravar: texto que não cabe seria cortado ou daria erro.
    private static async Task<string?> VerificarTamanhosAsync(SqlConnection conn, SqlTransaction tx, string refPeca, string? lugar, int tecno, CancellationToken ct)
    {
        await using var cmd = Cmd(conn, tx, "SELECT COL_LENGTH('bi', 'lobs3'), COL_LENGTH('bi', 'ref'), COL_LENGTH('bo', 'maquina')");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        int? Ler(int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
        var lobs = Ler(0); var refc = Ler(1); var maquina = Ler(2);
        if (maquina is null) return "O PHC não tem o campo bo.maquina: não é possível ligar o inventário ao técnico.";
        if (maquina < tecno.ToString().Length) return "O campo bo.maquina do PHC é pequeno de mais para o número do técnico.";
        if (lugar is not null)
        {
            if (lobs is null) return "O PHC não tem o campo bi.lobs3 nas linhas do dossier.";
            if (lobs < lugar.Length) return $"O lugar «{lugar}» não cabe no campo bi.lobs3 do PHC ({lobs} caracteres).";
            if (refc is not null && refc < refPeca.Length) return "A referência não cabe no campo bi.ref do PHC.";
        }
        return null;
    }

    public Task<ResultadoInventario> GravarLinhaAsync(string id, int tecno, string refPeca, string local, decimal qtt, string iniciais, CancellationToken ct) =>
        EscreverAsync(id, tecno, ct, async (conn, tx) =>
        {
            iniciais = string.IsNullOrWhiteSpace(iniciais) ? "XX" : iniciais.Trim();
            if (await VerificarTamanhosAsync(conn, tx, refPeca, local, tecno, ct) is { } erroTamanho)
                return ResultadoInventario.De(EstadoInventario.Invalido, erroTamanho);

            await using (var peca = Cmd(conn, tx, "SELECT COUNT(*) FROM st WITH (NOLOCK) WHERE ref = @ref", ("@ref", refPeca)))
                if ((int)(await peca.ExecuteScalarAsync(ct))! == 0) return ResultadoInventario.De(EstadoInventario.PecaInexistente, "Peça não encontrada.");

            // A mesma peça e lugar soma-se à linha que já existe (nunca duplica); aceita o lugar escrito com ou sem o prefixo LOC-.
            string? existente = null;
            decimal atual = 0;
            await using (var cmd = Cmd(conn, tx, """
                SELECT TOP 1 i.bistamp, i.qtt FROM bi i WITH (UPDLOCK, ROWLOCK)
                WHERE i.bostamp = @id AND i.ndos = @ndos AND UPPER(LTRIM(RTRIM(i.ref))) = UPPER(@ref)
                  AND UPPER(LTRIM(RTRIM(ISNULL(i.lobs3, '')))) IN (@lugar, @etiqueta)
                ORDER BY i.lordem
                """, ("@id", id), ("@ndos", RegrasInventario.Ndos), ("@ref", refPeca), ("@lugar", local), ("@etiqueta", "LOC-" + local)))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                if (await r.ReadAsync(ct)) { existente = r.GetString(0); atual = r.GetDecimal(1); }

            if (existente is not null)
            {
                var soma = atual + qtt;
                if (soma > RegrasInventario.QuantidadeMaxima) return ResultadoInventario.De(EstadoInventario.Invalido, "A quantidade somada é demasiado grande.");
                await AtualizarQuantidadeAsync(conn, tx, existente, soma, iniciais, ct);
                return null;
            }

            // Mesmo padrão da linha do dossier 63 (SqlRepositorioPecas.InserirLinhaAsync), sem emconf e sem toques: ndos 65 não move stock.
            await using var ins = Cmd(conn, tx, $"""
                DECLARE @bistamp varchar(25) = @novo;
                DECLARE @lordem decimal(10,0) = (SELECT ISNULL(MAX(lordem) + 10000, 10000) FROM bi WITH (NOLOCK) WHERE bostamp = @id);
                DECLARE @ordem decimal(10,0) = (SELECT ISNULL(MAX(binum1) + 1, 1) FROM bi WITH (NOLOCK) WHERE bostamp = @id);
                DECLARE @obrano decimal(10,0) = (SELECT obrano FROM bo WITH (NOLOCK) WHERE bostamp = @id);
                DECLARE @nmdos varchar(60) = (SELECT nmdos FROM bo WITH (NOLOCK) WHERE bostamp = @id);
                DECLARE @design varchar(60), @unidade varchar(4), @epcusto decimal(19,6), @pcusto decimal(19,6), @edebito decimal(19,6), @debito decimal(19,6);
                SELECT @design = LTRIM(RTRIM(design)), @unidade = LTRIM(RTRIM(unidade)), @epcusto = epcpond, @pcusto = pcpond, @edebito = epvultimo, @debito = pvultimo
                FROM st WITH (NOLOCK) WHERE ref = @ref;
                IF @design IS NULL
                BEGIN
                    RAISERROR(N'Peça não encontrada.', 16, 1);
                    RETURN;
                END
                INSERT INTO bi (bistamp, ref, design, qtt, unidade, edebito, debito, armazem, iva, tabiva, rdata, lordem, bostamp, obranome,
                                dataobra, obrano, ndos, nmdos, lote, usalote, binum1, lobs3, epcusto, pcusto, ttdeb, ettdeb,
                                ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
                VALUES (@bistamp, @ref, @design, @qtt, ISNULL(NULLIF(@unidade, ''), 'UN'), @edebito, @debito, @armazem, 0, 0, GETDATE(), @lordem, @id, '',
                        GETDATE(), @obrano, @ndos, @nmdos, '', 0, @ordem, @lobs3, @epcusto, @pcusto, @qtt * @debito, @qtt * @edebito,
                        @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora});
                INSERT INTO bi2 (bi2stamp) VALUES (@bistamp);
                """, ("@novo", Guid.NewGuid().ToString("N")[..25]), ("@id", id), ("@ndos", RegrasInventario.Ndos), ("@ref", refPeca), ("@qtt", qtt),
                     ("@lobs3", local), ("@iniciais", iniciais), ("@armazem", RegrasPecas.Armazem));
            await ins.ExecuteNonQueryAsync(ct);
            return null;
        });

    private static async Task AtualizarQuantidadeAsync(SqlConnection conn, SqlTransaction tx, string linhaId, decimal qtt, string iniciais, CancellationToken ct)
    {
        await using var cmd = Cmd(conn, tx, $"""
            UPDATE bi SET qtt = @qtt, ttdeb = @qtt * debito, ettdeb = @qtt * edebito, usrinis = @iniciais, usrdata = {Hoje}, usrhora = {Hora}
            WHERE bistamp = @linha
            """, ("@qtt", qtt), ("@iniciais", iniciais), ("@linha", linhaId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> LinhaExisteAsync(SqlConnection conn, SqlTransaction tx, string id, string linhaId, CancellationToken ct)
    {
        await using var cmd = Cmd(conn, tx, "SELECT COUNT(*) FROM bi WITH (UPDLOCK, ROWLOCK) WHERE bistamp = @linha AND bostamp = @id AND ndos = @ndos",
            ("@linha", linhaId), ("@id", id), ("@ndos", RegrasInventario.Ndos));
        return (int)(await cmd.ExecuteScalarAsync(ct))! > 0;
    }

    public Task<ResultadoInventario> AlterarQuantidadeAsync(string id, string linhaId, int tecno, decimal qtt, string iniciais, CancellationToken ct) =>
        EscreverAsync(id, tecno, ct, async (conn, tx) =>
        {
            if (!await LinhaExisteAsync(conn, tx, id, linhaId, ct)) return ResultadoInventario.De(EstadoInventario.LinhaInexistente);
            await AtualizarQuantidadeAsync(conn, tx, linhaId, qtt, string.IsNullOrWhiteSpace(iniciais) ? "XX" : iniciais.Trim(), ct);
            return null;
        });

    public Task<ResultadoInventario> ApagarLinhaAsync(string id, string linhaId, int tecno, string iniciais, CancellationToken ct) =>
        EscreverAsync(id, tecno, ct, async (conn, tx) =>
        {
            if (!await LinhaExisteAsync(conn, tx, id, linhaId, ct)) return ResultadoInventario.De(EstadoInventario.LinhaInexistente);
            await using var cmd = Cmd(conn, tx, "DELETE FROM bi2 WHERE bi2stamp = @linha; DELETE FROM bi WHERE bistamp = @linha AND bostamp = @id AND ndos = @ndos;",
                ("@linha", linhaId), ("@id", id), ("@ndos", RegrasInventario.Ndos));
            await cmd.ExecuteNonQueryAsync(ct);
            return null;
        });

    public Task<ResultadoInventario> ConcluirAsync(string id, int tecno, string iniciais, CancellationToken ct) =>
        EscreverAsync(id, tecno, ct, async (conn, tx) =>
        {
            await using (var n = Cmd(conn, tx, "SELECT COUNT(*) FROM bi WITH (NOLOCK) WHERE bostamp = @id AND ndos = @ndos", ("@id", id), ("@ndos", RegrasInventario.Ndos)))
                if ((int)(await n.ExecuteScalarAsync(ct))! == 0) return ResultadoInventario.De(EstadoInventario.Invalido, RegrasInventario.ErroSemLinhas);
            await using var cmd = Cmd(conn, tx, $"""
                UPDATE bo SET logi1 = 1, usrinis = @iniciais, usrdata = {Hoje}, usrhora = {Hora}
                WHERE bostamp = @id AND ndos = @ndos AND ISNULL(logi1, 0) = 0 AND ISNULL(logi2, 0) = 0
                """, ("@iniciais", string.IsNullOrWhiteSpace(iniciais) ? "XX" : iniciais.Trim()), ("@id", id), ("@ndos", RegrasInventario.Ndos));
            return await cmd.ExecuteNonQueryAsync(ct) == 1 ? null : ResultadoInventario.De(EstadoInventario.Fechado, RegrasInventario.ErroFechado);
        });
}
