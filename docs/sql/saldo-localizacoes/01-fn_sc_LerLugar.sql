-- ======================================================================
-- SigmaCode, Lda
-- Cliente:   comum (Gestor de Manutencao)
-- Local:     SQL - funcoes auxiliares do saldo por localizacao
-- Trigger:   Execucao manual (instalar antes de 02 a 05)
-- Objetivo:  fn_sc_Inteiro e fn_sc_Qtd (texto varchar para numero, sem falhar) e
--            fn_sc_LerLugar (mesmas regras que RegrasLocalizacao.LerLugar da API)
-- Autor:     SigmaCode
-- Historico:
--   v01  2026-11-26  Versao inicial
--   v02  2026-11-26  qtt '' e NULL contam como 0 legivel (fn_sc_QtdOk/fn_sc_Qtd); so texto nao numerico e ilegivel
-- ======================================================================
-- Compativel com nivel de compatibilidade 100. Nenhuma funcao falha por dados do utilizador.
-- AVISO: correr primeiro o SELECT DB_NAME() e confirmar a base.
-- Diferenca conhecida face ao C#: letras reconhecidas = A-Z e as letras latinas do cp1252 (Latin-1), nao todo o Unicode.

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

-- Inteiro so com digitos (zeros a esquerda aceites); NULL se vazio ou com outros caracteres; mais de 9 digitos significativos = 999999999.
IF OBJECT_ID('dbo.fn_sc_Inteiro', 'FN') IS NOT NULL DROP FUNCTION dbo.fn_sc_Inteiro;
GO
CREATE FUNCTION dbo.fn_sc_Inteiro (@t varchar(50))
RETURNS int
AS
BEGIN
    DECLARE @s varchar(50);
    SET @s = LTRIM(RTRIM(ISNULL(@t, '')));
    IF @s = '' OR @s LIKE '%[^0-9]%' RETURN NULL;
    IF @s NOT LIKE '%[^0]%' RETURN 0;
    SET @s = SUBSTRING(@s, PATINDEX('%[^0]%', @s), 50);
    IF LEN(@s) > 9 RETURN 999999999;
    RETURN CAST(@s AS int);
END
GO

