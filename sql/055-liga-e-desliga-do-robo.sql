/* =============================================================================
   CRM-Cartorio — Liga e desliga do robô: por setor, por conversa e por contato
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-30
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 054-pergunta-de-duvida.sql

   MULTIEMPRESA
       Os bancos dos cartórios contratados pelo site (CRM_Tenant_*) recebem a mesma
       mudança por sql/saas/tenant/005-liga-e-desliga-do-robo.sql.

   O QUE FAZ
       Setores
         1. ModoDoRobo TINYINT NOT NULL DEFAULT (1)     1 ligado, 2 só equipe, 3 fora do menu
            + CK_Setores_ModoDoRobo
       Conversas
         2. RoboPausadoEm DATETIME2(3) NULL             quando pausaram o robô nesta conversa
         3. RoboPausadoPorAtendenteId UNIQUEIDENTIFIER NULL
       Clientes
         4. AtendimentoSemRobo BIT NOT NULL DEFAULT (0) contato atendido sempre sem robô
         5. AtendimentoSemRoboDesde DATETIME2(3) NULL
       MensagensAutomaticas
         6. CK_MensagensAutomaticas_Gatilho passa a aceitar 17 (ContatoSemRobo)
         7. O texto do gatilho 17, ATIVO, espelhando CatalogoDeDemonstracao.cs

   O PROBLEMA QUE ISTO RESOLVE
       Pedido do cartório parceiro em 2026-09-30: poder cortar o robô na hora — num setor
       inteiro, numa conversa só, ou para um contato que não sabe lidar com robô —
       e religar quando quiser. Até aqui só havia a chave geral (tudo ou nada) e o
       "Assumir", que calava o robô numa conversa sem ter como devolvê-la a ele.

   SEGURO COM O BINARIO ANTERIOR RODANDO
       Tudo aditivo. As colunas NOT NULL nascem com DEFAULT (setor ligado,
       contato com robô), então todo INSERT do binário anterior grava o estado de
       sempre; as colunas NULL ficam nulas (robô ligado). O CHECK de gatilho só
       amplia. O texto novo é uma linha de gatilho que o binário anterior nunca
       pede. Ver deploy/migration-compatibility.json.

   IDEMPOTENCIA
       Cada coluna, constraint e texto só entra se ainda não existir.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF DB_NAME() NOT IN (N'CRM_Cartorio', N'CRM_Cartorio_Teste', N'CRM_Cartorio_Dev', N'CRM_Cartorio_Demo')
BEGIN
    RAISERROR('Recusado: este script so roda em CRM_Cartorio, CRM_Cartorio_Teste ou CRM_Cartorio_Dev. Confira o banco selecionado.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Setores', N'U') IS NULL OR OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Clientes', N'U') IS NULL OR OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- Colunas (cada uma no próprio lote: o nome só existe a partir do próximo GO) */

IF COL_LENGTH(N'dbo.Setores', N'ModoDoRobo') IS NULL
    ALTER TABLE dbo.Setores ADD ModoDoRobo TINYINT NOT NULL CONSTRAINT DF_Setores_ModoDoRobo DEFAULT (1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Setores_ModoDoRobo')
    ALTER TABLE dbo.Setores ADD CONSTRAINT CK_Setores_ModoDoRobo CHECK (ModoDoRobo IN (1, 2, 3));
GO
IF COL_LENGTH(N'dbo.Conversas', N'RoboPausadoEm') IS NULL
    ALTER TABLE dbo.Conversas ADD RoboPausadoEm DATETIME2(3) NULL;
GO
IF COL_LENGTH(N'dbo.Conversas', N'RoboPausadoPorAtendenteId') IS NULL
    ALTER TABLE dbo.Conversas ADD RoboPausadoPorAtendenteId UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH(N'dbo.Clientes', N'AtendimentoSemRobo') IS NULL
    ALTER TABLE dbo.Clientes ADD AtendimentoSemRobo BIT NOT NULL CONSTRAINT DF_Clientes_AtendimentoSemRobo DEFAULT (0);
GO
IF COL_LENGTH(N'dbo.Clientes', N'AtendimentoSemRoboDesde') IS NULL
    ALTER TABLE dbo.Clientes ADD AtendimentoSemRoboDesde DATETIME2(3) NULL;
GO

/* --- Gatilho 17 no CHECK ------------------------------------------------------ */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MensagensAutomaticas_Gatilho')
    ALTER TABLE dbo.MensagensAutomaticas DROP CONSTRAINT CK_MensagensAutomaticas_Gatilho;
GO
ALTER TABLE dbo.MensagensAutomaticas ADD CONSTRAINT CK_MensagensAutomaticas_Gatilho
    CHECK (Gatilho IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17));
GO

/* --- O texto do gatilho 17 ----------------------------------------------------
   ESPELHO OBRIGATORIO: idêntico ao de
   Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs.
   ------------------------------------------------------------------------- */
DECLARE @agora DATETIME2(3) = SYSUTCDATETIME();

INSERT INTO dbo.MensagensAutomaticas
    (Id, TenantId, Gatilho, EtapaAlvoId, Conteudo, Ativo, CriadoEm, AtualizadoEm)
SELECT
    CASE WHEN t.Id = '0199B2C0-0000-7000-8000-000000000001'
              AND NOT EXISTS (SELECT 1 FROM dbo.MensagensAutomaticas x WHERE x.Id = '0199B2C0-0006-7000-8000-000000000018')
         THEN CAST('0199B2C0-0006-7000-8000-000000000018' AS UNIQUEIDENTIFIER)
         ELSE NEWID() END,
    t.Id, 17, NULL,
    N'Oi, {NomeCliente}! Recebemos sua mensagem e já vamos te responder. 😊',
    1, @agora, @agora
FROM dbo.Tenants t
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.MensagensAutomaticas m WHERE m.TenantId = t.Id AND m.Gatilho = 17);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'055-liga-e-desliga-do-robo.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'055-liga-e-desliga-do-robo.sql',
                N'Liga e desliga do robo: Setores.ModoDoRobo, Conversas.RoboPausadoEm/RoboPausadoPorAtendenteId, Clientes.AtendimentoSemRobo/AtendimentoSemRoboDesde, gatilho 17 (ContatoSemRobo) com texto ativo.');
END
GO

COMMIT TRANSACTION;
GO

SELECT OBJECT_NAME(c.object_id) AS Tabela, c.name AS Coluna, t.name AS Tipo, c.is_nullable AS AceitaNulo
  FROM sys.columns c
  JOIN sys.types t ON t.user_type_id = c.user_type_id
 WHERE (c.object_id = OBJECT_ID(N'dbo.Setores') AND c.name = N'ModoDoRobo')
    OR (c.object_id = OBJECT_ID(N'dbo.Conversas') AND c.name IN (N'RoboPausadoEm', N'RoboPausadoPorAtendenteId'))
    OR (c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name IN (N'AtendimentoSemRobo', N'AtendimentoSemRoboDesde'));
GO

SELECT Gatilho, Ativo, LEFT(Conteudo, 60) AS InicioDoTexto
  FROM dbo.MensagensAutomaticas
 WHERE Gatilho = 17;
GO

PRINT 'Migracao 055 concluida.';
GO
