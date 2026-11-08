/* =============================================================================
   CRM-Cartorio — TokenVersion do Atendente (SEC-01)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-26
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql

   O QUE FAZ
       Acrescenta dbo.Atendentes.TokenVersion (INT, DEFAULT 1). Nenhuma linha
       existente muda de valor alem do DEFAULT que o ALTER TABLE aplica —
       todo atendente ja cadastrado nasce com TokenVersion = 1.

   POR QUE ISSO EXISTE
       Ate aqui um JWT emitido no login continuava valido ate expirar (ate 8h),
       mesmo depois de troca de senha, desativacao ou mudanca de Perfil — o
       backend nunca revalidava nada alem da assinatura e da expiracao do
       proprio token. TokenVersion e o mecanismo de revogacao: sobe a cada
       evento critico, e todo request autenticado confere se a versao do JWT
       ainda bate com a do banco. Ver Infrastructure/Security/ValidadorDeSessao.

   IDEMPOTENCIA
       Coluna criada so se faltar (COL_LENGTH). Rodar duas vezes nao duplica
       nada nem altera valor ja gravado.
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

IF OBJECT_ID(N'dbo.Atendentes', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'dbo.Atendentes', N'TokenVersion') IS NULL
    ALTER TABLE dbo.Atendentes
        ADD TokenVersion INT NOT NULL CONSTRAINT DF_Atendentes_TokenVersion DEFAULT (1);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'031-token-version-atendente.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'031-token-version-atendente.sql',
                N'SEC-01: TokenVersion em Atendentes. Troca de senha, redefinicao pelo admin, desativacao e mudanca de Perfil incrementam; um JWT emitido com a versao anterior deixa de ser aceito no proximo request.');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.Atendentes', N'TokenVersion') AS TokenVersion;
GO

PRINT 'Migracao 031 concluida. Nenhum historico foi alterado.';
GO