-- 1 se o texto e uma quantidade legivel (ex.: '7.500', '1', '-2.000'); '' e NULL contam como 0 (igual ao C#), logo 1;
-- 0 para '12,5', texto, etc.
IF OBJECT_ID('dbo.fn_sc_QtdOk', 'FN') IS NOT NULL DROP FUNCTION dbo.fn_sc_QtdOk;
GO
CREATE FUNCTION dbo.fn_sc_QtdOk (@t varchar(50))
RETURNS bit
AS
BEGIN
    DECLARE @s varchar(50), @inteira varchar(50);
    SET @s = LTRIM(RTRIM(ISNULL(@t, '')));
    IF @s = '' RETURN 1;
    IF @s LIKE '%[0-9]%' AND @s NOT LIKE '%[^0-9.-]%' AND LEN(@s) <= 13
       AND LEN(@s) - LEN(REPLACE(@s, '.', '')) <= 1
       AND (LEN(@s) - LEN(REPLACE(@s, '-', '')) = 0 OR (LEFT(@s, 1) = '-' AND LEN(@s) - LEN(REPLACE(@s, '-', '')) = 1))
    BEGIN
        SET @inteira = REPLACE(CASE WHEN CHARINDEX('.', @s) > 0 THEN LEFT(@s, CHARINDEX('.', @s) - 1) ELSE @s END, '-', '');
        IF LEN(@inteira) <= 10 RETURN 1;
    END
    RETURN 0;
END
GO

-- Quantidade em texto para decimal(13,3); '' e NULL = 0; ilegivel = 0 (o trigger nunca escreve por cima de uma qtt ilegivel: ver fn_sc_QtdOk).
IF OBJECT_ID('dbo.fn_sc_Qtd', 'FN') IS NOT NULL DROP FUNCTION dbo.fn_sc_Qtd;
GO
CREATE FUNCTION dbo.fn_sc_Qtd (@t varchar(50))
RETURNS decimal(13, 3)
AS
BEGIN
    DECLARE @s varchar(50), @r decimal(13, 3);
    SET @r = 0;
    SET @s = LTRIM(RTRIM(ISNULL(@t, '')));
    IF @s <> '' AND dbo.fn_sc_QtdOk(@s) = 1 SET @r = CAST(@s AS decimal(13, 3));
    RETURN @r;
END
GO

-- Devolve 0 linhas se o texto nao for um lugar completo valido; senao 1 linha (corredor, estante normalizada, nivel, posicao).
-- Regras: separador = qualquer caracter que nao seja letra nem digito; prefixo LOC opcional; 4 partes (corredor-estante-nivel-posicao);
-- corredor ate 3 caracteres; estante 1..999; nivel 1..99; posicao 1..99. Estante normalizada = CORREDOR-NN (ex.: A-03).
IF OBJECT_ID('dbo.fn_sc_LerLugar', 'TF') IS NOT NULL DROP FUNCTION dbo.fn_sc_LerLugar;
GO
CREATE FUNCTION dbo.fn_sc_LerLugar (@texto varchar(200))
RETURNS @r TABLE (corredor varchar(10), estante varchar(20), nivel int, posicao int)
AS
BEGIN
    DECLARE @t varchar(200), @i int, @n int, @c char(1), @a int, @tok varchar(200), @partes int;
    DECLARE @p1 varchar(200), @p2 varchar(200), @p3 varchar(200), @p4 varchar(200), @p5 varchar(200);
    DECLARE @estante int, @nivel int, @posicao int;

    SET @t = UPPER(LTRIM(RTRIM(ISNULL(@texto, ''))));
    SET @n = LEN(@t);
    SET @i = 1;
    SET @tok = '';
    SET @partes = 0;

    -- parte o texto em fichas de letras/digitos (uma posicao a mais para fechar a ultima ficha)
    WHILE @i <= @n + 1
    BEGIN
        IF @i <= @n SET @c = SUBSTRING(@t, @i, 1)
        ELSE SET @c = ' ';
        SET @a = ASCII(@c);
        IF (@a BETWEEN 48 AND 57) OR (@a BETWEEN 65 AND 90) OR (@a BETWEEN 97 AND 122)
           OR (@a >= 192 AND @a NOT IN (215, 247)) OR @a IN (138, 140, 142, 154, 156, 158, 159, 170, 181, 186)
            SET @tok = @tok + @c
        ELSE IF @tok <> ''
        BEGIN
            SET @partes = @partes + 1;
            IF @partes = 1 SET @p1 = @tok
            ELSE IF @partes = 2 SET @p2 = @tok
            ELSE IF @partes = 3 SET @p3 = @tok
            ELSE IF @partes = 4 SET @p4 = @tok
            ELSE IF @partes = 5 SET @p5 = @tok
            ELSE RETURN;
            SET @tok = '';
        END
        SET @i = @i + 1;
    END

    IF @partes >= 1 AND @p1 = 'LOC'
    BEGIN
        SET @p1 = @p2; SET @p2 = @p3; SET @p3 = @p4; SET @p4 = @p5;
        SET @partes = @partes - 1;
    END
    IF @partes <> 4 RETURN;
    IF LEN(@p1) > 3 RETURN;

    SET @estante = dbo.fn_sc_Inteiro(@p2);
    SET @nivel = dbo.fn_sc_Inteiro(@p3);
    SET @posicao = dbo.fn_sc_Inteiro(@p4);
    IF @estante IS NULL OR @nivel IS NULL OR @posicao IS NULL RETURN;
    IF @estante < 1 OR @estante > 999 OR @nivel < 1 OR @nivel > 99 OR @posicao < 1 OR @posicao > 99 RETURN;

    INSERT @r (corredor, estante, nivel, posicao)
    VALUES (@p1, @p1 + '-' + CASE WHEN @estante < 10 THEN '0' ELSE '' END + CAST(@estante AS varchar(3)), @nivel, @posicao);
    RETURN;
END
GO
SET NOEXEC OFF;
GO
