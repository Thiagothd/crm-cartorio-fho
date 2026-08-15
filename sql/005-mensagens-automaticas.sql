/* =============================================================================
   CRM-Cartorio — Mensagens automaticas e respostas rapidas
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-14
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql

   O QUE FAZ
       1. Cria dbo.MensagensAutomaticas — modelos de mensagem que o sistema envia
          sozinho quando um evento do atendimento acontece.
       2. Cria dbo.RespostasRapidas — textos prontos que o atendente insere no
          campo de resposta digitando "/atalho".
       3. Amplia dbo.Conversas com a flag de triagem e os dois marcadores de aviso
          de prazo.
       4. Amplia dbo.Mensagens com EhAutomatica, que separa a anotacao interna do
          fio da mensagem endereçada ao cliente.
       5. Insere os cadastros de demonstracao, iguais aos de
          Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs.

   O QUE NAO FAZ
       Nao apaga nem reescreve conversa, mensagem ou cadastro existente. As colunas
       novas nascem com o valor neutro: nenhuma conversa antiga passa a aguardar
       triagem e nenhuma mensagem antiga vira automatica.

   IDEMPOTENCIA
       Tabela, coluna, constraint, indice e linha de cadastro so sao criados quando
       ainda nao existem. Rodar de novo nao duplica nem altera nada.

   ATENCAO — OS TEXTOS SAO RASCUNHO
       As cinco mensagens automaticas e as quatro respostas rapidas inseridas aqui
       existem para o cartório parceiro ver o mecanismo funcionando e dizer como o cartorio
       realmente fala com o cliente. Nao sao texto aprovado
       (docs/REQUISITOS.md, secao 5.7).

       Duas nascem DESATIVADAS de proposito: o aviso de prazo perto de vencer e o
       de prazo vencido. Avisar o cliente de que o cartorio estourou o proprio
       prazo e decisao de postura comercial, nao detalhe tecnico — o cartório parceiro liga na
       tela quando decidir.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Exigidas pelo SQL Server para criar os indices filtrados da varredura de prazo.
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

IF OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL
   OR OBJECT_ID(N'dbo.EtapasFunil', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* -----------------------------------------------------------------------------
   1. MensagensAutomaticas — modelos disparados por evento
   -----------------------------------------------------------------------------
   Gatilho: 1 NovoAtendimento, 2 EtapaAlterada, 3 PrazoProximoVencimento,
            4 PrazoVencido, 5 TriagemSemCorrespondencia

   O texto NAO mora em codigo. Nenhuma frase que o cliente final le deveria exigir
   release para mudar.
   -------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MensagensAutomaticas
    (
        Id           UNIQUEIDENTIFIER NOT NULL,
        TenantId     UNIQUEIDENTIFIER NOT NULL,
        Gatilho      TINYINT          NOT NULL,

        -- Obrigatorio no gatilho 2, nulo em todos os outros. Ver o CHECK abaixo.
        EtapaAlvoId  UNIQUEIDENTIFIER NULL,

        -- 1000, e nao os 4000 de Mensagens.Conteudo: o que fica aqui e o MODELO, e
        -- os marcadores crescem na substituicao. A folga evita que a mensagem
        -- renderizada estoure a coluna de destino.
        Conteudo     NVARCHAR(1000)   NOT NULL,

        Ativo        BIT              NOT NULL CONSTRAINT DF_MensagensAutomaticas_Ativo DEFAULT (1),
        CriadoEm     DATETIME2(3)     NOT NULL,
        AtualizadoEm DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_MensagensAutomaticas PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_MensagensAutomaticas_Tenants
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_MensagensAutomaticas_EtapasFunil
            FOREIGN KEY (EtapaAlvoId) REFERENCES dbo.EtapasFunil (Id),
        CONSTRAINT CK_MensagensAutomaticas_Gatilho CHECK (Gatilho IN (1, 2, 3, 4, 5)),

        -- Gatilho de etapa SEM etapa alvo dispararia mensagem ao cliente em toda
        -- movimentacao interna do funil; etapa alvo em gatilho que nao e de etapa e
        -- dado morto que engana quem le a tabela.
        CONSTRAINT CK_MensagensAutomaticas_EtapaAlvo CHECK
        (
            (Gatilho  = 2 AND EtapaAlvoId IS NOT NULL) OR
            (Gatilho <> 2 AND EtapaAlvoId IS NULL)
        )
    );

    -- Consulta do disparador: as regras ativas de um gatilho, num tenant.
    CREATE INDEX IX_MensagensAutomaticas_Tenant_Gatilho
        ON dbo.MensagensAutomaticas (TenantId, Gatilho, Ativo);
END
GO

/* -----------------------------------------------------------------------------
   2. RespostasRapidas — insercao assistida, nunca envio automatico
   -----------------------------------------------------------------------------
   Nada sai daqui sozinho: o atalho preenche o campo, o atendente le, edita se
   quiser e envia. E a diferenca de responsabilidade em relacao a tabela acima —
   aquela o sistema assina, esta o atendente assina.
   -------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.RespostasRapidas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RespostasRapidas
    (
        Id           UNIQUEIDENTIFIER NOT NULL,
        TenantId     UNIQUEIDENTIFIER NOT NULL,

        -- Sem a barra, em minusculas e sem espaco. VARCHAR porque o atendente
        -- digita isso no meio do atendimento e nao deveria precisar de acento.
        Atalho       VARCHAR(30)      NOT NULL,

        Titulo       NVARCHAR(120)    NOT NULL,
        Conteudo     NVARCHAR(1000)   NOT NULL,
        Ativo        BIT              NOT NULL CONSTRAINT DF_RespostasRapidas_Ativo DEFAULT (1),
        CriadoEm     DATETIME2(3)     NOT NULL,
        AtualizadoEm DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_RespostasRapidas PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_RespostasRapidas_Tenants
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id)
    );

    -- Dois atalhos iguais deixariam o seletor ambiguo: o atendente digita "/prazo",
    -- ve duas linhas identicas e nao tem como escolher a certa.
    CREATE UNIQUE INDEX UQ_RespostasRapidas_Tenant_Atalho
        ON dbo.RespostasRapidas (TenantId, Atalho);
END
GO

/* -----------------------------------------------------------------------------
   3. Conversas — triagem e marcadores de aviso de prazo
   -----------------------------------------------------------------------------
   Os dois marcadores NAO contradizem a regra de nao persistir situacao de prazo: o
   que fica gravado nao e "esta em alerta" — que envelhece e mentiria entre duas
   leituras — e sim "o aviso saiu", que e um fato datado e nao muda mais. E o que
   torna o disparo idempotente: a varredura roda a cada poucos minutos e
   reencontraria a mesma conversa em alerta a cada volta.
   -------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Conversas', 'AguardandoTriagem') IS NULL
    ALTER TABLE dbo.Conversas ADD AguardandoTriagem BIT NOT NULL
        CONSTRAINT DF_Conversas_AguardandoTriagem DEFAULT (0) WITH VALUES;

IF COL_LENGTH('dbo.Conversas', 'AvisoDeAlertaEnviadoEm') IS NULL
    ALTER TABLE dbo.Conversas ADD AvisoDeAlertaEnviadoEm DATETIME2(3) NULL;

IF COL_LENGTH('dbo.Conversas', 'AvisoDeVencimentoEnviadoEm') IS NULL
    ALTER TABLE dbo.Conversas ADD AvisoDeVencimentoEnviadoEm DATETIME2(3) NULL;
GO

/* Indices FILTRADOS: em regime, quase toda conversa antiga ja avisou — indexar
   essas seria carregar peso morto na varredura. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Conversas') AND name = 'IX_Conversas_AvisoAlertaPendente')
    CREATE INDEX IX_Conversas_AvisoAlertaPendente
        ON dbo.Conversas (TenantId, PrazoAlertaEm)
        WHERE AvisoDeAlertaEnviadoEm IS NULL AND PrazoLimite IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Conversas') AND name = 'IX_Conversas_AvisoVencimentoPendente')
    CREATE INDEX IX_Conversas_AvisoVencimentoPendente
        ON dbo.Conversas (TenantId, PrazoLimite)
        WHERE AvisoDeVencimentoEnviadoEm IS NULL AND PrazoLimite IS NOT NULL;
GO

/* -----------------------------------------------------------------------------
   4. Mensagens — EhAutomatica
   -----------------------------------------------------------------------------
   Combinada com AutorTipo = 3 (Sistema), separa duas coisas que sem ela ficariam
   iguais no banco:

       Sistema + 0 .... anotacao interna do fio ("Conversa assumida por Fulano").
                        O cliente nunca receberia isso.
       Sistema + 1 .... mensagem endereçada AO CLIENTE, que sai pelo WhatsApp
                        quando a Fase 2 conectar.

   Por isso a interface as desenha diferente, e por isso o atendente precisa do
   rotulo "Automatica": sem ele, ele acharia que um colega escreveu aquilo.
   -------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Mensagens', 'EhAutomatica') IS NULL
    ALTER TABLE dbo.Mensagens ADD EhAutomatica BIT NOT NULL
        CONSTRAINT DF_Mensagens_EhAutomatica DEFAULT (0) WITH VALUES;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Mensagens_AutomaticaEhDoSistema')
    ALTER TABLE dbo.Mensagens WITH CHECK ADD CONSTRAINT CK_Mensagens_AutomaticaEhDoSistema
        CHECK ((EhAutomatica = 0) OR (EhAutomatica = 1 AND AutorTipo = 3));
GO

/* -----------------------------------------------------------------------------
   5. Cadastros de demonstracao
   -----------------------------------------------------------------------------
   Os GUIDs sao os mesmos de
   Infrastructure/Persistence/Seeding/IdentificadoresDeDemonstracao.cs e o texto e
   o mesmo de CatalogoDeDemonstracao.cs. Se mudar aqui, mude la tambem.

   Sem acento, como o resto do seed, para que as duas versoes batam caractere a
   caractere. Na tela o campo aceita acento normalmente — a coluna e NVARCHAR.
   -------------------------------------------------------------------------- */
