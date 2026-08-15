/* =============================================================================
   CRM-Cartorio — Reset dos atendimentos de demonstracao
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-12
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (ex.: CRM_Cartorio_Dev)

   ATENCAO — ESTE SCRIPT APAGA DADOS.
   E o unico script do repositorio que apaga alguma coisa. O produto nunca apaga
   conversa; este arquivo existe para devolver o ambiente de demonstracao ao
   estado inicial depois de mexerem nele durante testes manuais.

   NAO RODAR EM PRODUCAO. Se algum dia houver producao, a existencia deste
   arquivo la ja e o erro.

   O QUE APAGA
       Mensagens, HistoricoEtapas, Conversas e Clientes — ou seja, o movimento.

   O QUE PRESERVA
       Tenant, Setores, EtapasFunil, Atendentes, TiposServico, PrazosServico,
       MensagensAutomaticas e RespostasRapidas — ou seja, a configuracao vinda do
       002-seed-simulado.sql.

   DEPOIS DE RODAR
       Reinicie a API com Seed:Habilitado = true. O semeador detecta que nao ha
       conversa e recria os 10 atendimentos de demonstracao com datas relativas
       ao momento da execucao — inclusive a conversa atrasada e a em alerta.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* Ordem obrigatoria: filhos antes dos pais, por causa das FKs. */
DELETE FROM dbo.Mensagens;
DELETE FROM dbo.HistoricoEtapas;
DELETE FROM dbo.Conversas;
DELETE FROM dbo.Clientes;

GO

COMMIT TRANSACTION;
GO

SELECT 'Clientes restantes  = ' + CONVERT(varchar, COUNT(*)) FROM dbo.Clientes;
SELECT 'Conversas restantes = ' + CONVERT(varchar, COUNT(*)) FROM dbo.Conversas;
SELECT 'Atendentes mantidos = ' + CONVERT(varchar, COUNT(*)) FROM dbo.Atendentes;
SELECT 'Setores mantidos    = ' + CONVERT(varchar, COUNT(*)) FROM dbo.Setores;
GO

PRINT 'Reset concluido. Reinicie a API para o semeador recriar os atendimentos.';
PRINT 'ATENCAO: recarregue as abas do painel que estiverem abertas. As conversas';
PRINT 'recriadas tem Id novo, e uma aba antiga aponta para Id que nao existe mais.';
GO
