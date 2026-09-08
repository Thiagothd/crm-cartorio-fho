/* =============================================================================
   CRM-Cartorio — MFA / TOTP para contas privilegiadas (SEC-03)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-31
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql, 033-protecao-contra-forca-bruta-no-login.sql

   O QUE FAZ
       dbo.Atendentes += 9 colunas de MFA (ver secao "Atendentes" abaixo)
       dbo.Tenants    += MfaObrigatorioParaAdministradores BIT NOT NULL DEFAULT (0)
       + CK_Atendentes_MfaFalhas       (MfaFalhasConsecutivas >= 0)
       + CK_Atendentes_MfaNivel        (MfaNivelDeBloqueio BETWEEN 0 AND 6)
       + CK_Atendentes_MfaAtivoTemSegredo (MfaAtivo = 0 OR MfaSegredoCifrado IS NOT NULL)
       CREATE TABLE dbo.DesafiosDeMfa
       CREATE TABLE dbo.CodigosDeRecuperacaoMfa
       CREATE TABLE dbo.EventosDeSeguranca

       Nenhuma linha existente e alterada de valor: as colunas novas nascem no
       estado "MFA nunca configurado", que e exatamente o que toda conta e hoje.

   O PROBLEMA QUE ISTO RESOLVE (SEC-03)
       Ate aqui a conta de um Administrador ou Titular era protegida so por
       senha — SEC-02 (backoff por conta) e o limite por IP amortecem forca
       bruta, mas nenhum dos dois ajuda se a senha em si vazar (phishing,
       reuso, vazamento de terceiro). SEC-03 acrescenta um segundo fator (TOTP,
       RFC 6238) exigivel por conta privilegiada, com desafio persistente e
       hasheado (nunca JWT de escopo reduzido — uso unico de verdade exige uma
       linha que possa ser consumida atomicamente), bloqueio proprio por conta
       (nao so por desafio: quem ja tem a senha pode abrir quantos desafios
       quiser), e uma trilha de auditoria dedicada.

   ATENDENTES — OS NOVE CAMPOS
       MfaAtivo                    BIT           So vira 1 na CONFIRMACAO do 1o codigo — nunca ao gerar o segredo pendente.
       MfaSegredoCifrado           VARBINARY(256) AES-256-GCM (nonce+tag+ciphertext, formato versionado). Nulo se MFA nunca foi ativado.
       MfaSegredoPendenteCifrado   VARBINARY(256) Aguardando confirmacao. Nunca autentica sozinho. TTL de 10 min (MfaPendenteExpiraEm).
       MfaPendenteExpiraEm         DATETIME2(3)
       MfaConfirmadoEm             DATETIME2(3)
       MfaUltimoPassoUsado         BIGINT        Anti-replay: compare-and-set do ultimo passo TOTP (30s) aceito.
       MfaFalhasConsecutivas       INT           Falhas de SEGUNDO FATOR — contador PROPRIO, nunca somado a FalhasDeLoginConsecutivas (senha, SEC-02).
       MfaNivelDeBloqueio          TINYINT       Mesma progressao de NivelDeBloqueio (SEC-02): 1, 2, 4, 8, 16, 30 min, teto no 6o nivel.
       MfaBloqueadoAte             DATETIME2(3)  Enquanto no futuro, nenhum JWT sai da etapa de MFA — nem com codigo correto.

   TRES REGRAS QUE O CODIGO IMPOE E QUE NAO SAO OBVIAS
       1. Bloqueio de MFA e POR CONTA, nao por desafio. 5 tentativas por
          desafio nao bastam sozinhas: quem ja tem a senha cria quantos
          desafios quiser. E MfaFalhasConsecutivas/MfaBloqueadoAte quem de
          fato impede forca bruta persistente do segundo fator.
       2. Tentativa feita DURANTE bloqueio (de senha OU de MFA) nao conta e
          nao estende o bloqueio — em nenhum dos dois contadores.
       3. Desafio de MFA carrega TokenVersion no instante da emissao
          (DesafiosDeMfa.TokenVersionNoDesafio) e a conclusao reconfirma que
          ainda bate — troca de senha, desativacao, mudanca de Perfil ou reset
          de MFA entre a etapa 1 e a etapa 2 do login invalida qualquer
          desafio pendente, porque todos esses eventos ja sobem TokenVersion.

   POR QUE NAO PRECISA DE BACKFILL
       Antes desta migracao MFA nao existia, entao nenhuma conta esta "a meio
       caminho" de nada — os defaults ja descrevem a verdade de toda linha
       existente (destravada, sem segredo, sem exigencia).

   COMPATIBILIDADE DE DEPLOY
       Esta migration e compativel com o binario ANTIGO rodando: ele ignora as
       colunas e tabelas novas. O binario SEC-03, por sua vez, MAPEIA e
       CONSULTA essas colunas em toda autenticacao — nao deve ser iniciado
       antes desta migration estar aplicada. Ver docs/DEPLOY.md, secao 1.2.

   IDEMPOTENCIA
       Cada coluna/CHECK/tabela/indice so entra quando ainda nao existe. Rodar
       o script de novo nao encontra nada para fazer.
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

IF OBJECT_ID(N'dbo.Atendentes', N'U') IS NULL OR OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1. Atendentes ------------------------------------------------------- */

