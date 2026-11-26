-- ======================================================================
-- SigmaCode, Lda
-- Cliente:   comum (Gestor de Manutencao)
-- Local:     SQL - views de leitura do saldo por localizacao
-- Trigger:   Execucao manual (instalar depois de 01)
-- Objetivo:  v_sc_pecaloc_disponivel, v_sc_pecaloc_semlugar, v_sc_pecaloc_divergencias (so leitura)
-- Autor:     SigmaCode
-- Historico:
--   v01  2026-11-26  Versao inicial
--   v02  2026-11-26  semlugar lista tambem 64/66 com buffer invalido; divergencias com ILEGIVEL; guarda de base de dados
--   v03  2026-11-26  semlugar mede o corredor do buffer como o trigger; corredor/estante NULL tratados; qtt '' e NULL nao sao ILEGIVEL
-- ======================================================================
-- Views so de leitura (WITH (NOLOCK) apenas aqui). Pressuposto: o reservado so conta saidas 63 de lugares (nao do buffer).
-- semlugar e divergencias percorrem bi/u_pecaloc: sao para diagnostico, nao para ecras frequentes.
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

-- Saldo por lugar, reservado (63 em conferencia, emconf = 1) e disponivel.
IF OBJECT_ID('dbo.v_sc_pecaloc_disponivel', 'V') IS NOT NULL DROP VIEW dbo.v_sc_pecaloc_disponivel;
GO
CREATE VIEW dbo.v_sc_pecaloc_disponivel
AS
SELECT LTRIM(RTRIM(u.ref)) AS ref, u.armazem, ISNULL(LTRIM(RTRIM(u.corredor)), '') AS corredor, ISNULL(LTRIM(RTRIM(u.estante)), '') AS estante,
       ISNULL(dbo.fn_sc_Inteiro(u.nivel), 0) AS nivel, ISNULL(dbo.fn_sc_Inteiro(u.posicao), 0) AS posicao,
       dbo.fn_sc_Qtd(u.qtt) AS qtt,
       ISNULL(r.reservado, 0) AS reservado,
       dbo.fn_sc_Qtd(u.qtt) - ISNULL(r.reservado, 0) AS disponivel
FROM dbo.u_pecaloc u WITH (NOLOCK)
LEFT JOIN (
    SELECT LTRIM(RTRIM(b.ref)) AS ref, l.corredor, l.estante, l.nivel, l.posicao,
           SUM(CASE WHEN ABS(b.qtt) < 9999999999 THEN CAST(b.qtt AS decimal(13, 3)) ELSE 0 END) AS reservado
    FROM dbo.bi b WITH (NOLOCK)
    CROSS APPLY dbo.fn_sc_LerLugar(CASE WHEN LTRIM(RTRIM(ISNULL(b.lobs2, ''))) <> '' THEN LTRIM(RTRIM(b.lobs2))
                                        WHEN CHARINDEX('|', ISNULL(b.lobs, '')) > 0 THEN SUBSTRING(b.lobs, CHARINDEX('|', b.lobs) + 1, 200)
                                        ELSE '' END) l
    WHERE b.ndos = 63 AND b.armazem = 104 AND b.emconf = 1
    GROUP BY LTRIM(RTRIM(b.ref)), l.corredor, l.estante, l.nivel, l.posicao
) r ON u.armazem = 104 AND r.ref = LTRIM(RTRIM(u.ref)) AND r.corredor = ISNULL(LTRIM(RTRIM(u.corredor)), '')
   AND r.estante = ISNULL(LTRIM(RTRIM(u.estante)), '')
   AND r.nivel = ISNULL(dbo.fn_sc_Inteiro(u.nivel), 0) AND r.posicao = ISNULL(dbo.fn_sc_Inteiro(u.posicao), 0);
GO

