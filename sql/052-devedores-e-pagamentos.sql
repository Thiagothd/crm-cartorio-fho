/* =============================================================================
   CRM-Cartorio — Modulo de Devedores e Pagamentos de Pendencias
   -----------------------------------------------------------------------------
   Gerado em .... 2026-10-28
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql, 047-cifra-em-repouso.sql

   O QUE FAZ
       1. Cria dbo.PendenciasFinanceiras (registro de debito/pendencia de cliente,
          com valor opcional, valor pago acumulado, data de referencia, promessa
          de pagamento, vinculo opcional com conversa/servico e controle de
          cobrancas enviadas pelo WhatsApp).
       2. Cria dbo.PagamentosDePendencia (historico append-only de abatimentos
          parciais e quitacoes totais de uma pendencia).
       3. Cria indices filtrados para consulta da tela de Devedores e para o
          selo de alerta na Inbox (ProjecaoDeConversa).

   SEGURO COM O BINARIO ANTERIOR RODANDO
       Expand-only puro: duas tabelas novas (CREATE TABLE) e seus proprios
       indices, sem alterar nenhuma tabela, coluna ou constraint existente.
       O binario N-1 desconhece ambas as tabelas e o EF Core nao valida schema
       no boot. Ver deploy/migration-compatibility.json.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF DB_NAME() NOT IN (N'CRM_Cartorio', N'CRM_Cartorio_Teste', N'CRM_Cartorio_Dev')
BEGIN
    RAISERROR('Recusado: este script so roda em CRM_Cartorio, CRM_Cartorio_Teste ou CRM_Cartorio_Dev. Confira o banco selecionado.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Clientes', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Atendentes', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'dbo.PendenciasFinanceiras', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PendenciasFinanceiras
    (
        Id                           UNIQUEIDENTIFIER NOT NULL,
        TenantId                     UNIQUEIDENTIFIER NOT NULL,
        ClienteId                    UNIQUEIDENTIFIER NOT NULL,
        ConversaOrigemId             UNIQUEIDENTIFIER NULL,
        TipoServicoId                UNIQUEIDENTIFIER NULL,
        Titulo                       NVARCHAR(200)    NOT NULL,

        -- Cifrada em repouso (PR3, ConversorCifrado com limite de 1000 chars em claro).
        Observacoes                  NVARCHAR(MAX)    NULL,

        -- Nulo quando o contato foi marcado como devedor sem valor especificado ainda.
        ValorOriginal                DECIMAL(12, 2)   NULL,
        ValorPago                    DECIMAL(12, 2)   NOT NULL CONSTRAINT DF_PendenciasFinanceiras_ValorPago DEFAULT (0),

        DataReferencia               DATETIME2(3)     NOT NULL,
        DataPromessaPagamento        DATETIME2(3)     NULL,

        -- 1 Aberta, 2 Quitada, 3 Cancelada
        Status                       TINYINT          NOT NULL CONSTRAINT DF_PendenciasFinanceiras_Status DEFAULT (1),

        UltimaCobrancaEm             DATETIME2(3)     NULL,
        UltimaCobrancaPorAtendenteId UNIQUEIDENTIFIER NULL,
        TotalCobrancas               INT              NOT NULL CONSTRAINT DF_PendenciasFinanceiras_TotalCobrancas DEFAULT (0),

        CriadoPorAtendenteId         UNIQUEIDENTIFIER NOT NULL,
        QuitadaEm                    DATETIME2(3)     NULL,
        QuitadaPorAtendenteId        UNIQUEIDENTIFIER NULL,
        CanceladaEm                  DATETIME2(3)     NULL,
        CanceladaPorAtendenteId      UNIQUEIDENTIFIER NULL,
        MotivoCancelamento           NVARCHAR(500)    NULL,

        CriadoEm                     DATETIME2(3)     NOT NULL,
        AtualizadoEm                 DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_PendenciasFinanceiras PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_PendenciasFinanceiras_Tenants
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_PendenciasFinanceiras_Clientes
            FOREIGN KEY (ClienteId) REFERENCES dbo.Clientes (Id),
        CONSTRAINT FK_PendenciasFinanceiras_Conversas
            FOREIGN KEY (ConversaOrigemId) REFERENCES dbo.Conversas (Id),
        CONSTRAINT FK_PendenciasFinanceiras_TiposServico
            FOREIGN KEY (TipoServicoId) REFERENCES dbo.TiposServico (Id),
        CONSTRAINT FK_PendenciasFinanceiras_CriadoPor
            FOREIGN KEY (CriadoPorAtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT FK_PendenciasFinanceiras_UltimaCobrancaPor
            FOREIGN KEY (UltimaCobrancaPorAtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT FK_PendenciasFinanceiras_QuitadaPor
            FOREIGN KEY (QuitadaPorAtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT FK_PendenciasFinanceiras_CanceladaPor
            FOREIGN KEY (CanceladaPorAtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT CK_PendenciasFinanceiras_Status
            CHECK (Status IN (1, 2, 3)),
        CONSTRAINT CK_PendenciasFinanceiras_Valores
            CHECK ((ValorOriginal IS NULL OR ValorOriginal > 0) AND ValorPago >= 0),
        CONSTRAINT CK_PendenciasFinanceiras_TotalCobrancas
            CHECK (TotalCobrancas >= 0)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = 'IX_PendenciasFinanceiras_Tenant_Status_Data'
                  AND object_id = OBJECT_ID(N'dbo.PendenciasFinanceiras'))
    CREATE INDEX IX_PendenciasFinanceiras_Tenant_Status_Data
        ON dbo.PendenciasFinanceiras (TenantId, Status, DataReferencia DESC)
        INCLUDE (ClienteId, ValorOriginal, ValorPago, DataPromessaPagamento, UltimaCobrancaEm);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = 'IX_PendenciasFinanceiras_Tenant_Cliente_Aberta'
                  AND object_id = OBJECT_ID(N'dbo.PendenciasFinanceiras'))
    CREATE INDEX IX_PendenciasFinanceiras_Tenant_Cliente_Aberta
        ON dbo.PendenciasFinanceiras (TenantId, ClienteId)
        INCLUDE (ValorOriginal, ValorPago)
        WHERE Status = 1;
GO

IF OBJECT_ID(N'dbo.PagamentosDePendencia', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PagamentosDePendencia
    (
        Id                       UNIQUEIDENTIFIER NOT NULL,
        TenantId                 UNIQUEIDENTIFIER NOT NULL,
        PendenciaFinanceiraId    UNIQUEIDENTIFIER NOT NULL,
        Valor                    DECIMAL(12, 2)   NOT NULL,
        PagoEm                   DATETIME2(3)     NOT NULL,

        -- 1 Pix, 2 Dinheiro, 3 Cartao, 4 Transferencia, 5 Outro
        MeioDePagamento          TINYINT          NOT NULL CONSTRAINT DF_PagamentosDePendencia_Meio DEFAULT (1),

        -- Cifrada em repouso (PR3, ConversorCifrado com limite de 500 chars em claro).
        Observacao               NVARCHAR(MAX)    NULL,

        RegistradoPorAtendenteId UNIQUEIDENTIFIER NOT NULL,
        CriadoEm                 DATETIME2(3)     NOT NULL,
        AtualizadoEm             DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_PagamentosDePendencia PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_PagamentosDePendencia_Tenants
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_PagamentosDePendencia_Pendencias
            FOREIGN KEY (PendenciaFinanceiraId) REFERENCES dbo.PendenciasFinanceiras (Id),
        CONSTRAINT FK_PagamentosDePendencia_Atendentes
            FOREIGN KEY (RegistradoPorAtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT CK_PagamentosDePendencia_Valor
            CHECK (Valor > 0),
        CONSTRAINT CK_PagamentosDePendencia_Meio
            CHECK (MeioDePagamento BETWEEN 1 AND 5)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = 'IX_PagamentosDePendencia_Pendencia_PagoEm'
                  AND object_id = OBJECT_ID(N'dbo.PagamentosDePendencia'))
    CREATE INDEX IX_PagamentosDePendencia_Pendencia_PagoEm
        ON dbo.PagamentosDePendencia (PendenciaFinanceiraId, PagoEm DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = 'IX_PagamentosDePendencia_Tenant_PagoEm'
                  AND object_id = OBJECT_ID(N'dbo.PagamentosDePendencia'))
    CREATE INDEX IX_PagamentosDePendencia_Tenant_PagoEm
        ON dbo.PagamentosDePendencia (TenantId, PagoEm DESC)
        INCLUDE (Valor, PendenciaFinanceiraId);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'052-devedores-e-pagamentos.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'052-devedores-e-pagamentos.sql',
                N'Modulo de Devedores e Pagamentos: tabelas PendenciasFinanceiras e PagamentosDePendencia com indices filtrados. Expand-only.');
END
GO

COMMIT TRANSACTION;
GO