IF COL_LENGTH(N'dbo.Atendentes', N'MfaAtivo') IS NULL
    ALTER TABLE dbo.Atendentes ADD MfaAtivo BIT NOT NULL CONSTRAINT DF_Atendentes_MfaAtivo DEFAULT (0);
GO

IF COL_LENGTH(N'dbo.Atendentes', N'MfaSegredoCifrado') IS NULL
    ALTER TABLE dbo.Atendentes ADD MfaSegredoCifrado VARBINARY(256) NULL;
GO

IF COL_LENGTH(N'dbo.Atendentes', N'MfaSegredoPendenteCifrado') IS NULL
    ALTER TABLE dbo.Atendentes ADD MfaSegredoPendenteCifrado VARBINARY(256) NULL;
GO

IF COL_LENGTH(N'dbo.Atendentes', N'MfaPendenteExpiraEm') IS NULL
    ALTER TABLE dbo.Atendentes ADD MfaPendenteExpiraEm DATETIME2(3) NULL;
GO

IF COL_LENGTH(N'dbo.Atendentes', N'MfaConfirmadoEm') IS NULL
    ALTER TABLE dbo.Atendentes ADD MfaConfirmadoEm DATETIME2(3) NULL;
GO

IF COL_LENGTH(N'dbo.Atendentes', N'MfaUltimoPassoUsado') IS NULL
    ALTER TABLE dbo.Atendentes ADD MfaUltimoPassoUsado BIGINT NULL;
GO

IF COL_LENGTH(N'dbo.Atendentes', N'MfaFalhasConsecutivas') IS NULL
    ALTER TABLE dbo.Atendentes ADD MfaFalhasConsecutivas INT NOT NULL
        CONSTRAINT DF_Atendentes_MfaFalhasConsecutivas DEFAULT (0);
GO

IF COL_LENGTH(N'dbo.Atendentes', N'MfaNivelDeBloqueio') IS NULL
    ALTER TABLE dbo.Atendentes ADD MfaNivelDeBloqueio TINYINT NOT NULL
        CONSTRAINT DF_Atendentes_MfaNivelDeBloqueio DEFAULT (0);
GO

IF COL_LENGTH(N'dbo.Atendentes', N'MfaBloqueadoAte') IS NULL
    ALTER TABLE dbo.Atendentes ADD MfaBloqueadoAte DATETIME2(3) NULL;
GO

-- Precisa de GO separado dos ALTER TABLE acima: colunas novas so ficam
-- visiveis para o proximo LOTE, mesmo dentro da mesma transacao.

IF OBJECT_ID(N'dbo.CK_Atendentes_MfaFalhas', N'C') IS NULL
    ALTER TABLE dbo.Atendentes WITH CHECK ADD CONSTRAINT CK_Atendentes_MfaFalhas
        CHECK (MfaFalhasConsecutivas >= 0);
GO

IF OBJECT_ID(N'dbo.CK_Atendentes_MfaNivel', N'C') IS NULL
    ALTER TABLE dbo.Atendentes WITH CHECK ADD CONSTRAINT CK_Atendentes_MfaNivel
        CHECK (MfaNivelDeBloqueio BETWEEN 0 AND 6);
GO

