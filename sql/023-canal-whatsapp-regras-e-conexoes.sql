/* =============================================================================
   CRM-Cartorio — Canal WhatsApp: regras de numero e historico de conexao
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-22
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 010-canal-whatsapp.sql, 021-versao-do-schema.sql

   O QUE FAZ
       1. Amplia dbo.Tenants com o modo de recebimento do canal e o perfil da
          sessao pareada (numero ja mora em WhatsappNumeroExibicao, de
          001-schema.sql — nao duplicado aqui).
       2. Cria dbo.CanalWhatsappNumeros — numeros liberados ou bloqueados a falar
          com o canal desta serventia.
       3. Cria dbo.CanalWhatsappConexoes — historico de sessoes (quando pareou,
          quando caiu, quem trocou).
       4. Cria dbo.CanalWhatsappDescartes — mensagens recebidas e descartadas pelo
          filtro de numero, para a tela responder "por que a mensagem de fulano
          nao chegou".

   POR QUE WhatsappModoDeRecebimento NASCE NULL, E NAO COM UM PADRAO
       NULL e um QUARTO ESTADO por fora do enum ModoDeRecebimento (0 Todos,
       1 SomenteLiberados, 2 TodosMenosBloqueados): significa "ninguem configurou
       isto pela tela ainda". Nesse estado o backend continua obedecendo
       CanalWhatsApp:NumerosDeTeste do appsettings — o comportamento que ja esta
       em producao nos tres ambientes. Gravar um padrao aqui (0 ou 1) mudaria esse
       comportamento na hora em que a migracao rodasse, sem ninguem ter decidido
       nada pela tela. So quando alguem salva um modo pelo painel o banco passa a
       mandar, e so dai o appsettings vira letra morta (ver
       Modules/CanalWhatsApp/Application/RegrasDoCanal.cs).

   POR QUE TRES TABELAS, E NAO DUAS LISTAS DENTRO DE UMA SO
       CanalWhatsappNumeros e uma tabela so, com a coluna Tipo decidindo se a
       linha e liberacao ou bloqueio — o indice unico (TenantId, Telefone) ja
       impede o mesmo numero de estar nas duas listas ao mesmo tempo, contradicao
       que duas tabelas separadas permitiriam.

       CanalWhatsappConexoes e CanalWhatsappDescartes sao historico, nao
       configuracao: nenhuma delas alimenta a decisao "pode falar com o canal
       agora" (isso e sempre CanalWhatsappNumeros + Tenants.WhatsappModoDeRecebimento).

   IDEMPOTENCIA
       Coluna, tabela e indice so sao criados quando ainda nao existem. Rodar de
       novo nao muda nada.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT IN (N'CRM_Cartorio', N'CRM_Cartorio_Teste', N'CRM_Cartorio_Dev')
BEGIN
    RAISERROR('Recusado: este script so roda em CRM_Cartorio, CRM_Cartorio_Teste ou CRM_Cartorio_Dev. Confira o banco selecionado.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL OR OBJECT_ID(N'dbo.Atendentes', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* -----------------------------------------------------------------------------
   1. Tenants — modo de recebimento e perfil da sessao pareada
   -------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.Tenants', N'WhatsappModoDeRecebimento') IS NULL
    ALTER TABLE dbo.Tenants ADD WhatsappModoDeRecebimento TINYINT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Tenants_WhatsappModoDeRecebimento')
    ALTER TABLE dbo.Tenants WITH CHECK ADD CONSTRAINT CK_Tenants_WhatsappModoDeRecebimento
        CHECK (WhatsappModoDeRecebimento IS NULL OR WhatsappModoDeRecebimento IN (0, 1, 2));
GO

IF COL_LENGTH(N'dbo.Tenants', N'WhatsappPerfilNome') IS NULL
    ALTER TABLE dbo.Tenants ADD WhatsappPerfilNome NVARCHAR(120) NULL;
GO

IF COL_LENGTH(N'dbo.Tenants', N'WhatsappPerfilFotoUrl') IS NULL
    ALTER TABLE dbo.Tenants ADD WhatsappPerfilFotoUrl NVARCHAR(500) NULL;
GO

IF COL_LENGTH(N'dbo.Tenants', N'WhatsappConectadoEm') IS NULL
    ALTER TABLE dbo.Tenants ADD WhatsappConectadoEm DATETIME2(3) NULL;
GO

/* -----------------------------------------------------------------------------
   2. CanalWhatsappNumeros — liberados e bloqueados, numa tabela so
   -------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.CanalWhatsappNumeros', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CanalWhatsappNumeros
    (
        Id                    UNIQUEIDENTIFIER NOT NULL,
        TenantId              UNIQUEIDENTIFIER NOT NULL,

        -- So digitos, com o codigo do pais (ex.: "5538991234567").
        Telefone              VARCHAR(20)      NOT NULL,

        Rotulo                NVARCHAR(80)     NULL,

        -- 1 Liberado, 2 Bloqueado.
        Tipo                  TINYINT          NOT NULL,

        -- Quem adicionou. Nulo em registro que vier de migracao retroativa.
        CriadoPorAtendenteId  UNIQUEIDENTIFIER NULL,

        CriadoEm              DATETIME2(3)     NOT NULL,
        AtualizadoEm          DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_CanalWhatsappNumeros PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_CanalWhatsappNumeros_Tenants
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_CanalWhatsappNumeros_Atendentes
            FOREIGN KEY (CriadoPorAtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT CK_CanalWhatsappNumeros_Tipo CHECK (Tipo IN (1, 2))
    );

    -- O mesmo numero nao pode estar liberado e bloqueado ao mesmo tempo — a
    -- contradicao e recusada aqui, nao so validada na aplicacao.
    CREATE UNIQUE INDEX UQ_CanalWhatsappNumeros_Tenant_Telefone
        ON dbo.CanalWhatsappNumeros (TenantId, Telefone);
END
GO

/* -----------------------------------------------------------------------------
   3. CanalWhatsappConexoes — historico de sessoes do canal
   -------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.CanalWhatsappConexoes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CanalWhatsappConexoes
    (
        Id             UNIQUEIDENTIFIER NOT NULL,
        TenantId       UNIQUEIDENTIFIER NOT NULL,

        -- Numero pareado nesta sessao. Nulo se a sessao caiu antes do gateway
        -- informar o numero.
        Numero         VARCHAR(20)      NULL,
        PerfilNome     NVARCHAR(120)    NULL,

        ConectadoEm    DATETIME2(3)     NOT NULL,

        -- Nulo enquanto a sessao esta em curso — no maximo uma linha assim por
        -- tenant.
        DesconectadoEm DATETIME2(3)     NULL,

        -- Quem pediu a troca ou a desconexao pela tela. Nulo em pareamento e em
        -- queda avisada pelo proprio gateway (connection.update).
        AtendenteId    UNIQUEIDENTIFIER NULL,

        -- 1 Pareamento, 2 Troca, 3 Desconexao, 4 QuedaDoGateway.
        Motivo         TINYINT          NOT NULL,

        CriadoEm       DATETIME2(3)     NOT NULL,
        AtualizadoEm   DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_CanalWhatsappConexoes PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_CanalWhatsappConexoes_Tenants
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_CanalWhatsappConexoes_Atendentes
            FOREIGN KEY (AtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT CK_CanalWhatsappConexoes_Motivo CHECK (Motivo IN (1, 2, 3, 4))
    );

    -- Consulta da tela: as ultimas sessoes de um tenant, mais recente primeiro.
    CREATE INDEX IX_CanalWhatsappConexoes_Tenant_ConectadoEm
        ON dbo.CanalWhatsappConexoes (TenantId, ConectadoEm);
END
GO

/* -----------------------------------------------------------------------------
   4. CanalWhatsappDescartes — mensagens recebidas e descartadas pelo filtro
   -----------------------------------------------------------------------------
   Nao e dado de atendimento: e telefone de quem nao esta autorizado a falar com
   o canal. A aplicacao poda linhas com mais de 7 dias antes de cada leitura ou
   escrita (IRegrasDoCanal) — nao deveria virar arquivo permanente.
   -------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.CanalWhatsappDescartes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CanalWhatsappDescartes
    (
        Id           UNIQUEIDENTIFIER NOT NULL,
        TenantId     UNIQUEIDENTIFIER NOT NULL,
        Telefone     VARCHAR(20)      NOT NULL,

        -- 1 ForaDaLista, 2 Bloqueado.
        Motivo       TINYINT          NOT NULL,

        CriadoEm     DATETIME2(3)     NOT NULL,
        AtualizadoEm DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_CanalWhatsappDescartes PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_CanalWhatsappDescartes_Tenants
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT CK_CanalWhatsappDescartes_Motivo CHECK (Motivo IN (1, 2))
    );

    CREATE INDEX IX_CanalWhatsappDescartes_Tenant_CriadoEm
        ON dbo.CanalWhatsappDescartes (TenantId, CriadoEm);
END
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'023-canal-whatsapp-regras-e-conexoes.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (
            N'023-canal-whatsapp-regras-e-conexoes.sql',
            N'Tenants.WhatsappModoDeRecebimento/PerfilNome/PerfilFotoUrl/ConectadoEm + tabelas CanalWhatsappNumeros, CanalWhatsappConexoes, CanalWhatsappDescartes.'
        );
END
GO

COMMIT TRANSACTION;
GO

SELECT
    COL_LENGTH('dbo.Tenants', 'WhatsappModoDeRecebimento') AS ColunaModo,
    (SELECT COUNT(*) FROM dbo.CanalWhatsappNumeros)        AS TotalNumeros,
    (SELECT COUNT(*) FROM dbo.CanalWhatsappConexoes)       AS TotalConexoes,
    (SELECT COUNT(*) FROM dbo.CanalWhatsappDescartes)      AS TotalDescartes;
GO

PRINT 'Migracao 023 concluida.';
GO
