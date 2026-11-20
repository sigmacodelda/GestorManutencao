using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Peças e movimentos de armazém por dossier PHC (63 saída por ficha, 64 entrada por dia), como a LandeiroMaui.
// Os "toques" (UPDATE x = x) fazem os triggers do PHC moverem o stock. Só na base de testes até estar validado.
public sealed class SqlRepositorioPecas(IConfiguration configuracao, ILogger<SqlRepositorioPecas> log) : IRepositorioPecas
{
    private const string Hoje = "CAST(CONVERT(date, GETDATE()) AS datetime)";
    private const string Hora = "CONVERT(varchar(8), GETDATE(), 108)";
    private static bool _campoExiste;

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
            log.LogError("Falha SQL nas peças (erro {Numero}).", ex.Number);
            throw new RepositorioIndisponivelException(ex);
        }
    }

    private static SqlCommand Cmd(SqlConnection conn, SqlTransaction? tx, string sql, params (string Nome, object? Valor)[] ps)
    {
        var cmd = new SqlCommand(sql, conn, tx);
        foreach (var (nome, valor) in ps) Parametro(cmd, nome, valor);
        return cmd;
    }

    // Texto como varchar com tamanho (escalões, para o plano de execução não variar) e quantidade como decimal(16,3).
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
        // Um trigger do PHC pode já ter feito ROLLBACK: o segundo falha e não interessa.
        try { await tx.RollbackAsync(CancellationToken.None); }
        catch (Exception) { }
    }

    // Só o texto de RAISERROR/THROW (50000) vai ao cliente; o resto fica no log, sem mensagem.
    private ResultadoMovimento Falhou(Exception ex)
    {
        if (ex is SqlException { Number: 50000 } negocio) return ResultadoMovimento.Falha(negocio.Message);
        if (ex is SqlException sql) log.LogError("Falha SQL no movimento de peças (erro {Numero}).", sql.Number);
        else log.LogError("Falha no movimento de peças: {Mensagem}", ex.Message);
        return ResultadoMovimento.Falha("Não foi possível gravar o movimento.");
    }

    private static bool ErroDeNegocio(Exception ex) => ex is InvalidOperationException or SqlException { Number: 50000 } or SqlException { Class: 16 };

    // Serializa a criação do dossier do dia/da ficha; liberta-se no commit/rollback.
    private static async Task<bool> BloquearAsync(SqlConnection conn, SqlTransaction tx, int ndos, CancellationToken ct)
    {
        await using var cmd = Cmd(conn, tx, "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; SELECT @r;",
            ("@res", $"pecas-dossier-{ndos}"));
        return (int)(await cmd.ExecuteScalarAsync(ct))! >= 0;
    }

    // Mesmo critério da listagem: marcada em stobs.u_manut e não inativa.
    private static async Task<bool> EPecaAsync(SqlConnection conn, SqlTransaction tx, string refPeca, CancellationToken ct)
    {
        if (!await CampoExisteAsync(conn, ct, tx)) return false;
        await using var cmd = Cmd(conn, tx, $"SELECT COUNT(*) FROM st s WITH (NOLOCK) JOIN stobs o WITH (NOLOCK) ON o.ref = s.ref WHERE s.ref = @ref AND o.[{RegrasPecas.CampoStobs}] = 1 AND s.inactivo = 0",
            ("@ref", refPeca));
        return (int)(await cmd.ExecuteScalarAsync(ct))! > 0;
    }

    private static async Task<bool> CampoExisteAsync(SqlConnection conn, CancellationToken ct, SqlTransaction? tx = null)
    {
        if (_campoExiste) return true;
        await using var cmd = Cmd(conn, tx, $"SELECT COL_LENGTH('stobs', '{RegrasPecas.CampoStobs}')");
        _campoExiste = await cmd.ExecuteScalarAsync(ct) is not (null or DBNull);
        return _campoExiste;
    }

    private const string Colunas = """
        LTRIM(RTRIM(s.ref)) AS Ref, LTRIM(RTRIM(s.design)) AS Design, LTRIM(RTRIM(s.codigo)) AS Codigo,
        LTRIM(RTRIM(s.local)) AS LocalArtigo,
        CAST(COALESCE(NULLIF(s.epcpond, 0), NULLIF(s.epcult, 0), NULLIF(s.epcusto, 0), 0) AS decimal(19, 6)) AS Preco,
        ISNULL(a.stock, 0) AS Stock, ISNULL(a.stmin, 0) AS StMin, LTRIM(RTRIM(ISNULL(a.local, ''))) AS LocalArm
        """;

    private const string De = $"""
        FROM st s WITH (NOLOCK)
        JOIN stobs o WITH (NOLOCK) ON o.ref = s.ref
        LEFT JOIN sa a WITH (NOLOCK) ON a.ref = s.ref AND a.armazem = @arm
        """;

    private static PecaDto Ler(SqlDataReader r)
    {
        var localArm = r.GetString(r.GetOrdinal("LocalArm"));
        return new PecaDto(r.GetString(r.GetOrdinal("Ref")), r.GetString(r.GetOrdinal("Design")), r.GetString(r.GetOrdinal("Codigo")),
            localArm != "" ? localArm : r.GetString(r.GetOrdinal("LocalArtigo")),
            r.GetDecimal(r.GetOrdinal("Stock")), r.GetDecimal(r.GetOrdinal("StMin")), r.GetDecimal(r.GetOrdinal("Preco")));
    }

    public Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct) => Executar(async conn =>
    {
        if (!await CampoExisteAsync(conn, ct)) return new ResultadoPecasDto(false, [], false);
        var palavras = RegrasPecas.Palavras(texto);
        var filtros = palavras.Select((_, i) => $"(s.ref LIKE @t{i} ESCAPE '\\' OR s.design LIKE @t{i} ESCAPE '\\' OR s.codigo LIKE @t{i} ESCAPE '\\')");
        var onde = palavras.Length == 0 ? "" : " AND " + string.Join(" AND ", filtros);
        await using var cmd = Cmd(conn, null,
            $"SELECT TOP (@max) {Colunas} {De} WHERE o.[{RegrasPecas.CampoStobs}] = 1 AND s.inactivo = 0{onde} ORDER BY s.ref",
            ("@max", maximo + 1), ("@arm", RegrasPecas.Armazem));
        for (var i = 0; i < palavras.Length; i++) Parametro(cmd, $"@t{i}", "%" + RegrasPecas.EscaparLike(palavras[i]) + "%");
        var lista = new List<PecaDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(Ler(r));
        return new ResultadoPecasDto(true, lista.Take(maximo).ToList(), lista.Count > maximo);
    });

    public Task<PecaDto?> ObterPorCodigoAsync(string codigo, CancellationToken ct) => Executar<PecaDto?>(async conn =>
    {
        codigo = codigo.Trim();
        if (codigo.Length == 0 || !await CampoExisteAsync(conn, ct)) return null;
        await using var cmd = Cmd(conn, null, $"""
            SELECT TOP 1 {Colunas} {De}
            WHERE o.[{RegrasPecas.CampoStobs}] = 1 AND s.inactivo = 0 AND (LTRIM(RTRIM(s.codigo)) = @c OR LTRIM(RTRIM(s.ref)) = @c)
            ORDER BY CASE WHEN LTRIM(RTRIM(s.codigo)) = @c THEN 0 ELSE 1 END
            """, ("@c", codigo), ("@arm", RegrasPecas.Armazem));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Ler(r) : null;
    });

    // ---------- Entrada (dossier 64, um por dia)

    public Task<ResultadoMovimento> DarEntradaAsync(string refPeca, decimal qtt, string iniciais, CancellationToken ct) => Executar(async conn =>
    {
        iniciais = string.IsNullOrWhiteSpace(iniciais) ? "XX" : iniciais.Trim();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            if (!await BloquearAsync(conn, tx, RegrasPecas.NdosEntrada, ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroOcupado); }
            if (!await EPecaAsync(conn, tx, refPeca, ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha("Referência não é uma peça de manutenção."); }
            await using var proc = Cmd(conn, tx, "SELECT TOP 1 bostamp FROM bo WITH (NOLOCK) WHERE ndos = @ndos AND CAST(dataopen AS date) = CAST(GETDATE() AS date) ORDER BY obrano",
                ("@ndos", RegrasPecas.NdosEntrada));
            var bostamp = await proc.ExecuteScalarAsync(ct) as string
                ?? await CriarDossierAsync(conn, tx, RegrasPecas.NdosEntrada, $"Entradas {DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}", iniciais, ct);
            var linha = await InserirLinhaAsync(conn, tx, bostamp, RegrasPecas.NdosEntrada, refPeca, qtt, "", iniciais, ct);
            await tx.CommitAsync(ct);
            return ResultadoMovimento.Sucesso(linha);
        }
        catch (Exception ex) when (ErroDeNegocio(ex))
        {
            await RollbackSeguroAsync(tx);
            return Falhou(ex);
        }
    });

    public Task<IReadOnlyList<EntradaPecaDto>> EntradasDeHojeAsync(CancellationToken ct) => Executar<IReadOnlyList<EntradaPecaDto>>(async conn =>
    {
        await using var cmd = Cmd(conn, null, """
            SELECT i.bistamp, LTRIM(RTRIM(i.ref)), LTRIM(RTRIM(i.design)), i.qtt, LTRIM(RTRIM(i.ousrinis)), LTRIM(RTRIM(i.ousrhora))
            FROM bi i WITH (NOLOCK) JOIN bo b WITH (NOLOCK) ON b.bostamp = i.bostamp
            WHERE b.ndos = @ndos AND CAST(b.dataopen AS date) = CAST(GETDATE() AS date)
            ORDER BY i.ousrhora DESC, i.lordem DESC
            """, ("@ndos", RegrasPecas.NdosEntrada));
        var lista = new List<EntradaPecaDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(new EntradaPecaDto(r.GetString(0).Trim(), r.GetString(1), r.GetString(2), r.GetDecimal(3), r.GetString(4), r.GetString(5)));
        return lista;
    });

    public Task<ResultadoMovimento> AnularEntradaAsync(string id, CancellationToken ct) => ApagarLinhaAsync(id, RegrasPecas.NdosEntrada, ct);

    // ---------- Saída (dossier 63, um por ficha; ligação = bi.lobs = stamp do material)

    public Task<ResultadoMovimento> DarSaidaAsync(string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct) => Executar(async conn =>
    {
        if (!RegrasPecas.ReferenciaValida(refPeca) || !RegrasPecas.QuantidadeValida(qtt)) return ResultadoMovimento.Falha("Indique uma peça e uma quantidade positiva, com no máximo 3 casas decimais.");
        refPeca = refPeca.Trim();
        iniciais = string.IsNullOrWhiteSpace(iniciais) ? "XX" : iniciais.Trim();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            if (!await BloquearAsync(conn, tx, RegrasPecas.NdosSaida, ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroOcupado); }
            if (!await EPecaAsync(conn, tx, refPeca, ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha("Referência não é uma peça de manutenção."); }
            await using (var ja = Cmd(conn, tx, "SELECT COUNT(*) FROM bi WITH (NOLOCK) WHERE ndos = @ndos AND LTRIM(RTRIM(lobs)) = @m", ("@ndos", RegrasPecas.NdosSaida), ("@m", materialId)))
                if ((int)(await ja.ExecuteScalarAsync(ct))! > 0) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha("Já foi dada saída desta peça."); }

            await using var lobs = Cmd(conn, tx, "SELECT ISNULL(COL_LENGTH('bi', 'lobs'), 0)");
            if ((int)(await lobs.ExecuteScalarAsync(ct))! < 25) throw new InvalidOperationException("A coluna bi.lobs não comporta o stamp do material (25 caracteres).");

            string ficha, equip; decimal numero;
            await using (var f = Cmd(conn, tx, """
                SELECT mi.u_fichaintstamp, f.no, LTRIM(RTRIM(ISNULL(e.ref, f.ref))) + ' ' + LTRIM(RTRIM(ISNULL(e.design, f.design)))
                FROM u_matinterv mi WITH (NOLOCK)
                JOIN u_fichaint f WITH (NOLOCK) ON f.u_fichaintstamp = mi.u_fichaintstamp
                LEFT JOIN u_equip e WITH (NOLOCK) ON e.u_equipstamp = f.u_equipstamp
                WHERE mi.u_matintervstamp = @m
                """, ("@m", materialId)))
            {
                await using var r = await f.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha("O material já não existe."); }
                ficha = r.GetString(0); numero = r.GetDecimal(1); equip = r.GetString(2);
            }

            // Dossier da intervenção: o que já tem linhas de materiais desta ficha; senão cria-se.
            await using var proc = Cmd(conn, tx, """
                SELECT TOP 1 b.bostamp FROM bo b WITH (NOLOCK) JOIN bi i WITH (NOLOCK) ON i.bostamp = b.bostamp
                WHERE b.ndos = @ndos AND LTRIM(RTRIM(i.lobs)) IN (SELECT mi.u_matintervstamp FROM u_matinterv mi WITH (NOLOCK) WHERE mi.u_fichaintstamp = @ficha)
                ORDER BY b.obrano
                """, ("@ndos", RegrasPecas.NdosSaida), ("@ficha", ficha));
            var bostamp = await proc.ExecuteScalarAsync(ct) as string
                ?? await CriarDossierAsync(conn, tx, RegrasPecas.NdosSaida, $"Ficha {numero:0} {equip}", iniciais, ct);
            await InserirLinhaAsync(conn, tx, bostamp, RegrasPecas.NdosSaida, refPeca, qtt, materialId, iniciais, ct);

            await using var liga = Cmd(conn, tx, $"UPDATE u_matinterv SET ref = @ref, usrinis = @iniciais, usrdata = {Hoje}, usrhora = {Hora} WHERE u_matintervstamp = @m",
                ("@ref", refPeca), ("@iniciais", iniciais), ("@m", materialId));
            await liga.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return ResultadoMovimento.Sucesso(materialId);
        }
        catch (Exception ex) when (ErroDeNegocio(ex))
        {
            await RollbackSeguroAsync(tx);
            return Falhou(ex);
        }
    });

    public Task<ResultadoMovimento> AnularSaidaAsync(string materialId, CancellationToken ct) => Executar(async conn =>
    {
        await using var cmd = Cmd(conn, null, "SELECT bistamp FROM bi WITH (NOLOCK) WHERE ndos = @ndos AND LTRIM(RTRIM(lobs)) = @m", ("@ndos", RegrasPecas.NdosSaida), ("@m", materialId));
        var r = await cmd.ExecuteScalarAsync(ct) is string bistamp ? await ApagarLinhaAsync(bistamp, RegrasPecas.NdosSaida, ct) : ResultadoMovimento.Sucesso(materialId);
        // Sem saída a ligação à peça deixa de valer (senão a lista mostraria uma saída por dar).
        if (r.Ok)
        {
            await using var limpa = Cmd(conn, null, "UPDATE u_matinterv SET ref = '' WHERE u_matintervstamp = @m AND ref <> ''", ("@m", materialId));
            await limpa.ExecuteNonQueryAsync(ct);
        }
        return r;
    });

    public Task<IReadOnlyList<MaterialDto>> EnriquecerMateriaisAsync(IReadOnlyList<MaterialDto> materiais, CancellationToken ct) => Executar<IReadOnlyList<MaterialDto>>(async conn =>
    {
        if (materiais.Count == 0) return materiais;
        var ids = materiais.Select(m => m.Id).ToList();
        var nomes = string.Join(",", ids.Select((_, i) => $"@m{i}"));
        await using var cmd = Cmd(conn, null, $"""
            SELECT LTRIM(RTRIM(mi.u_matintervstamp)), LTRIM(RTRIM(ISNULL(mi.ref, ''))),
                   CASE WHEN EXISTS (SELECT 1 FROM bi i WITH (NOLOCK) WHERE i.ndos = @ndos AND LTRIM(RTRIM(i.lobs)) = LTRIM(RTRIM(mi.u_matintervstamp))) THEN 1 ELSE 0 END
            FROM u_matinterv mi WITH (NOLOCK) WHERE mi.u_matintervstamp IN ({nomes})
            """, ("@ndos", RegrasPecas.NdosSaida));
        for (var i = 0; i < ids.Count; i++) Parametro(cmd, $"@m{i}", ids[i]);
        var porId = new Dictionary<string, (string Ref, bool Saida)>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) porId[r.GetString(0)] = (r.GetString(1), r.GetInt32(2) == 1);
        return materiais.Select(m => porId.TryGetValue(m.Id, out var x) ? m with { Ref = x.Ref, SaidaDada = x.Saida } : m).ToList();
    });

    // ---------- Dossier e linhas (padrão do TransferenciasService da LandeiroMaui)

    private static async Task<string> CriarDossierAsync(SqlConnection conn, SqlTransaction tx, int ndos, string obranome, string iniciais, CancellationToken ct)
    {
        var bostamp = Guid.NewGuid().ToString("N")[..25];
        await using (var t = Cmd(conn, tx, "SELECT ISNULL(COL_LENGTH('bo', 'obranome'), 20)"))
        {
            var tamanho = (int)(await t.ExecuteScalarAsync(ct))!;
            if (tamanho > 0 && obranome.Length > tamanho) obranome = obranome[..tamanho].TrimEnd();
        }
        await using var bo = Cmd(conn, tx, $"""
            DECLARE @obrano numeric(10,0) = (SELECT ISNULL(MAX(obrano), 0) + 1 FROM bo WITH (NOLOCK) WHERE ndos = @ndos AND YEAR(dataobra) = YEAR(GETDATE()));
            INSERT INTO bo (bostamp, boano, obrano, obranome, dataobra, dataopen, memissao, origem, ndos, nmdos, ocupacao, lifref,
                            [no], estab, nome, moeda, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            SELECT @bostamp, YEAR(GETDATE()), @obrano, @obranome, CAST(GETDATE() AS date), GETDATE(), 'EURO', 'Gestor Manutencao',
                   ts.ndos, ts.nmdos, ts.ocupacao, ts.lifref, 1, 0, 'Carnes Landeiro, SA.', 'PTE ou EURO',
                   @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora}
            FROM ts WITH (NOLOCK) WHERE ts.ndos = @ndos;
            """, ("@bostamp", bostamp), ("@ndos", ndos), ("@obranome", obranome), ("@iniciais", iniciais));
        if (await bo.ExecuteNonQueryAsync(ct) == 0) throw new InvalidOperationException($"Tipo de dossier {ndos} inexistente (ts).");
        await using var extra = Cmd(conn, tx, $"""
            INSERT INTO bo2 (bo2stamp, idserie, xpdhora, tiposaft, ncin, ncout, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            SELECT @bostamp, 'BO', CONVERT(varchar(5), GETDATE(), 108), ts.tiposaft, ts.ncin, ts.ncout, @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora}
            FROM ts WITH (NOLOCK) WHERE ts.ndos = @ndos;
            INSERT INTO bo3 (bo3stamp, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            VALUES (@bostamp, @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora});
            UPDATE bo SET [no] = [no] WHERE bostamp = @bostamp;
            """, ("@bostamp", bostamp), ("@ndos", ndos), ("@iniciais", iniciais));
        await extra.ExecuteNonQueryAsync(ct);
        return bostamp;
    }

    // lobs = stamp do material (saída) ou vazio (entrada). Devolve o bistamp. RAISERROR classe 16 se a referência não existir.
    private static async Task<string> InserirLinhaAsync(SqlConnection conn, SqlTransaction tx, string bostamp, int ndos, string refPeca,
        decimal qtt, string lobs, string iniciais, CancellationToken ct)
    {
        var bistamp = Guid.NewGuid().ToString("N")[..25];
        await using var cmd = Cmd(conn, tx, $"""
            DECLARE @lordem decimal(10,0) = (SELECT ISNULL(MAX(lordem) + 10000, 10000) FROM bi WITH (NOLOCK) WHERE bostamp = @bostamp);
            DECLARE @ordem decimal(10,0) = (SELECT ISNULL(MAX(binum1) + 1, 1) FROM bi WITH (NOLOCK) WHERE bostamp = @bostamp);
            DECLARE @obrano decimal(10,0) = (SELECT obrano FROM bo WITH (NOLOCK) WHERE bostamp = @bostamp);
            DECLARE @nmdos varchar(60) = (SELECT nmdos FROM bo WITH (NOLOCK) WHERE bostamp = @bostamp);
            DECLARE @design varchar(60), @unidade varchar(4), @epcusto decimal(19,6), @pcusto decimal(19,6), @edebito decimal(19,6), @debito decimal(19,6);
            SELECT @design = LTRIM(RTRIM(design)), @unidade = LTRIM(RTRIM(unidade)), @epcusto = epcpond, @pcusto = pcpond, @edebito = epvultimo, @debito = pvultimo
            FROM st WITH (NOLOCK) WHERE ref = @ref;
            IF @design IS NULL
            BEGIN
                RAISERROR(N'Referência inexistente na ST.', 16, 1);
                RETURN;
            END
            INSERT INTO bi (bistamp, ref, design, qtt, unidade, edebito, debito, armazem, iva, tabiva, rdata, lordem, bostamp, obranome,
                            dataobra, obrano, ndos, nmdos, lote, usalote, binum1, lobs, epcusto, pcusto, ttdeb, ettdeb,
                            ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            VALUES (@bistamp, @ref, @design, @qtt, ISNULL(NULLIF(@unidade, ''), 'UN'), @edebito, @debito, @armazem, 0, 0, GETDATE(), @lordem, @bostamp, '',
                    GETDATE(), @obrano, @ndos, @nmdos, '', 0, @ordem, @lobs, @epcusto, @pcusto, @qtt * @debito, @qtt * @edebito,
                    @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora});
            INSERT INTO bi2 (bi2stamp) VALUES (@bistamp);
            UPDATE bo SET [no] = [no] WHERE bostamp = @bostamp;
            UPDATE bi SET [ref] = [ref] WHERE bostamp = @bostamp;
            """, ("@bistamp", bistamp), ("@bostamp", bostamp), ("@ndos", ndos), ("@ref", refPeca), ("@qtt", qtt),
                 ("@lobs", lobs), ("@iniciais", iniciais), ("@armazem", RegrasPecas.Armazem));
        await cmd.ExecuteNonQueryAsync(ct);
        return bistamp;
    }

    // Apaga a linha (e a bi2) e, se o dossier ficar sem linhas, o dossier (bo/bo2/bo3). Só linhas do ndos indicado.
    private Task<ResultadoMovimento> ApagarLinhaAsync(string bistamp, int ndos, CancellationToken ct) => Executar(async conn =>
    {
        bistamp = bistamp.Trim();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            if (!await BloquearAsync(conn, tx, ndos, ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroOcupado); }
            await using var proc = Cmd(conn, tx, "SELECT bostamp FROM bi WITH (NOLOCK) WHERE bistamp = @b AND ndos = @ndos", ("@b", bistamp), ("@ndos", ndos));
            if (await proc.ExecuteScalarAsync(ct) is not string bostamp) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroLinhaInexistente); }
            await using (var del = Cmd(conn, tx, "DELETE FROM bi2 WHERE bi2stamp = @b; DELETE FROM bi WHERE bistamp = @b;", ("@b", bistamp)))
                await del.ExecuteNonQueryAsync(ct);
            await using var restam = Cmd(conn, tx, "SELECT COUNT(*) FROM bi WITH (NOLOCK) WHERE bostamp = @s", ("@s", bostamp));
            if ((int)(await restam.ExecuteScalarAsync(ct))! == 0)
                await using (var d = Cmd(conn, tx, "DELETE FROM bo3 WHERE bo3stamp = @s; DELETE FROM bo2 WHERE bo2stamp = @s; DELETE FROM bo WHERE bostamp = @s;", ("@s", bostamp)))
                    await d.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return ResultadoMovimento.Sucesso(bistamp);
        }
        catch (Exception ex) when (ErroDeNegocio(ex))
        {
            await RollbackSeguroAsync(tx);
            return Falhou(ex);
        }
    });
}
