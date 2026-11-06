-- Tabela de utilizadores da app Gestor de Manutenção (ver docs/tabela-utilizadores.md).
-- A correr por quem administra o PHC, primeiro numa base de testes. O Claude Code não executa este script.
-- Compatível com SQL Server nível de compatibilidade 100.

IF OBJECT_ID('dbo.u_manutil') IS NULL
BEGIN
    CREATE TABLE dbo.u_manutil
    (
        u_manutilstamp  char(25)     NOT NULL,
        utilizador      varchar(30)  NOT NULL,
        nome            varchar(60)  NOT NULL,
        perfil          varchar(12)  NOT NULL,
        ativo           bit          NOT NULL CONSTRAINT DF_u_manutil_ativo DEFAULT (1),
        pinhash         varchar(64)  NOT NULL,
        pinmudar        bit          NOT NULL CONSTRAINT DF_u_manutil_pinmudar DEFAULT (1),
        pinalteradopor  varchar(30)  NOT NULL CONSTRAINT DF_u_manutil_pinpor DEFAULT (''),
        pinalteradoem   datetime     NULL,
        tecno           numeric(5,0) NOT NULL CONSTRAINT DF_u_manutil_tecno DEFAULT (0),
        ousrinis        varchar(30)  NOT NULL CONSTRAINT DF_u_manutil_ousrinis DEFAULT (''),
        ousrdata        datetime     NOT NULL CONSTRAINT DF_u_manutil_ousrdata DEFAULT ('19000101'),
        ousrhora        varchar(8)   NOT NULL CONSTRAINT DF_u_manutil_ousrhora DEFAULT (''),
        usrinis         varchar(30)  NOT NULL CONSTRAINT DF_u_manutil_usrinis DEFAULT (''),
        usrdata         datetime     NOT NULL CONSTRAINT DF_u_manutil_usrdata DEFAULT ('19000101'),
        usrhora         varchar(8)   NOT NULL CONSTRAINT DF_u_manutil_usrhora DEFAULT (''),
        CONSTRAINT PK_u_manutil PRIMARY KEY CLUSTERED (u_manutilstamp),
        CONSTRAINT CK_u_manutil_perfil CHECK (perfil IN ('OPERADOR', 'SUPERVISOR'))
    );

    -- Um código por pessoa e um PIN por pessoa (entra-se só com o PIN, por isso não podem repetir-se).
    CREATE UNIQUE INDEX UX_u_manutil_utilizador ON dbo.u_manutil (utilizador);
    CREATE UNIQUE INDEX UX_u_manutil_pinhash    ON dbo.u_manutil (pinhash);
END
GO

-- Para a tabela que já existe sem o campo: número de utilizador do PHC (us.userno), a identidade da pessoa nas fichas.
-- 0 = ainda sem número. O número é indicado pelo supervisor na app (Equipa).
IF COL_LENGTH('dbo.u_manutil', 'tecno') IS NULL
    ALTER TABLE dbo.u_manutil ADD tecno numeric(5,0) NOT NULL CONSTRAINT DF_u_manutil_tecno DEFAULT (0);
GO

-- Login SQL próprio da API, só com o necessário (substituir o nome do utilizador SQL; a password define-se à parte).
-- GRANT SELECT, INSERT, UPDATE ON dbo.u_manutil TO [<utilizador_sql_da_api>];
