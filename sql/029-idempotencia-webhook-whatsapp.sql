/* =============================================================================
   CRM-Cartorio — Idempotencia atomica do webhook de WhatsApp (IA-03)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-26
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql

   Cria uma chave unica somente para mensagens RECEBIDAS do cliente. A coluna
   IdExternoWhatsapp tambem identifica mensagens de saida, por isso AutorTipo = 1
   faz parte do filtro e os dois namespaces nao sao misturados.

   Nenhum historico e alterado. Se houver duplicatas no escopo protegido, o
   script mostra as chaves conflitantes e falha antes de criar o indice.
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

IF OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

IF EXISTS
(
    SELECT 1 FROM dbo.Mensagens
     WHERE IdExternoWhatsapp IS NOT NULL AND AutorTipo = 1
     GROUP BY TenantId, IdExternoWhatsapp HAVING COUNT_BIG(*) > 1
)
BEGIN
    SELECT TenantId, IdExternoWhatsapp, COUNT_BIG(*) AS Quantidade
      FROM dbo.Mensagens
     WHERE IdExternoWhatsapp IS NOT NULL AND AutorTipo = 1
     GROUP BY TenantId, IdExternoWhatsapp HAVING COUNT_BIG(*) > 1
     ORDER BY TenantId, IdExternoWhatsapp;

    RAISERROR('Migracao 029 recusada: existem mensagens recebidas duplicadas por (TenantId, IdExternoWhatsapp). Nenhum historico foi alterado; corrija os conflitos exibidos e execute novamente.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

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

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'029-idempotencia-webhook-whatsapp.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'029-idempotencia-webhook-whatsapp.sql',
                N'IA-03: UNIQUE filtrado por tenant e id externo somente para mensagens recebidas do cliente; impede reentrega concorrente de iniciar dois ciclos de automacao/IA.');
END
GO

COMMIT TRANSACTION;
GO

SELECT name, is_unique, filter_definition
  FROM sys.indexes
 WHERE object_id = OBJECT_ID(N'dbo.Mensagens')
   AND name = N'UQ_Mensagens_Tenant_IdExterno_Cliente';
GO

PRINT 'Migracao 029 concluida.';
GO
