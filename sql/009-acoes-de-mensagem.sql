/* =============================================================================
   CRM-Cartorio — Fixar, ocultar e encaminhar mensagem
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-15
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql

   O QUE FAZ
       Amplia dbo.Mensagens com as colunas das acoes do menu de contexto:
       fixar no topo, ocultar do fio e encaminhar para outra conversa.

   O QUE NAO FAZ — E ISTO E O PONTO
       NAO cria caminho de exclusao de mensagem. "Ocultar" grava OcultaEm e
       OcultaPorAtendenteId; a LINHA PERMANECE NO BANCO e continua vindo na API,
       marcada. Quem oculta esconde de quem atende, nao do registro.

       E a mesma postura de conversa encerrada e cliente desativado (RNF-008).
       Em cartorio, o que foi dito fica dito. Exclusao real, se um dia for
       pedida, e decisao a ser tomada explicitamente — nao efeito colateral de
       um item de menu chamado "apagar".

   IDEMPOTENCIA
       Cada coluna, FK e indice so e criado quando ainda nao existe.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Exigidas pelo SQL Server para criar o indice filtrado da mensagem fixada.
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%' AND DB_NAME() <> N'CRM_Cartorio_Demo'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* -----------------------------------------------------------------------------
   Fixar — uma mensagem por conversa. Fixar outra desafixa a anterior, e isso e
   regra de servico: um mural de mensagens fixadas disputaria o topo do fio, que
   e o lugar mais caro da tela.
   -------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Mensagens', 'FixadaEm') IS NULL
    ALTER TABLE dbo.Mensagens ADD FixadaEm DATETIME2(3) NULL;

IF COL_LENGTH('dbo.Mensagens', 'FixadaPorAtendenteId') IS NULL
    ALTER TABLE dbo.Mensagens ADD FixadaPorAtendenteId UNIQUEIDENTIFIER NULL;
GO

/* -----------------------------------------------------------------------------
   Ocultar — guardar QUEM e QUANDO e o que separa "ocultar" de "apagar com outro
   nome": ha a quem perguntar depois.
   -------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Mensagens', 'OcultaEm') IS NULL
    ALTER TABLE dbo.Mensagens ADD OcultaEm DATETIME2(3) NULL;

IF COL_LENGTH('dbo.Mensagens', 'OcultaPorAtendenteId') IS NULL
    ALTER TABLE dbo.Mensagens ADD OcultaPorAtendenteId UNIQUEIDENTIFIER NULL;
GO

/* -----------------------------------------------------------------------------
   Encaminhar — guarda a REFERENCIA da origem, nao um recorte do texto. Mesma
   escolha de MensagemRespondidaId: a procedencia nao se perde e nao ha duas
   versoes da mesma frase no banco.
   -------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Mensagens', 'EncaminhadaDeMensagemId') IS NULL
    ALTER TABLE dbo.Mensagens ADD EncaminhadaDeMensagemId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Mensagens_FixadaPor')
    ALTER TABLE dbo.Mensagens WITH CHECK ADD CONSTRAINT FK_Mensagens_FixadaPor
        FOREIGN KEY (FixadaPorAtendenteId) REFERENCES dbo.Atendentes (Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Mensagens_OcultaPor')
    ALTER TABLE dbo.Mensagens WITH CHECK ADD CONSTRAINT FK_Mensagens_OcultaPor
        FOREIGN KEY (OcultaPorAtendenteId) REFERENCES dbo.Atendentes (Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Mensagens_EncaminhadaDe')
    ALTER TABLE dbo.Mensagens WITH CHECK ADD CONSTRAINT FK_Mensagens_EncaminhadaDe
        FOREIGN KEY (EncaminhadaDeMensagemId) REFERENCES dbo.Mensagens (Id);
GO

/* Filtrado: a mensagem fixada de uma conversa e uma entre dezenas, e a consulta
   do topo do fio nao deve varrer o resto. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Mensagens') AND name = 'IX_Mensagens_Fixada')
    CREATE INDEX IX_Mensagens_Fixada
        ON dbo.Mensagens (ConversaId)
        WHERE FixadaEm IS NOT NULL;
GO

COMMIT TRANSACTION;
GO

SELECT
    COL_LENGTH('dbo.Mensagens', 'FixadaEm')                AS ColunaFixada,
    COL_LENGTH('dbo.Mensagens', 'OcultaEm')                AS ColunaOculta,
    COL_LENGTH('dbo.Mensagens', 'EncaminhadaDeMensagemId') AS ColunaEncaminhada,
    (SELECT COUNT(*) FROM dbo.Mensagens)                   AS MensagensPreservadas;
GO

PRINT 'Migracao 009 concluida. Nenhuma mensagem foi apagada — e nao existe caminho para isso.';
GO
