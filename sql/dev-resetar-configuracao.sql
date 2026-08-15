/* =============================================================================
   CRM-Cartorio — Reset da CONFIGURACAO de demonstracao
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-13
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (ex.: CRM_Cartorio_Dev)

   ATENCAO — ESTE SCRIPT APAGA DADOS.

   NAO RODAR EM PRODUCAO. Se algum dia houver producao, a existencia deste
   arquivo la ja e o erro.

   QUANDO USAR
       Quando a CONFIGURACAO mudar de forma (setor removido, servico removido,
       nome de atendente corrigido) e for preciso repopular do zero. O
       dev-resetar-atendimentos.sql limpa apenas o movimento e nao resolve isso.

   O QUE APAGA
       Movimento .... Mensagens, HistoricoEtapas, Conversas, Clientes
       Configuracao . PrazosServico, TiposServico, MensagensAutomaticas,
                      RespostasRapidas, Atendentes, EtapasFunil, Setores, Tenants

       Ou seja: a base fica com o schema vazio, como logo apos o 001-schema.sql.

   ORDEM DE USO
       1. dev-resetar-configuracao.sql   (este arquivo)
       2. 002-seed-simulado.sql          (recria a configuracao)
       3. reiniciar a API                (o semeador recria os atendimentos)
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Obrigatorias para qualquer DELETE em dbo.Conversas: a tabela tem indices
-- FILTRADOS (criados na migracao 005), e o SQL Server recusa DML sobre tabela com
-- indice filtrado se estas opcoes nao estiverem ligadas — erro 1934.
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* Ordem obrigatoria: filhos antes dos pais, por causa das FKs do schema.
   Atendentes vem antes de Setores porque aponta para SetorPadraoId, e
   MensagensAutomaticas antes de EtapasFunil porque aponta para EtapaAlvoId. */
DELETE FROM dbo.Mensagens;
DELETE FROM dbo.HistoricoEtapas;
DELETE FROM dbo.Conversas;
DELETE FROM dbo.Clientes;
DELETE FROM dbo.PrazosServico;
DELETE FROM dbo.TiposServico;
DELETE FROM dbo.MensagensAutomaticas;
DELETE FROM dbo.RespostasRapidas;
DELETE FROM dbo.Atendentes;
DELETE FROM dbo.EtapasFunil;
DELETE FROM dbo.Setores;
DELETE FROM dbo.Tenants;

GO

COMMIT TRANSACTION;
GO

SELECT 'Tenants     = ' + CONVERT(varchar, COUNT(*)) FROM dbo.Tenants;
SELECT 'Setores     = ' + CONVERT(varchar, COUNT(*)) FROM dbo.Setores;
SELECT 'Atendentes  = ' + CONVERT(varchar, COUNT(*)) FROM dbo.Atendentes;
SELECT 'Servicos    = ' + CONVERT(varchar, COUNT(*)) FROM dbo.TiposServico;
SELECT 'Conversas   = ' + CONVERT(varchar, COUNT(*)) FROM dbo.Conversas;
GO

PRINT 'Base zerada. Rode 002-seed-simulado.sql e reinicie a API.';
PRINT 'ATENCAO: recarregue as abas do painel que estiverem abertas. Tudo foi';
PRINT 'recriado com Id novo, e aba antiga aponta para Id que nao existe mais.';
GO
