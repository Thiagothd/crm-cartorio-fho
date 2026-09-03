/* =============================================================================
   CRM-Cartorio — conhecimento aprovado da IA
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-02
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql

   O QUE FAZ
       dbo.ConhecimentosDeIa (tabela nova) — conteudo cadastrado por um humano
       com permissao de gerir cadastros, consumido por MontadorDeContexto no
       grounding da IA. dbo.HistoricoDeConhecimentosDeIa (tabela nova,
       append-only) — snapshot do registro tirado imediatamente ANTES de cada
       edicao, para o conteudo poder ser inspecionado e corrigido sem perder
       o que valia antes.

   O PROBLEMA QUE ISTO RESOLVE
       Ate aqui, o unico jeito de ensinar algo pontual a IA era o cadastro de
       RespostaRapida — solto, sem vinculo a servico/tema, sem vigencia e sem
       rastro de quem mudou o que. Este cadastro e a versao governada: quem
       criou, quem editou por ultimo, em que versao esta e por quanto tempo
       vale.

   COMO O CODIGO USA ISTO
       ConhecimentoDeIaService.CriarAsync/AtualizarAsync (novo, modulo
       RespostaInteligente) — AtualizarAsync grava em
       HistoricoDeConhecimentosDeIa o estado ANTES de aplicar a edicao, na
       mesma transacao, depois incrementa Versao e sobrescreve o registro
       vivo. GarantirPermissao() exige PodeGerirCadastros (mesmo grupo que ja
       mexe em Setor, Servico, Mensagem Automatica e Resposta Rapida).

       MontadorDeContexto.MontarAsync le dbo.ConhecimentosDeIa filtrando
       Ativo=1 e a janela de vigencia (VigenteDesde/VigenteAte, ambos
       opcionais) contra a data corrente, e inclui o resultado numa secao
       nova do texto de grounding da IA.

   POR QUE DUAS TABELAS E NAO RowVersion/timestamp
       RowVersion (usado em Conversa) resolve concorrencia — rejeitar uma
       escrita sobre dado desatualizado. Aqui o objetivo e outro: manter a
       versao ANTERIOR consultavel, nao so detectar conflito. Por isso a
       trilha e uma tabela apendice, no mesmo espirito de HistoricoEtapas
       (so cresce, nunca e editada nem apagada).

   POR QUE NAO PRECISA DE BACKFILL
       Tabelas novas, nascem vazias. Nenhum conhecimento existe ate o primeiro
       cadastro pela tela — o grounding da IA continua identico ate la (a
       consulta nova simplesmente nao encontra linhas).

   COMPATIBILIDADE DE DEPLOY
       Compativel com o binario ANTIGO rodando: ele ignora as tabelas novas
       por completo. O binario NOVO consulta dbo.ConhecimentosDeIa em
       MontadorDeContexto.MontarAsync — nao deve ser iniciado antes desta
       migration estar aplicada, mesmo padrao das migrations anteriores (ver
       docs/DEPLOY.md, secao 1.2).

   IDEMPOTENCIA
       As tabelas so sao criadas quando ainda nao existem. Rodar o script de
       novo nao encontra nada para fazer.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT IN (N'CRM_Cartorio', N'CRM_Cartorio_Teste', N'CRM_Cartorio_Dev')
BEGIN
    RAISERROR('Recusado: este script so roda em CRM_Cartorio, CRM_Cartorio_Teste ou CRM_Cartorio_Dev. Confira o banco selecionado.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL OR OBJECT_ID(N'dbo.Atendentes', N'U') IS NULL
    OR OBJECT_ID(N'dbo.TiposServico', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'dbo.ConhecimentosDeIa', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ConhecimentosDeIa
    (
        Id                       UNIQUEIDENTIFIER NOT NULL,
        TenantId                 UNIQUEIDENTIFIER NOT NULL,

        Titulo                   NVARCHAR(160)    NOT NULL,
        Conteudo                 NVARCHAR(2000)   NOT NULL,

        -- Categoria livre. Usada quando o conhecimento nao e de um servico
        -- especifico do catalogo (ex.: "Politica de reagendamento").
        Tema                     NVARCHAR(80)     NOT NULL,

        -- Vinculo opcional a um servico do catalogo.
        TipoServicoId            UNIQUEIDENTIFIER NULL,

        -- Ambas opcionais. Nulo em VigenteDesde = vale desde sempre; nulo em
        -- VigenteAte = vale para sempre.
        VigenteDesde             DATE             NULL,
        VigenteAte               DATE             NULL,

        Versao                   INT              NOT NULL CONSTRAINT DF_ConhecimentosDeIa_Versao DEFAULT (1),

        -- Nulo quando o registro veio de uma migracao retroativa.
        CriadoPorAtendenteId     UNIQUEIDENTIFIER NULL,
        -- Nulo enquanto o registro nunca foi editado.
        AtualizadoPorAtendenteId UNIQUEIDENTIFIER NULL,

        Ativo                    BIT              NOT NULL CONSTRAINT DF_ConhecimentosDeIa_Ativo DEFAULT (1),
        CriadoEm                 DATETIME2(3)     NOT NULL,
        AtualizadoEm             DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_ConhecimentosDeIa PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ConhecimentosDeIa_Tenants
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_ConhecimentosDeIa_TiposServico
            FOREIGN KEY (TipoServicoId) REFERENCES dbo.TiposServico (Id),
        CONSTRAINT FK_ConhecimentosDeIa_Atendentes_CriadoPor
            FOREIGN KEY (CriadoPorAtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT FK_ConhecimentosDeIa_Atendentes_AtualizadoPor
            FOREIGN KEY (AtualizadoPorAtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT CK_ConhecimentosDeIa_Vigencia
            CHECK (VigenteDesde IS NULL OR VigenteAte IS NULL OR VigenteDesde <= VigenteAte)
    );

    -- A tela lista por tenant, filtrando so o que esta ativo — mesmo padrao de
    -- indice usado em IX_TiposServico_Tenant_Setor_Ativo. MontadorDeContexto
    -- tambem filtra por essas duas colunas em toda leitura do grounding.
    CREATE INDEX IX_ConhecimentosDeIa_Tenant_Ativo
        ON dbo.ConhecimentosDeIa (TenantId, Ativo);
END
GO

IF OBJECT_ID(N'dbo.HistoricoDeConhecimentosDeIa', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HistoricoDeConhecimentosDeIa
    (
        Id                     UNIQUEIDENTIFIER NOT NULL,
        TenantId               UNIQUEIDENTIFIER NOT NULL,
        ConhecimentoDeIaId     UNIQUEIDENTIFIER NOT NULL,

        -- Numero da versao que este snapshot representa (o estado ANTES da
        -- edicao que o gerou).
        Versao                 INT              NOT NULL,

        Titulo                 NVARCHAR(160)    NOT NULL,
        Conteudo               NVARCHAR(2000)   NOT NULL,
        Tema                   NVARCHAR(80)     NOT NULL,
        TipoServicoId          UNIQUEIDENTIFIER NULL,
        VigenteDesde           DATE             NULL,
        VigenteAte             DATE             NULL,
        Ativo                  BIT              NOT NULL,

        -- Nulo quando o registro veio de uma migracao retroativa.
        AlteradoPorAtendenteId UNIQUEIDENTIFIER NULL,
        OcorridoEm             DATETIME2(3)     NOT NULL,

        CriadoEm               DATETIME2(3)     NOT NULL,
        AtualizadoEm           DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_HistoricoDeConhecimentosDeIa PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_HistoricoDeConhecimentosDeIa_Tenants
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_HistoricoDeConhecimentosDeIa_ConhecimentosDeIa
            FOREIGN KEY (ConhecimentoDeIaId) REFERENCES dbo.ConhecimentosDeIa (Id),
        CONSTRAINT FK_HistoricoDeConhecimentosDeIa_TiposServico
            FOREIGN KEY (TipoServicoId) REFERENCES dbo.TiposServico (Id),
        CONSTRAINT FK_HistoricoDeConhecimentosDeIa_Atendentes
            FOREIGN KEY (AlteradoPorAtendenteId) REFERENCES dbo.Atendentes (Id)
    );

    -- Reconstroi a linha do tempo de um item: todas as versoes, em ordem.
    CREATE INDEX IX_HistoricoDeConhecimentosDeIa_Conhecimento_Versao
        ON dbo.HistoricoDeConhecimentosDeIa (ConhecimentoDeIaId, Versao);
END
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'039-conhecimento-de-ia.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'039-conhecimento-de-ia.sql',
                N'Conhecimento aprovado da IA: ConhecimentosDeIa (cadastro governado, por servico/tema, com vigencia) + HistoricoDeConhecimentosDeIa (trilha append-only, snapshot antes de cada edicao). Consumido por MontadorDeContexto no grounding. Binario novo exige esta migration antes de subir (docs/DEPLOY.md 1.2).');
END
GO

COMMIT TRANSACTION;
GO

SELECT OBJECT_ID(N'dbo.ConhecimentosDeIa', N'U') AS ConhecimentosDeIa,
       OBJECT_ID(N'dbo.HistoricoDeConhecimentosDeIa', N'U') AS HistoricoDeConhecimentosDeIa;
GO

PRINT 'Migracao 039 concluida. Nenhum dado existente foi afetado por esta migracao.';
GO
