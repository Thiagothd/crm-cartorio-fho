/* =============================================================================
   CRM-Cartorio — Invariante de conversa ativa por cliente
   -----------------------------------------------------------------------------
   Corrige a chave criada pela 032: o sistema sempre tratou atendimento ativo
   como unico por tenant e cliente, sem separar por canal. Nenhuma conversa ou
   mensagem e apagada. Em caso de conflito legado, o script para antes do DDL.
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

IF OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

-- Nao e seguro decidir silenciosamente qual protocolo manter. A intervencao
-- operacional deve encerrar ou consolidar cada par antes da nova unicidade.
IF EXISTS
(
    SELECT 1
      FROM dbo.Conversas
     WHERE Status = 1
     GROUP BY TenantId, ClienteId
    HAVING COUNT_BIG(*) > 1
)
BEGIN
    SELECT TenantId, ClienteId, COUNT_BIG(*) AS Quantidade,
           STRING_AGG(CONVERT(NVARCHAR(MAX), Protocolo), N', ') AS Protocolos
      FROM dbo.Conversas
     WHERE Status = 1
     GROUP BY TenantId, ClienteId
    HAVING COUNT_BIG(*) > 1
     ORDER BY TenantId, ClienteId;

    RAISERROR('Migracao 033 recusada: existem conversas ativas duplicadas por tenant e cliente. Nenhum dado foi alterado; corrija os conflitos exibidos e execute novamente.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF EXISTS
(
    SELECT 1 FROM sys.indexes
     WHERE object_id = OBJECT_ID(N'dbo.Conversas')
       AND name = N'UQ_Conversas_Tenant_Cliente_Canal_Ativa'
)
    DROP INDEX UQ_Conversas_Tenant_Cliente_Canal_Ativa ON dbo.Conversas;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
     WHERE object_id = OBJECT_ID(N'dbo.Conversas')
       AND name = N'UQ_Conversas_Tenant_Cliente_Ativa'
)
BEGIN
    CREATE UNIQUE INDEX UQ_Conversas_Tenant_Cliente_Ativa
        ON dbo.Conversas (TenantId, ClienteId)
        WHERE Status = 1;
END
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'033-invariante-conversa-ativa-por-cliente.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'033-invariante-conversa-ativa-por-cliente.sql',
                N'BUG-WA-IN-02: restaura a unicidade de conversa ativa por tenant e cliente, independentemente do canal.');
END
GO

COMMIT TRANSACTION;
GO

PRINT 'Migracao 033 concluida. Nenhuma conversa ou mensagem foi apagada.';
GO
