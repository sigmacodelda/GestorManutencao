using Microsoft.Data.SqlClient;

namespace GestorManutencao.Api;

// Dossier 66 (arrumação): um por movimento, com uma linha bi com a peça, a quantidade, a origem (bi.lobs) e o destino (bi.lobs2).
// O saldo na u_pecaloc é movido pelo trigger sc_bi_pecaloc; a app só regista o movimento.
public sealed partial class SqlRepositorioLocalizacoes
{
    // Devolve null se criou o dossier, ou a mensagem do que falta no PHC. Corre dentro da transação do movimento.
    private static async Task<string?> CriarMovimentoAsync(SqlConnection conn, SqlTransaction tx, string tipo, string refPeca, string origem, string destino,
        decimal qtt, string iniciais, CancellationToken ct)
    {
        int ndos = RegrasLocalizacao.NdosArrumacao;
        await using (var ts = Cmd(conn, tx, "SELECT COUNT(*) FROM ts WITH (NOLOCK) WHERE ndos = @n", ("@n", ndos)))
            if ((int)(await ts.ExecuteScalarAsync(ct))! == 0) return $"Falta criar o tipo de dossier {ndos} (arrumação) no PHC.";

        // bi.lobs e bi.lobs2 (varchar(40) nesta base): o tamanho vem do esquema (-1 = max, nunca corta).
        var tamanhos = new Dictionary<string, int>();
        await using (var t = Cmd(conn, tx, @"SELECT TABLE_NAME + '.' + COLUMN_NAME, ISNULL(CHARACTER_MAXIMUM_LENGTH, 0) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE (TABLE_NAME = 'bo' AND COLUMN_NAME = 'obranome') OR (TABLE_NAME = 'bi' AND COLUMN_NAME IN ('lobs', 'lobs2'))"))
        await using (var r = await t.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) tamanhos[r.GetString(0)] = Convert.ToInt32(r.GetValue(1));
        var falta = new[] { "bi.lobs", "bi.lobs2" }.Where(c => !tamanhos.ContainsKey(c)).ToList();
        if (falta.Count > 0) return "A tabela bi não tem o(s) campo(s): " + string.Join(", ", falta.Select(c => c[3..])) + ".";

        var bostamp = Guid.NewGuid().ToString("N")[..25];
        var obranome = RegrasPecas.AjustarACampo($"{tipo} {refPeca.Trim()}", tamanhos.GetValueOrDefault("bo.obranome", 60)) ?? "";
        await using (var bo = Cmd(conn, tx, $"""
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
            """, ("@bostamp", bostamp), ("@ndos", ndos), ("@obranome", obranome), ("@iniciais", iniciais ?? "")))
            await bo.ExecuteNonQueryAsync(ct);

        // Linha sem conferência (emconf = 0), com a origem em lobs e o destino em lobs2 (é o que o trigger lê);
        // RAISERROR classe 16 (50000 não: apanha-se como falha do pedido) se a referência não existir.
        await using var bi = Cmd(conn, tx, $"""
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
            VALUES (@bistamp, 0, @ref, @design, @qtt, ISNULL(NULLIF(@unidade, ''), 'UN'), @edebito, @debito, @armazem, 0, 0, GETDATE(), 10000, @bostamp, '',
                    GETDATE(), @obrano, @ndos, @nmdos, '', 0, 1, @lobs, @lobs2, @epcusto, @pcusto, @qtt * @debito, @qtt * @edebito,
                    @iniciais, {Hoje}, {Hora}, @iniciais, {Hoje}, {Hora});
            INSERT INTO bi2 (bi2stamp) VALUES (@bistamp);
            """, ("@bistamp", Guid.NewGuid().ToString("N")[..25]), ("@bostamp", bostamp), ("@ndos", ndos), ("@ref", refPeca.Trim()), ("@qtt", qtt),
                 ("@iniciais", iniciais ?? ""), ("@armazem", RegrasPecas.Armazem),
                 ("@lobs", RegrasPecas.AjustarACampo(origem, tamanhos["bi.lobs"]) ?? ""), ("@lobs2", RegrasPecas.AjustarACampo(destino, tamanhos["bi.lobs2"]) ?? ""));
        try { await bi.ExecuteNonQueryAsync(ct); }
        catch (SqlException ex) when (ex.Number == 50000 || ex.Class == 16 && ex.Message.StartsWith("Referência inexistente", StringComparison.Ordinal))
        {
            return "Referência inexistente na ST.";
        }
        return null;
    }
}
