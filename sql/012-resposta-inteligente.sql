/* =============================================================================
   CRM-Cartorio — Resposta Inteligente (IA grounded na triagem)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-17
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql

   O QUE FAZ
       Acrescenta a dbo.Conversas o estado da tentativa de resposta inteligente
       (aguardando resposta + contador de trocas) e a dbo.Mensagens a marca de que
       uma mensagem automatica veio da IA, nao de uma regra de MensagemAutomatica
       cadastrada.

   POR QUE DUAS COLUNAS EM CONVERSAS, E NAO UMA SO
       AguardandoRespostaInteligente decide se a PROXIMA mensagem do cliente volta
       para a IA ou cai direto no encaminhamento humano — mesmo papel que
       AguardandoTriagem ja cumpre para o menu, so que depois dele.
       TentativasRespostaInteligente e o contador que impede a conversa de virar um
       loop de robo: estourado OpcoesDaIA:MaxTrocas, escala para humano mesmo que a
       IA continue "querendo" responder.

   POR QUE GeradaPorIa EM MENSAGENS
       EhAutomatica ja existe desde a Fase 1 e cobre as duas origens (regra
       cadastrada e IA) com o mesmo valor — sem este campo elas ficariam
       indistinguiveis no banco, e a tela nao teria como rotular "IA" em vez de
       "Automatica".

   IDEMPOTENCIA
       Cada coluna so nasce quando ainda nao existe. Rodar de novo nao muda nada.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* Obrigatorio para QUALQUER escrita em tabela com indice filtrado (dbo.Tenants,
   desde a migracao 010). O sqlcmd sobe com OFF por padrao. */
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Conversas', N'U') IS NULL OR OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* A proxima mensagem do cliente volta para a Resposta Inteligente em vez de cair
   direto no encaminhamento humano. Limpa quando a IA nao sabe responder, quando o
   cliente pede humano, quando as trocas estouram MaxTrocas, ou quando um
   atendente assume o setor manualmente. */
IF COL_LENGTH('dbo.Conversas', 'AguardandoRespostaInteligente') IS NULL
    ALTER TABLE dbo.Conversas ADD AguardandoRespostaInteligente BIT NOT NULL
        CONSTRAINT DF_Conversas_AguardandoRespostaInteligente DEFAULT (0);
GO

/* Quantas vezes a IA ja tentou responder nesta sequencia sem setor. */
IF COL_LENGTH('dbo.Conversas', 'TentativasRespostaInteligente') IS NULL
    ALTER TABLE dbo.Conversas ADD TentativasRespostaInteligente INT NOT NULL
        CONSTRAINT DF_Conversas_TentativasRespostaInteligente DEFAULT (0);
GO

/* Sempre acompanha EhAutomatica = 1. Distingue resposta de regra fixa (o cartório parceiro
   escreveu o texto) de resposta gerada por modelo de linguagem. */
IF COL_LENGTH('dbo.Mensagens', 'GeradaPorIa') IS NULL
    ALTER TABLE dbo.Mensagens ADD GeradaPorIa BIT NOT NULL
        CONSTRAINT DF_Mensagens_GeradaPorIa DEFAULT (0);
GO

/* Mesma logica de CK_Mensagens_AutomaticaEhDoSistema (001-schema.sql): mensagem
   gerada por IA e sempre automatica. GeradaPorIa=1 com EhAutomatica=0 inverteria
   a autoria na tela. */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Mensagens_IaEhAutomatica')
    ALTER TABLE dbo.Mensagens ADD CONSTRAINT CK_Mensagens_IaEhAutomatica CHECK
    (
        ([GeradaPorIa] = 0) OR ([GeradaPorIa] = 1 AND [EhAutomatica] = 1)
    );
GO

COMMIT TRANSACTION;
GO

SELECT COUNT(*) AS ConversasAguardandoIA FROM dbo.Conversas WHERE AguardandoRespostaInteligente = 1;
GO

PRINT 'Migracao 012 concluida.';
GO
