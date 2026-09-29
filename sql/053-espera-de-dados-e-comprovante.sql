/* =============================================================================
   CRM-Cartorio — Espera dos dados do serviço e texto depois do comprovante
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-29
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 052-menu-em-niveis-e-dados-do-servico.sql

   MULTIEMPRESA
       Os bancos dos cartórios contratados pelo site (CRM_Tenant_*) recebem a mesma
       mudança por sql/saas/tenant/003-espera-de-dados-e-comprovante.sql, aplicado
       pelo provisionador (--atualizar-esquema-dos-cartorios).

   O QUE FAZ
       Conversas
         1. DadosDoServicoRecebidosEm DATETIME2(3) NULL   quando os dados começaram a chegar
         2. IX_Conversas_EsperaDeDados (TenantId, DadosDoServicoRecebidosEm)
            filtrado por DadosDoServicoRecebidosEm IS NOT NULL
       TiposServico
         3. MensagemAposComprovante NVARCHAR(MAX) NULL    texto ao receber o comprovante
         4. MensagemAoEscolher e MensagemAposDados passam de NVARCHAR(4000) para
            NVARCHAR(MAX): o texto pode vir em várias mensagens, separadas por uma
            linha "---", cada uma até 4000 (teto validado no cadastro, 12000 no total)

   O PROBLEMA QUE ISTO RESOLVE
       Teste ao vivo em 2026-09-29 do menu do cartório parceiro: o robô respondia com a chave
       Pix à PRIMEIRA mensagem de dados. Quem manda o nome numa mensagem e o CPF em
       outra recebia o Pix no meio da digitação, e o CPF era tomado como comprovante.
       Agora a primeira mensagem só marca DadosDoServicoRecebidosEm, e uma varredura
       manda o Pix quando o cliente para de digitar. E o aviso depois do comprovante,
       que era o geral de pedido de atendente, passa a poder ter texto próprio.
       O texto do cartório parceiro para habilitação de casamento tem 4217 caracteres: não cabe
       numa mensagem do WhatsApp (4096) nem na coluna antiga, e sai em duas partes.

   SEGURO COM O BINARIO ANTERIOR RODANDO
       As colunas novas são anuláveis e sem DEFAULT: o binário anterior não as
       conhece, omite nos INSERT e elas ficam nulas. O índice é filtrado e nasce
       vazio. O alargamento para NVARCHAR(MAX) só aumenta o que a coluna aceita: o
       binário anterior grava no máximo 4000, e isso continua cabendo. Ver
       deploy/migration-compatibility.json.

   IDEMPOTENCIA
       Cada coluna e o índice só entram se ainda não existirem.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF DB_NAME() NOT IN (N'CRM_Cartorio', N'CRM_Cartorio_Teste', N'CRM_Cartorio_Dev')
BEGIN
    RAISERROR('Recusado: este script so roda em CRM_Cartorio, CRM_Cartorio_Teste ou CRM_Cartorio_Dev. Confira o banco selecionado.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.TiposServico', N'U') IS NULL
   OR COL_LENGTH(N'dbo.Conversas', N'AguardandoDadosDoServico') IS NULL
BEGIN
    RAISERROR('Schema anterior ausente. Rode antes a migracao do menu em niveis (052).', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF COL_LENGTH(N'dbo.Conversas', N'DadosDoServicoRecebidosEm') IS NULL
    ALTER TABLE dbo.Conversas ADD DadosDoServicoRecebidosEm DATETIME2(3) NULL;
GO
IF COL_LENGTH(N'dbo.TiposServico', N'MensagemAposComprovante') IS NULL
    ALTER TABLE dbo.TiposServico ADD MensagemAposComprovante NVARCHAR(MAX) NULL;
GO
IF COL_LENGTH(N'dbo.TiposServico', N'MensagemAoEscolher') <> -1
    ALTER TABLE dbo.TiposServico ALTER COLUMN MensagemAoEscolher NVARCHAR(MAX) NULL;
GO
IF COL_LENGTH(N'dbo.TiposServico', N'MensagemAposDados') <> -1
    ALTER TABLE dbo.TiposServico ALTER COLUMN MensagemAposDados NVARCHAR(MAX) NULL;
GO
IF COL_LENGTH(N'dbo.TiposServico', N'MensagemAposComprovante') <> -1
    ALTER TABLE dbo.TiposServico ALTER COLUMN MensagemAposComprovante NVARCHAR(MAX) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Conversas_EsperaDeDados' AND object_id = OBJECT_ID(N'dbo.Conversas'))
    CREATE INDEX IX_Conversas_EsperaDeDados ON dbo.Conversas (TenantId, DadosDoServicoRecebidosEm)
        WHERE DadosDoServicoRecebidosEm IS NOT NULL;
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'053-espera-de-dados-e-comprovante.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'053-espera-de-dados-e-comprovante.sql',
                N'Conversas.DadosDoServicoRecebidosEm + IX_Conversas_EsperaDeDados (filtrado); TiposServico.MensagemAposComprovante; textos do servico em NVARCHAR(MAX). Aditiva, sem backfill.');
END
GO

COMMIT TRANSACTION;
GO

SELECT OBJECT_NAME(c.object_id) AS Tabela, c.name AS Coluna, t.name AS Tipo, c.max_length AS Tamanho, c.is_nullable AS AceitaNulo
  FROM sys.columns c
  JOIN sys.types t ON t.user_type_id = c.user_type_id
 WHERE (c.object_id = OBJECT_ID(N'dbo.Conversas') AND c.name = N'DadosDoServicoRecebidosEm')
    OR (c.object_id = OBJECT_ID(N'dbo.TiposServico') AND c.name IN (N'MensagemAoEscolher', N'MensagemAposDados', N'MensagemAposComprovante'));
GO

PRINT 'Migracao 053 concluida.';
GO
