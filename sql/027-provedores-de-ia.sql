/* =============================================================================
   CRM-Cartorio — Provedores de IA (Gemini + Anthropic)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-25
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 012-resposta-inteligente.sql, 021-versao-do-schema.sql

   O QUE FAZ
       Cria dbo.ChamadasDeIa: uma linha por tentativa de chamada a um provedor de
       IA (Gemini ou Anthropic), com tokens, latencia, sucesso/falha, motivo do
       fallback e custo estimado. Duas linhas quando ha fallback (a que falhou e
       a que resolveu).

   O PROBLEMA QUE ISTO RESOLVE
       Ate aqui a Resposta Inteligente falava so com o Haiku e nao registrava
       chamada nenhuma — nao havia como saber tokens, latencia, motivo de falha
       ou custo. A Fase 2 passa a ter dois provedores (Gemini como principal,
       Haiku como contingencia) e o controle de quota do Gemini, o disjuntor de
       fallback e o painel novo em /automacao/ia dependem deste historico.

   POR QUE UMA TABELA NOVA, E NAO REAPROVEITAR Mensagens.GeradaPorIa
       Mensagens.GeradaPorIa so sabe dizer "esta resposta veio de IA" — nao diz
       qual provedor, se houve fallback, quantos tokens, nem registra as
       tentativas que FALHARAM (que nunca viram Mensagem nenhuma). O controle de
       quota do Gemini e o disjuntor de fallback precisam exatamente dessas
       tentativas fracassadas para funcionar — sem elas o disjuntor nunca abriria.

   POR QUE ChaveDeProjeto E NAO A CHAVE EM SI
       A franquia do Gemini e por projeto Google, nao por tenant. ChaveDeProjeto
       guarda so um fingerprint nao secreto (8 caracteres do SHA-256) da chave —
       nunca a chave — usado para particionar o contador global de RPM/TPM/RPD
       pelo projeto real e para nao misturar contagem de uma chave antiga com a
       nova depois de uma rotacao.

   SEM FOREIGN KEY PARA Conversas
       Uma sonda do disjuntor (a mensagem que testa se o Gemini voltou a
       funcionar, no meio-aberto) pode nao ter conversa nenhuma associada. A
       tabela de log tambem nao pode travar nenhuma rotina de manutencao futura
       sobre Conversas.

   IDEMPOTENCIA
       Tabela e indices so entram quando ainda nao existem.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT IN (N'CRM_Cartorio', N'CRM_Cartorio_Teste', N'CRM_Cartorio_Dev', N'CRM_Cartorio_Demo')
BEGIN
    RAISERROR('Recusado: este script so roda em CRM_Cartorio, CRM_Cartorio_Teste ou CRM_Cartorio_Dev. Confira o banco selecionado.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1. dbo.ChamadasDeIa ----------------------------------------------------- */

IF OBJECT_ID(N'dbo.ChamadasDeIa', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChamadasDeIa
    (
        Id               UNIQUEIDENTIFIER NOT NULL,
        TenantId         UNIQUEIDENTIFIER NOT NULL,
        ConversaId       UNIQUEIDENTIFIER NULL,
        -- Fingerprint da chave do Gemini (8 caracteres do SHA-256), nunca a
        -- chave. Nula para chamadas ao Anthropic.
        ChaveDeProjeto   VARCHAR(16)      NULL,
        -- Provedor: 1 Gemini, 2 Anthropic.
        Provedor         TINYINT          NOT NULL,
        Modelo           VARCHAR(60)      NOT NULL,
        IniciadaEm       DATETIME2(3)     NOT NULL,
        LatenciaMs       INT              NOT NULL,
        Sucesso          BIT              NOT NULL,
        -- So preenchido quando Sucesso = 1: o podeResponder devolvido pelo modelo.
        PodeResponder    BIT              NULL,
        -- TipoDeFalha: 0 Nenhuma, 1 RateLimit, 2 QuotaEsgotada, 3 Indisponibilidade,
        -- 4 Timeout, 5 Rede, 6 Configuracao, 7 RespostaInvalida.
        TipoDeFalha      TINYINT          NOT NULL CONSTRAINT DF_ChamadasDeIa_TipoDeFalha DEFAULT (0),
        CodigoHttp       INT              NULL,
        MotivoDoFallback NVARCHAR(200)    NULL,
        FoiFallback      BIT              NOT NULL CONSTRAINT DF_ChamadasDeIa_FoiFallback DEFAULT (0),
        TokensEntrada    INT              NOT NULL CONSTRAINT DF_ChamadasDeIa_TokensEntrada DEFAULT (0),
        TokensSaida      INT              NOT NULL CONSTRAINT DF_ChamadasDeIa_TokensSaida DEFAULT (0),
        TokensPensamento INT              NOT NULL CONSTRAINT DF_ChamadasDeIa_TokensPensamento DEFAULT (0),
        CustoEstimadoUsd DECIMAL(10,6)    NULL,
        NivelDeRaciocinio VARCHAR(10)     NULL,
        CriadoEm         DATETIME2(3)     NOT NULL,
        AtualizadoEm     DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_ChamadasDeIa PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ChamadasDeIa_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT CK_ChamadasDeIa_Provedor CHECK (Provedor IN (1, 2)),
        CONSTRAINT CK_ChamadasDeIa_TipoDeFalha CHECK (TipoDeFalha BETWEEN 0 AND 7),
        CONSTRAINT CK_ChamadasDeIa_PodeResponder CHECK
        (
            ([Sucesso] = 1) OR ([Sucesso] = 0 AND [PodeResponder] IS NULL)
        )
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChamadasDeIa_Tenant_Iniciada' AND object_id = OBJECT_ID(N'dbo.ChamadasDeIa'))
    CREATE INDEX IX_ChamadasDeIa_Tenant_Iniciada ON dbo.ChamadasDeIa (TenantId, IniciadaEm);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChamadasDeIa_Projeto_Iniciada' AND object_id = OBJECT_ID(N'dbo.ChamadasDeIa'))
    CREATE INDEX IX_ChamadasDeIa_Projeto_Iniciada ON dbo.ChamadasDeIa (ChaveDeProjeto, IniciadaEm)
        WHERE ChaveDeProjeto IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChamadasDeIa_Tenant_Provedor_Sucesso' AND object_id = OBJECT_ID(N'dbo.ChamadasDeIa'))
    CREATE INDEX IX_ChamadasDeIa_Tenant_Provedor_Sucesso ON dbo.ChamadasDeIa (TenantId, Provedor, Sucesso, IniciadaEm);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'027-provedores-de-ia.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'027-provedores-de-ia.sql',
                N'dbo.ChamadasDeIa: historico de tentativas de chamada aos provedores de IA (Gemini principal, Anthropic contingencia) — tokens, latencia, falha, motivo de fallback e custo estimado.');
END
GO

COMMIT TRANSACTION;
GO

SELECT OBJECT_ID(N'dbo.ChamadasDeIa', N'U') AS TabelaExiste;
GO

PRINT 'Migracao 027 concluida.';
GO
