/* =============================================================================
   CRM-Cartorio — Cifra em repouso (blindagem LGPD, PR3)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-12
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql,
                   045-registros-de-acesso.sql, 046-acesso-tecnico.sql

   O QUE FAZ
       1) Alarga para NVARCHAR(MAX) as colunas que passam a ser cifradas em
          repouso: Mensagens.Conteudo/ArquivoNome, HistoricoEtapas.Observacao,
          Conversas.MotivoEncerramento, e um conjunto de campos sensiveis de
          Clientes (documento, RG, filiacao, endereco, contato alternativo,
          observacoes). O texto em claro cabia em colunas mais estreitas; o
          blob cifrado ($c1$ + base64 de nonce+tag+ciphertext) nao cabe mais
          no limite antigo.
       2) Remove IX_Clientes_Tenant_Documento — SQL Server nao aceita
          NVARCHAR(MAX) como coluna-chave de indice, e Documento precisou
          alargar para caber o ciphertext.
       3) Adiciona Clientes.DocumentoHash (CHAR(64), indice cego HMAC do
          documento, so digitos) + indice — sustenta busca EXATA por
          CPF/CNPJ quando a cifra estiver ativa, sem indexar o documento em
          claro.
       4) Adiciona Clientes.DataNascimentoCifrada (NVARCHAR(MAX)) — coluna
          NOVA, ao lado da DataNascimento (date) existente, que fica intocada
          e orfa (ninguem mais le/grava nela a partir desta migration; limpeza
          fica para uma migration futura, so depois de confirmado que nao
          sobrou leitor). Nao dava para so mudar o TIPO da coluna existente:
          o binario anterior (N-1), se ainda estiver rodando durante o
          deploy, le DataNascimento como DATE — virar texto quebraria essa
          leitura no meio da troca.
       Nenhuma linha existente e apagada. Nenhum dado em claro vira cifrado
       aqui — isto so prepara o ESPACO da coluna; quem de fato cifra e o
       comando --cifrar-dados-legados (FerramentaDeCifraDeDadosLegados.cs),
       rodado manualmente DEPOIS que a chave (ProtecaoDeDados:Chaves/
       ChaveDeIndice) for provisionada em appsettings.<Ambiente>.json por
       quem tem acesso direto ao servidor.

   POR QUE ESTA MIGRATION E REALMENTE NECESSARIA (nao so codigo)
       Mensagens.Conteudo e NVARCHAR(4000) hoje; um blob cifrado de uma
       mensagem longa nao cabe ali. DocumentoHash e coluna nova — o proprio
       banco recusa fisicamente ate ela existir.

   O PROBLEMA QUE ISTO RESOLVE
       Ate esta migration, o conteudo da conversa e o dado sensivel da ficha
       do cliente ficam em texto puro no banco, no backup e (quando aplicavel)
       no disco do anexo. Isto prepara o schema para a cifra AES-256-GCM em
       repouso (Infrastructure/Security/ICifraDeDados) — ver
       docs/PROTECAO-DE-DADOS.md.

   POR QUE NAO PRECISA DE BACKFILL AQUI
       Alargar coluna nao muda o conteudo das linhas existentes. O backfill
       de verdade (cifrar o que ja esta em claro) e o --cifrar-dados-legados,
       deliberadamente FORA desta migration — so roda depois que a chave
       existe, e essa chave nunca pode estar num arquivo .sql versionado.

   COMPATIBILIDADE DE DEPLOY
       Expand-only para o binario N-1 (antes desta entrega): ALTER COLUMN
       para NVARCHAR(MAX) so amplia o que a coluna aceita — todo INSERT/UPDATE
       que N-1 gera continua cabendo, e toda leitura de N-1 continua
       funcionando (Contains/== sobre string nao se importa com o teto
       declarado). Perder o indice de Documento so torna a busca de N-1 por
       documento um table scan em vez de um seek — mais lento, nunca errado,
       e o volume de clientes de um cartorio nao chega perto de doer. As duas
       colunas novas (DocumentoHash, DataNascimentoCifrada) ficam inertes
       para N-1 (ele nao as conhece). EF Core nao valida schema no boot.
       Rollback automatico do binario para N-1 e seguro: nenhuma coluna foi
       removida, nenhum tipo foi estreitado, nenhuma linha foi apagada.
       Direcao da incompatibilidade e forward-only: o binario NOVO
       (CrmDbContext com os ValueConverter de cifra) e quem depende deste
       schema; nao pode subir antes desta migration.

   IDEMPOTENCIA
       Todo ALTER COLUMN e guardado por checagem do tipo/tamanho atual em
       sys.columns; toda coluna nova, por COL_LENGTH; o indice novo, por
       sys.indexes. Rodar de novo e no-op.
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

IF OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL
   OR OBJECT_ID(N'dbo.HistoricoEtapas', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Clientes', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* -----------------------------------------------------------------------------
   1. Mensagens — Conteudo (NOT NULL) e ArquivoNome (NULL)
   -------------------------------------------------------------------------- */
IF EXISTS (
    SELECT 1 FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'dbo.Mensagens') AND c.name = N'Conteudo' AND c.max_length <> -1
)
    ALTER TABLE dbo.Mensagens ALTER COLUMN Conteudo NVARCHAR(MAX) NOT NULL;
GO

