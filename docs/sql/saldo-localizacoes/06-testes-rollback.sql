-- ======================================================================
-- SigmaCode, Lda
-- Cliente:   comum (Gestor de Manutencao)
-- Local:     SQL - testes do saldo por localizacao (so base TESTES_LAND2)
-- Trigger:   Execucao manual (depois de instalar 01 a 05)
-- Objetivo:  verificar sc_bi_pecaloc, sc_u_matinterv_del, sc_u_fichaint_del, as views e fn_sc_LerLugar
-- Autor:     SigmaCode
-- Historico:
--   v01  2026-11-26  Versao inicial
--   v02  2026-11-26  Casos 14 a 17 (qtt '' e NULL, UPDATE de ref e armazem, varias refs, buffer LOC-...); resumo falha com SALTADO ou FALHOU
-- ======================================================================
-- Cada caso corre dentro de BEGIN TRAN ... ROLLBACK (com TRY/CATCH) e nada fica gravado. Aborta se DB_NAME() <> 'TESTES_LAND2'.
-- Imprime esperado vs obtido por passo (PASSOU/FALHOU/SALTADO) e um resumo no fim. SALTADO = a base de testes nao tem dados para o caso.
-- O resumo final termina com erro se existir algum passo SALTADO ou FALHOU.
-- Os dossiers e a referencia (LIKE 'PE%A3', por causa dos acentos cp1252) vem da propria base. Lugares de teste: Z-90-1-01 a Z-90-1-03.
-- Os testes 7 apagam u_matinterv e u_fichaint reais dentro da transacao (dispara os triggers encriptados do PHC) e fazem rollback.
-- O que se mede no buffer e a diferenca face ao valor de partida (a base pode ter saldo no buffer).
-- Correr o ficheiro inteiro, com a mesma ligacao (os procedimentos #... vivem na sessao).
-- NAO COBERTO: concorrencia (duas transacoes em simultaneo, deadlock, corrida de INSERT do mesmo lugar). Estes testes correm numa so ligacao.

SET NOCOUNT ON;
IF DB_NAME() <> 'TESTES_LAND2'
BEGIN
    RAISERROR('Base errada: estes testes so correm em TESTES_LAND2.', 16, 1);
    SET NOEXEC ON;
END
SELECT DB_NAME() AS base;
GO

-- contexto da sessao
CREATE TABLE #ctx (b64 varchar(25), b66 varchar(25), b63 varchar(25), ref varchar(18), buf varchar(60),
                   mat varchar(25), matficha varchar(25), fichaf varchar(25));
INSERT INTO #ctx
SELECT (SELECT TOP 1 bostamp FROM bo WITH (NOLOCK) WHERE ndos = 64 ORDER BY bostamp),
       (SELECT TOP 1 bostamp FROM bo WITH (NOLOCK) WHERE ndos = 66 ORDER BY bostamp),
       (SELECT TOP 1 bostamp FROM bo WITH (NOLOCK) WHERE ndos = 63 AND emconf = 1 ORDER BY bostamp),
       (SELECT TOP 1 ref FROM u_pecaloc WITH (NOLOCK) WHERE ref LIKE 'PE%A3' ORDER BY ref),
       ISNULL((SELECT TOP 1 UPPER(LTRIM(RTRIM(ISNULL(localentrada, '')))) FROM u_configman WITH (NOLOCK)), ''),
       (SELECT TOP 1 m.u_matintervstamp FROM u_matinterv m WITH (NOLOCK) INNER JOIN bo o WITH (NOLOCK) ON o.bostamp = m.u_fichaintstamp
        WHERE o.ndos = 63 AND o.emconf = 1 ORDER BY m.u_matintervstamp),
       (SELECT TOP 1 m.u_fichaintstamp FROM u_matinterv m WITH (NOLOCK) INNER JOIN bo o WITH (NOLOCK) ON o.bostamp = m.u_fichaintstamp
        WHERE o.ndos = 63 AND o.emconf = 1 ORDER BY m.u_matintervstamp),
       (SELECT TOP 1 o.bostamp FROM bo o WITH (NOLOCK) INNER JOIN u_fichaint f WITH (NOLOCK) ON f.u_fichaintstamp = o.bostamp
        WHERE o.ndos = 63 AND o.emconf = 1 AND NOT EXISTS (SELECT 1 FROM bi x WITH (NOLOCK) WHERE x.bostamp = o.bostamp AND ISNULL(x.emconf, 0) = 0)
        ORDER BY o.bostamp);
SELECT * FROM #ctx;

CREATE TABLE #res (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));

-- pre-requisitos: objetos instalados, dossiers, referencia, buffer, lugares de teste livres
IF OBJECT_ID('dbo.fn_sc_LerLugar', 'TF') IS NULL OR OBJECT_ID('dbo.sc_bi_pecaloc', 'TR') IS NULL
   OR OBJECT_ID('dbo.sc_u_matinterv_del', 'TR') IS NULL OR OBJECT_ID('dbo.sc_u_fichaint_del', 'TR') IS NULL
   OR OBJECT_ID('dbo.v_sc_pecaloc_disponivel', 'V') IS NULL OR OBJECT_ID('dbo.v_sc_pecaloc_semlugar', 'V') IS NULL
   OR OBJECT_ID('dbo.v_sc_pecaloc_divergencias', 'V') IS NULL OR OBJECT_ID('dbo.fn_sc_QtdOk', 'FN') IS NULL
BEGIN
    RAISERROR('Faltam objetos: instalar 01 a 05 antes dos testes.', 16, 1);
    SET NOEXEC ON;