-- Linhas bi (armazem 104) em que o trigger nao consegue mover o saldo, ainda que o documento tenha gravado:
--   63: lugar ilegivel; 66: origem ou destino ilegivel (ou vazio); 64: buffer (u_configman.localentrada) vazio ou com corredor de mais de 10 caracteres;
--   63/66 cujo texto e o buffer, quando o buffer e invalido.
-- Criterio do buffer = o do trigger: corredor de fn_sc_LerLugar(localentrada) ou, se nao for um lugar completo, o proprio texto;
-- invalido se vazio ou com esse corredor acima de 10 caracteres (ex.: 'LOC-A-03-2-04' e valido, o corredor e 'A').
IF OBJECT_ID('dbo.v_sc_pecaloc_semlugar', 'V') IS NOT NULL DROP VIEW dbo.v_sc_pecaloc_semlugar;
GO
CREATE VIEW dbo.v_sc_pecaloc_semlugar
AS
SELECT b.ndos, b.bostamp, b.bistamp, LTRIM(RTRIM(b.ref)) AS ref, b.qtt, b.emconf, b.lobs, b.lobs2,
       CASE WHEN b.ndos = 63 THEN 'saida sem lugar legivel'
            WHEN b.ndos = 64 THEN 'entrada sem buffer valido'
            ELSE 'arrumacao com origem ou destino ilegivel' END AS motivo
FROM dbo.bi b WITH (NOLOCK)
CROSS JOIN (SELECT z.buf, CASE WHEN z.buf = CHAR(1) OR CASE WHEN lb.corredor IS NULL THEN LEN(z.buf) ELSE LEN(lb.corredor) END > 10 THEN 1 ELSE 0 END AS bufinv
            FROM (SELECT ISNULL(NULLIF((SELECT TOP 1 UPPER(LTRIM(RTRIM(ISNULL(localentrada, '')))) FROM dbo.u_configman WITH (NOLOCK)), ''), CHAR(1)) AS buf) z
            OUTER APPLY dbo.fn_sc_LerLugar(z.buf) lb) c
CROSS APPLY (SELECT UPPER(CASE WHEN b.ndos <> 63 THEN ''
                               WHEN LTRIM(RTRIM(ISNULL(b.lobs2, ''))) <> '' THEN LTRIM(RTRIM(b.lobs2))
                               WHEN CHARINDEX('|', ISNULL(b.lobs, '')) > 0 THEN LTRIM(RTRIM(SUBSTRING(b.lobs, CHARINDEX('|', b.lobs) + 1, 200)))
                               ELSE '' END) AS t63,
                    UPPER(LTRIM(RTRIM(ISNULL(b.lobs, '')))) AS o66, UPPER(LTRIM(RTRIM(ISNULL(b.lobs2, '')))) AS d66) t
OUTER APPLY dbo.fn_sc_LerLugar(t.t63) l63
OUTER APPLY dbo.fn_sc_LerLugar(CASE WHEN b.ndos = 66 THEN t.o66 ELSE '' END) l66o
OUTER APPLY dbo.fn_sc_LerLugar(CASE WHEN b.ndos = 66 THEN t.d66 ELSE '' END) l66d
WHERE b.armazem = 104 AND b.ndos IN (63, 64, 66)
  AND (
        (b.ndos = 63 AND ((l63.corredor IS NULL AND t.t63 <> c.buf) OR (c.bufinv = 1 AND t.t63 = c.buf)))
     OR (b.ndos = 64 AND c.bufinv = 1)
     OR (b.ndos = 66 AND ((l66o.corredor IS NULL AND t.o66 <> c.buf) OR (l66d.corredor IS NULL AND t.d66 <> c.buf)
                          OR (c.bufinv = 1 AND (t.o66 = c.buf OR t.d66 = c.buf))))
      );
GO

