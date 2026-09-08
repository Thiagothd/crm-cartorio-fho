/* =============================================================================
   CRM-Cartorio — Modo de atendimento humano da conversa
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-26
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql

   O QUE FAZ
       dbo.Conversas += EmAtendimentoHumano BIT NOT NULL DEFAULT (0)
       Faz o backfill de EmAtendimentoHumano = 1 para toda conversa ATIVA que
       ja tem AtendenteId preenchido no momento desta migracao.

   O PROBLEMA QUE ISTO RESOLVE (BUG-ATD-01)
       Ate aqui, "a conversa esta em atendimento humano" so existia como
       AtendenteId != NULL — e isso nao bastava. Nenhum ponto do despacho de
       mensagem do cliente olhava esse campo antes de decidir chamar a IA/bot,
       entao um atendente que assumia a conversa e respondia via WhatsApp
       continuava recebendo a IA respondendo por cima dele na mensagem
       seguinte. Pior: ConversaService.LiberarAsync zera AtendenteId para
       devolver a conversa a fila humana, mas a conversa continua devendo
       humano, nao devendo bot — com AtendenteId sozinho como sinal, essas
       duas situacoes (nunca teve humano / teve humano e foi devolvida a
       fila) eram indistinguiveis.

       EmAtendimentoHumano e o campo que resolve essa ambiguidade: liga
       quando o cliente pede atendente ou quando um atendente assume, e so
       desliga no encerramento — nunca ao liberar, transferir ou a IA/bot
       ficarem momentaneamente sem trabalho.

   POR QUE NAO REAPROVEITAR AtendenteId OU Status
       AtendenteId responde "quem e o dono agora", nao "esta conversa ja foi
       entregue a um humano alguma vez neste ciclo". Status e puramente ciclo
       de vida (Ativa/Encerrada) — misturar "quem cuida disso agora" nesse
       enum obrigaria revisar toda consulta que ja filtra por Status para nao
       quebrar. Um campo ortogonal e mais barato e mais seguro.

   POR QUE O BACKFILL, E POR QUE ELE TEM DE RODAR ANTES DO CODIGO NOVO
       Sem o backfill, toda conversa JA assumida por um atendente no instante
       do deploy nasceria com EmAtendimentoHumano = 0 — reabrindo exatamente
       o bug para quem estava em atendimento na hora da virada. O pipeline de
       deploy (deploy-teste.yml / deploy-producao.yml) ja aplica migrations
       pendentes ANTES de publicar o binario novo — e essa ordem que fecha a
       janela. Conversas ja ENCERRADAS nao precisam de backfill: nunca voltam
       a ser despachadas.

   IDEMPOTENCIA
       Coluna so entra quando ainda nao existe. O backfill roda sempre que a
       coluna acabou de ser criada nesta mesma execucao (script novo, coluna
       ainda com o default 0 em todo mundo) — rodar o script de novo depois
       nao teria mais nada para corrigir, porque a coluna ja existiria e o
       bloco de ALTER TABLE seria pulado.
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

BEGIN TRANSACTION;
GO

/* --- 1. A coluna ----------------------------------------------------------- */

IF COL_LENGTH(N'dbo.Conversas', N'EmAtendimentoHumano') IS NULL
    ALTER TABLE dbo.Conversas ADD EmAtendimentoHumano BIT NOT NULL CONSTRAINT DF_Conversas_EmAtendimentoHumano DEFAULT (0);
GO

-- Precisa de GO separado do ALTER TABLE acima: o SQL Server resolve nomes de
-- coluna na compilacao do LOTE inteiro, entao uma coluna criada por ALTER
-- TABLE so fica visivel a partir do proximo lote, mesmo dentro da mesma
-- transacao (BEGIN TRANSACTION nao fecha lote, so GO fecha).
--
-- Backfill: so conversas ATIVAS (Status = 1) com atendente ja atribuido.
-- Conversa encerrada nunca mais e despachada, entao nao importa o valor dela
-- aqui — fica 0, que e honesto (nao ha mais atendimento acontecendo).
--
-- O "AND EmAtendimentoHumano = 0" no WHERE e a idempotencia deste UPDATE: se
-- o script rodar de novo (ou a linha 78 acima for pulada porque a coluna ja
-- existe), nao ha nada para corrigir — toda conversa Ativa com AtendenteId
-- ja deveria ter EmAtendimentoHumano = 1 pelo proprio codigo da aplicacao.
UPDATE dbo.Conversas
   SET EmAtendimentoHumano = 1
 WHERE Status = 1 -- Ativa
   AND AtendenteId IS NOT NULL
   AND EmAtendimentoHumano = 0;
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'028-atendimento-humano.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'028-atendimento-humano.sql',
                N'Conversas.EmAtendimentoHumano: liga quando o cliente pede atendente ou um atendente assume, so desliga no encerramento — nunca ao liberar/transferir. Corrige BUG-ATD-01 (IA respondendo por cima do atendimento humano). Backfill para conversas ja assumidas no momento da migracao.');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.Conversas', N'EmAtendimentoHumano') AS ColunaExiste;
GO

PRINT 'Migracao 028 concluida.';
GO