END
IF EXISTS (SELECT 1 FROM #ctx WHERE b64 IS NULL OR b66 IS NULL OR b63 IS NULL OR ref IS NULL OR buf = '')
BEGIN
    RAISERROR('Faltam dados na base de testes: dossier 64, 66, 63 (emconf = 1), referencia PE%%A3 ou u_configman.localentrada.', 16, 1);
    SET NOEXEC ON;
END
IF EXISTS (SELECT 1 FROM u_pecaloc u WITH (NOLOCK) INNER JOIN #ctx c ON LTRIM(RTRIM(u.ref)) = LTRIM(RTRIM(c.ref))
           WHERE LTRIM(RTRIM(u.corredor)) = 'Z' AND LTRIM(RTRIM(u.estante)) = 'Z-90')
BEGIN
    RAISERROR('Os lugares de teste Z-90-* ja existem para a referencia de teste: limpar antes de correr.', 16, 1);
    SET NOEXEC ON;
END
GO

-- inserir 1 a 3 linhas bi numa so instrucao (bi2 acompanha)
CREATE PROCEDURE #ins @id varchar(25), @ndos int, @emconf bit, @lobs varchar(40), @lobs2 varchar(40),
    @s1 varchar(25), @q1 decimal(19, 6), @s2 varchar(25) = NULL, @q2 decimal(19, 6) = NULL, @s3 varchar(25) = NULL, @q3 decimal(19, 6) = NULL, @arm int = 104
AS
BEGIN
    DECLARE @ref varchar(18);
    SET @ref = (SELECT ref FROM #ctx);
    DECLARE @lordem decimal(10, 0), @ordem decimal(10, 0), @obrano decimal(10, 0), @nmdos varchar(60);
    SET @lordem = (SELECT ISNULL(MAX(lordem) + 10000, 10000) FROM bi WITH (NOLOCK) WHERE bostamp = @id);
    SET @ordem = (SELECT ISNULL(MAX(binum1) + 1, 1) FROM bi WITH (NOLOCK) WHERE bostamp = @id);
    SET @obrano = (SELECT obrano FROM bo WITH (NOLOCK) WHERE bostamp = @id);
    SET @nmdos = (SELECT nmdos FROM bo WITH (NOLOCK) WHERE bostamp = @id);
    DECLARE @design varchar(60), @unidade varchar(4), @epcusto decimal(19, 6), @pcusto decimal(19, 6), @edebito decimal(19, 6), @debito decimal(19, 6);
    SELECT @design = LTRIM(RTRIM(design)), @unidade = LTRIM(RTRIM(unidade)), @epcusto = epcpond, @pcusto = pcpond, @edebito = epvultimo, @debito = pvultimo
    FROM st WITH (NOLOCK) WHERE ref = @ref;
    DECLARE @hoje datetime, @hora varchar(8);
    SET @hoje = CONVERT(datetime, CONVERT(char(10), GETDATE(), 120));
    SET @hora = CONVERT(varchar(8), GETDATE(), 108);

    INSERT INTO bi (bistamp, emconf, ref, design, qtt, unidade, edebito, debito, armazem, iva, tabiva, rdata, lordem, bostamp, obranome,
                    dataobra, obrano, ndos, nmdos, lote, usalote, binum1, lobs, lobs2, epcusto, pcusto, ttdeb, ettdeb,
                    ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
    SELECT v.s, @emconf, @ref, @design, v.q, ISNULL(NULLIF(@unidade, ''), 'UN'), @edebito, @debito, @arm, 0, 0, GETDATE(),
           CAST(@lordem + ROW_NUMBER() OVER (ORDER BY v.s) * 10000 AS decimal(10, 0)), @id, '',
           GETDATE(), @obrano, @ndos, @nmdos, '', 0, CAST(@ordem + ROW_NUMBER() OVER (ORDER BY v.s) AS decimal(10, 0)), @lobs, @lobs2,
           @epcusto, @pcusto, v.q * @debito, v.q * @edebito,
           'XP', @hoje, @hora, 'XP', @hoje, @hora
    FROM (VALUES (@s1, @q1), (@s2, @q2), (@s3, @q3)) v(s, q) WHERE v.s IS NOT NULL;

    INSERT INTO bi2 (bi2stamp) SELECT v.s FROM (VALUES (@s1), (@s2), (@s3)) v(s) WHERE v.s IS NOT NULL;
END
GO

-- apagar ate 3 linhas bi (e bi2) numa so instrucao
CREATE PROCEDURE #del @s1 varchar(25), @s2 varchar(25) = NULL, @s3 varchar(25) = NULL
AS
BEGIN
    DELETE FROM bi2 WHERE bi2stamp IN (@s1, @s2, @s3);
    DELETE FROM bi WHERE bistamp IN (@s1, @s2, @s3);
END
GO

-- le saldo, reservado e disponivel de um lugar (ou do buffer) pela view
CREATE PROCEDURE #g @ref varchar(18), @lug varchar(60), @q decimal(13, 3) OUTPUT, @r decimal(13, 3) OUTPUT, @d decimal(13, 3) OUTPUT
AS
BEGIN
    DECLARE @c varchar(30), @e varchar(30), @n int, @p int, @buf varchar(60);
    SET @buf = (SELECT buf FROM #ctx);
    SET @q = NULL; SET @r = NULL; SET @d = NULL;
    IF UPPER(LTRIM(RTRIM(@lug))) = @buf
    BEGIN
        SET @c = @buf; SET @e = ''; SET @n = 0; SET @p = 0;
    END
    ELSE
        SELECT @c = corredor, @e = estante, @n = nivel, @p = posicao FROM dbo.fn_sc_LerLugar(@lug);
    SELECT @q = qtt, @r = reservado, @d = disponivel FROM dbo.v_sc_pecaloc_disponivel
    WHERE ref = @ref AND armazem = 104 AND corredor = @c AND estante = @e AND nivel = @n AND posicao = @p;
END
GO

-- devolve linhas (caso, passo, esperado, obtido) para INSERT ... EXEC
CREATE PROCEDURE #t @caso varchar(8), @passo varchar(100), @ref varchar(18), @lug varchar(60), @esp varchar(30),
    @espr varchar(30) = NULL, @espd varchar(30) = NULL
AS
BEGIN
    DECLARE @q decimal(13, 3), @r decimal(13, 3), @d decimal(13, 3);
    EXEC #g @ref, @lug, @q OUTPUT, @r OUTPUT, @d OUTPUT;
    SELECT @caso AS caso, CAST(@passo + ' [saldo ' + @lug + ']' AS varchar(100)) AS passo, @esp AS esperado, ISNULL(CONVERT(varchar(30), @q), 'sem linha') AS obtido
    UNION ALL
    SELECT @caso, CAST(@passo + ' [reservado ' + @lug + ']' AS varchar(100)), @espr, ISNULL(CONVERT(varchar(30), @r), 'sem linha') WHERE @espr IS NOT NULL
    UNION ALL
    SELECT @caso, CAST(@passo + ' [disponivel ' + @lug + ']' AS varchar(100)), @espd, ISNULL(CONVERT(varchar(30), @d), 'sem linha') WHERE @espd IS NOT NULL;
END
GO

-- ===== Caso 1: entrada 64 (emconf = 0) soma ao buffer; apagar repoe =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @buf varchar(60), @b64 varchar(25), @q decimal(13, 3), @r decimal(13, 3), @d decimal(13, 3), @b0 decimal(13, 3), @e varchar(30), @e0 varchar(30);
SELECT @ref = ref, @buf = buf, @b64 = b64 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    EXEC #g @ref, @buf, @q OUTPUT, @r OUTPUT, @d OUTPUT; SET @b0 = @q;
    SET @e = CONVERT(varchar(30), ISNULL(@b0, 0) + 7); SET @e0 = ISNULL(CONVERT(varchar(30), @b0), 'sem linha');
    EXEC #ins @b64, 64, 0, '', '', 'XPT1', 7;
    INSERT @o EXEC #t 'C1', '64 emconf=0 insert', @ref, @buf, @e;
    UPDATE bi SET ref = ref WHERE bistamp = 'XPT1';
    INSERT @o EXEC #t 'C1', '64 depois de tocar', @ref, @buf, @e;
    EXEC #del 'XPT1';
    INSERT @o EXEC #t 'C1', '64 apagado repoe', @ref, @buf, @e0;
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C1', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 2: arrumacao 66 BUFFER -> lugar move; apagar repoe =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @buf varchar(60), @b64 varchar(25), @b66 varchar(25), @q decimal(13, 3), @r decimal(13, 3), @d decimal(13, 3), @b0 decimal(13, 3), @e varchar(30), @e0 varchar(30), @e1 varchar(30), @e5 varchar(30);
SELECT @ref = ref, @buf = buf, @b64 = b64, @b66 = b66 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    EXEC #g @ref, @buf, @q OUTPUT, @r OUTPUT, @d OUTPUT; SET @b0 = @q;
    SET @e0 = ISNULL(CONVERT(varchar(30), @b0), 'sem linha'); SET @e5 = CONVERT(varchar(30), ISNULL(@b0, 0) + 5); SET @e = CONVERT(varchar(30), ISNULL(@b0, 0) + 3);
    EXEC #ins @b64, 64, 0, '', '', 'XPT2A', 5;
    INSERT @o EXEC #t 'C2', '64 qtt 5', @ref, @buf, @e5;
    EXEC #ins @b66, 66, 0, @buf, 'A-01-2-01', 'XPT2B', 2;
    EXEC #ins @b66, 66, 0, @buf, 'Z-90-1-01', 'XPT2C', 2;
    SET @e1 = CONVERT(varchar(30), ISNULL(@b0, 0) + 1);
    INSERT @o EXEC #t 'C2', '66 BUFFER->Z-90-1-01 qtt 2 e BUFFER->A-01-2-01 qtt 2', @ref, @buf, @e1;
    EXEC #del 'XPT2B';
    INSERT @o EXEC #t 'C2', '66 para A-01-2-01 apagada', @ref, @buf, @e;
    INSERT @o EXEC #t 'C2', '66 destino', @ref, 'Z-90-1-01', '2.000';
    EXEC #del 'XPT2C';
    INSERT @o EXEC #t 'C2', '66 apagada: buffer volta', @ref, @buf, @e5;
    INSERT @o EXEC #t 'C2', '66 apagada: lugar sem linha', @ref, 'Z-90-1-01', 'sem linha';
    EXEC #del 'XPT2A';
    INSERT @o EXEC #t 'C2', '64 apagado', @ref, @buf, @e0;
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C2', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 3: saida 63 (emconf 0 desce; emconf 1 so reserva; validar desce uma vez; tocar nao repete) =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @buf varchar(60), @b64 varchar(25), @b66 varchar(25), @b63 varchar(25), @q decimal(13, 3), @r decimal(13, 3), @d decimal(13, 3);
SELECT @ref = ref, @buf = buf, @b64 = b64, @b66 = b66, @b63 = b63 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    EXEC #ins @b64, 64, 0, '', '', 'XPT3A', 10;
    EXEC #ins @b66, 66, 0, @buf, 'Z-90-1-01', 'XPT3B', 10;
    INSERT @o EXEC #t 'C3', 'semente: lugar com 10', @ref, 'Z-90-1-01', '10.000';
    EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-01', 'Z-90-1-01', 'XPT3C', 3;
    INSERT @o EXEC #t 'C3', '63 emconf=0 qtt 3 desce', @ref, 'Z-90-1-01', '7.000';
    UPDATE bi SET ref = ref WHERE bistamp = 'XPT3C';
    INSERT @o EXEC #t 'C3', '63 emconf=0 tocar nao desce outra vez', @ref, 'Z-90-1-01', '7.000';
    EXEC #del 'XPT3C';
    INSERT @o EXEC #t 'C3', '63 emconf=0 apagado repoe', @ref, 'Z-90-1-01', '10.000';

    EXEC #ins @b63, 63, 1, 'MATX|Z-90-1-01', 'Z-90-1-01', 'XPT3D', 4;
    INSERT @o EXEC #t 'C3', '63 emconf=1 insert: saldo igual, disponivel desce', @ref, 'Z-90-1-01', '10.000', '4.000', '6.000';
    UPDATE bi SET emconf = 0 WHERE bistamp = 'XPT3D';
    INSERT @o EXEC #t 'C3', 'validar (emconf 1->0): desce uma vez', @ref, 'Z-90-1-01', '6.000', '0.000', '6.000';
    UPDATE bi SET ref = ref WHERE bistamp = 'XPT3D';
    INSERT @o EXEC #t 'C3', 'tocar depois de validar: nao desce duas', @ref, 'Z-90-1-01', '6.000', '0.000', '6.000';
    UPDATE bi SET emconf = 1 WHERE bistamp = 'XPT3D';
    INSERT @o EXEC #t 'C3', 'emconf 0->1: repoe e volta a reservar', @ref, 'Z-90-1-01', '10.000', '4.000', '6.000';
    EXEC #del 'XPT3D';
    INSERT @o EXEC #t 'C3', '63 emconf=1 apagado: disponivel volta', @ref, 'Z-90-1-01', '10.000', '0.000', '10.000';

    EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-01', '', 'XPT3E', 1;
    INSERT @o EXEC #t 'C3', '63 emconf=0 so com lobs (sem lobs2)', @ref, 'Z-90-1-01', '9.000';
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C3', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 4: UPDATE de quantidade e de lugar =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @buf varchar(60), @b64 varchar(25), @b66 varchar(25), @b63 varchar(25), @q decimal(13, 3), @r decimal(13, 3), @d decimal(13, 3), @b0 decimal(13, 3), @e varchar(30), @n int;
SELECT @ref = ref, @buf = buf, @b64 = b64, @b66 = b66, @b63 = b63 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    EXEC #g @ref, @buf, @q OUTPUT, @r OUTPUT, @d OUTPUT; SET @b0 = @q;
    EXEC #ins @b64, 64, 0, '', '', 'XPT4A', 10;
    EXEC #ins @b66, 66, 0, @buf, 'Z-90-1-01', 'XPT4B', 10;
    EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-01', 'Z-90-1-01', 'XPT4C', 3;
    INSERT @o EXEC #t 'C4', 'partida', @ref, 'Z-90-1-01', '7.000';
    UPDATE bi SET qtt = 5 WHERE bistamp = 'XPT4C';
    INSERT @o EXEC #t 'C4', 'UPDATE qtt 3->5 no 63', @ref, 'Z-90-1-01', '5.000';
    UPDATE bi SET lobs = 'MATX|Z-90-1-02', lobs2 = 'Z-90-1-02' WHERE bistamp = 'XPT4C';
    INSERT @o EXEC #t 'C4', 'UPDATE lugar do 63: origem repoe', @ref, 'Z-90-1-01', '10.000';
    INSERT @o EXEC #t 'C4', 'UPDATE lugar do 63: novo lugar fica negativo', @ref, 'Z-90-1-02', '-5.000';
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_divergencias WHERE tipo = 'NEGATIVO' AND ref = LTRIM(RTRIM(@ref)) AND estante = 'Z-90');
    INSERT @o VALUES ('C4', 'divergencias: 1 NEGATIVO no lugar de teste', '1', CONVERT(varchar(10), @n));
    UPDATE bi SET qtt = 8 WHERE bistamp = 'XPT4B';
    INSERT @o EXEC #t 'C4', 'UPDATE qtt do 66 (10->8): destino', @ref, 'Z-90-1-01', '8.000';
    SET @e = CONVERT(varchar(30), ISNULL(@b0, 0) + 2);
    INSERT @o EXEC #t 'C4', 'UPDATE qtt do 66 (10->8): buffer (+10 -8)', @ref, @buf, @e;
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C4', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 5: varias linhas numa so instrucao (set-based) =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @buf varchar(60), @b64 varchar(25), @b66 varchar(25), @q decimal(13, 3), @r decimal(13, 3), @d decimal(13, 3), @b0 decimal(13, 3), @e varchar(30), @e0 varchar(30), @e6 varchar(30);
SELECT @ref = ref, @buf = buf, @b64 = b64, @b66 = b66 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    EXEC #g @ref, @buf, @q OUTPUT, @r OUTPUT, @d OUTPUT; SET @b0 = @q;
    SET @e0 = ISNULL(CONVERT(varchar(30), @b0), 'sem linha'); SET @e6 = CONVERT(varchar(30), ISNULL(@b0, 0) + 6);
    EXEC #ins @b64, 64, 0, '', '', 'XPT5A', 1, 'XPT5B', 2, 'XPT5C', 3;
    INSERT @o EXEC #t 'C5', 'INSERT de 3 linhas 64 (1+2+3)', @ref, @buf, @e6;
    UPDATE bi SET emconf = 1 WHERE bistamp IN ('XPT5A', 'XPT5B', 'XPT5C');
    INSERT @o EXEC #t 'C5', 'UPDATE emconf=1 das 3 numa instrucao', @ref, @buf, @e0;
    UPDATE bi SET emconf = 0 WHERE bistamp IN ('XPT5A', 'XPT5B', 'XPT5C');
    INSERT @o EXEC #t 'C5', 'UPDATE emconf=0 das 3 numa instrucao', @ref, @buf, @e6;
    EXEC #ins @b66, 66, 0, @buf, 'Z-90-1-01', 'XPT5D', 1, 'XPT5E', 2, 'XPT5F', 3;
    INSERT @o EXEC #t 'C5', 'INSERT de 3 linhas 66 para o mesmo lugar', @ref, 'Z-90-1-01', '6.000';
    INSERT @o EXEC #t 'C5', 'buffer depois das 3 arrumacoes', @ref, @buf, @e0;
    EXEC #del 'XPT5D', 'XPT5E', 'XPT5F';
    INSERT @o EXEC #t 'C5', 'DELETE das 3 linhas 66: lugar sem linha', @ref, 'Z-90-1-01', 'sem linha';
    INSERT @o EXEC #t 'C5', 'DELETE das 3 linhas 66: buffer volta', @ref, @buf, @e6;
    EXEC #del 'XPT5A', 'XPT5B', 'XPT5C';
    INSERT @o EXEC #t 'C5', 'DELETE das 3 linhas 64', @ref, @buf, @e0;
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C5', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 6: lobs vazio ou invalido grava e aparece em v_sc_pecaloc_semlugar =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @buf varchar(60), @b66 varchar(25), @b63 varchar(25), @q decimal(13, 3), @r decimal(13, 3), @d decimal(13, 3), @b0 decimal(13, 3), @e varchar(30), @n int;
SELECT @ref = ref, @buf = buf, @b66 = b66, @b63 = b63 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    EXEC #g @ref, @buf, @q OUTPUT, @r OUTPUT, @d OUTPUT; SET @b0 = @q;
    EXEC #ins @b63, 63, 0, '', '', 'XPT6A', 1;
    SET @n = (SELECT COUNT(*) FROM bi WHERE bistamp = 'XPT6A');
    INSERT @o VALUES ('C6', '63 com lobs vazio: o documento gravou', '1', CONVERT(varchar(10), @n));
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_semlugar WHERE bistamp = 'XPT6A');
    INSERT @o VALUES ('C6', '63 com lobs vazio: aparece em semlugar', '1', CONVERT(varchar(10), @n));
    EXEC #ins @b63, 63, 0, 'XYZ|lixo', '', 'XPT6B', 1;
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_semlugar WHERE bistamp = 'XPT6B');
    INSERT @o VALUES ('C6', '63 com lugar invalido: aparece em semlugar', '1', CONVERT(varchar(10), @n));
    EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-01', '', 'XPT6D', 1;
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_semlugar WHERE bistamp = 'XPT6D');
    INSERT @o VALUES ('C6', '63 com lugar valido: nao aparece em semlugar', '0', CONVERT(varchar(10), @n));
    INSERT @o EXEC #t 'C6', '63 ilegiveis nao mexem; o valido (-1) desce o lugar', @ref, 'Z-90-1-01', '-1.000';
    EXEC #ins @b66, 66, 0, @buf, 'lixo', 'XPT6C', 2;
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_semlugar WHERE bistamp = 'XPT6C');
    INSERT @o VALUES ('C6', '66 com destino invalido: aparece em semlugar', '1', CONVERT(varchar(10), @n));
    SET @e = CONVERT(varchar(30), ISNULL(@b0, 0) - 2);
    INSERT @o EXEC #t 'C6', '66 com destino invalido: so a perna da origem (-2) conta', @ref, @buf, @e;
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C6', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 7a: apagar material com emconf=1 limpa a bi =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @mat varchar(25), @matficha varchar(25), @lobs varchar(40), @n int;
SELECT @mat = mat, @matficha = matficha FROM #ctx;
IF @mat IS NULL
BEGIN
    INSERT @o VALUES ('C7a', 'sem u_matinterv ligado a um dossier 63 emconf=1', 'dados de teste', 'SEM DADOS');
