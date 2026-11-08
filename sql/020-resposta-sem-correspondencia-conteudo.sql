/* =============================================================================
   CRM-Cartorio — IA so escala por pedido explicito: conteudo (gatilho 11)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-18
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 019-resposta-sem-correspondencia-schema.sql

   O QUE FAZ
       Insere a mensagem automatica nova da Fase 2G:
         11 RespostaSemCorrespondencia — a IA nao encontrou resposta especifica,
                                          cliente nao pediu atendente.

   ESPELHO OBRIGATORIO
       O mesmo conteudo esta em
       Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs — os dois
       precisam bater caractere a caractere, mesma disciplina de 002/015/016/018.

   IDEMPOTENCIA
       Guardado por um IF NOT EXISTS no Id novo.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%' AND DB_NAME() <> N'CRM_Cartorio_Demo'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

IF EXISTS (SELECT 1 FROM dbo.MensagensAutomaticas WHERE Id = '0199B2C0-0006-7000-8000-000000000012')
BEGIN
    PRINT 'Migracao 020 ja aplicada — nada a fazer.';
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

DECLARE @agora    DATETIME2(3)     = SYSUTCDATETIME();
DECLARE @tenantId UNIQUEIDENTIFIER = '0199B2C0-0000-7000-8000-000000000001';

INSERT INTO dbo.MensagensAutomaticas
    (Id, TenantId, Gatilho, EtapaAlvoId, Conteudo, Ativo, CriadoEm, AtualizadoEm)
VALUES
    -- Gatilho 11 = RespostaSemCorrespondencia
    ('0199B2C0-0006-7000-8000-000000000012', @tenantId, 11, NULL,
     N'Não tenho essa informação específica por aqui, {NomeCliente}, mas fico à disposição! Se quiser, é só pedir para falar com um de nossos atendentes. 😊',
     1, @agora, @agora);
GO

COMMIT TRANSACTION;
GO

SELECT Gatilho, LEFT(Conteudo, 60) AS InicioDoTexto FROM dbo.MensagensAutomaticas WHERE Gatilho = 11;
GO

PRINT 'Migracao 020 concluida.';
GO