-- Invariante central: nunca existe conta com MfaAtivo=1 sem segredo — ativar
-- so acontece atomicamente com a gravacao do segredo confirmado.
IF OBJECT_ID(N'dbo.CK_Atendentes_MfaAtivoTemSegredo', N'C') IS NULL
    ALTER TABLE dbo.Atendentes WITH CHECK ADD CONSTRAINT CK_Atendentes_MfaAtivoTemSegredo
        CHECK (MfaAtivo = 0 OR MfaSegredoCifrado IS NOT NULL);
GO

/* --- 2. Tenants ------------------------------------------------------------ */

IF COL_LENGTH(N'dbo.Tenants', N'MfaObrigatorioParaAdministradores') IS NULL
    ALTER TABLE dbo.Tenants ADD MfaObrigatorioParaAdministradores BIT NOT NULL
        CONSTRAINT DF_Tenants_MfaObrigatorioParaAdministradores DEFAULT (0);
GO

/* --- 3. DesafiosDeMfa ------------------------------------------------------- */

IF OBJECT_ID(N'dbo.DesafiosDeMfa', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DesafiosDeMfa
    (
        Id                    UNIQUEIDENTIFIER NOT NULL,
        TenantId              UNIQUEIDENTIFIER NOT NULL,
        AtendenteId           UNIQUEIDENTIFIER NOT NULL,

        -- SHA-256 (hex) do token em claro. O token em si NUNCA e gravado.
        HashDoDesafio         CHAR(64)         NOT NULL,

        -- 1 SegundoFator, 2 MatriculaPrivilegiada, 3 Ticket
        Tipo                  TINYINT          NOT NULL,

        -- Snapshot de Atendentes.TokenVersion no instante da emissao. Comparado
        -- de novo na conclusao — se TokenVersion mudou (senha trocada, conta
        -- desativada, Perfil mudou, MFA resetado), o desafio morre sozinho.
        TokenVersionNoDesafio INT              NOT NULL,

        ExpiraEm              DATETIME2(3)     NOT NULL,

        -- Incrementado ANTES de validar o codigo (reserva de tentativa) —
        -- e o proprio esgotamento em 5, sem passo separado de "inutilizar".
        TentativasDeCodigo    INT              NOT NULL CONSTRAINT DF_DesafiosDeMfa_Tentativas DEFAULT (0),

        -- Nulo enquanto em aberto. So marcado no caminho de SUCESSO.
        ConsumidoEm           DATETIME2(3)     NULL,
        IpDeOrigem            VARCHAR(45)      NULL,

        CriadoEm              DATETIME2(3)     NOT NULL,
        AtualizadoEm          DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_DesafiosDeMfa PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_DesafiosDeMfa_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_DesafiosDeMfa_Atendentes FOREIGN KEY (AtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT CK_DesafiosDeMfa_Tipo CHECK (Tipo IN (1, 2, 3)),
        CONSTRAINT CK_DesafiosDeMfa_Tentativas CHECK (TentativasDeCodigo >= 0)
    );

    CREATE UNIQUE INDEX UQ_DesafiosDeMfa_Hash ON dbo.DesafiosDeMfa (HashDoDesafio);

    -- Varredura de expirados: so os ainda em aberto interessam.
    CREATE INDEX IX_DesafiosDeMfa_ExpiraEm_Abertos ON dbo.DesafiosDeMfa (ExpiraEm)
        WHERE ConsumidoEm IS NULL;
END
GO

/* --- 4. CodigosDeRecuperacaoMfa ---------------------------------------------- */

IF OBJECT_ID(N'dbo.CodigosDeRecuperacaoMfa', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CodigosDeRecuperacaoMfa
    (
        Id           UNIQUEIDENTIFIER NOT NULL,
        TenantId     UNIQUEIDENTIFIER NOT NULL,
        AtendenteId  UNIQUEIDENTIFIER NOT NULL,

        -- SHA-256 (hex) do codigo normalizado (16 chars Crockford Base32, 80
        -- bits). O codigo em claro so existe na resposta da ativacao/regeneracao.
        HashDoCodigo CHAR(64)         NOT NULL,

        -- Nulo enquanto disponivel. Consumo e atomico e definitivo.
        UsadoEm      DATETIME2(3)     NULL,

        CriadoEm     DATETIME2(3)     NOT NULL,
        AtualizadoEm DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_CodigosDeRecuperacaoMfa PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_CodigosDeRecuperacaoMfa_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_CodigosDeRecuperacaoMfa_Atendentes FOREIGN KEY (AtendenteId) REFERENCES dbo.Atendentes (Id)
    );

    CREATE UNIQUE INDEX UQ_CodigosDeRecuperacaoMfa_Atendente_Hash
        ON dbo.CodigosDeRecuperacaoMfa (AtendenteId, HashDoCodigo);
END
GO

/* --- 5. EventosDeSeguranca ---------------------------------------------------

   Append-only, mesmo padrao de CanalWhatsappConexoes: existe para a tela (e
   uma investigacao) responderem "o que aconteceu e quando" sem depender do
   log do servidor. NUNCA grava segredo, codigo, ticket, senha ou desafio — so
   o FATO. Isto vale tambem para Motivo, texto livre digitado por um humano
   numa acao administrativa, nunca preenchido a partir de dado sensivel. ---- */

IF OBJECT_ID(N'dbo.EventosDeSeguranca', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventosDeSeguranca
    (
        Id               UNIQUEIDENTIFIER NOT NULL,
        TenantId         UNIQUEIDENTIFIER NOT NULL,

        -- De quem este evento fala — a conta afetada.
        AtendenteId      UNIQUEIDENTIFIER NOT NULL,

        -- Quem executou, quando foi OUTRO atendente (reset, ativacao de
        -- politica). Nulo quando o proprio alvo agiu sobre si mesmo.
        AtorAtendenteId  UNIQUEIDENTIFIER NULL,

        -- Identidade de quem agiu FORA do sistema (acesso SSH que rodou a
        -- ferramenta de bootstrap, ou break-glass). Nunca guarda motivo nem
        -- qualquer outro dado — e so identidade.
        AtorExterno      NVARCHAR(120)    NULL,

        -- 1 MfaAtivado, 2 MfaDesativado, 3 CodigoDeRecuperacaoUsado,
        -- 4 CodigosDeRecuperacaoRegenerados, 5 ResetAdministrativo,
        -- 6 ResetDeEmergencia, 7 BloqueioDeMfaPorFalhas,
        -- 8 TicketDeMatriculaEmitido, 9 TicketDeBootstrapEmitido,
        -- 10 PoliticaDeMfaAtivada
        Tipo             TINYINT          NOT NULL,

        OcorridoEm       DATETIME2(3)     NOT NULL,
        IpDeOrigem       VARCHAR(45)      NULL,
        Motivo           NVARCHAR(400)    NULL,

        CriadoEm         DATETIME2(3)     NOT NULL,
        AtualizadoEm     DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_EventosDeSeguranca PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_EventosDeSeguranca_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_EventosDeSeguranca_Atendentes FOREIGN KEY (AtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT CK_EventosDeSeguranca_Tipo CHECK (Tipo BETWEEN 1 AND 10)
    );

    CREATE INDEX IX_EventosDeSeguranca_Tenant_Ocorrido ON dbo.EventosDeSeguranca (TenantId, OcorridoEm);
    CREATE INDEX IX_EventosDeSeguranca_Atendente ON dbo.EventosDeSeguranca (AtendenteId);
END
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'034-mfa-totp.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'034-mfa-totp.sql',
                N'SEC-03: TOTP para contas privilegiadas. 9 colunas de MFA em Atendentes (segredo AES-256-GCM, bloqueio por conta, anti-replay), MfaObrigatorioParaAdministradores em Tenants, tabelas DesafiosDeMfa/CodigosDeRecuperacaoMfa/EventosDeSeguranca. Binario SEC-03 exige esta migration antes de subir (docs/DEPLOY.md 1.2).');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.Atendentes', N'MfaAtivo') AS MfaAtivo,
       COL_LENGTH(N'dbo.Atendentes', N'MfaSegredoCifrado') AS MfaSegredoCifrado,
       COL_LENGTH(N'dbo.Tenants', N'MfaObrigatorioParaAdministradores') AS TenantsMfaObrigatorio,
       OBJECT_ID(N'dbo.DesafiosDeMfa', N'U') AS TabelaDesafiosDeMfa,
       OBJECT_ID(N'dbo.CodigosDeRecuperacaoMfa', N'U') AS TabelaCodigosDeRecuperacaoMfa,
       OBJECT_ID(N'dbo.EventosDeSeguranca', N'U') AS TabelaEventosDeSeguranca;
GO

PRINT 'Migracao 034 concluida. Nenhuma conta foi afetada por esta migracao.';
GO