END
ELSE
BEGIN
BEGIN TRY
    BEGIN TRAN;
    SET @lobs = @mat + '|Z-90-1-01';
    EXEC #ins @matficha, 63, 1, @lobs, 'Z-90-1-01', 'XPT7A', 1;
    SET @n = (SELECT COUNT(*) FROM bi WHERE bistamp = 'XPT7A');
    INSERT @o VALUES ('C7a', 'linha do material existe', '1', CONVERT(varchar(10), @n));
    DELETE FROM u_matinterv WHERE u_matintervstamp = @mat;
    SET @n = (SELECT COUNT(*) FROM bi WHERE bistamp = 'XPT7A');
    INSERT @o VALUES ('C7a', 'apagar material emconf=1: linha bi apagada', '0', CONVERT(varchar(10), @n));
    SET @n = (SELECT COUNT(*) FROM bi2 WHERE bi2stamp = 'XPT7A');
    INSERT @o VALUES ('C7a', 'apagar material emconf=1: bi2 apagada', '0', CONVERT(varchar(10), @n));
    SET @n = (SELECT COUNT(*) FROM u_matinterv WHERE u_matintervstamp = @mat);
    INSERT @o VALUES ('C7a', 'apagar material emconf=1: material apagado', '0', CONVERT(varchar(10), @n));
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C7a', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
END
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 7b: apagar material com emconf=0 recusa =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @mat varchar(25), @matficha varchar(25), @lobs varchar(40);
SELECT @mat = mat, @matficha = matficha FROM #ctx;
IF @mat IS NULL
BEGIN
    INSERT @o VALUES ('C7b', 'sem u_matinterv ligado a um dossier 63 emconf=1', 'dados de teste', 'SEM DADOS');
