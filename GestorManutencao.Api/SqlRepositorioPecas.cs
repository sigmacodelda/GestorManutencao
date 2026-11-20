using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Peças e conferência de saídas por dossier PHC (63, um por ficha; a entrada é da LandeiroMaui), como a LandeiroMaui.
// O dossier e as linhas nascem com emconf = 1 (sem mover stock); ao validar passam a 0 e os "toques" (UPDATE x = x) fazem
// os triggers do PHC moverem o stock. bo.bostamp = id da ficha (25 caracteres), para achar o dossier sem campo novo.
// Pressuposto por verificar na base de testes: emconf = 1 não move stock e 0 mais toques move.
public sealed class SqlRepositorioPecas(IConfiguration configuracao, ILogger<SqlRepositorioPecas> log) : IRepositorioPecas
{
    private const string Hoje = "CAST(CONVERT(date, GETDATE()) AS datetime)";
    private const string Hora = "CONVERT(varchar(8), GETDATE(), 108)";
    private static bool _campoExiste;
    private static bool _emconfExiste;

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

    // Sem bi.emconf a pesquisa continua a funcionar, com EmConferencia = 0.
    private static async Task<bool> EmconfExisteAsync(SqlConnection conn, CancellationToken ct, SqlTransaction? tx = null)
    {
        if (_emconfExiste) return true;
        await using var cmd = Cmd(conn, tx, "SELECT COL_LENGTH('bi', 'emconf')");
        _emconfExiste = await cmd.ExecuteScalarAsync(ct) is not (null or DBNull);
        return _emconfExiste;
    }