DECLARE @agora    DATETIME2(3)     = SYSUTCDATETIME();
DECLARE @tenantId UNIQUEIDENTIFIER = '0199B2C0-0000-7000-8000-000000000001';
DECLARE @etapaProntoEntrega UNIQUEIDENTIFIER = '0199B2C0-0002-7000-8000-000000000004';
DECLARE @lf CHAR(1) = CHAR(10);

IF EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @tenantId)
   AND NOT EXISTS (SELECT 1 FROM dbo.MensagensAutomaticas WHERE TenantId = @tenantId)
BEGIN
    INSERT INTO dbo.MensagensAutomaticas
        (Id, TenantId, Gatilho, EtapaAlvoId, Conteudo, Ativo, CriadoEm, AtualizadoEm)
    VALUES
        -- 1. Boas-vindas do primeiro contato, com o menu de triagem.
        --    {MenuSetores} e substituido pelos setores ATIVOS na hora do disparo:
        --    setor adicionado ou desativado depois muda o menu sozinho.
        ('0199B2C0-0006-7000-8000-000000000001', @tenantId, 1, NULL,
         N'Ola, {NomeCliente}! Aqui e o {NomeCartorio}.' + @lf +
         N'Para adiantar seu atendimento, responda com o numero do assunto:' + @lf + @lf +
         N'{MenuSetores}' + @lf + @lf +
         N'Se preferir, escreva sua duvida que um atendente assume a conversa.',
         1, @agora, @agora),

        -- 2. Fallback da triagem. UMA tentativa e o suficiente: repetir o menu ate o
        --    cliente acertar prenderia quem escreveu "bom dia" num loop de robo.
        ('0199B2C0-0006-7000-8000-000000000002', @tenantId, 5, NULL,
         N'Certo, {NomeCliente}. Vou te encaminhar para um de nossos atendentes. ' +
         N'Seu protocolo e {Protocolo} — guarde esse numero para acompanhar o atendimento.',
         1, @agora, @agora),

        -- 3. Aviso de conclusao, preso a etapa final POR ID. Se o cartório parceiro renomear ou
        --    reordenar o funil, a regra continua valendo.
        ('0199B2C0-0006-7000-8000-000000000003', @tenantId, 2, @etapaProntoEntrega,
         N'Boa noticia, {NomeCliente}! O atendimento de protocolo {Protocolo} esta concluido.' + @lf +
         N'Servico: {Servico}.' + @lf +
         N'Ja pode retirar no balcao do {NomeCartorio}, de segunda a sexta, das 8h as 17h.',
         1, @agora, @agora),

        -- 4. Prazo perto de vencer. DESATIVADA — aguarda decisao do cartório parceiro.
        ('0199B2C0-0006-7000-8000-000000000004', @tenantId, 3, NULL,
         N'{NomeCliente}, passando para avisar que seu atendimento de protocolo {Protocolo} ' +
         N'({Servico}) esta em andamento e proximo do prazo previsto. ' +
         N'Qualquer duvida, e so responder por aqui.',
         0, @agora, @agora),

        -- 5. Prazo vencido. DESATIVADA — aguarda decisao do cartório parceiro.
        ('0199B2C0-0006-7000-8000-000000000005', @tenantId, 4, NULL,
         N'{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) passou do prazo ' +
         N'previsto e esta com prioridade na nossa fila. Assim que houver novidade, aviso por aqui.',
         0, @agora, @agora);
