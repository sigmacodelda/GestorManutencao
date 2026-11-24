-- Validação (só leitura) do SQL novo do Gestor de Manutenção. Correr na base de TESTES, no SSMS.
-- Nada aqui escreve: são só SELECT. Confirme primeiro que está na base de testes.
SELECT DB_NAME() AS base;

-- 1) Tamanhos das colunas que a app grava (bytes; varchar(n) = n; -1 = max; NULL = não existe)
SELECT 'bo.maquina' AS coluna, COL_LENGTH('bo', 'maquina') AS tamanho
UNION ALL SELECT 'bi.lobs',  COL_LENGTH('bi', 'lobs')
UNION ALL SELECT 'bi.lobs2', COL_LENGTH('bi', 'lobs2')
UNION ALL SELECT 'bi.lobs3', COL_LENGTH('bi', 'lobs3')
UNION ALL SELECT 'u_configman.iniciarsemstock', COL_LENGTH('u_configman', 'iniciarsemstock')
UNION ALL SELECT 'stobs.u_diasent', COL_LENGTH('stobs', 'u_diasent');
-- Esperado: bo.maquina >= 25 (stamp da ficha); bi.lobs3 >= 13 (lugar tipo A-03-2-04) e bi.lobs2 idem; os dois últimos podem ser NULL.

-- 2) O dossier 65 existe e o que os técnicos lá têm (só os 20 mais recentes)
SELECT TOP 20 b.bostamp, b.obrano, b.obranome, b.maquina, b.logi1, b.logi2, b.dataopen
FROM bo b WITH (NOLOCK) WHERE b.ndos = 65 ORDER BY b.dataopen DESC;
SELECT ndos, nmdos, stocks FROM ts WITH (NOLOCK) WHERE ndos IN (63, 64, 65);  -- stocks do 65 tem de ser 0

-- 3) bo.maquina não numérico em inventários (o desktop faz CAST; '1e5' rebentaria)
SELECT bostamp, maquina FROM bo WITH (NOLOCK) WHERE ndos = 65 AND ISNUMERIC(maquina) = 1 AND TRY_CAST(LTRIM(RTRIM(maquina)) AS numeric(18, 0)) IS NULL;
-- Esperado: 0 linhas.

-- 4) Linhas do 65 já contadas: o lugar tem de estar em lobs3 e com posição (4 partes, ex. A-03-2-04)
SELECT TOP 50 i.bostamp, LTRIM(RTRIM(i.ref)) AS ref, i.qtt, i.lobs, i.lobs3
FROM bi i WITH (NOLOCK) WHERE i.ndos = 65 ORDER BY i.bostamp, i.lordem;

-- 5) Dossiers 63: bo.maquina = stamp da ficha (depois de a app acrescentar linhas)
SELECT TOP 20 b.bostamp, b.maquina, CASE WHEN b.maquina = b.bostamp THEN 1 ELSE 0 END AS ligado
FROM bo b WITH (NOLOCK) WHERE b.ndos = 63 ORDER BY b.dataopen DESC;
-- Linhas do 63 com origem e motivo
SELECT TOP 20 i.bistamp, i.lobs, i.lobs2 AS origem, i.lobs3 AS motivo
FROM bi i WITH (NOLOCK) WHERE i.ndos = 63 ORDER BY i.ousrdata DESC, i.ousrhora DESC;

-- 6) Iniciar sem stock: o campo e o stock no armazém 104 de uma peça (trocar @ref)
DECLARE @ref varchar(18) = 'TROCAR-REF';
SELECT ISNULL(SUM(stock), 0) AS stock104 FROM sa WITH (NOLOCK) WHERE LTRIM(RTRIM(ref)) = @ref AND armazem = 104;
SELECT TOP 1 column_name FROM information_schema.columns WHERE table_name = 'u_tarefapeca' AND column_name LIKE '%itmanutlistamp';
SELECT COL_LENGTH('u_fichaint', 'uitmanutlistamp') AS tarefa_na_ficha;  -- NULL = o nome da coluna da tarefa é outro

-- 7) Triggers em bo e bi (a app faz UPDATE de logi1 e INSERT de linhas 65): só ver que existem
SELECT OBJECT_NAME(parent_id) AS tabela, name, is_disabled FROM sys.triggers WHERE OBJECT_NAME(parent_id) IN ('bo', 'bi') ORDER BY 1, 2;
