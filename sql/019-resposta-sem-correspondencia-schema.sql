/* =============================================================================
   CRM-Cartorio — IA so escala por pedido explicito: schema (gatilho 11)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-18
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql, 017-piso-da-ia-schema.sql

   O QUE FAZ
       Amplia o CHECK de Gatilho para o valor novo:
         11 RespostaSemCorrespondencia — a IA nao encontrou resposta especifica
                                          no catalogo, e o cliente NAO pediu
                                          atendente. Nunca promete atendente;
                                          convida a pedir um se quiser.

   POR QUE ISSO EXISTE
       Achado ao vivo em 2026-08-18: com `podeResponder=false` da IA, o sistema
       escalava direto para "um atendente vai continuar" mesmo sem o cliente
       ter pedido nada. Dois clientes reais, no mesmo dia, receberam essa
       mensagem por terem dito so "somente" (queria dizer "so isso, obrigado")
       ou por terem perguntado algo que a IA nao sabia responder. A partir de
       agora, PedidoDeAtendente e TriagemSemCorrespondencia SO disparam por
       pedido explicito do cliente — este gatilho novo e o que a IA usa no
       lugar disso. O conteudo vem numa migracao seguinte (020).

   IDEMPOTENCIA
       Constraint so muda quando ainda nao esta no formato novo.
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

BEGIN TRANSACTION;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MensagensAutomaticas_Gatilho')
    ALTER TABLE dbo.MensagensAutomaticas DROP CONSTRAINT CK_MensagensAutomaticas_Gatilho;
GO

ALTER TABLE dbo.MensagensAutomaticas ADD CONSTRAINT CK_MensagensAutomaticas_Gatilho
    CHECK (Gatilho IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11));
GO

COMMIT TRANSACTION;
GO

PRINT 'Migracao 019 concluida.';
GO