-- Divergencias: STOCK (soma da u_pecaloc, buffer incluido, difere do sa.stock do armazem 104),
-- NEGATIVO (saldo de um lugar abaixo de zero), DUPLICADO (mais de uma linha para o mesmo lugar)
-- e ILEGIVEL (qtt com texto nao numerico: o trigger nao lhe toca; '' e NULL contam como 0 e nao entram aqui).
IF OBJECT_ID('dbo.v_sc_pecaloc_divergencias', 'V') IS NOT NULL DROP VIEW dbo.v_sc_pecaloc_divergencias;
GO
CREATE VIEW dbo.v_sc_pecaloc_divergencias
AS
SELECT CAST('STOCK' AS varchar(10)) AS tipo, CAST(COALESCE(p.ref, s.ref) AS varchar(18)) AS ref,
       CAST(NULL AS varchar(30)) AS corredor, CAST(NULL AS varchar(30)) AS estante, CAST(NULL AS int) AS nivel, CAST(NULL AS int) AS posicao,
       CAST(ISNULL(p.q, 0) AS decimal(19, 3)) AS qtt_pecaloc, CAST(ISNULL(s.stock, 0) AS decimal(19, 3)) AS stock_sa,
       CAST('soma por lugar difere do stock do armazem 104' AS varchar(60)) AS detalhe
FROM (SELECT LTRIM(RTRIM(ref)) AS ref, SUM(dbo.fn_sc_Qtd(qtt)) AS q
      FROM dbo.u_pecaloc WITH (NOLOCK) WHERE armazem = 104 GROUP BY LTRIM(RTRIM(ref))) p
FULL OUTER JOIN (SELECT LTRIM(RTRIM(ref)) AS ref, SUM(stock) AS stock
                 FROM dbo.sa WITH (NOLOCK) WHERE armazem = 104 GROUP BY LTRIM(RTRIM(ref))) s ON p.ref = s.ref
WHERE ISNULL(p.q, 0) <> ISNULL(s.stock, 0)
UNION ALL
SELECT 'NEGATIVO', LTRIM(RTRIM(ref)), ISNULL(LTRIM(RTRIM(corredor)), ''), ISNULL(LTRIM(RTRIM(estante)), ''),
       ISNULL(dbo.fn_sc_Inteiro(nivel), 0), ISNULL(dbo.fn_sc_Inteiro(posicao), 0),
       dbo.fn_sc_Qtd(qtt), NULL, 'saldo negativo no lugar'
FROM dbo.u_pecaloc WITH (NOLOCK)
WHERE armazem = 104 AND dbo.fn_sc_QtdOk(qtt) = 1 AND dbo.fn_sc_Qtd(qtt) < 0
UNION ALL
SELECT 'ILEGIVEL', LTRIM(RTRIM(ref)), ISNULL(LTRIM(RTRIM(corredor)), ''), ISNULL(LTRIM(RTRIM(estante)), ''),
       ISNULL(dbo.fn_sc_Inteiro(nivel), 0), ISNULL(dbo.fn_sc_Inteiro(posicao), 0),
       NULL, NULL, 'qtt ilegivel (' + LEFT(ISNULL(qtt, ''), 13) + '): o trigger nao lhe toca'
FROM dbo.u_pecaloc WITH (NOLOCK)
WHERE armazem = 104 AND dbo.fn_sc_QtdOk(qtt) = 0
UNION ALL
SELECT 'DUPLICADO', ref, corredor, estante, nivel, posicao, SUM(q), NULL, 'lugar com ' + CAST(COUNT(*) AS varchar(10)) + ' linhas'
FROM (SELECT LTRIM(RTRIM(ref)) AS ref, ISNULL(LTRIM(RTRIM(corredor)), '') AS corredor, ISNULL(LTRIM(RTRIM(estante)), '') AS estante,
             ISNULL(dbo.fn_sc_Inteiro(nivel), 0) AS nivel, ISNULL(dbo.fn_sc_Inteiro(posicao), 0) AS posicao, dbo.fn_sc_Qtd(qtt) AS q
      FROM dbo.u_pecaloc WITH (NOLOCK) WHERE armazem = 104) x
GROUP BY ref, corredor, estante, nivel, posicao
HAVING COUNT(*) > 1;
GO
SET NOEXEC OFF;
GO
