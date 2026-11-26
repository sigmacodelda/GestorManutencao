-- ======================================================================
-- SigmaCode, Lda
-- Cliente:   comum (Gestor de Manutencao)
-- Local:     u_matinterv (SQL - trigger de apagar material da ficha)
-- Trigger:   AFTER DELETE em u_matinterv
-- Objetivo:  ao apagar o material de uma ficha, apagar as linhas bi (e bi2) do dossier 63 ligadas a esse material
-- Autor:     SigmaCode
-- Historico:
--   v01  2026-11-26  Versao inicial
-- ======================================================================
-- Ligacao: bi.bostamp = u_matinterv.u_fichaintstamp (o dossier 63 tem o stamp da ficha) e a parte de bi.lobs antes do '|'
-- = u_matinterv.u_matintervstamp.
-- So apaga se a linha estiver em conferencia (emconf = 1, o stock real do PHC ainda nao se moveu). Se alguma linha ligada
-- tiver emconf = 0 (ja confirmada), recusa: ROLLBACK + RAISERROR (por esta ordem, para funcionar tambem dentro de TRY/CATCH),
-- e o material nao e apagado.
-- RISCO: os DELETE em bi/bi2 disparam os triggers encriptados do PHC (e o sc_bi_pecaloc, sem efeito com emconf = 1).
-- Testar na base de testes antes de qualquer instalacao (06-testes-rollback.sql, casos 7).
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

IF OBJECT_ID('dbo.sc_u_matinterv_del', 'TR') IS NOT NULL DROP TRIGGER dbo.sc_u_matinterv_del;
GO
CREATE TRIGGER dbo.sc_u_matinterv_del ON dbo.u_matinterv
AFTER DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @lig TABLE (bistamp varchar(25), emconf bit);

    INSERT @lig (bistamp, emconf)
    SELECT b.bistamp, ISNULL(b.emconf, 0)
    FROM dbo.bi b
    INNER JOIN deleted d ON b.bostamp = d.u_fichaintstamp
    WHERE b.ndos = 63
      AND CASE WHEN CHARINDEX('|', ISNULL(b.lobs, '')) > 0
               THEN LTRIM(RTRIM(LEFT(b.lobs, CHARINDEX('|', b.lobs) - 1))) ELSE '' END = LTRIM(RTRIM(d.u_matintervstamp));

    IF NOT EXISTS (SELECT 1 FROM @lig) RETURN;

    IF EXISTS (SELECT 1 FROM @lig WHERE emconf = 0)
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR('sc_u_matinterv_del: este material ja foi confirmado (emconf = 0) e nao pode ser apagado.', 16, 1);
        RETURN;
    END

    DELETE FROM dbo.bi2 WHERE bi2stamp IN (SELECT bistamp FROM @lig);
    DELETE FROM dbo.bi WHERE bistamp IN (SELECT bistamp FROM @lig);
END
GO
SET NOEXEC OFF;
GO
