/* =============================================================================
   CRM-Cartorio — Qualificacao de clientes e resposta a mensagem
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-13
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql

   O QUE FAZ
       1. Amplia Clientes com os dados de qualificacao usados em atos de Notas
          e Registro Civil: identificacao, estado civil, filiacao e endereco.
       2. Cria indices para busca por documento e cidade.
       3. Permite que uma Mensagem referencie a mensagem original que ela
          responde, sem copiar o texto citado.

   O QUE NAO FAZ
       Nao apaga nem reescreve cliente, conversa ou mensagem existente. Os campos
       novos opcionais nascem nulos; Tipo, EstadoCivil e RegimeDeBens recebem os
       valores neutros usados pelo dominio.

   IDEMPOTENCIA
       Cada coluna, constraint e indice so e criado quando ainda nao existe.
       Rodar novamente nao duplica objeto nem altera os dados.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Opcoes exigidas pelo SQL Server para criar o indice filtrado de respostas.
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

IF OBJECT_ID(N'dbo.Clientes', N'U') IS NULL OR OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* -----------------------------------------------------------------------------
   Clientes — identificacao e qualificacao
   -------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Clientes', 'Tipo') IS NULL
    ALTER TABLE dbo.Clientes ADD Tipo TINYINT NOT NULL
        CONSTRAINT DF_Clientes_Tipo DEFAULT (1) WITH VALUES;

IF COL_LENGTH('dbo.Clientes', 'TelefoneAlternativo') IS NULL
    ALTER TABLE dbo.Clientes ADD TelefoneAlternativo VARCHAR(20) NULL;

IF COL_LENGTH('dbo.Clientes', 'Rg') IS NULL
    ALTER TABLE dbo.Clientes ADD Rg VARCHAR(20) NULL;

IF COL_LENGTH('dbo.Clientes', 'OrgaoEmissor') IS NULL
    ALTER TABLE dbo.Clientes ADD OrgaoEmissor NVARCHAR(20) NULL;

IF COL_LENGTH('dbo.Clientes', 'UfOrgaoEmissor') IS NULL
    ALTER TABLE dbo.Clientes ADD UfOrgaoEmissor VARCHAR(2) NULL;

IF COL_LENGTH('dbo.Clientes', 'DataNascimento') IS NULL
    ALTER TABLE dbo.Clientes ADD DataNascimento DATE NULL;

IF COL_LENGTH('dbo.Clientes', 'EstadoCivil') IS NULL
    ALTER TABLE dbo.Clientes ADD EstadoCivil TINYINT NOT NULL
        CONSTRAINT DF_Clientes_EstadoCivil DEFAULT (0) WITH VALUES;

IF COL_LENGTH('dbo.Clientes', 'RegimeDeBens') IS NULL
    ALTER TABLE dbo.Clientes ADD RegimeDeBens TINYINT NOT NULL
        CONSTRAINT DF_Clientes_RegimeDeBens DEFAULT (0) WITH VALUES;

IF COL_LENGTH('dbo.Clientes', 'Profissao') IS NULL
    ALTER TABLE dbo.Clientes ADD Profissao NVARCHAR(100) NULL;

IF COL_LENGTH('dbo.Clientes', 'Nacionalidade') IS NULL
    ALTER TABLE dbo.Clientes ADD Nacionalidade NVARCHAR(60) NULL;

IF COL_LENGTH('dbo.Clientes', 'Naturalidade') IS NULL
    ALTER TABLE dbo.Clientes ADD Naturalidade NVARCHAR(120) NULL;

IF COL_LENGTH('dbo.Clientes', 'NomeDoPai') IS NULL
    ALTER TABLE dbo.Clientes ADD NomeDoPai NVARCHAR(200) NULL;

IF COL_LENGTH('dbo.Clientes', 'NomeDaMae') IS NULL
    ALTER TABLE dbo.Clientes ADD NomeDaMae NVARCHAR(200) NULL;

IF COL_LENGTH('dbo.Clientes', 'RazaoSocial') IS NULL
    ALTER TABLE dbo.Clientes ADD RazaoSocial NVARCHAR(200) NULL;

IF COL_LENGTH('dbo.Clientes', 'RepresentanteLegal') IS NULL
    ALTER TABLE dbo.Clientes ADD RepresentanteLegal NVARCHAR(200) NULL;

IF COL_LENGTH('dbo.Clientes', 'Cep') IS NULL
    ALTER TABLE dbo.Clientes ADD Cep VARCHAR(8) NULL;

IF COL_LENGTH('dbo.Clientes', 'Logradouro') IS NULL
    ALTER TABLE dbo.Clientes ADD Logradouro NVARCHAR(200) NULL;

IF COL_LENGTH('dbo.Clientes', 'Numero') IS NULL
    ALTER TABLE dbo.Clientes ADD Numero NVARCHAR(20) NULL;

IF COL_LENGTH('dbo.Clientes', 'Complemento') IS NULL
    ALTER TABLE dbo.Clientes ADD Complemento NVARCHAR(100) NULL;

IF COL_LENGTH('dbo.Clientes', 'Bairro') IS NULL
    ALTER TABLE dbo.Clientes ADD Bairro NVARCHAR(100) NULL;

IF COL_LENGTH('dbo.Clientes', 'Cidade') IS NULL
    ALTER TABLE dbo.Clientes ADD Cidade NVARCHAR(120) NULL;

IF COL_LENGTH('dbo.Clientes', 'Uf') IS NULL
    ALTER TABLE dbo.Clientes ADD Uf VARCHAR(2) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Clientes_Tipo')
    ALTER TABLE dbo.Clientes WITH CHECK ADD CONSTRAINT CK_Clientes_Tipo
        CHECK (Tipo IN (1, 2));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Clientes_EstadoCivil')
    ALTER TABLE dbo.Clientes WITH CHECK ADD CONSTRAINT CK_Clientes_EstadoCivil
        CHECK (EstadoCivil IN (0, 1, 2, 3, 4, 5, 6));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Clientes_RegimeDeBens')
    ALTER TABLE dbo.Clientes WITH CHECK ADD CONSTRAINT CK_Clientes_RegimeDeBens
        CHECK (RegimeDeBens IN (0, 1, 2, 3, 4, 5));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Clientes') AND name = 'IX_Clientes_Tenant_Documento')
    CREATE INDEX IX_Clientes_Tenant_Documento ON dbo.Clientes (TenantId, Documento);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Clientes') AND name = 'IX_Clientes_Tenant_Cidade')
    CREATE INDEX IX_Clientes_Tenant_Cidade ON dbo.Clientes (TenantId, Cidade);
GO

/* -----------------------------------------------------------------------------
   Mensagens — citacao/resposta
   -------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Mensagens', 'MensagemRespondidaId') IS NULL
    ALTER TABLE dbo.Mensagens ADD MensagemRespondidaId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Mensagens_MensagemRespondida')
    ALTER TABLE dbo.Mensagens WITH CHECK ADD CONSTRAINT FK_Mensagens_MensagemRespondida
        FOREIGN KEY (MensagemRespondidaId) REFERENCES dbo.Mensagens (Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Mensagens') AND name = 'IX_Mensagens_MensagemRespondida')
    CREATE INDEX IX_Mensagens_MensagemRespondida
        ON dbo.Mensagens (MensagemRespondidaId)
        WHERE MensagemRespondidaId IS NOT NULL;
GO

COMMIT TRANSACTION;
GO

SELECT
    COL_LENGTH('dbo.Clientes', 'Tipo') AS ColunaTipoCliente,
    COL_LENGTH('dbo.Clientes', 'Cidade') AS ColunaCidade,
    COL_LENGTH('dbo.Mensagens', 'MensagemRespondidaId') AS ColunaMensagemRespondida;
GO

PRINT 'Migracao 004 concluida sem apagar dados.';
GO
