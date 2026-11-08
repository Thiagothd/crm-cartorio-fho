/* =============================================================================
   CRM-Cartorio — Foto de perfil do WhatsApp no cadastro do Cliente
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-18
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql

   O QUE FAZ
       Adiciona as colunas FotoPerfilUrl e FotoPerfilAtualizadaEm na tabela Clientes:
         - FotoPerfilUrl: URL direta da foto de perfil no WhatsApp (ou nulo se sem foto)
         - FotoPerfilAtualizadaEm: instante UTC da ultima consulta/atualizacao

   POR QUE ISSO EXISTE
       Permite exibir fotos reais de perfil dos clientes nos avatares da lista de
       atendimentos e no cabecalho da conversa ativa, reduzindo consultas repetidas
       a API do WhatsApp (cache com TTL de 24h).

   IDEMPOTENCIA
       Colunas adicionadas somente se ainda nao existirem.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT IN (N'CRM_Cartorio', N'CRM_Cartorio_Teste', N'CRM_Cartorio_Dev', N'CRM_Cartorio_Demo')
BEGIN
    RAISERROR('Recusado: este script so roda em CRM_Cartorio, CRM_Cartorio_Teste ou CRM_Cartorio_Dev. Confira o banco selecionado.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Clientes', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'dbo.Clientes', N'FotoPerfilUrl') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes ADD FotoPerfilUrl NVARCHAR(500) NULL;
END
GO

IF COL_LENGTH(N'dbo.Clientes', N'FotoPerfilAtualizadaEm') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes ADD FotoPerfilAtualizadaEm DATETIME2(3) NULL;
END
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'022-foto-perfil-cliente.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'022-foto-perfil-cliente.sql', N'Adiciona FotoPerfilUrl e FotoPerfilAtualizadaEm na tabela Clientes.');
END
GO

COMMIT TRANSACTION;
GO

PRINT 'Migracao 022 concluida.';
GO