-- CK_Mensagens_Arquivo referencia ArquivoNome — SQL Server recusa ALTER COLUMN
-- em coluna presa a CHECK constraint. Derruba e recria em volta do ALTER,
-- com a MESMA definicao de MensagemConfiguration.cs (nao muda a regra, so
-- destrava a largura da coluna).
IF EXISTS (
    SELECT 1 FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'dbo.Mensagens') AND c.name = N'ArquivoNome' AND c.max_length <> -1
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Mensagens_Arquivo' AND parent_object_id = OBJECT_ID(N'dbo.Mensagens'))
        ALTER TABLE dbo.Mensagens DROP CONSTRAINT CK_Mensagens_Arquivo;

    ALTER TABLE dbo.Mensagens ALTER COLUMN ArquivoNome NVARCHAR(MAX) NULL;

    ALTER TABLE dbo.Mensagens ADD CONSTRAINT CK_Mensagens_Arquivo CHECK (
        ([ArquivoCaminho] IS NULL AND [ArquivoNome] IS NULL AND [ArquivoTipoMime] IS NULL AND [ArquivoTamanhoBytes] IS NULL)
        OR
        ([ArquivoCaminho] IS NOT NULL AND [ArquivoNome] IS NOT NULL AND [ArquivoTipoMime] IS NOT NULL AND [ArquivoTamanhoBytes] IS NOT NULL)
    );
END
GO

/* -----------------------------------------------------------------------------
   2. HistoricoEtapas.Observacao
   -------------------------------------------------------------------------- */
IF EXISTS (
    SELECT 1 FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'dbo.HistoricoEtapas') AND c.name = N'Observacao' AND c.max_length <> -1
)
    ALTER TABLE dbo.HistoricoEtapas ALTER COLUMN Observacao NVARCHAR(MAX) NULL;
GO

/* -----------------------------------------------------------------------------
   3. Conversas.MotivoEncerramento
   -------------------------------------------------------------------------- */
IF EXISTS (
    SELECT 1 FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'dbo.Conversas') AND c.name = N'MotivoEncerramento' AND c.max_length <> -1
)
    ALTER TABLE dbo.Conversas ALTER COLUMN MotivoEncerramento NVARCHAR(MAX) NULL;
GO

/* -----------------------------------------------------------------------------
   4. Clientes — indice antigo de Documento sai ANTES de alargar a coluna
      (SQL Server recusa NVARCHAR(MAX) como coluna-chave de indice).
   -------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Clientes_Tenant_Documento' AND object_id = OBJECT_ID(N'dbo.Clientes'))
    DROP INDEX IX_Clientes_Tenant_Documento ON dbo.Clientes;
GO

/* -----------------------------------------------------------------------------
   5. Clientes — colunas de texto que passam a ser cifradas
   -------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'TelefoneAlternativo' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN TelefoneAlternativo NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Email' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Email NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Documento' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Documento NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Rg' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Rg NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'OrgaoEmissor' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN OrgaoEmissor NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'UfOrgaoEmissor' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN UfOrgaoEmissor NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Profissao' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Profissao NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Nacionalidade' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Nacionalidade NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Naturalidade' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Naturalidade NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'NomeDoPai' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN NomeDoPai NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'NomeDaMae' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN NomeDaMae NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'RepresentanteLegal' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN RepresentanteLegal NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Cep' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Cep NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Logradouro' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Logradouro NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Numero' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Numero NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Complemento' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Complemento NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Bairro' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Bairro NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.Clientes') AND c.name = N'Observacoes' AND c.max_length <> -1)
    ALTER TABLE dbo.Clientes ALTER COLUMN Observacoes NVARCHAR(MAX) NULL;
GO

/* -----------------------------------------------------------------------------
   6. Clientes — colunas novas: indice cego do documento + data de nascimento
      cifrada (a DataNascimento "date" antiga fica intocada, orfa).
   -------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.Clientes', N'DocumentoHash') IS NULL
    ALTER TABLE dbo.Clientes ADD DocumentoHash CHAR(64) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Clientes_Tenant_DocumentoHash' AND object_id = OBJECT_ID(N'dbo.Clientes'))
    CREATE INDEX IX_Clientes_Tenant_DocumentoHash ON dbo.Clientes (TenantId, DocumentoHash);
GO

IF COL_LENGTH(N'dbo.Clientes', N'DataNascimentoCifrada') IS NULL
    ALTER TABLE dbo.Clientes ADD DataNascimentoCifrada NVARCHAR(MAX) NULL;
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'047-cifra-em-repouso.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'047-cifra-em-repouso.sql',
                N'Blindagem LGPD: alarga p/ NVARCHAR(MAX) as colunas cifradas (Mensagens.Conteudo/ArquivoNome, HistoricoEtapas.Observacao, Conversas.MotivoEncerramento, campos sensiveis de Clientes); remove IX_Clientes_Tenant_Documento; adiciona Clientes.DocumentoHash e DataNascimentoCifrada. Nao cifra nada ainda — isso e --cifrar-dados-legados, a parte, apos a chave ser provisionada.');
END
GO

COMMIT TRANSACTION;
GO

PRINT 'Migracao 047 concluida. Colunas alargadas/criadas; nenhum dado foi cifrado ainda (rode --cifrar-dados-legados depois de provisionar a chave).';
GO
