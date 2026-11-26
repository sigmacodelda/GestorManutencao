-- ======================================================================
-- SigmaCode, Lda
-- Cliente:   comum (Gestor de Manutencao)
-- Local:     ANALISE (SQL - levantamento, so leitura)
-- Trigger:   Execucao manual (SSMS)
-- Objetivo:  descobrir o que o trigger existente do PHC (LandeiroMaui) ja faz na
--            u_pecaloc, antes de instalar o trigger sc_bi_pecaloc
-- Autor:     SigmaCode
-- Historico:
--   v01  2026-11-26  Versao inicial
-- ======================================================================
-- So SELECT, INFORMATION_SCHEMA e sys: nunca INSERT, UPDATE, DELETE nem DDL.
-- Correr primeiro o 1.o SELECT e confirmar que a base e a de TESTES.
-- Colar os resultados (sem dados pessoais) de volta na conversa.

SELECT DB_NAME() AS base;                                                     -- 0. confirmar TESTES_LAND2

-- 1. Compatibilidade e versao
SELECT name, compatibility_level FROM sys.databases WHERE name = DB_NAME();
SELECT @@VERSION AS versao;

-- 2. Triggers existentes (IsEncrypted = 1: texto escondido; se for 0 colar a definicao)
SELECT OBJECT_NAME(t.parent_id) AS tabela, t.name AS trigger_nome, t.is_disabled,
       OBJECTPROPERTY(t.object_id, 'IsEncrypted') AS encriptado,
       OBJECTPROPERTY(t.object_id, 'ExecIsInsertTrigger') AS ins,
       OBJECTPROPERTY(t.object_id, 'ExecIsUpdateTrigger') AS upd,
       OBJECTPROPERTY(t.object_id, 'ExecIsDeleteTrigger') AS del
FROM sys.triggers t
WHERE OBJECT_NAME(t.parent_id) IN ('bi', 'bo', 'u_matinterv', 'u_fichaint', 'u_pecaloc')
ORDER BY tabela, trigger_nome;

-- 3. Tipos de dossier 63, 64, 66
SELECT * FROM ts WHERE ndos IN (63, 64, 66);

-- 4. Tipos e tamanhos reais
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE (TABLE_NAME = 'bi' AND COLUMN_NAME IN ('lobs', 'lobs2', 'lobs3', 'emconf', 'qtt', 'ref', 'armazem', 'ndos', 'bostamp', 'bistamp'))
   OR (TABLE_NAME = 'bo' AND COLUMN_NAME IN ('lobs', 'lobs2', 'emconf', 'bostamp', 'ndos'))
   OR (TABLE_NAME = 'u_matinterv' AND COLUMN_NAME IN ('u_matintervstamp', 'u_fichaintstamp', 'ref'))
   OR (TABLE_NAME = 'u_fichaint' AND COLUMN_NAME = 'u_fichaintstamp')
   OR (TABLE_NAME = 'u_configman' AND COLUMN_NAME = 'localentrada')
   OR TABLE_NAME = 'u_pecaloc'
ORDER BY TABLE_NAME, ORDINAL_POSITION;

-- 5. u_pecaloc: indices, linhas, duplicados por lugar e buffer
SELECT i.name AS indice, i.is_unique, i.is_primary_key FROM sys.indexes i WHERE i.object_id = OBJECT_ID('u_pecaloc') AND i.name IS NOT NULL;
SELECT COUNT(*) AS linhas FROM u_pecaloc WITH (NOLOCK);
SELECT ref, armazem, corredor, estante, nivel, posicao, COUNT(*) AS n
FROM u_pecaloc WITH (NOLOCK) GROUP BY ref, armazem, corredor, estante, nivel, posicao HAVING COUNT(*) > 1;
SELECT localentrada FROM u_configman WITH (NOLOCK);

-- 6. Dossiers 64 e 66 por estado
SELECT ndos, emconf, COUNT(*) AS dossiers FROM bo WITH (NOLOCK) WHERE ndos IN (63, 64, 66) GROUP BY ndos, emconf ORDER BY ndos, emconf;

SELECT TOP 5 b.bostamp, b.obrano, b.emconf, b.lobs, b.lobs2,
       i.ref, i.qtt, i.emconf AS emconf_linha, i.lobs AS bi_lobs, i.lobs2 AS bi_lobs2, i.lobs3 AS bi_lobs3
FROM bo b WITH (NOLOCK) JOIN bi i WITH (NOLOCK) ON i.bostamp = b.bostamp
WHERE b.ndos = 66 ORDER BY b.dataobra DESC;

SELECT TOP 5 i.ref, i.qtt, i.emconf, i.lobs, i.armazem
FROM bi i WITH (NOLOCK) WHERE i.ndos = 64 ORDER BY i.rdata DESC;

-- 7. Entradas 64 vs u_pecaloc (inclui buffer) vs stock sa, para 3 refs de entradas recentes (armazem 104)
SELECT x.ref,
       (SELECT SUM(i.qtt) FROM bi i WITH (NOLOCK) WHERE i.ndos = 64 AND i.armazem = 104 AND i.ref = x.ref AND i.emconf = 0) AS soma_64,
       (SELECT SUM(CAST(p.qtt AS decimal(13,3))) FROM u_pecaloc p WITH (NOLOCK) WHERE p.ref = x.ref AND p.armazem = 104) AS soma_u_pecaloc,
       (SELECT s.stock FROM sa s WITH (NOLOCK) WHERE s.ref = x.ref AND s.armazem = 104) AS stock_sa
FROM (SELECT DISTINCT TOP 3 i.ref FROM bi i WITH (NOLOCK) WHERE i.ndos = 64 AND i.armazem = 104 ORDER BY i.ref) x;

-- 8. Saidas 63: por emconf e exemplos de bi.lobs ('<stamp>|<lugar>')
SELECT emconf, COUNT(*) AS linhas FROM bi WITH (NOLOCK) WHERE ndos = 63 GROUP BY emconf;
SELECT TOP 5 i.ref, i.qtt, i.emconf, i.lobs FROM bi i WITH (NOLOCK) WHERE i.ndos = 63 ORDER BY i.rdata DESC;