END
ELSE
BEGIN
BEGIN TRY
    BEGIN TRAN;
    SET @lobs = @mat + '|Z-90-1-01';
    EXEC #ins @matficha, 63, 0, @lobs, 'Z-90-1-01', 'XPT7B', 1;
    DELETE FROM u_matinterv WHERE u_matintervstamp = @mat;
    INSERT @o VALUES ('C7b', 'apagar material emconf=0 recusado', 'recusado', 'apagou sem erro');
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C7b', 'apagar material emconf=0 recusado', 'recusado',
                      CASE WHEN ERROR_MESSAGE() LIKE '%nao pode ser apagado%' OR ERROR_NUMBER() = 3609 THEN 'recusado' ELSE LEFT(ISNULL(ERROR_MESSAGE(), ''), 80) END);
    INSERT @o VALUES ('C7b', 'o trigger fez rollback (sem transacao aberta)', '0', CONVERT(varchar(10), @@TRANCOUNT));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
END
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 7c: apagar ficha com o dossier todo em emconf=1 limpa o dossier 63 =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @fichaf varchar(25), @n int;
SELECT @fichaf = fichaf FROM #ctx;
IF @fichaf IS NULL
BEGIN
    INSERT @o VALUES ('C7c', 'sem u_fichaint com dossier 63 emconf=1', 'dados de teste', 'SEM DADOS');
