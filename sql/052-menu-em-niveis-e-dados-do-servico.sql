/* =============================================================================
   CRM-Cartorio — Menu de serviços em níveis e sequência de dados do serviço
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-29
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql

   O QUE FAZ
       TiposServico
         1. Ordem INT NOT NULL DEFAULT (0)            posição no menu do WhatsApp
         2. ServicoPaiId UNIQUEIDENTIFIER NULL        submenu (FK para TiposServico)
         3. MensagemAoEscolher NVARCHAR(4000) NULL    texto próprio ao ser escolhido
         4. MensagemAposDados NVARCHAR(4000) NULL     texto depois dos dados (ex.: Pix)
       Conversas
         5. ServicoDoSubmenuId UNIQUEIDENTIFIER NULL  submenu no ar (FK para TiposServico)
         6. AguardandoDadosDoServico BIT NOT NULL DEFAULT (0)
         7. AguardandoComprovante BIT NOT NULL DEFAULT (0)

   O PROBLEMA QUE ISTO RESOLVE
       Pedido do cartório parceiro em 2026-09-28: o menu do Registro Civil na ordem dele (não
       alfabética), "Segunda via de certidão" abrindo um submenu com nascimento,
       casamento e óbito, e cada certidão pedindo os dados do cliente e depois
       respondendo com a chave Pix, até o comprovante passar o atendimento para
       uma pessoa. Até aqui o menu só tinha dois níveis, saía em ordem
       alfabética e todo serviço respondia com o mesmo texto.

   SEGURO COM O BINARIO ANTERIOR RODANDO
       Tudo aditivo. As colunas BIT e a Ordem nascem com DEFAULT, então todo INSERT
       do binário anterior (que não as conhece) grava o estado de sempre: ordem
       zero (= alfabética), sem submenu, sem espera. As colunas NULL ficam nulas.
       As FKs só olham linhas com valor, que o binário anterior nunca grava. Ver
       deploy/migration-compatibility.json.

   IDEMPOTENCIA
       Cada coluna, FK e índice só entra se ainda não existir.

   MULTIEMPRESA
       Os bancos dos cartórios contratados pelo site (CRM_Tenant_*) recebem a mesma
       mudança por sql/saas/tenant/002-menu-em-niveis-e-dados-do-servico.sql,
       aplicado pelo provisionador (--atualizar-esquema-dos-cartorios).
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF DB_NAME() NOT IN (N'CRM_Cartorio', N'CRM_Cartorio_Teste', N'CRM_Cartorio_Dev', N'CRM_Cartorio_Demo')
BEGIN
    RAISERROR('Recusado: este script so roda em CRM_Cartorio, CRM_Cartorio_Teste ou CRM_Cartorio_Dev. Confira o banco selecionado.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.TiposServico', N'U') IS NULL OR OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- Colunas (cada uma no próprio lote: o nome só existe a partir do próximo GO) */

IF COL_LENGTH(N'dbo.TiposServico', N'Ordem') IS NULL
    ALTER TABLE dbo.TiposServico ADD Ordem INT NOT NULL CONSTRAINT DF_TiposServico_Ordem DEFAULT (0);
GO
IF COL_LENGTH(N'dbo.TiposServico', N'ServicoPaiId') IS NULL
    ALTER TABLE dbo.TiposServico ADD ServicoPaiId UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH(N'dbo.TiposServico', N'MensagemAoEscolher') IS NULL
    ALTER TABLE dbo.TiposServico ADD MensagemAoEscolher NVARCHAR(4000) NULL;
GO
IF COL_LENGTH(N'dbo.TiposServico', N'MensagemAposDados') IS NULL
    ALTER TABLE dbo.TiposServico ADD MensagemAposDados NVARCHAR(4000) NULL;
GO
IF COL_LENGTH(N'dbo.Conversas', N'ServicoDoSubmenuId') IS NULL
    ALTER TABLE dbo.Conversas ADD ServicoDoSubmenuId UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH(N'dbo.Conversas', N'AguardandoDadosDoServico') IS NULL
    ALTER TABLE dbo.Conversas ADD AguardandoDadosDoServico BIT NOT NULL
        CONSTRAINT DF_Conversas_AguardandoDadosDoServico DEFAULT (0);
GO
IF COL_LENGTH(N'dbo.Conversas', N'AguardandoComprovante') IS NULL
    ALTER TABLE dbo.Conversas ADD AguardandoComprovante BIT NOT NULL
        CONSTRAINT DF_Conversas_AguardandoComprovante DEFAULT (0);
GO

/* --- Chaves estrangeiras e índices ------------------------------------------ */

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TiposServico_TiposServico_ServicoPaiId')
    ALTER TABLE dbo.TiposServico WITH CHECK ADD CONSTRAINT FK_TiposServico_TiposServico_ServicoPaiId
        FOREIGN KEY (ServicoPaiId) REFERENCES dbo.TiposServico (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TiposServico_ServicoPaiId' AND object_id = OBJECT_ID(N'dbo.TiposServico'))
    CREATE INDEX IX_TiposServico_ServicoPaiId ON dbo.TiposServico (ServicoPaiId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Conversas_TiposServico_ServicoDoSubmenuId')
    ALTER TABLE dbo.Conversas WITH CHECK ADD CONSTRAINT FK_Conversas_TiposServico_ServicoDoSubmenuId
        FOREIGN KEY (ServicoDoSubmenuId) REFERENCES dbo.TiposServico (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Conversas_ServicoDoSubmenuId' AND object_id = OBJECT_ID(N'dbo.Conversas'))
    CREATE INDEX IX_Conversas_ServicoDoSubmenuId ON dbo.Conversas (ServicoDoSubmenuId);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'052-menu-em-niveis-e-dados-do-servico.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'052-menu-em-niveis-e-dados-do-servico.sql',
                N'Menu de servicos em niveis: TiposServico.Ordem, ServicoPaiId, MensagemAoEscolher, MensagemAposDados; Conversas.ServicoDoSubmenuId, AguardandoDadosDoServico, AguardandoComprovante. Aditiva, sem backfill.');
END
GO

COMMIT TRANSACTION;
GO

SELECT OBJECT_NAME(c.object_id) AS Tabela, c.name AS Coluna, t.name AS Tipo, c.is_nullable AS AceitaNulo
  FROM sys.columns c
  JOIN sys.types t ON t.user_type_id = c.user_type_id
 WHERE (c.object_id = OBJECT_ID(N'dbo.TiposServico') AND c.name IN (N'Ordem', N'ServicoPaiId', N'MensagemAoEscolher', N'MensagemAposDados'))
    OR (c.object_id = OBJECT_ID(N'dbo.Conversas') AND c.name IN (N'ServicoDoSubmenuId', N'AguardandoDadosDoServico', N'AguardandoComprovante'));
GO

PRINT 'Migracao 052 concluida.';
GO
