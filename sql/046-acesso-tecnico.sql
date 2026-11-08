/* =============================================================================
   CRM-Cartorio — Acesso tecnico autorizado (blindagem LGPD)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-12
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql,
                   045-registros-de-acesso.sql

   O QUE FAZ
       1) Adiciona dbo.Atendentes.EhContaTecnica (BIT, default 0) — marca uma
          conta como da equipe do projeto, nao do cartorio.
       2) Cria dbo.AutorizacoesDeAcessoTecnico — o pedido, aprovado ou nao,
          para essa conta ler dado de cliente por um tempo limitado.
       Nenhuma linha existente e alterada.

   POR QUE ESTA MIGRATION E REALMENTE NECESSARIA (nao so codigo)
       EhContaTecnica e uma coluna nova — o proprio banco recusa fisicamente
       um SELECT/INSERT que a referencie ate ela existir.
       AutorizacoesDeAcessoTecnico e tabela nova — mesmo motivo.

   O PROBLEMA QUE ISTO RESOLVE
       Ate esta migration, a UNICA forma de a equipe do projeto ler uma conversa era
       ter acesso direto ao banco de producao. Isto cria o caminho normal:
       "a equipe do projeto pede, o cartorio aprova com segundo fator, a faixa avisa,
       a trilha registra" (docs/PROTECAO-DE-DADOS.md) — uma conta marcada
       EhContaTecnica nasce sem NENHUMA permissao de leitura
       (AtendenteService.DefinirPermissoesAsync recusa conceder qualquer
       permissao de atendimento, clientes ou relatorios a ela, ver
       043-permissoes-do-atendente.sql para a coluna Permissoes) e so ganha AtendimentoVer/
       ClientesVer, TEMPORARIAMENTE, enquanto existir uma linha aqui com
       AprovadaEm preenchido, RevogadaEm nulo e ValidaAte no futuro
       (ValidadorDeSessao.CarregarSessaoAsync, resolvido a cada requisicao).

   POR QUE NAO PRECISA DE BACKFILL
       EhContaTecnica nasce 0 (false) em toda linha existente — nenhum
       Atendente de hoje deveria virar conta tecnica sem decisao explicita.
       AutorizacoesDeAcessoTecnico nasce vazia (nao ha pedido retroativo a
       reconstruir).

   COMPATIBILIDADE DE DEPLOY
       Duas mudancas independentes, ambas expand-only. Confirmado em
       Atendente.cs do binario N-1 (antes desta entrega): a classe nao
       possui EhContaTecnica, entao N-1 nunca a le nem grava — todo
       INSERT/UPDATE que N-1 gera em dbo.Atendentes omite a coluna, que
       aceita a omissao pelo proprio DEFAULT (0). AutorizacoesDeAcessoTecnico
       e tabela nova, sem FK apontando PARA ela em nenhuma tabela existente —
       fica completamente inerte para N-1. EF Core nao valida schema no
       boot. Rollback automatico do binario para N-1 e seguro nos dois
       casos: a coluna e a tabela ficam inertes, sem leitura nem escrita por
       N-1, e nao ha perda de dado nem registro orfao ao avancar de novo.
       Nao existe rollback automatico de banco, por desenho. Direcao da
       incompatibilidade e forward-only: o binario NOVO
       (AcessoTecnicoService, ValidadorDeSessao.CarregarSessaoAsync) le e
       grava as duas coisas, e nao pode subir antes desta migration.

   IDEMPOTENCIA
       ALTER TABLE ADD COLUMN e CREATE TABLE guardados por IF NOT EXISTS —
       rodar de novo e no-op.
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

IF OBJECT_ID(N'dbo.Atendentes', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
   OR OBJECT_ID(N'dbo.RegistrosDeAcesso', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* -----------------------------------------------------------------------------
   1. Atendentes.EhContaTecnica
   -------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.Atendentes', N'EhContaTecnica') IS NULL
    ALTER TABLE dbo.Atendentes ADD EhContaTecnica BIT NOT NULL CONSTRAINT DF_Atendentes_EhContaTecnica DEFAULT (0);
GO

/* -----------------------------------------------------------------------------
   2. AutorizacoesDeAcessoTecnico
   -------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.AutorizacoesDeAcessoTecnico', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AutorizacoesDeAcessoTecnico
    (
        Id                        UNIQUEIDENTIFIER NOT NULL,
        TenantId                  UNIQUEIDENTIFIER NOT NULL,
        ContaTecnicaId            UNIQUEIDENTIFIER NOT NULL,
        Motivo                    NVARCHAR(400)     NOT NULL,

        -- 1 Geral, 2 Conversa.
        Escopo                    TINYINT           NOT NULL,

        -- Preenchida so quando Escopo = 2.
        ConversaId                UNIQUEIDENTIFIER  NULL,

        DuracaoSolicitadaMinutos  INT               NOT NULL,
        SolicitadaEm              DATETIME2(3)      NOT NULL,
        IpDeSolicitacao           VARCHAR(45)       NULL,

        AprovadaPorAtendenteId    UNIQUEIDENTIFIER  NULL,
        AprovadaEm                DATETIME2(3)      NULL,
        ValidaAte                 DATETIME2(3)      NULL,

        NegadaPorAtendenteId      UNIQUEIDENTIFIER  NULL,
        NegadaEm                  DATETIME2(3)      NULL,

        RevogadaPorAtendenteId    UNIQUEIDENTIFIER  NULL,
        RevogadaEm                DATETIME2(3)      NULL,

        CriadoEm                  DATETIME2(3)      NOT NULL,
        AtualizadoEm              DATETIME2(3)      NOT NULL,

        CONSTRAINT PK_AutorizacoesDeAcessoTecnico PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_AutorizacoesDeAcessoTecnico_Tenants
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_AutorizacoesDeAcessoTecnico_ContaTecnica
            FOREIGN KEY (ContaTecnicaId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT FK_AutorizacoesDeAcessoTecnico_Conversas
            FOREIGN KEY (ConversaId) REFERENCES dbo.Conversas (Id),
        CONSTRAINT CK_AutorizacoesDeAcessoTecnico_Escopo CHECK (Escopo IN (1, 2)),

        -- Espelha a regra de negocio (Escopo=2 exige ConversaId) fisicamente
        -- no schema — nao so no service.
        CONSTRAINT CK_AutorizacoesDeAcessoTecnico_EscopoConversa
            CHECK ((Escopo = 2 AND ConversaId IS NOT NULL) OR (Escopo = 1 AND ConversaId IS NULL))
    );

    -- "Autorizacao ativa desta conta" — a consulta de
    -- ValidadorDeSessao.CarregarSessaoAsync, a cada requisicao.
    CREATE INDEX IX_AutorizacoesDeAcessoTecnico_Tenant_Conta_ValidaAte
        ON dbo.AutorizacoesDeAcessoTecnico (TenantId, ContaTecnicaId, ValidaAte);

    -- Historico paginado da tela "Acesso aos dados".
    CREATE INDEX IX_AutorizacoesDeAcessoTecnico_Tenant_Solicitada
        ON dbo.AutorizacoesDeAcessoTecnico (TenantId, SolicitadaEm);
END
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'046-acesso-tecnico.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'046-acesso-tecnico.sql',
                N'Blindagem LGPD: Atendentes.EhContaTecnica (conta da equipe do projeto, nao do cartorio) e dbo.AutorizacoesDeAcessoTecnico (pedido/aprovacao/negacao/revogacao de acesso tecnico temporario). AcessoTecnicoService e ValidadorDeSessao.CarregarSessaoAsync exigem esta migration antes de subir.');
END
GO

COMMIT TRANSACTION;
GO

PRINT 'Migracao 046 concluida. EhContaTecnica e AutorizacoesDeAcessoTecnico criadas (se ausentes); nenhuma linha existente foi alterada.';
GO
