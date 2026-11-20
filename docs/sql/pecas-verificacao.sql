-- ======================================================================
-- SigmaCode, Lda
-- Cliente:   comum (Gestor de Manutencao)
-- Local:     ANALISE (SQL - verificacao de pre-requisitos, so leitura)
-- Trigger:   Execucao manual
-- Objetivo:  confirmar numa base que tem o que o circuito de pecas v2 precisa
--            (conferencia emconf, u_pecaloc, bi.lobs, ts 63 e 64, armazem 104)
-- Autor:     SigmaCode
-- Historico:
--   v01  2026-11-20  Versao inicial (circuito antigo: entrada e saida)
--   v02  2026-11-20  Circuito v2: emconf, u_pecaloc, u_tarefapeca; so leituras
-- ======================================================================
-- So SELECT, INFORMATION_SCHEMA e sys: nunca INSERT, UPDATE, DELETE nem DDL.
-- AVISO: correr primeiro o 1.o SELECT e confirmar que a base e a esperada (TESTES) antes de correr o resto.
-- Se algo faltar, criar o que falta e no PHC (campo, dossier, armazem, tabela), nao por script do Claude Code.
-- Verificado em TESTES_LAND2 em 2026-11-20. Nota: COL_LENGTH devolve smallint.

SELECT DB_NAME() AS base;                                                     -- confirmar que e de TESTES

-- 1. Conferencia: campo emconf em bo e bi (esperado: 2 linhas, tipo bit)
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('bo', 'bi') AND COLUMN_NAME = 'emconf';

-- 2. bi.lobs (esperado: varchar, >= 40). Guarda "<stamp do material>|<lugar>". bi.lobs2 so informacao (o dicionario base nao o tem).
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'bi' AND COLUMN_NAME IN ('lobs', 'lobs2');

-- 3. u_pecaloc: existe? colunas e tipos (esperado: qtt, nivel e posicao varchar; marcada bit)
SELECT OBJECT_ID('u_pecaloc') AS u_pecaloc_object_id;                         -- NULL = nao existe nesta base
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'u_pecaloc'
ORDER BY ORDINAL_POSITION;
SELECT
  CASE WHEN COUNT(*) = 3 THEN 'OK: qtt, nivel e posicao sao varchar' ELSE 'ATENCAO: qtt, nivel ou posicao nao sao varchar' END AS u_pecaloc_texto
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'u_pecaloc' AND COLUMN_NAME IN ('qtt', 'nivel', 'posicao') AND DATA_TYPE = 'varchar';

-- Indices de u_pecaloc (esperado: so a chave primaria; sem indice unico por lugar)
SELECT i.name AS indice, i.is_unique, i.is_primary_key
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('u_pecaloc') AND i.name IS NOT NULL;

-- 4. u_tarefapeca (pecas do plano; nao usada pela app): colunas esperadas itmanutlistamp, ref, design, qtt
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'u_tarefapeca'
ORDER BY ORDINAL_POSITION;

-- 5. Campo stobs.u_manut (peca = artigo com u_manut = 1) e quantas pecas ha
SELECT COL_LENGTH('stobs', 'u_manut') AS campo_u_manut;                       -- esperado: nao NULL
SELECT COUNT(*) AS pecas FROM stobs WHERE u_manut = 1;                        -- esperado: > 0

-- 6. Tipos de dossier 63 (saida da ficha, em conferencia) e 64 (entrada, da LandeiroMaui)
SELECT ndos, nmdos FROM ts WHERE ndos IN (63, 64);                            -- esperado: 2 linhas

-- 7. Armazem de manutencao 104
SELECT no, nome FROM sz WHERE no = 104;

-- 8. Colunas de u_matinterv que a saida escreve (ligacao material -> peca) e tamanho do stamp usado em bi.lobs
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'u_matinterv' AND COLUMN_NAME IN ('u_matintervstamp', 'u_fichaintstamp', 'ref', 'usrinis', 'usrdata', 'usrhora');
SELECT MAX(LEN(LTRIM(RTRIM(u_matintervstamp)))) AS stamp_material_max FROM u_matinterv;   -- esperado: < 25 (o codigo parte bi.lobs no '|')

-- 9. Triggers de bo e bi: so a listar (nunca alterar, desativar nem apagar); is_disabled = 0 significa ativo
SELECT OBJECT_NAME(parent_id) AS tabela, name AS trigger_nome, is_disabled
FROM sys.triggers
WHERE OBJECT_NAME(parent_id) IN ('bo', 'bi')
ORDER BY tabela, trigger_nome;

-- 10. Dossiers 63 e 64 recentes e quantos estao em conferencia (so leitura)
SELECT TOP 5 bostamp, ndos, obrano, obranome, dataopen, emconf FROM bo WHERE ndos = 63 ORDER BY dataopen DESC;
SELECT TOP 5 bostamp, ndos, obrano, obranome, dataopen FROM bo WHERE ndos = 64 ORDER BY dataopen DESC;
SELECT COUNT(*) AS fichas_em_conferencia FROM bo WHERE ndos = 63 AND emconf = 1;
