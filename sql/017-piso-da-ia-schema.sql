/* =============================================================================
   CRM-Cartorio — Piso da IA: schema (AguardandoMenuDeContinuacao + gatilhos 8-10)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-17
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql, 015-checklist-e-mensagens-de-servico.sql

   O QUE FAZ
       Prepara o banco para o "piso da IA": nenhuma mensagem de cliente pode
       ficar sem tratamento, nem quando nenhuma das flags de fluxo esta ligada.
       Acrescenta Conversas.AguardandoMenuDeContinuacao e amplia o CHECK de
       Gatilho para os tres valores novos:
         8  MenuDeContinuacao   — "posso ajudar em mais algo?" depois do checklist
         9  PedidoDeAtendente   — encaminha para humano NO MEIO do atendimento
                                   (diferente de TriagemSemCorrespondencia, que e
                                   so de primeiro contato)
         10 Despedida           — cliente escolheu encerrar pelo menu

   POR QUE ISSO EXISTE
       Achado ao vivo em 2026-08-17: o codigo de recebimento tinha um
       if/else-if SEM else. Com as tres flags de fluxo desligadas — exatamente
       o estado normal depois que um atendimento termina — qualquer mensagem
       do cliente era gravada e NADA respondia. "Obrigada", "ainda ai?", ate
       "esse e o cartorio do cartório parceiro?" numa conversa em andamento ficavam mudos.
       O conteudo das 3 mensagens novas vem numa migracao seguinte, depois do
       codigo que as dispara estar pronto.

   IDEMPOTENCIA
       Coluna e constraint so mudam quando ainda nao estao no formato novo.
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

IF OBJECT_ID(N'dbo.Conversas', N'U') IS NULL OR OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH('dbo.Conversas', 'AguardandoMenuDeContinuacao') IS NULL
    ALTER TABLE dbo.Conversas ADD AguardandoMenuDeContinuacao BIT NOT NULL
        CONSTRAINT DF_Conversas_AguardandoMenuDeContinuacao DEFAULT (0);
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MensagensAutomaticas_Gatilho')
    ALTER TABLE dbo.MensagensAutomaticas DROP CONSTRAINT CK_MensagensAutomaticas_Gatilho;
GO

ALTER TABLE dbo.MensagensAutomaticas ADD CONSTRAINT CK_MensagensAutomaticas_Gatilho
    CHECK (Gatilho IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10));
GO

COMMIT TRANSACTION;
GO

PRINT 'Migracao 017 concluida.';
GO
