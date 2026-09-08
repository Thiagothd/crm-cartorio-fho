/* =============================================================================
   CRM-Cartorio — Claim atomico de mensagens de saida (WA-OUT-01)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-26
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql

   Acrescenta os marcadores do claim persistente. Nenhuma linha existente e
   atualizada: os dois campos novos nascem nulos e os valores 1..5 permanecem.
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

IF OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'dbo.Mensagens', N'EnvioIniciadoEm') IS NULL
    ALTER TABLE dbo.Mensagens ADD EnvioIniciadoEm DATETIME2(3) NULL;
GO

IF COL_LENGTH(N'dbo.Mensagens', N'EnvioDisparadoEm') IS NULL
    ALTER TABLE dbo.Mensagens ADD EnvioDisparadoEm DATETIME2(3) NULL;
GO

IF OBJECT_ID(N'dbo.CK_Mensagens_StatusEntrega', N'C') IS NOT NULL
    ALTER TABLE dbo.Mensagens DROP CONSTRAINT CK_Mensagens_StatusEntrega;
GO

ALTER TABLE dbo.Mensagens WITH CHECK ADD CONSTRAINT CK_Mensagens_StatusEntrega
    CHECK (StatusEntrega IN (1, 2, 3, 4, 5, 6));
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'030-claim-atomico-saida-whatsapp.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'030-claim-atomico-saida-whatsapp.sql',
                N'WA-OUT-01: estado Enviando e marcadores de inicio/disparo impedem dois envios concorrentes normais do mesmo MensagemId.');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.Mensagens', N'EnvioIniciadoEm') AS EnvioIniciadoEm,
       COL_LENGTH(N'dbo.Mensagens', N'EnvioDisparadoEm') AS EnvioDisparadoEm;
GO

PRINT 'Migracao 030 concluida. Nenhum historico foi alterado.';
GO
