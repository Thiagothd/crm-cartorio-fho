/* =============================================================================
   CRM-Cartorio — Servico por setor + checklist de documento
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-17
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql, 013-catalogo-atos-candidatos.sql

   O QUE FAZ
       Liga cada TipoServico ao Setor que o oferece no menu de 2o nivel da
       triagem automatica (depois que o cliente ja escolheu o setor), acrescenta
       o checklist de documento em texto livre por servico, e o estado de
       conversa que espera essa escolha.

   POR QUE ISSO NAO EXISTIA
       Setor (eixo de roteamento) e TipoServico (catalogo) sempre foram eixos
       independentes — nenhum dos dois referencia o outro, so Conversa referencia
       os dois. Achado ao vivo nesta sessao: sem o vinculo, a triagem automatica
       nao tem como perguntar "qual desses servicos" depois de rotear o setor, e
       o checklist de documento/prazo por servico nao entra sozinho no fluxo.

   BACKFILL
       Os 19 servicos ja cadastrados ganham SetorId pela convencao que ja vale
       hoje: Categoria Notas -> Setor "Tabelionato de Notas", Categoria
       RegistroCivil -> Setor "Registro Civil". O servico 14 (Notificacao
       extrajudicial, Categoria Outros) fica com SetorId nulo de proposito: a
       propria atribuicao dele ainda aguarda confirmacao do cartório parceiro
       (CatalogoDeDemonstracao.cs, AguardandoConfirmacao) — sem SetorId ele nao
       entra em nenhum menu automatico ate isso ser resolvido.

   DocumentosNecessarios FICA NULO NESTE SCRIPT
       O conteudo dos 19 checklists e da migracao seguinte (015), que espelha
       CatalogoDeDemonstracao.cs caractere a caractere — mesma separacao que
       003/002 ja usam entre estrutura e conteudo extenso.

   IDEMPOTENCIA
       Cada coluna/indice/constraint so nasce quando ainda nao existe. O backfill
       so roda nas linhas que ainda estao com SetorId nulo, entao rodar de novo
       nao reverte um SetorId que o cartório parceiro tenha ajustado manualmente depois.
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

IF OBJECT_ID(N'dbo.TiposServico', N'U') IS NULL OR OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Setores', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* Nulo de proposito — ver cabecalho. FK sem cascade: apagar um Setor com servico
   vinculado e erro de cadastro, nao operacao silenciosa. */
IF COL_LENGTH('dbo.TiposServico', 'SetorId') IS NULL
    ALTER TABLE dbo.TiposServico ADD SetorId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TiposServico_Setores')
    ALTER TABLE dbo.TiposServico ADD CONSTRAINT FK_TiposServico_Setores
        FOREIGN KEY (SetorId) REFERENCES dbo.Setores (Id);
GO

IF COL_LENGTH('dbo.TiposServico', 'DocumentosNecessarios') IS NULL
    ALTER TABLE dbo.TiposServico ADD DocumentosNecessarios NVARCHAR(800) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TiposServico_Tenant_Setor_Ativo')
    CREATE INDEX IX_TiposServico_Tenant_Setor_Ativo
        ON dbo.TiposServico (TenantId, SetorId, Ativo);
GO

/* A proxima mensagem do cliente e interpretada como escolha de servico dentro
   do setor ja roteado, em vez de cair direto na Resposta Inteligente. Mesma
   disciplina de AguardandoTriagem/AguardandoRespostaInteligente (migracoes
   001/012): dura uma mensagem, limpa tenha ela correspondido ou nao. */
IF COL_LENGTH('dbo.Conversas', 'AguardandoEscolhaDeServico') IS NULL
    ALTER TABLE dbo.Conversas ADD AguardandoEscolhaDeServico BIT NOT NULL
        CONSTRAINT DF_Conversas_AguardandoEscolhaDeServico DEFAULT (0);
GO

/* Backfill: so toca linha ainda sem SetorId, para nao sobrescrever ajuste manual
   feito entre uma execucao e outra deste script. */
UPDATE ts
SET ts.SetorId = s.Id
FROM dbo.TiposServico ts
INNER JOIN dbo.Setores s
    ON s.TenantId = ts.TenantId
   AND (
        (ts.Categoria = 1 AND s.Nome = N'Tabelionato de Notas') OR  -- Notas
        (ts.Categoria = 2 AND s.Nome = N'Registro Civil')           -- RegistroCivil
   )
WHERE ts.SetorId IS NULL;
GO

COMMIT TRANSACTION;
GO

SELECT ts.Nome AS Servico, s.Nome AS Setor
FROM dbo.TiposServico ts
LEFT JOIN dbo.Setores s ON s.Id = ts.SetorId
ORDER BY s.Nome, ts.Nome;
GO

PRINT 'Migracao 014 concluida.';
GO
