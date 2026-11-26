-- ======================================================================
-- SigmaCode, Lda
-- Cliente:   comum (Gestor de Manutencao)
-- Local:     u_fichaint (SQL - trigger de apagar ficha de intervencao)
-- Trigger:   AFTER DELETE em u_fichaint
-- Objetivo:  ao apagar a ficha, apagar o dossier 63 com o mesmo stamp e dependentes (bi2, bi, bo3, bo2, bo)
-- Autor:     SigmaCode
-- Historico:
--   v01  2026-11-26  Versao inicial
-- ======================================================================
-- Ligacao: bo.bostamp = u_fichaint.u_fichaintstamp, ndos = 63. Ordem de apagar igual ao cancelar da API
-- (bi2, bi, bo3, bo2, bo).
-- So apaga se o dossier todo estiver em conferencia (bo.emconf = 1 e todas as linhas bi com emconf = 1). Se houver
-- qualquer emconf = 0 (ja confirmado), recusa: ROLLBACK + RAISERROR (por esta ordem, para funcionar tambem dentro de TRY/CATCH),
-- e a ficha nao e apagada.
-- RISCO: os DELETE em bi, bo e dependentes disparam os triggers encriptados do PHC. Testar na base de testes antes
-- de qualquer instalacao (06-testes-rollback.sql, casos 7).
-- Nao altera triggers existentes. AVISO: correr primeiro o SELECT DB_NAME() e confirmar a base.

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

IF OBJECT_ID('dbo.sc_u_fichaint_del', 'TR') IS NOT NULL DROP TRIGGER dbo.sc_u_fichaint_del;
GO
CREATE TRIGGER dbo.sc_u_fichaint_del ON dbo.u_fichaint
AFTER DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @dossier TABLE (bostamp varchar(25));

    INSERT @dossier (bostamp)
    SELECT o.bostamp
    FROM dbo.bo o
    INNER JOIN deleted d ON o.bostamp = d.u_fichaintstamp
    WHERE o.ndos = 63;

    IF NOT EXISTS (SELECT 1 FROM @dossier) RETURN;

    IF EXISTS (SELECT 1 FROM dbo.bo o INNER JOIN @dossier x ON o.bostamp = x.bostamp WHERE ISNULL(o.emconf, 0) = 0)
       OR EXISTS (SELECT 1 FROM dbo.bi b INNER JOIN @dossier x ON b.bostamp = x.bostamp WHERE ISNULL(b.emconf, 0) = 0)
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR('sc_u_fichaint_del: esta ficha ja foi confirmada (emconf = 0) e nao pode ser apagada.', 16, 1);
        RETURN;
    END

    DELETE FROM dbo.bi2 WHERE bi2stamp IN (SELECT b.bistamp FROM dbo.bi b INNER JOIN @dossier x ON b.bostamp = x.bostamp);
    DELETE FROM dbo.bi WHERE bostamp IN (SELECT bostamp FROM @dossier);
    DELETE FROM dbo.bo3 WHERE bo3stamp IN (SELECT bostamp FROM @dossier);
    DELETE FROM dbo.bo2 WHERE bo2stamp IN (SELECT bostamp FROM @dossier);
    DELETE FROM dbo.bo WHERE bostamp IN (SELECT bostamp FROM @dossier);
END
GO
SET NOEXEC OFF;
GO
