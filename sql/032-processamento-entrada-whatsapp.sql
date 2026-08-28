/* =============================================================================
   CRM-Cartorio — Aceitacao e processamento duravel da entrada WhatsApp
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-28
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 029-idempotencia-webhook-whatsapp.sql,
                  030-claim-atomico-saida-whatsapp.sql,
                  031-token-version-atendente.sql

   Acrescenta estado recuperavel nas mensagens recebidas e impede duas conversas
   ativas do mesmo cliente no mesmo canal. Nenhuma mensagem e apagada.
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

IF OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL OR OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

-- Diagnostico ANTES de qualquer ALTER. O indice de conversa ativa nao pode
-- escolher silenciosamente qual protocolo conservar.
IF EXISTS
(
    SELECT 1
      FROM dbo.Conversas
     WHERE Status = 1
     GROUP BY TenantId, ClienteId, Canal
    HAVING COUNT_BIG(*) > 1
)
BEGIN
    SELECT TenantId, ClienteId, Canal, COUNT_BIG(*) AS Quantidade
      FROM dbo.Conversas
     WHERE Status = 1
     GROUP BY TenantId, ClienteId, Canal
    HAVING COUNT_BIG(*) > 1
     ORDER BY TenantId, ClienteId, Canal;

    RAISERROR('Migracao 032 recusada: existem conversas ativas duplicadas por tenant, cliente e canal. Nenhum dado foi alterado; corrija os conflitos exibidos e execute novamente.', 16, 1);
    RETURN;
END
GO

-- IA-03 so pode ser criado se o historico ja respeitar a mesma chave.
-- Nao escolhemos nem apagamos linhas: a incompatibilidade para a migracao.
IF EXISTS
(
    SELECT 1
      FROM dbo.Mensagens
     WHERE AutorTipo = 1
       AND IdExternoWhatsapp IS NOT NULL
     GROUP BY TenantId, IdExternoWhatsapp
    HAVING COUNT_BIG(*) > 1
)
BEGIN
    SELECT TenantId, IdExternoWhatsapp, COUNT_BIG(*) AS Quantidade
      FROM dbo.Mensagens
     WHERE AutorTipo = 1
       AND IdExternoWhatsapp IS NOT NULL
     GROUP BY TenantId, IdExternoWhatsapp
    HAVING COUNT_BIG(*) > 1
     ORDER BY TenantId, IdExternoWhatsapp;

    RAISERROR('Migracao 032 recusada: existem entradas WhatsApp duplicadas por tenant e id externo. Nenhum dado foi alterado; corrija os conflitos exibidos e execute novamente.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'dbo.Mensagens', N'EstadoProcessamentoEntrada') IS NULL
    ALTER TABLE dbo.Mensagens ADD EstadoProcessamentoEntrada TINYINT NULL;
GO

IF COL_LENGTH(N'dbo.Mensagens', N'ProcessamentoEntradaIniciadoEm') IS NULL
    ALTER TABLE dbo.Mensagens ADD ProcessamentoEntradaIniciadoEm DATETIME2(3) NULL;
GO

IF COL_LENGTH(N'dbo.Mensagens', N'ProcessamentoEntradaConcluidoEm') IS NULL
    ALTER TABLE dbo.Mensagens ADD ProcessamentoEntradaConcluidoEm DATETIME2(3) NULL;
GO

IF COL_LENGTH(N'dbo.Mensagens', N'ProcessamentoEntradaDisponivelEm') IS NULL
    ALTER TABLE dbo.Mensagens ADD ProcessamentoEntradaDisponivelEm DATETIME2(3) NULL;
GO

IF COL_LENGTH(N'dbo.Mensagens', N'TentativasProcessamentoEntrada') IS NULL
    ALTER TABLE dbo.Mensagens ADD TentativasProcessamentoEntrada INT NOT NULL
        CONSTRAINT DF_Mensagens_TentativasProcessamentoEntrada DEFAULT (0);
GO

IF COL_LENGTH(N'dbo.Mensagens', N'ErroProcessamentoEntrada') IS NULL
    ALTER TABLE dbo.Mensagens ADD ErroProcessamentoEntrada NVARCHAR(1000) NULL;
GO

-- Historico anterior ao worker ja passou pelo fluxo sincrono antigo. Marca-lo
-- Pendente repetiria IA/automacoes; Processada preserva exatamente o que ocorreu.
UPDATE dbo.Mensagens
   SET EstadoProcessamentoEntrada = 3,
       ProcessamentoEntradaConcluidoEm = COALESCE(ProcessamentoEntradaConcluidoEm, AtualizadoEm)
 WHERE AutorTipo = 1
   AND IdExternoWhatsapp IS NOT NULL
   AND EstadoProcessamentoEntrada IS NULL;
GO

IF OBJECT_ID(N'dbo.CK_Mensagens_ProcessamentoEntrada', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.Mensagens WITH CHECK ADD CONSTRAINT CK_Mensagens_ProcessamentoEntrada CHECK
    (
        EstadoProcessamentoEntrada IS NULL OR
        (AutorTipo = 1 AND IdExternoWhatsapp IS NOT NULL AND EstadoProcessamentoEntrada IN (1, 2, 3, 4))
    );
END
GO

-- IA-03 e a barreira definitiva de idempotencia da entrada. Alguns bancos
-- antigos podem ter sido criados antes desse indice constar do esquema-base;
-- o preflight acima interrompe a migracao caso existam duplicidades legadas.
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
     WHERE object_id = OBJECT_ID(N'dbo.Mensagens')
       AND name = N'UQ_Mensagens_Tenant_IdExterno_Cliente'
)
BEGIN
    CREATE UNIQUE INDEX UQ_Mensagens_Tenant_IdExterno_Cliente
        ON dbo.Mensagens (TenantId, IdExternoWhatsapp)
        WHERE IdExternoWhatsapp IS NOT NULL AND AutorTipo = 1;
END
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
     WHERE object_id = OBJECT_ID(N'dbo.Mensagens')
       AND name = N'IX_Mensagens_ProcessamentoEntrada'
)
BEGIN
    CREATE INDEX IX_Mensagens_ProcessamentoEntrada
        ON dbo.Mensagens
           (EstadoProcessamentoEntrada, ProcessamentoEntradaDisponivelEm,
            ProcessamentoEntradaIniciadoEm, ConversaId, EnviadaEm)
        WHERE EstadoProcessamentoEntrada IN (1, 2, 4);
END
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
     WHERE object_id = OBJECT_ID(N'dbo.Conversas')
       AND name = N'UQ_Conversas_Tenant_Cliente_Canal_Ativa'
)
BEGIN
    CREATE UNIQUE INDEX UQ_Conversas_Tenant_Cliente_Canal_Ativa
        ON dbo.Conversas (TenantId, ClienteId, Canal)
        WHERE Status = 1;
END
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'032-processamento-entrada-whatsapp.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'032-processamento-entrada-whatsapp.sql',
                N'BUG-WA-IN-02: entrada aceita em commit curto; fila duravel com claim/recuperacao; uma conversa ativa por tenant, cliente e canal.');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.Mensagens', N'EstadoProcessamentoEntrada') AS EstadoProcessamentoEntrada,
       COL_LENGTH(N'dbo.Mensagens', N'ProcessamentoEntradaIniciadoEm') AS ProcessamentoEntradaIniciadoEm,
       COL_LENGTH(N'dbo.Mensagens', N'ProcessamentoEntradaConcluidoEm') AS ProcessamentoEntradaConcluidoEm;
GO

PRINT 'Migracao 032 concluida. Nenhuma mensagem foi apagada.';
GO
