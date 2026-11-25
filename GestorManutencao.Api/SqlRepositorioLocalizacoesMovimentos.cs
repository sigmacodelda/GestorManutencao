using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Dossier 66 (arrumação): um por dia, com uma linha bi por movimento (peça e quantidade); origem em bi.lobs e destino em bi.lobs2.
// O stock na u_pecaloc passa a ser movido por um trigger do PHC; a app só regista o movimento.
public sealed partial class SqlRepositorioLocalizacoes
{
    // Devolve null se registou o movimento, ou a mensagem do que falta no PHC. Corre dentro da transação do movimento.
    // O dossier do dia cria-se no primeiro movimento; applock pelo dia para dois telemóveis não criarem dois.
    private static async Task<string?> CriarMovimentoAsync(SqlConnection conn, SqlTransaction tx, string tipo, string refPeca, string origem, string destino,
        decimal qtt, string iniciais, CancellationToken ct)
    {
        int ndos = RegrasLocalizacao.NdosArrumacao;
        await using (var ts = Cmd(conn, tx, "SELECT COUNT(*) FROM ts WITH (NOLOCK) WHERE ndos = @n", ("@n", ndos)))
            if ((int)(await ts.ExecuteScalarAsync(ct))! == 0) return $"Falta criar o tipo de dossier {ndos} (arrumação) no PHC.";

        // bi.lobs e bi.lobs2 podem ser varchar ou memo: o tamanho vem do esquema (memo = muito grande, nunca corta).
        var tamanhos = new Dictionary<string, int>();
        await using (var t = Cmd(conn, tx, "SELECT COLUMN_NAME, ISNULL(CHARACTER_MAXIMUM_LENGTH, 0) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'bi' AND COLUMN_NAME IN ('lobs', 'lobs2')"))
        await using (var r = await t.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) tamanhos[r.GetString(0)] = Convert.ToInt32(r.GetValue(1));
        var falta = new[] { "lobs", "lobs2" }.Where(c => !tamanhos.ContainsKey(c)).ToList();
        if (falta.Count > 0) return "A tabela bi não tem o(s) campo(s): " + string.Join(", ", falta) + ".";

        var hoje = DateTime.Today;
        await using (var bloqueio = Cmd(conn, tx, "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; SELECT @r;",
            ("@res", $"arrumacao-dossier-{hoje:yyyyMMdd}")))
            if ((int)(await bloqueio.ExecuteScalarAsync(ct))! < 0) return RegrasPecas.ErroOcupado;

        string? bostamp;
        await using (var existe = Cmd(conn, tx, "SELECT TOP 1 bostamp FROM bo WITH (UPDLOCK, HOLDLOCK) WHERE ndos = @ndos AND dataobra = CAST(GETDATE() AS date) ORDER BY obrano", ("@ndos", ndos)))
            bostamp = await existe.ExecuteScalarAsync(ct) as string;
        if (bostamp is null)
        {
            bostamp = Guid.NewGuid().ToString("N")[..25];
            int tamanhoNome;
            await using (var t = Cmd(conn, tx, "SELECT ISNULL(COL_LENGTH('bo', 'obranome'), 60)"))
                tamanhoNome = Convert.ToInt32(await t.ExecuteScalarAsync(ct));
            var obranome = RegrasPecas.AjustarACampo($"Arrumação {hoje:dd/MM/yyyy}", tamanhoNome) ?? "";
            await using var bo = Cmd(conn, tx, $"""
                DECLARE @obrano numeric(10,0) = (SELECT ISNULL(MAX(obrano), 0) + 1 FROM bo WITH (NOLOCK) WHERE ndos = @ndos AND YEAR(dataobra) = YEAR(GETDATE()));
                INSERT INTO bo (bostamp, emconf, boano, obrano, obranome, dataobra, dataopen, memissao, origem, ndos, nmdos, ocupacao, lifref,
                                [no], estab, nome, moeda, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
                SELECT @bostamp, 0, YEAR(GETDATE()), @obrano, @obranome, CAST(GETDATE() AS date), GETDATE(), 'EURO', 'Gestor Manutencao',
                       ts.ndos, ts.nmdos, ts.ocupacao, ts.lifref, 1, 0, 'Carnes Landeiro, SA.', 'PTE ou EURO',
                       @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora}
                FROM ts WITH (NOLOCK) WHERE ts.ndos = @ndos;
                INSERT INTO bo2 (bo2stamp, idserie, xpdhora, tiposaft, ncin, ncout, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
                SELECT @bostamp, 'BO', CONVERT(varchar(5), GETDATE(), 108), ts.tiposaft, ts.ncin, ts.ncout, @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora}
                FROM ts WITH (NOLOCK) WHERE ts.ndos = @ndos;
                INSERT INTO bo3 (bo3stamp, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
                VALUES (@bostamp, @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora});
                """, ("@bostamp", bostamp), ("@ndos", ndos), ("@obranome", obranome), ("@iniciais", iniciais ?? ""));
            await bo.ExecuteNonQueryAsync(ct);
        }

        // Linha sem conferência (emconf = 0): origem em lobs e destino em lobs2. RAISERROR classe 16 se a referência não existir.
        await using var bi = Cmd(conn, tx, $"""
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
                            dataobra, obrano, ndos, nmdos, lote, usalote, binum1, lobs, lobs2, epcusto, pcusto, ttdeb, ettdeb,
                            ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
            VALUES (@bistamp, 0, @ref, @design, @qtt, ISNULL(NULLIF(@unidade, ''), 'UN'), @edebito, @debito, @armazem, 0, 0, GETDATE(), @lordem, @bostamp, '',
                    GETDATE(), @obrano, @ndos, @nmdos, '', 0, @ordem, @lobs, @lobs2, @epcusto, @pcusto, @qtt * @debito, @qtt * @edebito,
                    @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora});
            INSERT INTO bi2 (bi2stamp) VALUES (@bistamp);
            """, ("@bistamp", Guid.NewGuid().ToString("N")[..25]), ("@bostamp", bostamp), ("@ndos", ndos), ("@ref", refPeca.Trim()), ("@qtt", qtt),
                 ("@lobs", RegrasPecas.AjustarACampo(origem, tamanhos["lobs"]) ?? ""), ("@lobs2", RegrasPecas.AjustarACampo(destino, tamanhos["lobs2"]) ?? ""),
                 ("@iniciais", iniciais ?? ""), ("@armazem", RegrasPecas.Armazem));
        try { await bi.ExecuteNonQueryAsync(ct); }
        catch (SqlException ex) when (ex.Number == 50000)
        {
            return "Referência inexistente na ST.";
        }
        return null;
    }
}