END
ELSE
BEGIN
BEGIN TRY
    BEGIN TRAN;
    SET @n = (SELECT COUNT(*) FROM bo WHERE bostamp = @fichaf AND ndos = 63);
    INSERT @o VALUES ('C7c', 'dossier 63 existe antes', '1', CONVERT(varchar(10), @n));
    DELETE FROM u_fichaint WHERE u_fichaintstamp = @fichaf;
    SET @n = (SELECT COUNT(*) FROM bo WHERE bostamp = @fichaf);
    INSERT @o VALUES ('C7c', 'apagar ficha emconf=1: bo apagado', '0', CONVERT(varchar(10), @n));
    SET @n = (SELECT COUNT(*) FROM bi WHERE bostamp = @fichaf);
    INSERT @o VALUES ('C7c', 'apagar ficha emconf=1: bi apagadas', '0', CONVERT(varchar(10), @n));
    SET @n = (SELECT COUNT(*) FROM bo2 WHERE bo2stamp = @fichaf) + (SELECT COUNT(*) FROM bo3 WHERE bo3stamp = @fichaf);
    INSERT @o VALUES ('C7c', 'apagar ficha emconf=1: bo2 e bo3 apagados', '0', CONVERT(varchar(10), @n));
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C7c', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
END
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 7d: apagar ficha com uma linha emconf=0 recusa =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @fichaf varchar(25);
SELECT @fichaf = fichaf FROM #ctx;
IF @fichaf IS NULL
BEGIN
    INSERT @o VALUES ('C7d', 'sem u_fichaint com dossier 63 emconf=1', 'dados de teste', 'SEM DADOS');
END
ELSE
BEGIN
BEGIN TRY
    BEGIN TRAN;
    EXEC #ins @fichaf, 63, 0, 'MATX|Z-90-1-01', 'Z-90-1-01', 'XPT7D', 1;
    DELETE FROM u_fichaint WHERE u_fichaintstamp = @fichaf;
    INSERT @o VALUES ('C7d', 'apagar ficha com emconf=0 recusado', 'recusado', 'apagou sem erro');
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C7d', 'apagar ficha com emconf=0 recusado', 'recusado',
                      CASE WHEN ERROR_MESSAGE() LIKE '%nao pode ser apagada%' OR ERROR_NUMBER() = 3609 THEN 'recusado' ELSE LEFT(ISNULL(ERROR_MESSAGE(), ''), 80) END);
    INSERT @o VALUES ('C7d', 'o trigger fez rollback (sem transacao aberta)', '0', CONVERT(varchar(10), @@TRANCOUNT));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
