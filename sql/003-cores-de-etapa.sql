/* =============================================================================
   CRM-Cartorio — Cores de etapa validadas com o cliente
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-13
   Depende de ... 002-seed-simulado.sql

   POR QUE ESTE SCRIPT EXISTE
       O esquema de cores por etapa foi validado com o cliente depois que a base
       ja estava populada. Quem recriar a base do zero pega as cores certas
       direto do 002 — este arquivo e para as bases que ja existem.

   O QUE FAZ
       Atualiza EtapasFunil.Cor de duas etapas. Nao apaga nada, nao mexe em
       conversa, mensagem ou cadastro. Idempotente: rodar duas vezes nao muda
       nada na segunda.

   | Etapa                        | Antes    | Depois   |
   |------------------------------|----------|----------|
   | Atendimento inicial          | #5C5852  | #2E5170  |  azul acinzentado
   | Captacao do servico          | #7A2E32  | #6B3F73  |  roxo suave
   | Em processamento / exigencia | #8A6516  | #8A6516  |  ambar (inalterada)
   | Pronto / entrega             | #3D5C3A  | #3D5C3A  |  verde (inalterada)

   A coluna Cor guarda a cor de TINTA da etapa. O fundo e a borda de cada pilula
   sao derivados dela no frontend (docs/DESIGN.md, secao 2.2) — nao ha coluna
   para os outros dois valores, e por isso este script mexe em uma coluna so.

   Nota: tirar o bordo (#7A2E32) da etapa de captacao tambem devolve o bordo ao
   uso restrito de acao primaria e atraso, como manda a regra das 3 ocorrencias
   por tela (DESIGN.md, secao 2.1).
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

UPDATE dbo.EtapasFunil
   SET Cor = '#2E5170',
       AtualizadoEm = SYSUTCDATETIME()
 WHERE Nome = N'Atendimento inicial'
   AND Cor <> '#2E5170';

UPDATE dbo.EtapasFunil
   SET Cor = '#6B3F73',
       AtualizadoEm = SYSUTCDATETIME()
 WHERE Nome = N'Captacao do servico'
   AND Cor <> '#6B3F73';

GO

COMMIT TRANSACTION;
GO

SELECT '  ' + Nome + ' -> ' + Cor
  FROM dbo.EtapasFunil
 ORDER BY Ordem;
GO

PRINT 'Cores de etapa atualizadas.';
GO
