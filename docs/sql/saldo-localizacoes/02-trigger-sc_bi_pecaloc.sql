-- ======================================================================
-- SigmaCode, Lda
-- Cliente:   comum (Gestor de Manutencao)
-- Local:     bi (SQL - trigger do saldo por localizacao)
-- Trigger:   AFTER INSERT, UPDATE, DELETE em bi
-- Objetivo:  manter u_pecaloc (saldo por lugar, armazem 104) a partir das linhas bi dos dossiers 63, 64 e 66
-- Autor:     SigmaCode
-- Historico:
--   v01  2026-11-26  Versao inicial
--   v02  2026-11-26  Revisao: uma linha por lugar duplicado, leitura sargavel da u_pecaloc, qtt ilegivel intocada,
--                    buffer invalido = efeito 0 com aviso, guarda de base de dados
--   v03  2026-11-26  Revisao: o UPDATE grava sobre o valor atual da propria linha (nao o lido antes); DELETE a zero com a mesma
--                    condicao e antes do UPDATE; qtt '' e NULL contam como 0; corredor/estante NULL tratados; mensagem do CATCH
-- ======================================================================
-- Regra: efeito(inserted) - efeito(deleted), agregado por (ref, corredor, estante, nivel, posicao), sem cursores.
--   63 saida,      emconf = 0: -qtt no lugar (bi.lobs2 se preenchido, senao a parte de bi.lobs depois do '|'); emconf = 1: sem efeito.
--   64 entrada,    emconf = 0: +qtt no buffer (u_configman.localentrada).
--   66 arrumacao,  emconf = 0: -qtt na origem (bi.lobs) e +qtt no destino (bi.lobs2); BUFFER/localentrada = buffer.
-- Lugar vazio ou ilegivel = efeito 0 nessa perna (a outra perna conta); o documento grava sempre (ver v_sc_pecaloc_semlugar).
-- Buffer (localentrada) vazio ou cujo corredor tem mais de 10 caracteres (corredor e varchar(10)): as pernas do buffer tem efeito 0
-- e sai um aviso. O corredor do buffer e o de fn_sc_LerLugar(localentrada) ou, se nao for um lugar completo, o proprio texto.
-- u_pecaloc: se houver lugares duplicados, so uma linha por lugar e atualizada (a de menor u_pecalocstamp; as outras ficam
-- para v_sc_pecaloc_divergencias, tipo DUPLICADO). Linha com qtt ilegivel (texto nao numerico) nunca e tocada nem se cria outra
-- por cima dela (ver v_sc_pecaloc_divergencias, tipo ILEGIVEL). qtt '' ou NULL conta como 0 e a linha e atualizada normalmente.
-- Gravacao: a escolha da linha (rn = 1) vem de @cand, mas o novo valor e calculado sobre o valor que a linha tem no momento do
-- UPDATE (fn_sc_Qtd(u.qtt) + delta), nunca sobre o lido antes. Ordem: DELETE dos que ficam a zero, UPDATE dos restantes, INSERT dos novos
-- (o DELETE tem de vir antes do UPDATE para nao apanhar linhas ja atualizadas).
-- Ordem das pernas: um UPDATE/DELETE set-based nao aceita ORDER BY e o motor nao garante a ordem pela qual toca as linhas;
-- a escolha por menor u_pecalocstamp fixa so qual linha e usada, nao a ordem de bloqueio. Duas transacoes concorrentes que toquem
-- varios lugares em ordem cruzada podem por isso entrar em deadlock (1205); esta limitacao nao esta resolvida aqui.
-- Sem hints de locking (decisao do utilizador). A rara corrida de dois INSERT do mesmo lugar novo apanha-se em v_sc_pecaloc_divergencias.
-- Corrida residual: @cand e um instantaneo; se outra transacao apagar a linha escolhida depois da leitura, o efeito dessa execucao perde-se (fica 0 em vez do esperado).
-- Janela estreita; apanha-se por v_sc_pecaloc_divergencias (tipo STOCK).
-- Erros de dados (truncagem, conversao) viram aviso de severidade 10 e o documento grava. Deadlock (1205) ou SET XACT_ABORT ON
-- ainda podem anular a transacao.
-- Pressuposto: u_configman.localentrada existe (verificado em TESTES_LAND2). Requer 01-fn_sc_LerLugar.sql.
-- Nao altera nem desativa triggers existentes.
-- Para instalar noutra base (cliente): so depois de GATE do utilizador e acrescentando o nome a lista da guarda abaixo.

DECLARE @bd nvarchar(128);
SET @bd = DB_NAME();
IF @bd NOT IN (N'TESTES_LAND2')
BEGIN
    RAISERROR(N'Base nao autorizada para este script: %s', 16, 1, @bd);
    SET NOEXEC ON;
END
GO
SELECT DB_NAME() AS base;
GO