END
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 8: paridade de fn_sc_LerLugar com RegrasLocalizacao.LerLugar (casos dos testes C#) =====
-- esperado = corredor|estante|nivel|posicao, ou NULL quando LerLugar devolve null
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
BEGIN TRY
    BEGIN TRAN;
    INSERT @o
    SELECT 'C8', CAST('LerLugar(' + ISNULL(v.texto, '<null>') + ')' AS varchar(100)), v.esperado,
           CAST(ISNULL(l.corredor + '|' + l.estante + '|' + CAST(l.nivel AS varchar(3)) + '|' + CAST(l.posicao AS varchar(3)), 'NULL') AS varchar(80))
    FROM (VALUES
        ('LOC-A-03-2-04', 'A|A-03|2|4'),
        ('LOC''A''03''2''04', 'A|A-03|2|4'),
        ('  loc a 03 2 04 ', 'A|A-03|2|4'),
        ('LOC-A-03-2', 'NULL'),
        ('A-02-1-01', 'A|A-02|1|1'),
        ('A-02-1', 'NULL'),
        ('LOC-A-02-1-01', 'A|A-02|1|1'),
        ('LOC-A-03', 'NULL'),
        ('LOC-A-0-2-1', 'NULL'),
        ('ABC-A-03-2-04', 'NULL'),
        ('LOC-A-03-2-0', 'NULL'),
        ('', 'NULL'),
        (NULL, 'NULL'),
        ('LOC-ABCD-03-2-04', 'NULL'),
        ('LOC-A-1000-2-04', 'NULL'),
        ('LOC-A-03-100-04', 'NULL'),
        ('LOC-A-03-2-100', 'NULL'),
        ('ABC-999-99-99', 'ABC|ABC-999|99|99'),
        ('A-2-1-1', 'A|A-02|1|1'),
        ('LOC-a-02-1-01', 'A|A-02|1|1'),
        ('LOC-A-1-1-1', 'A|A-01|1|1'),
        ('LOC-A-2-1-1', 'A|A-02|1|1'),
        ('A-01-2-01', 'A|A-01|2|1'),
        ('BUFFER', 'NULL')
    ) v(texto, esperado)
    OUTER APPLY dbo.fn_sc_LerLugar(v.texto) l;
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C8', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 9: lugar duplicado em u_pecaloc (sem indice unico): so uma linha mexe =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @b63 varchar(25), @n int, @s varchar(30);
SELECT @ref = ref, @b63 = b63 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    INSERT INTO u_pecaloc (u_pecalocstamp, ref, armazem, corredor, estante, nivel, posicao, qtt, marcada, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
    SELECT v.s, @ref, 104, 'Z', 'Z-90', '1', '1', '5.000', 0, 'XP', CAST(CONVERT(date, GETDATE()) AS datetime), CONVERT(varchar(8), GETDATE(), 108),
           'XP', CAST(CONVERT(date, GETDATE()) AS datetime), CONVERT(varchar(8), GETDATE(), 108)
    FROM (VALUES ('XPT9A'), ('XPT9B')) v(s);
    EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-01', 'Z-90-1-01', 'XPT9C', 3;
    SET @s = (SELECT LTRIM(RTRIM(qtt)) FROM u_pecaloc WHERE u_pecalocstamp = 'XPT9A');
    INSERT @o VALUES ('C9', 'duplicados: a linha escolhida (menor stamp) desce 5-3', '2.000', @s);
    SET @s = (SELECT LTRIM(RTRIM(qtt)) FROM u_pecaloc WHERE u_pecalocstamp = 'XPT9B');
    INSERT @o VALUES ('C9', 'duplicados: a outra linha fica intacta', '5.000', @s);
    SET @s = CONVERT(varchar(30), (SELECT SUM(dbo.fn_sc_Qtd(qtt)) FROM u_pecaloc WHERE u_pecalocstamp IN ('XPT9A', 'XPT9B')));
    INSERT @o VALUES ('C9', 'duplicados: soma das linhas', '7.000', @s);
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_divergencias WHERE tipo = 'DUPLICADO' AND ref = LTRIM(RTRIM(@ref)) AND estante = 'Z-90');
    INSERT @o VALUES ('C9', 'divergencias: 1 DUPLICADO', '1', CONVERT(varchar(10), @n));
    EXEC #del 'XPT9C';
    SET @s = CONVERT(varchar(30), (SELECT SUM(dbo.fn_sc_Qtd(qtt)) FROM u_pecaloc WHERE u_pecalocstamp IN ('XPT9A', 'XPT9B')));
    INSERT @o VALUES ('C9', 'duplicados: apagar a saida repoe (soma 10)', '10.000', @s);
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C9', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 10: qtt ilegivel na u_pecaloc nunca e tocada nem duplicada =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @b63 varchar(25), @n int, @s varchar(30);
SELECT @ref = ref, @b63 = b63 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    INSERT INTO u_pecaloc (u_pecalocstamp, ref, armazem, corredor, estante, nivel, posicao, qtt, marcada, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
    VALUES ('XPT10A', @ref, 104, 'Z', 'Z-90', '1', '1', '12,5', 0, 'XP', CAST(CONVERT(date, GETDATE()) AS datetime), CONVERT(varchar(8), GETDATE(), 108),
            'XP', CAST(CONVERT(date, GETDATE()) AS datetime), CONVERT(varchar(8), GETDATE(), 108));
    EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-01', 'Z-90-1-01', 'XPT10B', 3;
    SET @s = (SELECT LTRIM(RTRIM(qtt)) FROM u_pecaloc WHERE u_pecalocstamp = 'XPT10A');
    INSERT @o VALUES ('C10', 'qtt ilegivel fica intacta', '12,5', @s);
    SET @n = (SELECT COUNT(*) FROM u_pecaloc WHERE LTRIM(RTRIM(ref)) = LTRIM(RTRIM(@ref)) AND LTRIM(RTRIM(estante)) = 'Z-90');
    INSERT @o VALUES ('C10', 'nao se cria linha por cima da ilegivel', '1', CONVERT(varchar(10), @n));
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_divergencias WHERE tipo = 'ILEGIVEL' AND ref = LTRIM(RTRIM(@ref)) AND estante = 'Z-90');
    INSERT @o VALUES ('C10', 'divergencias: 1 ILEGIVEL', '1', CONVERT(varchar(10), @n));
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C10', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 11: u_configman.localentrada vazio ou com 13 caracteres: entradas 64 sem efeito, documento grava =====
-- (se a coluna localentrada tiver menos de 13 caracteres o UPDATE falha e o caso mostra ERRO)
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @buf varchar(60), @b64 varchar(25), @q decimal(13, 3), @r decimal(13, 3), @d decimal(13, 3), @b0 decimal(13, 3), @e0 varchar(30), @n int;
SELECT @ref = ref, @buf = buf, @b64 = b64 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    EXEC #g @ref, @buf, @q OUTPUT, @r OUTPUT, @d OUTPUT; SET @b0 = @q;
    SET @e0 = ISNULL(CONVERT(varchar(30), @b0), 'sem linha');
    UPDATE u_configman SET localentrada = '';
    EXEC #ins @b64, 64, 0, '', '', 'XPT11A', 4;
    SET @n = (SELECT COUNT(*) FROM bi WHERE bistamp = 'XPT11A');
    INSERT @o VALUES ('C11', 'localentrada vazio: o documento gravou', '1', CONVERT(varchar(10), @n));
    INSERT @o EXEC #t 'C11', 'localentrada vazio: buffer nao muda', @ref, @buf, @e0;
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_semlugar WHERE bistamp = 'XPT11A');
    INSERT @o VALUES ('C11', 'localentrada vazio: a linha 64 aparece em semlugar', '1', CONVERT(varchar(10), @n));
    UPDATE u_configman SET localentrada = 'BUFFER_LONGO1';
    EXEC #ins @b64, 64, 0, '', '', 'XPT11B', 4;
    SET @n = (SELECT COUNT(*) FROM bi WHERE bistamp = 'XPT11B');
    INSERT @o VALUES ('C11', 'localentrada com 13 caracteres: o documento gravou', '1', CONVERT(varchar(10), @n));
    INSERT @o EXEC #t 'C11', 'localentrada com 13 caracteres: buffer nao muda', @ref, @buf, @e0;
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_semlugar WHERE bistamp = 'XPT11B');
    INSERT @o VALUES ('C11', 'localentrada com 13 caracteres: aparece em semlugar', '1', CONVERT(varchar(10), @n));
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C11', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 12: 66 com bi.lobs (origem) vazio e lobs2 (destino) valido =====
-- Comportamento do codigo: a perna da origem e descartada (texto vazio) mas a do destino conta: o destino SOMA, nada sai do buffer.
-- A linha aparece em v_sc_pecaloc_semlugar (origem ilegivel).
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @buf varchar(60), @b66 varchar(25), @q decimal(13, 3), @r decimal(13, 3), @d decimal(13, 3), @b0 decimal(13, 3), @e0 varchar(30), @n int;
SELECT @ref = ref, @buf = buf, @b66 = b66 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    EXEC #g @ref, @buf, @q OUTPUT, @r OUTPUT, @d OUTPUT; SET @b0 = @q;
    SET @e0 = ISNULL(CONVERT(varchar(30), @b0), 'sem linha');
    EXEC #ins @b66, 66, 0, '', 'Z-90-1-01', 'XPT12A', 2;
    INSERT @o EXEC #t 'C12', '66 sem origem: o destino soma', @ref, 'Z-90-1-01', '2.000';
    INSERT @o EXEC #t 'C12', '66 sem origem: o buffer nao muda', @ref, @buf, @e0;
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_semlugar WHERE bistamp = 'XPT12A');
    INSERT @o VALUES ('C12', '66 sem origem: aparece em semlugar', '1', CONVERT(varchar(10), @n));
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C12', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 13: armazem 105 e ndos 65 nao tem efeito =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @buf varchar(60), @b63 varchar(25), @q decimal(13, 3), @r decimal(13, 3), @d decimal(13, 3), @b0 decimal(13, 3), @e0 varchar(30), @n int;
SELECT @ref = ref, @buf = buf, @b63 = b63 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    EXEC #g @ref, @buf, @q OUTPUT, @r OUTPUT, @d OUTPUT; SET @b0 = @q;
    SET @e0 = ISNULL(CONVERT(varchar(30), @b0), 'sem linha');
    EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-01', 'Z-90-1-01', 'XPT13A', 3, @arm = 105;
    INSERT @o EXEC #t 'C13', '63 no armazem 105: sem efeito', @ref, 'Z-90-1-01', 'sem linha';
    EXEC #ins @b63, 65, 0, 'MATX|Z-90-1-01', 'Z-90-1-01', 'XPT13B', 3;
    INSERT @o EXEC #t 'C13', 'ndos 65 no armazem 104: sem efeito', @ref, 'Z-90-1-01', 'sem linha';
    EXEC #ins @b63, 64, 0, '', '', 'XPT13C', 3, @arm = 105;
    INSERT @o EXEC #t 'C13', '64 no armazem 105: o buffer nao muda', @ref, @buf, @e0;
    SET @n = (SELECT COUNT(*) FROM u_pecaloc WHERE LTRIM(RTRIM(ref)) = LTRIM(RTRIM(@ref)) AND LTRIM(RTRIM(estante)) = 'Z-90');
    INSERT @o VALUES ('C13', 'nenhuma linha criada no lugar de teste', '0', CONVERT(varchar(10), @n));
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C13', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 14: qtt '' e NULL contam como 0 legivel; o DELETE a zero nao apanha a linha ja atualizada =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @buf varchar(60), @b64 varchar(25), @b66 varchar(25), @b63 varchar(25), @n int, @s varchar(30), @nulos int;
SELECT @ref = ref, @buf = buf, @b64 = b64, @b66 = b66, @b63 = b63 FROM #ctx;
SET @nulos = ISNULL(COLUMNPROPERTY(OBJECT_ID('dbo.u_pecaloc'), 'qtt', 'AllowsNull'), 0);
BEGIN TRY
    BEGIN TRAN;
    INSERT @o VALUES ('C14', 'fn_sc_QtdOk('''') = 1', '1', CONVERT(varchar(10), dbo.fn_sc_QtdOk('')));
    INSERT @o VALUES ('C14', 'fn_sc_QtdOk(NULL) = 1', '1', CONVERT(varchar(10), dbo.fn_sc_QtdOk(NULL)));
    INSERT @o VALUES ('C14', 'fn_sc_QtdOk(''abc'') = 0', '0', CONVERT(varchar(10), dbo.fn_sc_QtdOk('abc')));
    INSERT @o VALUES ('C14', 'fn_sc_QtdOk(''12,5'') = 0', '0', CONVERT(varchar(10), dbo.fn_sc_QtdOk('12,5')));
    INSERT @o VALUES ('C14', 'fn_sc_Qtd('''') = 0', '0.000', CONVERT(varchar(30), dbo.fn_sc_Qtd('')));
    INSERT @o VALUES ('C14', 'fn_sc_Qtd(NULL) = 0', '0.000', CONVERT(varchar(30), dbo.fn_sc_Qtd(NULL)));
    INSERT @o VALUES ('C14', 'fn_sc_Qtd(''7.500'') = 7.500', '7.500', CONVERT(varchar(30), dbo.fn_sc_Qtd('7.500')));

    -- linha com qtt '' : e atualizada (nao fica ilegivel nem se cria outra)
    INSERT INTO u_pecaloc (u_pecalocstamp, ref, armazem, corredor, estante, nivel, posicao, qtt, marcada, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
    VALUES ('XPT14A', @ref, 104, 'Z', 'Z-90', '1', '1', '', 0, 'XP', CAST(CONVERT(date, GETDATE()) AS datetime), CONVERT(varchar(8), GETDATE(), 108),
            'XP', CAST(CONVERT(date, GETDATE()) AS datetime), CONVERT(varchar(8), GETDATE(), 108));
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_divergencias WHERE tipo = 'ILEGIVEL' AND ref = LTRIM(RTRIM(@ref)) AND estante = 'Z-90');
    INSERT @o VALUES ('C14', 'qtt vazia nao e ILEGIVEL', '0', CONVERT(varchar(10), @n));
    EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-01', 'Z-90-1-01', 'XPT14B', 3;
    SET @s = (SELECT LTRIM(RTRIM(qtt)) FROM u_pecaloc WHERE u_pecalocstamp = 'XPT14A');
    INSERT @o VALUES ('C14', 'qtt vazia + saida 3: a propria linha fica -3.000', '-3.000', @s);
    SET @n = (SELECT COUNT(*) FROM u_pecaloc WHERE LTRIM(RTRIM(ref)) = LTRIM(RTRIM(@ref)) AND LTRIM(RTRIM(estante)) = 'Z-90');
    INSERT @o VALUES ('C14', 'qtt vazia: nao se criou outra linha', '1', CONVERT(varchar(10), @n));

    -- linha com qtt NULL (so se a coluna aceitar NULL)
    IF @nulos = 1
    BEGIN
        INSERT INTO u_pecaloc (u_pecalocstamp, ref, armazem, corredor, estante, nivel, posicao, qtt, marcada, ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        VALUES ('XPT14C', @ref, 104, 'Z', 'Z-90', '1', '2', NULL, 0, 'XP', CAST(CONVERT(date, GETDATE()) AS datetime), CONVERT(varchar(8), GETDATE(), 108),
                'XP', CAST(CONVERT(date, GETDATE()) AS datetime), CONVERT(varchar(8), GETDATE(), 108));
        EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-02', 'Z-90-1-02', 'XPT14D', 2;
        SET @s = (SELECT LTRIM(RTRIM(qtt)) FROM u_pecaloc WHERE u_pecalocstamp = 'XPT14C');
        INSERT @o VALUES ('C14', 'qtt NULL + saida 2: a propria linha fica -2.000', '-2.000', @s);
        EXEC #del 'XPT14D';
        INSERT @o EXEC #t 'C14', 'qtt NULL: apagar a saida deixa 0 e a linha desaparece', @ref, 'Z-90-1-02', 'sem linha';
    END
    ELSE
        INSERT @o VALUES ('C14', 'u_pecaloc.qtt nao aceita NULL: passos NULL nao se aplicam', 'n/a', 'n/a');

    -- saldo 10, saida 5: tem de ficar 5 (a linha ja atualizada nao pode ser apagada pela condicao de zero)
    EXEC #ins @b64, 64, 0, '', '', 'XPT14E', 10;
    EXEC #ins @b66, 66, 0, @buf, 'Z-90-1-03', 'XPT14F', 10;
    EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-03', 'Z-90-1-03', 'XPT14G', 5;
    INSERT @o EXEC #t 'C14', 'saldo 10, saida 5: fica 5 (a linha mantem-se)', @ref, 'Z-90-1-03', '5.000';
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C14', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 15: UPDATE de ref e de armazem numa linha bi =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @ref2 varchar(18), @buf varchar(60), @b64 varchar(25), @b66 varchar(25), @b63 varchar(25);
SELECT @ref = ref, @buf = buf, @b64 = b64, @b66 = b66, @b63 = b63 FROM #ctx;
SET @ref2 = (SELECT TOP 1 LTRIM(RTRIM(ref)) FROM st WITH (NOLOCK) WHERE LTRIM(RTRIM(ref)) <> LTRIM(RTRIM(@ref)) AND ref <> '' ORDER BY ref);
IF @ref2 IS NULL
BEGIN
    INSERT @o VALUES ('C15', 'sem segunda referencia em st', 'dados de teste', 'SEM DADOS');
END
ELSE
BEGIN
BEGIN TRY
    BEGIN TRAN;
    EXEC #ins @b64, 64, 0, '', '', 'XPT15A', 10;
    EXEC #ins @b66, 66, 0, @buf, 'Z-90-1-01', 'XPT15B', 10;
    EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-01', 'Z-90-1-01', 'XPT15C', 3;
    INSERT @o EXEC #t 'C15', 'partida (ref 1)', @ref, 'Z-90-1-01', '7.000';
    UPDATE bi SET ref = @ref2 WHERE bistamp = 'XPT15C';
    INSERT @o EXEC #t 'C15', 'UPDATE ref: a ref antiga repoe', @ref, 'Z-90-1-01', '10.000';
    INSERT @o EXEC #t 'C15', 'UPDATE ref: a ref nova desce', @ref2, 'Z-90-1-01', '-3.000';
    UPDATE bi SET armazem = 105 WHERE bistamp = 'XPT15C';
    INSERT @o EXEC #t 'C15', 'UPDATE armazem 104->105: a ref nova repoe (linha desaparece)', @ref2, 'Z-90-1-01', 'sem linha';
    INSERT @o EXEC #t 'C15', 'UPDATE armazem 104->105: a ref antiga nao muda', @ref, 'Z-90-1-01', '10.000';
    UPDATE bi SET armazem = 104 WHERE bistamp = 'XPT15C';
    INSERT @o EXEC #t 'C15', 'UPDATE armazem 105->104: volta a descer', @ref2, 'Z-90-1-01', '-3.000';
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C15', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
END
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 16: varias refs numa so instrucao (UPDATE e DELETE) =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @ref2 varchar(18), @b63 varchar(25);
SELECT @ref = ref, @b63 = b63 FROM #ctx;
SET @ref2 = (SELECT TOP 1 LTRIM(RTRIM(ref)) FROM st WITH (NOLOCK) WHERE LTRIM(RTRIM(ref)) <> LTRIM(RTRIM(@ref)) AND ref <> '' ORDER BY ref);
IF @ref2 IS NULL
BEGIN
    INSERT @o VALUES ('C16', 'sem segunda referencia em st', 'dados de teste', 'SEM DADOS');
END
ELSE
BEGIN
BEGIN TRY
    BEGIN TRAN;
    EXEC #ins @b63, 63, 0, 'MATX|Z-90-1-01', 'Z-90-1-01', 'XPT16A', 2, 'XPT16B', 4;
    INSERT @o EXEC #t 'C16', '2 saidas (2+4) da mesma ref', @ref, 'Z-90-1-01', '-6.000';
    UPDATE bi SET ref = @ref2 WHERE bistamp = 'XPT16B';
    INSERT @o EXEC #t 'C16', 'UPDATE de uma linha para a ref 2: ref 1', @ref, 'Z-90-1-01', '-2.000';
    INSERT @o EXEC #t 'C16', 'UPDATE de uma linha para a ref 2: ref 2', @ref2, 'Z-90-1-01', '-4.000';
    UPDATE bi SET ref = CASE WHEN LTRIM(RTRIM(ref)) = LTRIM(RTRIM(@ref)) THEN @ref2 ELSE @ref END WHERE bistamp IN ('XPT16A', 'XPT16B');
    INSERT @o EXEC #t 'C16', 'troca das refs numa so instrucao: ref 1 (a linha de 4)', @ref, 'Z-90-1-01', '-4.000';
    INSERT @o EXEC #t 'C16', 'troca das refs numa so instrucao: ref 2 (a linha de 2)', @ref2, 'Z-90-1-01', '-2.000';
    EXEC #del 'XPT16A', 'XPT16B';
    INSERT @o EXEC #t 'C16', 'DELETE das 2 linhas (refs diferentes): ref 1 sem linha', @ref, 'Z-90-1-01', 'sem linha';
    INSERT @o EXEC #t 'C16', 'DELETE das 2 linhas (refs diferentes): ref 2 sem linha', @ref2, 'Z-90-1-01', 'sem linha';
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C16', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
END
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Caso 17: buffer com formato de lugar completo (LOC-A-03-2-04): valido, nao aparece em semlugar =====
IF DB_NAME() <> 'TESTES_LAND2' BEGIN RAISERROR('Base errada.', 16, 1); RETURN; END
DECLARE @o TABLE (caso varchar(8), passo varchar(100), esperado varchar(80), obtido varchar(80));
DECLARE @ref varchar(18), @buf varchar(60), @b64 varchar(25), @b66 varchar(25), @q decimal(13, 3), @r decimal(13, 3), @d decimal(13, 3), @b0 decimal(13, 3), @e4 varchar(30), @e3 varchar(30), @n int;
SELECT @ref = ref, @buf = buf, @b64 = b64, @b66 = b66 FROM #ctx;
BEGIN TRY
    BEGIN TRAN;
    EXEC #g @ref, 'LOC-A-03-2-04', @q OUTPUT, @r OUTPUT, @d OUTPUT; SET @b0 = @q;
    SET @e4 = CONVERT(varchar(30), ISNULL(@b0, 0) + 4); SET @e3 = CONVERT(varchar(30), ISNULL(@b0, 0) + 3);
    UPDATE u_configman SET localentrada = 'LOC-A-03-2-04';
    EXEC #ins @b64, 64, 0, '', '', 'XPT17A', 4;
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_semlugar WHERE bistamp = 'XPT17A');
    INSERT @o VALUES ('C17', 'buffer LOC-A-03-2-04: a entrada 64 nao aparece em semlugar', '0', CONVERT(varchar(10), @n));
    INSERT @o EXEC #t 'C17', 'buffer LOC-A-03-2-04: a entrada soma no lugar A-03-2-04', @ref, 'LOC-A-03-2-04', @e4;
    EXEC #ins @b66, 66, 0, 'LOC-A-03-2-04', 'Z-90-1-01', 'XPT17B', 1;
    SET @n = (SELECT COUNT(*) FROM v_sc_pecaloc_semlugar WHERE bistamp = 'XPT17B');
    INSERT @o VALUES ('C17', 'buffer LOC-A-03-2-04: a arrumacao 66 nao aparece em semlugar', '0', CONVERT(varchar(10), @n));
    INSERT @o EXEC #t 'C17', 'buffer LOC-A-03-2-04: a arrumacao tira da origem', @ref, 'LOC-A-03-2-04', @e3;
    INSERT @o EXEC #t 'C17', 'buffer LOC-A-03-2-04: a arrumacao poe no destino', @ref, 'Z-90-1-01', '1.000';
    ROLLBACK;
