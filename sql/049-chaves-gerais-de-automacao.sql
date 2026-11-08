/* =============================================================================
   CRM-Cartorio — Chaves gerais de automacao por tenant
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-22
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql

   O QUE FAZ
       Adiciona duas colunas em dbo.Tenants, ambas BIT NOT NULL DEFAULT (1):
         1) MensagensAutomaticasHabilitadas — chave geral de TODAS as
            mensagens automaticas, inclusive o bot de triagem (pedido de nome,
            boas-vindas com menu de setores, menu de servicos, despedida).
         2) RespostaInteligenteHabilitada — chave geral da IA, pela tela.
       Nenhuma linha existente e alterada alem do preenchimento pelo DEFAULT.

   O PROBLEMA QUE ISTO RESOLVE
       Ligar/desligar a IA so era possivel por
       appsettings:RespostaInteligente:Habilitada — exigia editar arquivo no
       servidor e reciclar o App Pool. Desligar as mensagens automaticas
       exigia desativar regra por regra. As duas chaves passam a ser do
       cartorio, na tela, por tenant (Tenant.cs; portas unicas em
       DisparadorDeMensagemAutomatica.EnfileirarAsync e
       RespostaInteligenteService.ResponderAsync).

   POR QUE DEFAULT (1)
       E o comportamento de hoje: mensagens automaticas disparam, e a IA
       responde onde ha provedor configurado (TESTE e producao estao com
       Habilitada=true no appsettings). Nascer 0 desligaria o piloto no
       instante do deploy. Onde a IA estava desligada so por falta de chave
       (desenvolvimento), ela continua desligada: sem provedor, a chave do
       tenant nao liga nada.

   COMPATIBILIDADE DE DEPLOY
       Expand-only: dois ALTER TABLE ADD COLUMN com DEFAULT. Confirmado em
       Tenant.cs do binario N-1: a classe nao possui nenhuma das duas
       propriedades, entao N-1 nunca as le nem grava — todo INSERT/UPDATE que
       N-1 gera em dbo.Tenants omite as colunas, aceitas pelo DEFAULT (1).
       EF Core nao valida schema no boot. Rollback automatico do binario e
       seguro: as colunas ficam inertes para N-1, que volta a obedecer
       RespostaInteligente:Habilitada do appsettings do servidor (o arquivo
       nao e tocado pelo deploy). Direcao forward-only: o binario NOVO le as
       duas colunas no despacho de mensagem e nao pode subir antes desta
       migration.

   IDEMPOTENCIA
       Cada ALTER TABLE e guardado por COL_LENGTH IS NULL — rodar de novo e
       no-op.
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

IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'dbo.Tenants', N'MensagensAutomaticasHabilitadas') IS NULL
    ALTER TABLE dbo.Tenants ADD MensagensAutomaticasHabilitadas BIT NOT NULL
        CONSTRAINT DF_Tenants_MensagensAutomaticasHabilitadas DEFAULT (1);
GO

IF COL_LENGTH(N'dbo.Tenants', N'RespostaInteligenteHabilitada') IS NULL
    ALTER TABLE dbo.Tenants ADD RespostaInteligenteHabilitada BIT NOT NULL
        CONSTRAINT DF_Tenants_RespostaInteligenteHabilitada DEFAULT (1);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'049-chaves-gerais-de-automacao.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'049-chaves-gerais-de-automacao.sql',
                N'Tenants.MensagensAutomaticasHabilitadas e Tenants.RespostaInteligenteHabilitada (BIT, default 1): chaves gerais de liga/desliga pela tela, por tenant. A IA deixa de depender de appsettings:RespostaInteligente:Habilitada.');
END
GO

COMMIT TRANSACTION;
GO

PRINT 'Migracao 049 concluida. Chaves gerais de mensagens automaticas e Resposta Inteligente adicionadas em dbo.Tenants (ligadas).';
GO