    private static string Colunas(bool emconf) => $"""
        LTRIM(RTRIM(s.ref)) AS Ref, LTRIM(RTRIM(s.design)) AS Design, LTRIM(RTRIM(s.codigo)) AS Codigo,
        LTRIM(RTRIM(s.local)) AS LocalArtigo,
        CAST(COALESCE(NULLIF(s.epcpond, 0), NULLIF(s.epcult, 0), NULLIF(s.epcusto, 0), 0) AS decimal(19, 6)) AS Preco,
        ISNULL(a.stock, 0) AS Stock, ISNULL(a.stmin, 0) AS StMin, LTRIM(RTRIM(ISNULL(a.local, ''))) AS LocalArm,
        {(emconf ? "ISNULL((SELECT SUM(i.qtt) FROM bi i WITH (NOLOCK) WHERE i.ndos = @ndos AND i.armazem = @arm AND i.emconf = 1 AND i.ref = s.ref), 0)" : "CAST(0 AS decimal(19, 6))")} AS EmConf
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
            r.GetDecimal(r.GetOrdinal("Stock")), r.GetDecimal(r.GetOrdinal("StMin")), r.GetDecimal(r.GetOrdinal("Preco")), r.GetDecimal(r.GetOrdinal("EmConf")));
    }

    public Task<ResultadoPecasDto> ListarAsync(string? texto, int maximo, CancellationToken ct) => Executar(async conn =>
    {
        if (!await CampoExisteAsync(conn, ct)) return new ResultadoPecasDto(false, [], false);
        var colunas = Colunas(await EmconfExisteAsync(conn, ct));
        var palavras = RegrasPecas.Palavras(texto);
        var filtros = palavras.Select((_, i) => $"(s.ref LIKE @t{i} ESCAPE '\\' OR s.design LIKE @t{i} ESCAPE '\\' OR s.codigo LIKE @t{i} ESCAPE '\\')");
        var onde = palavras.Length == 0 ? "" : " AND " + string.Join(" AND ", filtros);
        await using var cmd = Cmd(conn, null,
            $"SELECT TOP (@max) {colunas} {De} WHERE o.[{RegrasPecas.CampoStobs}] = 1 AND s.inactivo = 0{onde} ORDER BY s.ref",
            ("@max", maximo + 1), ("@arm", RegrasPecas.Armazem), ("@ndos", RegrasPecas.NdosSaida));
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
        var colunas = Colunas(await EmconfExisteAsync(conn, ct));
        await using var cmd = Cmd(conn, null, $"""
            SELECT TOP 1 {colunas} {De}
            WHERE o.[{RegrasPecas.CampoStobs}] = 1 AND s.inactivo = 0 AND (LTRIM(RTRIM(s.codigo)) = @c OR LTRIM(RTRIM(s.ref)) = @c)
            ORDER BY CASE WHEN LTRIM(RTRIM(s.codigo)) = @c THEN 0 ELSE 1 END
            """, ("@c", codigo), ("@arm", RegrasPecas.Armazem), ("@ndos", RegrasPecas.NdosSaida));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Ler(r) : null;
    });

    // ---------- Conferência (dossier 63, um por ficha; ligação da linha = bi.lobs = stamp do material)

    private static string StampFicha(string fichaId) => fichaId.Trim();

    private static string Iniciais(string iniciais) => string.IsNullOrWhiteSpace(iniciais) ? "XX" : iniciais.Trim();

    // null = sem dossier; senão diz se continua em conferência.
    private static async Task<bool?> EstadoDossierAsync(SqlConnection conn, SqlTransaction tx, string bostamp, CancellationToken ct)
    {
        await using var cmd = Cmd(conn, tx, "SELECT emconf FROM bo WITH (NOLOCK) WHERE bostamp = @b AND ndos = @ndos", ("@b", bostamp), ("@ndos", RegrasPecas.NdosSaida));
        return await cmd.ExecuteScalarAsync(ct) is { } v and not DBNull ? Convert.ToBoolean(v) : null;
    }

    // Cria o dossier da ficha em conferência se faltar; devolve se está em conferência (null = a ficha já não existe).
    private static async Task<bool?> GarantirDossierAsync(SqlConnection conn, SqlTransaction tx, string fichaId, string iniciais, CancellationToken ct)
    {
        var bostamp = StampFicha(fichaId);
        if (await EstadoDossierAsync(conn, tx, bostamp, ct) is { } emConf) return emConf;
        string equip; decimal numero;
        await using (var f = Cmd(conn, tx, """
            SELECT f.no, LTRIM(RTRIM(ISNULL(e.ref, f.ref))) + ' ' + LTRIM(RTRIM(ISNULL(e.design, f.design)))
            FROM u_fichaint f WITH (NOLOCK)
            LEFT JOIN u_equip e WITH (NOLOCK) ON e.u_equipstamp = f.u_equipstamp
            WHERE f.u_fichaintstamp = @ficha
            """, ("@ficha", bostamp)))
        {
            await using var r = await f.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            numero = r.GetDecimal(0); equip = r.GetString(1);
        }
        await CriarDossierAsync(conn, tx, bostamp, RegrasPecas.NdosSaida, $"Ficha {numero:0} {equip}", iniciais, ct);
        return true;
    }

    public Task<ResultadoMovimento> AbrirConferenciaAsync(string fichaId, string iniciais, CancellationToken ct) => Executar(async conn =>
    {
        iniciais = Iniciais(iniciais);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            if (!await BloquearAsync(conn, tx, RegrasPecas.NdosSaida, ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroOcupado); }
            if (!await EmconfExisteAsync(conn, ct, tx)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroSemEmconf); }
            if (await GarantirDossierAsync(conn, tx, fichaId, iniciais, ct) is null) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroSemFicha); }
            await tx.CommitAsync(ct);
            return ResultadoMovimento.Sucesso(StampFicha(fichaId));
        }
        catch (Exception ex) when (ErroDeNegocio(ex))
        {
            await RollbackSeguroAsync(tx);
            return Falhou(ex);
        }
    });

    public Task<ResultadoMovimento> DarSaidaAsync(string fichaId, string materialId, string refPeca, decimal qtt, string iniciais, CancellationToken ct) => Executar(async conn =>
    {
        if (!RegrasPecas.ReferenciaValida(refPeca) || !RegrasPecas.QuantidadeValida(qtt)) return ResultadoMovimento.Falha("Indique uma peça e uma quantidade positiva, com no máximo 3 casas decimais.");
        refPeca = refPeca.Trim();
        iniciais = Iniciais(iniciais);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            if (!await BloquearAsync(conn, tx, RegrasPecas.NdosSaida, ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroOcupado); }
            if (!await EmconfExisteAsync(conn, ct, tx)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroSemEmconf); }
            if (!await EPecaAsync(conn, tx, refPeca, ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha("Referência não é uma peça de manutenção."); }
            await using (var ja = Cmd(conn, tx, "SELECT COUNT(*) FROM bi WITH (NOLOCK) WHERE ndos = @ndos AND LTRIM(RTRIM(lobs)) = @m", ("@ndos", RegrasPecas.NdosSaida), ("@m", materialId)))
                if ((int)(await ja.ExecuteScalarAsync(ct))! > 0) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha("Já foi dada saída desta peça."); }

            await using var lobs = Cmd(conn, tx, "SELECT ISNULL(COL_LENGTH('bi', 'lobs'), 0)");
            if ((int)(await lobs.ExecuteScalarAsync(ct))! < 25) throw new InvalidOperationException("A coluna bi.lobs não comporta o stamp do material (25 caracteres).");

            await using (var m = Cmd(conn, tx, "SELECT COUNT(*) FROM u_matinterv WITH (NOLOCK) WHERE u_matintervstamp = @m AND u_fichaintstamp = @ficha", ("@m", materialId), ("@ficha", StampFicha(fichaId))))
                if ((int)(await m.ExecuteScalarAsync(ct))! == 0) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha("O material já não existe."); }

            var dossier = await GarantirDossierAsync(conn, tx, fichaId, iniciais, ct);
            if (dossier is null) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroSemFicha); }
            if (dossier is false) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroValidada); }
            await InserirLinhaAsync(conn, tx, StampFicha(fichaId), RegrasPecas.NdosSaida, refPeca, qtt, materialId, iniciais, ct);

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

    // Anula a linha em conferência e limpa a ligação à peça na mesma transação e sob o mesmo applock; o dossier fica.
    public Task<ResultadoMovimento> AnularSaidaAsync(string materialId, string iniciais, CancellationToken ct) => Executar(async conn =>
    {
        iniciais = Iniciais(iniciais);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            if (!await BloquearAsync(conn, tx, RegrasPecas.NdosSaida, ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroOcupado); }
            if (!await EmconfExisteAsync(conn, ct, tx)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroSemEmconf); }
            string? bistamp = null; var emConf = false;
            await using (var proc = Cmd(conn, tx, "SELECT bistamp, emconf FROM bi WITH (NOLOCK) WHERE ndos = @ndos AND LTRIM(RTRIM(lobs)) = @m", ("@ndos", RegrasPecas.NdosSaida), ("@m", materialId)))
            await using (var r = await proc.ExecuteReaderAsync(ct))
                if (await r.ReadAsync(ct)) { bistamp = r.GetString(0); emConf = Convert.ToBoolean(r.GetValue(1)); }
            if (bistamp is not null)
            {
                if (!emConf) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroValidada); }
                await using var del = Cmd(conn, tx, "DELETE FROM bi2 WHERE bi2stamp = @b; DELETE FROM bi WHERE bistamp = @b;", ("@b", bistamp.Trim()));
                await del.ExecuteNonQueryAsync(ct);
            }
            // Sem saída a ligação à peça deixa de valer (senão a lista mostraria uma saída por dar).
            await using var limpa = Cmd(conn, tx, $"UPDATE u_matinterv SET ref = '', usrinis = @iniciais, usrdata = {Hoje}, usrhora = {Hora} WHERE u_matintervstamp = @m AND ref <> ''",
                ("@iniciais", iniciais), ("@m", materialId));
            await limpa.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return ResultadoMovimento.Sucesso(materialId);
        }
        catch (Exception ex) when (ErroDeNegocio(ex))
        {
            await RollbackSeguroAsync(tx);
            return Falhou(ex);
        }
    });

    // Validar: emconf a 0 em bo e nas linhas e só depois os toques que fazem o PHC mover o stock. Sem dossier ou já validado não faz nada.
    public Task<ResultadoMovimento> ConfirmarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct) => Executar(async conn =>
    {
        iniciais = Iniciais(iniciais);
        var bostamp = StampFicha(fichaId);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            if (!await BloquearAsync(conn, tx, RegrasPecas.NdosSaida, ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroOcupado); }
            if (!await EmconfExisteAsync(conn, ct, tx)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroSemEmconf); }
            if (await EstadoDossierAsync(conn, tx, bostamp, ct) is not true) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Sucesso(bostamp); }
            await using var cmd = Cmd(conn, tx, $"""
                UPDATE bo SET emconf = 0, usrinis = @iniciais, usrdata = {Hoje}, usrhora = {Hora} WHERE bostamp = @b;
                UPDATE bi SET emconf = 0 WHERE bostamp = @b AND emconf = 1;
                UPDATE bo SET [no] = [no] WHERE bostamp = @b;
                UPDATE bi SET [ref] = [ref] WHERE bostamp = @b;
                """, ("@b", bostamp), ("@iniciais", iniciais));
            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return ResultadoMovimento.Sucesso(bostamp);
        }
        catch (Exception ex) when (ErroDeNegocio(ex))
        {
            await RollbackSeguroAsync(tx);
            return Falhou(ex);
        }
    });

    // Só cancela enquanto em conferência; a ligação dos materiais à peça também se limpa, como na anulação.
    public Task<ResultadoMovimento> CancelarConferenciaAsync(string fichaId, string iniciais, CancellationToken ct) => Executar(async conn =>
    {
        iniciais = Iniciais(iniciais);
        var bostamp = StampFicha(fichaId);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            if (!await BloquearAsync(conn, tx, RegrasPecas.NdosSaida, ct)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroOcupado); }
            if (!await EmconfExisteAsync(conn, ct, tx)) { await RollbackSeguroAsync(tx); return ResultadoMovimento.Falha(RegrasPecas.ErroSemEmconf); }
            var estado = await EstadoDossierAsync(conn, tx, bostamp, ct);
            if (estado is not true)
            {
                await RollbackSeguroAsync(tx);
                return estado is null ? ResultadoMovimento.Sucesso(bostamp) : ResultadoMovimento.Falha(RegrasPecas.ErroValidada);
            }
            await using var cmd = Cmd(conn, tx, $"""
                UPDATE u_matinterv SET ref = '', usrinis = @iniciais, usrdata = {Hoje}, usrhora = {Hora}
                WHERE ref <> '' AND u_matintervstamp IN (SELECT LTRIM(RTRIM(lobs)) FROM bi WHERE bostamp = @b);
                DELETE FROM bi2 WHERE bi2stamp IN (SELECT bistamp FROM bi WHERE bostamp = @b);
                DELETE FROM bi WHERE bostamp = @b;
                DELETE FROM bo3 WHERE bo3stamp = @b;
                DELETE FROM bo2 WHERE bo2stamp = @b;
                DELETE FROM bo WHERE bostamp = @b;
                """, ("@b", bostamp), ("@iniciais", iniciais));
            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return ResultadoMovimento.Sucesso(bostamp);
        }
        catch (Exception ex) when (ErroDeNegocio(ex))
        {
            await RollbackSeguroAsync(tx);
            return Falhou(ex);
        }
    });

    public Task<IReadOnlyDictionary<string, decimal>> EmConferenciaPorPecaAsync(IReadOnlyCollection<string> refs, string? excetoFichaId, CancellationToken ct) => Executar<IReadOnlyDictionary<string, decimal>>(async conn =>
    {
        var lista = refs.Select(r => r.Trim()).Where(RegrasPecas.ReferenciaValida).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var total = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (lista.Count == 0) return total;
        if (!await EmconfExisteAsync(conn, ct))
        {
            foreach (var x in lista) total[x] = 0;
            return total;
        }
        var nomes = string.Join(",", lista.Select((_, i) => $"@r{i}"));
        await using var cmd = Cmd(conn, null, $"""
            SELECT LTRIM(RTRIM(i.ref)), SUM(i.qtt) FROM bi i WITH (NOLOCK)
            WHERE i.ndos = @ndos AND i.armazem = @arm AND i.emconf = 1 AND i.ref IN ({nomes}) AND (@exceto = '' OR i.bostamp <> @exceto)
            GROUP BY LTRIM(RTRIM(i.ref))
            """, ("@ndos", RegrasPecas.NdosSaida), ("@arm", RegrasPecas.Armazem), ("@exceto", excetoFichaId is null ? "" : StampFicha(excetoFichaId)));
        for (var i = 0; i < lista.Count; i++) Parametro(cmd, $"@r{i}", lista[i]);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) total[r.GetString(0)] = r.GetDecimal(1);
        foreach (var x in lista) total.TryAdd(x, 0);
        return total;
    });

    // Grava só a intenção "peça por dar" (a saída falhou): a Ref fica ligada ao material, sem movimento.
    public Task<bool> LigarPecaAsync(string stampMaterial, string refPeca, string usrinis, CancellationToken ct) => Executar(async conn =>
    {
        if (!RegrasPecas.ReferenciaValida(refPeca)) return false;
        usrinis = string.IsNullOrWhiteSpace(usrinis) ? "XX" : usrinis.Trim();
        await using var cmd = Cmd(conn, null, $"UPDATE u_matinterv SET ref = @ref, usrinis = @iniciais, usrdata = {Hoje}, usrhora = {Hora} WHERE u_matintervstamp = @m",
            ("@ref", refPeca.Trim()), ("@iniciais", usrinis), ("@m", stampMaterial));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
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

    private static async Task CriarDossierAsync(SqlConnection conn, SqlTransaction tx, string bostamp, int ndos, string obranome, string iniciais, CancellationToken ct)
    {
        await using (var t = Cmd(conn, tx, "SELECT ISNULL(COL_LENGTH('bo', 'obranome'), 20)"))
        {
            var tamanho = (int)(await t.ExecuteScalarAsync(ct))!;
            if (tamanho > 0 && obranome.Length > tamanho) obranome = obranome[..tamanho].TrimEnd();
        }
        await using var bo = Cmd(conn, tx, $"""
            DECLARE @obrano numeric(10,0) = (SELECT ISNULL(MAX(obrano), 0) + 1 FROM bo WITH (NOLOCK) WHERE ndos = @ndos AND YEAR(dataobra) = YEAR(GETDATE()));
            INSERT INTO bo (bostamp, emconf, boano, obrano, obranome, dataobra, dataopen, memissao, origem, ndos, nmdos, ocupacao, lifref,
                            [no], estab, nome, moeda, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            SELECT @bostamp, 1, YEAR(GETDATE()), @obrano, @obranome, CAST(GETDATE() AS date), GETDATE(), 'EURO', 'Gestor Manutencao',
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
            """, ("@bostamp", bostamp), ("@ndos", ndos), ("@iniciais", iniciais));
        await extra.ExecuteNonQueryAsync(ct);
    }

    // Linha em conferência (emconf = 1, sem toques: não move stock). lobs = stamp do material. Devolve o bistamp. RAISERROR classe 16 se a referência não existir.
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
            INSERT INTO bi (bistamp, emconf, ref, design, qtt, unidade, edebito, debito, armazem, iva, tabiva, rdata, lordem, bostamp, obranome,
                            dataobra, obrano, ndos, nmdos, lote, usalote, binum1, lobs, epcusto, pcusto, ttdeb, ettdeb,
                            ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            VALUES (@bistamp, 1, @ref, @design, @qtt, ISNULL(NULLIF(@unidade, ''), 'UN'), @edebito, @debito, @armazem, 0, 0, GETDATE(), @lordem, @bostamp, '',
                    GETDATE(), @obrano, @ndos, @nmdos, '', 0, @ordem, @lobs, @epcusto, @pcusto, @qtt * @debito, @qtt * @edebito,
                    @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora});
            INSERT INTO bi2 (bi2stamp) VALUES (@bistamp);
            """, ("@bistamp", bistamp), ("@bostamp", bostamp), ("@ndos", ndos), ("@ref", refPeca), ("@qtt", qtt),
                 ("@lobs", lobs), ("@iniciais", iniciais), ("@armazem", RegrasPecas.Armazem));
        await cmd.ExecuteNonQueryAsync(ct);
        return bistamp;
    }
}