IF OBJECT_ID('dbo.sc_bi_pecaloc', 'TR') IS NOT NULL DROP TRIGGER dbo.sc_bi_pecaloc;
GO
CREATE TRIGGER dbo.sc_bi_pecaloc ON dbo.bi
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted WHERE ndos IN (63, 64, 66) AND armazem = 104)
       AND NOT EXISTS (SELECT 1 FROM deleted WHERE ndos IN (63, 64, 66) AND armazem = 104)
        RETURN;

    BEGIN TRY
        DECLARE @src TABLE (sinal int, ndos int, ref varchar(18), qtt decimal(13, 3), lobs varchar(250), lobs2 varchar(250));
        DECLARE @perna TABLE (ref varchar(18), texto varchar(250), buf bit, delta decimal(13, 3));
        DECLARE @res TABLE (ref varchar(18), corredor varchar(30), estante varchar(30), nivel int, posicao int, delta decimal(13, 3));
        DECLARE @agg TABLE (ref varchar(18), corredor varchar(30), estante varchar(30), nivel int, posicao int, delta decimal(13, 3));
        DECLARE @cand TABLE (stamp varchar(25), ref varchar(18), corredor varchar(30), estante varchar(30), nivel int, posicao int,
                             qtt decimal(13, 3), legivel bit, rn int);
        DECLARE @bufTexto varchar(60), @bc varchar(30), @be varchar(30), @bn int, @bp int, @bufOk bit;
        DECLARE @hoje datetime, @hora varchar(8);

        SET @hoje = CAST(CONVERT(date, GETDATE()) AS datetime);
        SET @hora = CONVERT(varchar(8), GETDATE(), 108);

        -- so as linhas com emconf = 0 tem efeito (em conferencia, o 63 conta apenas como reservado)
        INSERT @src (sinal, ndos, ref, qtt, lobs, lobs2)
        SELECT 1, ndos, LTRIM(RTRIM(ref)), CASE WHEN ABS(qtt) < 9999999999 THEN CAST(qtt AS decimal(13, 3)) ELSE 0 END, ISNULL(lobs, ''), ISNULL(lobs2, '')
        FROM inserted WHERE ndos IN (63, 64, 66) AND armazem = 104 AND ISNULL(emconf, 0) = 0
        UNION ALL
        SELECT -1, ndos, LTRIM(RTRIM(ref)), CASE WHEN ABS(qtt) < 9999999999 THEN CAST(qtt AS decimal(13, 3)) ELSE 0 END, ISNULL(lobs, ''), ISNULL(lobs2, '')
        FROM deleted WHERE ndos IN (63, 64, 66) AND armazem = 104 AND ISNULL(emconf, 0) = 0;

        IF NOT EXISTS (SELECT 1 FROM @src) RETURN;

        -- Sem coluna estavel para ordenar em u_configman: fica TOP 1 (a tabela tem uma linha).
        SET @bufTexto = ISNULL((SELECT TOP 1 UPPER(LTRIM(RTRIM(ISNULL(localentrada, '')))) FROM dbo.u_configman), '');
        SELECT @bc = corredor, @be = estante, @bn = nivel, @bp = posicao FROM dbo.fn_sc_LerLugar(@bufTexto);
        IF @bc IS NULL
        BEGIN
            SET @bc = @bufTexto; SET @be = ''; SET @bn = 0; SET @bp = 0;
        END
        SET @bufOk = CASE WHEN @bufTexto <> '' AND LEN(@bc) <= 10 THEN 1 ELSE 0 END;

        INSERT @perna (ref, texto, buf, delta)
        SELECT ref,
               CASE WHEN LTRIM(RTRIM(lobs2)) <> '' THEN LTRIM(RTRIM(lobs2))
                    WHEN CHARINDEX('|', lobs) > 0 THEN LTRIM(RTRIM(SUBSTRING(lobs, CHARINDEX('|', lobs) + 1, 250)))
                    ELSE '' END,
               0, -sinal * qtt
        FROM @src WHERE ndos = 63
        UNION ALL
        SELECT ref, '', 1, sinal * qtt FROM @src WHERE ndos = 64
        UNION ALL
        SELECT ref, LTRIM(RTRIM(lobs)), 0, -sinal * qtt FROM @src WHERE ndos = 66
        UNION ALL
        SELECT ref, LTRIM(RTRIM(lobs2)), 0, sinal * qtt FROM @src WHERE ndos = 66;

        DELETE FROM @perna WHERE delta = 0 OR (texto = '' AND buf = 0);

        -- o que e o buffer (entradas, ou texto igual a localentrada)
        UPDATE @perna SET buf = 1 WHERE buf = 0 AND @bufTexto <> '' AND UPPER(texto) = @bufTexto;

        IF @bufOk = 0 AND EXISTS (SELECT 1 FROM @perna WHERE buf = 1)
            RAISERROR('sc_bi_pecaloc: u_configman.localentrada vazio ou com corredor de mais de 10 caracteres; as pernas do buffer ficam sem efeito (ver v_sc_pecaloc_semlugar).', 10, 1);

        INSERT @res (ref, corredor, estante, nivel, posicao, delta)
        SELECT ref, @bc, @be, @bn, @bp, delta FROM @perna WHERE buf = 1 AND @bufOk = 1
        UNION ALL
        SELECT p.ref, l.corredor, l.estante, l.nivel, l.posicao, p.delta
        FROM @perna p CROSS APPLY dbo.fn_sc_LerLugar(p.texto) l
        WHERE p.buf = 0;

        INSERT @agg (ref, corredor, estante, nivel, posicao, delta)
        SELECT ref, corredor, estante, nivel, posicao, SUM(delta)
        FROM @res GROUP BY ref, corredor, estante, nivel, posicao
        HAVING SUM(delta) <> 0;

        IF NOT EXISTS (SELECT 1 FROM @agg) RETURN;

        -- (a) uma leitura sargavel da u_pecaloc (sem funcoes sobre ref); a chave normaliza-se so aqui.
        -- rn = 1 e a linha escolhida por lugar (legivel primeiro, depois menor stamp).
        INSERT @cand (stamp, ref, corredor, estante, nivel, posicao, qtt, legivel, rn)
        SELECT x.stamp, x.ref, x.corredor, x.estante, x.nivel, x.posicao, x.qtt, x.legivel,
               ROW_NUMBER() OVER (PARTITION BY x.ref, x.corredor, x.estante, x.nivel, x.posicao
                                  ORDER BY CASE WHEN x.legivel = 1 THEN 0 ELSE 1 END, x.stamp)
        FROM (SELECT u.u_pecalocstamp AS stamp, LTRIM(RTRIM(u.ref)) AS ref,
                     ISNULL(LTRIM(RTRIM(u.corredor)), '') AS corredor, ISNULL(LTRIM(RTRIM(u.estante)), '') AS estante,
                     ISNULL(dbo.fn_sc_Inteiro(u.nivel), 0) AS nivel, ISNULL(dbo.fn_sc_Inteiro(u.posicao), 0) AS posicao,
                     dbo.fn_sc_Qtd(u.qtt) AS qtt, dbo.fn_sc_QtdOk(u.qtt) AS legivel
              FROM dbo.u_pecaloc u
              WHERE u.armazem = 104 AND u.ref IN (SELECT ref FROM @agg)) x;

        -- (b1) apaga o lugar escolhido que fica a zero (antes do UPDATE: assim a condicao ve ainda o valor de partida)
        DELETE u
        FROM @agg a
        INNER JOIN @cand c ON c.ref = a.ref AND c.corredor = a.corredor AND c.estante = a.estante AND c.nivel = a.nivel
             AND c.posicao = a.posicao AND c.rn = 1 AND c.legivel = 1
        INNER JOIN dbo.u_pecaloc u ON u.u_pecalocstamp = c.stamp
        WHERE dbo.fn_sc_QtdOk(u.qtt) = 1 AND dbo.fn_sc_Qtd(u.qtt) + a.delta = 0;

        -- (b2) soma ao lugar escolhido, sobre o valor atual da propria linha (so se legivel e o resultado nao for zero)
        UPDATE u SET u.qtt = CAST(CAST(dbo.fn_sc_Qtd(u.qtt) + a.delta AS decimal(13, 3)) AS varchar(13)),
                     u.usrinis = 'SC', u.usrdata = @hoje, u.usrhora = @hora
        FROM @agg a
        INNER JOIN @cand c ON c.ref = a.ref AND c.corredor = a.corredor AND c.estante = a.estante AND c.nivel = a.nivel
             AND c.posicao = a.posicao AND c.rn = 1 AND c.legivel = 1
        INNER JOIN dbo.u_pecaloc u ON u.u_pecalocstamp = c.stamp
        WHERE dbo.fn_sc_QtdOk(u.qtt) = 1 AND dbo.fn_sc_Qtd(u.qtt) + a.delta <> 0;

        -- (b3) cria os lugares que nao existem (uma linha ilegivel ou duplicada conta como existente)
        INSERT dbo.u_pecaloc (u_pecalocstamp, ref, armazem, corredor, estante, nivel, posicao, qtt, marcada,
                              ousrinis, ousrdata, ousrhora, usrinis, usrdata, usrhora)
        SELECT LEFT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 25), a.ref, 104, a.corredor, a.estante,
               CAST(a.nivel AS varchar(10)), CAST(a.posicao AS varchar(10)), CAST(CAST(a.delta AS decimal(13, 3)) AS varchar(13)), 0,
               'SC', @hoje, @hora, 'SC', @hoje, @hora
        FROM @agg a
        WHERE NOT EXISTS (SELECT 1 FROM @cand c
                          WHERE c.ref = a.ref AND c.corredor = a.corredor AND c.estante = a.estante AND c.nivel = a.nivel AND c.posicao = a.posicao);
    END TRY
    BEGIN CATCH
        DECLARE @msg varchar(400);
        SET @msg = LEFT(ISNULL(ERROR_MESSAGE(), ''), 400);
        RAISERROR('sc_bi_pecaloc: erro ao atualizar o saldo por lugar; o saldo pode ter sido parcialmente atualizado (%s).', 10, 1, @msg);
    END CATCH
END
GO
SET NOEXEC OFF;
GO