END

IF EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @tenantId)
   AND NOT EXISTS (SELECT 1 FROM dbo.RespostasRapidas WHERE TenantId = @tenantId)
BEGIN
    INSERT INTO dbo.RespostasRapidas
        (Id, TenantId, Atalho, Titulo, Conteudo, Ativo, CriadoEm, AtualizadoEm)
    VALUES
        ('0199B2C0-0007-7000-8000-000000000001', @tenantId, 'horario', N'Horario e endereco',
         N'O {NomeCartorio} atende de segunda a sexta, das 8h as 17h, ' +
         N'na Praca Sao Francisco, 70, Centro, Gameleiras/MG.',
         1, @agora, @agora),

        ('0199B2C0-0007-7000-8000-000000000002', @tenantId, 'prazo', N'Situacao do protocolo',
         N'Seu atendimento esta registrado sob o protocolo {Protocolo} ({Servico}). ' +
         N'Assim que ficar pronto, aviso por aqui.',
         1, @agora, @agora),

        ('0199B2C0-0007-7000-8000-000000000003', @tenantId, 'valor', N'Emolumentos',
         N'Os valores dos atos seguem a tabela de emolumentos do TJMG, definida por lei ' +
         N'estadual — nao e preco do cartorio. Vou levantar o valor exato do seu caso e te retorno.',
         1, @agora, @agora),

        ('0199B2C0-0007-7000-8000-000000000004', @tenantId, 'firma', N'Reconhecimento de firma',
         N'Para reconhecer firma, sua ficha de assinatura precisa estar aberta neste cartorio. ' +
         N'Se ainda nao tiver, traga documento de identidade original com foto para abrirmos na hora.',
         1, @agora, @agora);
END
GO

COMMIT TRANSACTION;
GO

SELECT
    (SELECT COUNT(*) FROM dbo.MensagensAutomaticas)             AS MensagensAutomaticas,
    (SELECT COUNT(*) FROM dbo.MensagensAutomaticas WHERE Ativo = 1) AS Ativas,
    (SELECT COUNT(*) FROM dbo.RespostasRapidas)                 AS RespostasRapidas,
    COL_LENGTH('dbo.Conversas', 'AguardandoTriagem')            AS ColunaTriagem,
    COL_LENGTH('dbo.Mensagens', 'EhAutomatica')                 AS ColunaAutomatica;
GO

PRINT 'Migracao 005 concluida sem apagar dados.';
PRINT 'ATENCAO: os textos inseridos sao RASCUNHO e aguardam a redacao do cartório parceiro.';
PRINT 'Os avisos de prazo (perto de vencer e vencido) nascem DESATIVADOS.';
GO