END TRY
BEGIN CATCH
    INSERT @o VALUES ('C17', 'ERRO', 'sem erro', LEFT(ISNULL(ERROR_MESSAGE(), ''), 80));
    IF @@TRANCOUNT > 0 ROLLBACK;
END CATCH
INSERT #res SELECT caso, passo, esperado, obtido FROM @o;
SELECT caso, passo, esperado, obtido, CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado FROM @o;
GO

-- ===== Resumo =====
SELECT CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END AS resultado, COUNT(*) AS passos
FROM #res GROUP BY CASE WHEN obtido = 'SEM DADOS' THEN 'SALTADO' WHEN esperado = obtido THEN 'PASSOU' ELSE 'FALHOU' END;
-- casos sem nenhum resultado (batch abortado): esperado 0 linhas
SELECT c.caso AS caso_sem_resultado
FROM (VALUES ('C1'), ('C2'), ('C3'), ('C4'), ('C5'), ('C6'), ('C7a'), ('C7b'), ('C7c'), ('C7d'), ('C8'), ('C9'), ('C10'), ('C11'), ('C12'), ('C13'),
             ('C14'), ('C15'), ('C16'), ('C17')) c(caso)
WHERE NOT EXISTS (SELECT 1 FROM #res r WHERE r.caso = c.caso);
SELECT 'FIM' AS x, @@TRANCOUNT AS trancount_aberta;
SELECT COUNT(*) AS restos_XP_bi FROM bi WITH (NOLOCK) WHERE bistamp LIKE 'XPT%';   -- esperado 0
SELECT COUNT(*) AS restos_Z90 FROM u_pecaloc WITH (NOLOCK) WHERE LTRIM(RTRIM(estante)) = 'Z-90';   -- esperado 0
DECLARE @salt int, @falh int;
SELECT @salt = ISNULL(SUM(CASE WHEN obtido = 'SEM DADOS' THEN 1 ELSE 0 END), 0),
       @falh = ISNULL(SUM(CASE WHEN obtido = 'SEM DADOS' THEN 0 WHEN esperado = obtido THEN 0 ELSE 1 END), 0)
FROM #res;
SET NOEXEC OFF;
IF @salt > 0 RAISERROR('RESULTADO FINAL: %d passo(s) SALTADO(S) - a base de testes nao tem dados para esses casos.', 16, 1, @salt);
IF @falh > 0 RAISERROR('RESULTADO FINAL: %d passo(s) FALHOU.', 16, 1, @falh);
GO
SET NOEXEC OFF;
GO
