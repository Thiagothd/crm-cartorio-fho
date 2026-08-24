/* =============================================================================
   CRM-Cartorio — Origem do cadastro do cliente
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-24
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql

   O QUE FAZ
       dbo.Clientes += OrigemDoCadastro TINYINT NULL
       (1 Webhook, 2 Manual, 3 ImportacaoPlanilha, 4 ImportacaoWhatsapp)

   O PROBLEMA QUE ISTO RESOLVE
       Ate aqui nao havia como responder "de onde vieram esses clientes?" — se
       falaram com o cartorio pelo WhatsApp, se foram cadastrados no balcao, ou
       se entraram por uma importacao de contatos. Essa distincao passa a
       importar quando existir um canal para falar com essas pessoas em massa
       (a via oficial da Meta, quando os pre-requisitos do cartório parceiro fecharem) —
       quem entrou por importacao fria nao e a mesma coisa de quem ja procurou
       o cartorio.

   POR QUE NULO NOS CLIENTES QUE JA EXISTEM, E NAO UM VALOR PADRAO
       A coluna nasce sem preencher nada retroativo. "Nao sabemos de onde este
       cliente veio" e a verdade para quem ja estava cadastrado antes desta
       migracao — inventar Webhook ou Manual para eles seria dado falso com
       aparencia de real. So o codigo (RecebimentoDeWhatsApp, ClienteService, e
       o importador de contatos) carimba o valor dai em diante.

   SEM MECANISMO DE DISPARO EM MASSA
       Esta migracao NAO cria nada de campanha, fila de envio, ou opt-out. O
       pedido original incluia disparo em massa; foi cancelado apos avaliar o
       risco de banimento do numero do piloto sobre Baileys (CLAUDE.md, secao
       8) — sem via oficial da Meta disponivel, nao ha como recuperar um numero
       banido. So a importacao de contatos segue, que nao envia nada e tem
       risco de banimento zero.

   IDEMPOTENCIA
       Coluna so entra quando ainda nao existe.
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

IF OBJECT_ID(N'dbo.Clientes', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1. A coluna ----------------------------------------------------------- */

IF COL_LENGTH(N'dbo.Clientes', N'OrigemDoCadastro') IS NULL
    ALTER TABLE dbo.Clientes ADD OrigemDoCadastro TINYINT NULL;
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'026-origem-do-cadastro-de-cliente.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'026-origem-do-cadastro-de-cliente.sql',
                N'Clientes.OrigemDoCadastro: de onde o cadastro veio (webhook, manual, importacao de planilha, importacao do WhatsApp). Nulo nos clientes que ja existiam.');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.Clientes', N'OrigemDoCadastro') AS ColunaExiste;
GO

PRINT 'Migracao 026 concluida.';
GO
