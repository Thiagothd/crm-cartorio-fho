/* =============================================================================
   CRM-Cartorio — "Ficou alguma dúvida?" depois da orientação de um serviço
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-30
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 053-espera-de-dados-e-comprovante.sql

   MULTIEMPRESA
       Os bancos dos cartórios contratados pelo site (CRM_Tenant_*) recebem a mesma
       mudança por sql/saas/tenant/004-pergunta-de-duvida.sql.

   O QUE FAZ
       1. CK_MensagensAutomaticas_Gatilho passa a aceitar 16 (PerguntaDeDuvida)
       2. Conversas.AguardandoDuvidaDoServico BIT NOT NULL DEFAULT (0)
       3. O texto do gatilho 16, ATIVO, espelhando CatalogoDeDemonstracao.cs,
          em todo tenant que ainda não tem

   O PROBLEMA QUE ISTO RESOLVE
       Teste do menu do cartório parceiro em 2026-09-30: os serviços que só orientam
       (registro de nascimento, habilitação de casamento, registro de óbito,
       averbações) mandavam o texto e, logo depois, o menu de continuação. O
       cliente lia, não escolhia nada e a conversa acabava encerrada por
       inatividade sem ninguém saber se ele entendeu. Agora o robô pergunta se
       ficou dúvida: "1" encerra, "2" chama atendente, e qualquer outra coisa é a
       dúvida — a IA responde com o texto do serviço no contexto e pergunta de
       novo.

   SEGURO COM O BINARIO ANTERIOR RODANDO
       O CHECK só amplia. A coluna nasce com DEFAULT (0), então todo INSERT do
       binário anterior grava "não está esperando dúvida". O texto novo é uma
       linha de gatilho que o binário anterior nunca lê (ele não conhece o 16).
       Ver deploy/migration-compatibility.json.

   IDEMPOTENCIA
       Constraint, coluna e texto só entram quando ainda não existem.
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

IF OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL OR OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1. Gatilho 16 no CHECK ------------------------------------------------ */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MensagensAutomaticas_Gatilho')
    ALTER TABLE dbo.MensagensAutomaticas DROP CONSTRAINT CK_MensagensAutomaticas_Gatilho;
GO
ALTER TABLE dbo.MensagensAutomaticas ADD CONSTRAINT CK_MensagensAutomaticas_Gatilho
    CHECK (Gatilho IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16));
GO

/* --- 2. A espera pela resposta à pergunta ---------------------------------- */
IF COL_LENGTH(N'dbo.Conversas', N'AguardandoDuvidaDoServico') IS NULL
    ALTER TABLE dbo.Conversas ADD AguardandoDuvidaDoServico BIT NOT NULL
        CONSTRAINT DF_Conversas_AguardandoDuvidaDoServico DEFAULT (0);
GO

/* --- 3. O texto do gatilho 16 ----------------------------------------------
   ESPELHO OBRIGATORIO: idêntico ao de
   Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs.
   ------------------------------------------------------------------------- */
DECLARE @agora DATETIME2(3) = SYSUTCDATETIME();

INSERT INTO dbo.MensagensAutomaticas
    (Id, TenantId, Gatilho, EtapaAlvoId, Conteudo, Ativo, CriadoEm, AtualizadoEm)
SELECT
    CASE WHEN t.Id = '0199B2C0-0000-7000-8000-000000000001'
              AND NOT EXISTS (SELECT 1 FROM dbo.MensagensAutomaticas x WHERE x.Id = '0199B2C0-0006-7000-8000-000000000017')
         THEN CAST('0199B2C0-0006-7000-8000-000000000017' AS UNIQUEIDENTIFIER)
         ELSE NEWID() END,
    t.Id, 16, NULL,
    N'Ficou alguma dúvida, {NomeCliente}? É só escrever aqui que eu te ajudo. 😊' + CHAR(10) + CHAR(10) +
    N'Se estiver tudo certo, responda:' + CHAR(10) +
    N'1 - Não tenho dúvidas, pode encerrar' + CHAR(10) +
    N'2 - Quero falar com um atendente',
    1, @agora, @agora
FROM dbo.Tenants t
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.MensagensAutomaticas m WHERE m.TenantId = t.Id AND m.Gatilho = 16);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'054-pergunta-de-duvida.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'054-pergunta-de-duvida.sql',
                N'Gatilho 16 (PerguntaDeDuvida): CHECK ampliado, Conversas.AguardandoDuvidaDoServico e texto ativo — pergunta se ficou duvida depois da orientacao de um servico.');
END
GO

COMMIT TRANSACTION;
GO

SELECT Gatilho, Ativo, LEFT(Conteudo, 60) AS InicioDoTexto
  FROM dbo.MensagensAutomaticas
 WHERE Gatilho = 16;
GO

PRINT 'Migracao 054 concluida.';
GO
