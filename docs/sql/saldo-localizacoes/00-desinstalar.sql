-- ======================================================================
-- SigmaCode, Lda
-- Cliente:   comum (Gestor de Manutencao)
-- Local:     SQL - saldo por localizacao (u_pecaloc) mantido por trigger
-- Trigger:   Execucao manual
-- Objetivo:  remover os objetos sc_ do saldo por localizacao (so os criados por 01 a 05)
-- Autor:     SigmaCode
-- Historico:
--   v01  2026-11-26  Versao inicial
-- ======================================================================
-- Remove apenas objetos com os nomes exatos abaixo, por tipo (TR, V, TF, FN). Nunca toca nos triggers do PHC.
-- AVISO: correr primeiro o SELECT DB_NAME() e confirmar a base.

-- Para correr noutra base (cliente): so depois de GATE do utilizador e acrescentando o nome a lista da guarda abaixo.
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
IF OBJECT_ID('dbo.sc_u_matinterv_del', 'TR') IS NOT NULL DROP TRIGGER dbo.sc_u_matinterv_del;
GO
IF OBJECT_ID('dbo.sc_u_fichaint_del', 'TR') IS NOT NULL DROP TRIGGER dbo.sc_u_fichaint_del;
GO
IF OBJECT_ID('dbo.v_sc_pecaloc_disponivel', 'V') IS NOT NULL DROP VIEW dbo.v_sc_pecaloc_disponivel;
GO
IF OBJECT_ID('dbo.v_sc_pecaloc_semlugar', 'V') IS NOT NULL DROP VIEW dbo.v_sc_pecaloc_semlugar;
GO
IF OBJECT_ID('dbo.v_sc_pecaloc_divergencias', 'V') IS NOT NULL DROP VIEW dbo.v_sc_pecaloc_divergencias;
GO
IF OBJECT_ID('dbo.fn_sc_LerLugar', 'TF') IS NOT NULL DROP FUNCTION dbo.fn_sc_LerLugar;
GO
IF OBJECT_ID('dbo.fn_sc_Qtd', 'FN') IS NOT NULL DROP FUNCTION dbo.fn_sc_Qtd;
GO
IF OBJECT_ID('dbo.fn_sc_QtdOk', 'FN') IS NOT NULL DROP FUNCTION dbo.fn_sc_QtdOk;
GO
IF OBJECT_ID('dbo.fn_sc_Inteiro', 'FN') IS NOT NULL DROP FUNCTION dbo.fn_sc_Inteiro;
GO

SELECT name, type_desc FROM sys.objects WHERE name IN ('sc_bi_pecaloc', 'sc_u_matinterv_del', 'sc_u_fichaint_del',
    'v_sc_pecaloc_disponivel', 'v_sc_pecaloc_semlugar', 'v_sc_pecaloc_divergencias', 'fn_sc_LerLugar', 'fn_sc_Qtd', 'fn_sc_QtdOk', 'fn_sc_Inteiro');  -- esperado: 0 linhas
GO
SET NOEXEC OFF;
GO
