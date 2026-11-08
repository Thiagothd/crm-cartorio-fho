/* =============================================================================
   CRM-Cartorio — RegistrosDeAcesso (blindagem LGPD, trilha de leitura)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-11
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql, 034-mfa-totp.sql,
                   037-reemissao-de-ticket-de-emergencia.sql,
                   044-eventos-de-seguranca-permissoes-alteradas.sql

   O QUE FAZ
       1) Cria dbo.RegistrosDeAcesso — tabela append-only que registra QUEM
          leu QUE conversa/ficha/anexo e QUANDO. E a resposta, visivel na
          tela ao cartorio, para "o programador consegue ver as conversas do
          meu cliente sem eu saber?" (docs/PROTECAO-DE-DADOS.md).
       2) Cria o gatilho TR_RegistrosDeAcesso_SomenteInsercao, que recusa
          fisicamente UPDATE e DELETE sobre a tabela.
       3) Alarga CK_EventosDeSeguranca_Tipo de "BETWEEN 1 AND 12" para
          "BETWEEN 1 AND 13", liberando o valor 13
          (TipoDeEventoDeSeguranca.LoginRealizado) — login bem-sucedido
          passa a virar evento de seguranca, o que nunca acontecia antes
          desta migration.
       Nenhuma linha existente e alterada.

   POR QUE ESTA MIGRATION E REALMENTE NECESSARIA (nao so codigo)
       RegistrosDeAcesso e tabela nova — o proprio banco recusa fisicamente
       um INSERT ate ela existir. CK_EventosDeSeguranca_Tipo e uma CHECK
       CONSTRAINT gravada no schema, mesmo motivo das migrations 037/044: o
       banco recusa um INSERT com Tipo = 13 ate o ALTER rodar.

   O PROBLEMA QUE ISTO RESOLVE
       Ate esta entrega, ler uma conversa, um anexo ou a ficha de um cliente
       pela API nao deixava rastro nenhum alem do log generico da aplicacao
       (ConversaService/MensagemService/ClienteService filtravam so por
       TenantId) — nem sequer exigiam a permissao atendimento.ver, que ja
       existia no catalogo sem nenhuma checagem no backend. RegistradorDeAcesso
       (Infrastructure/Auditoria) agora grava uma linha a cada leitura de
       verdade, e a tela "Acesso aos dados" (SegurancaController) devolve essa
       trilha ao cartorio.

   POR QUE UMA TABELA PROPRIA, NAO EventosDeSeguranca
       Volume e semantica diferentes: leitura e rotina do dia a dia (cada
       abertura de conversa), EventosDeSeguranca e evento raro (reset de MFA,
       break-glass). RegistradorDeAcesso tambem deduplica na gravacao (janela
       de 15 min) — jogar isso em EventosDeSeguranca faria essa tabela, que
       tambem serve o SEC-03, herdar uma logica que nao e dela.

   O TRIGGER, E O LIMITE HONESTO DELE
       INSTEAD OF UPDATE, DELETE com RAISERROR: impede o ACIDENTE (um
       ExecuteUpdate/ExecuteDelete escrito por engano contra esta tabela) e o
       ATALHO (um UPDATE avulso rodado sem pensar). NAO impede um
       administrador determinado, que tem acesso direto ao SQL Server e pode
       rodar `DISABLE TRIGGER TR_RegistrosDeAcesso_SomenteInsercao ON
       dbo.RegistrosDeAcesso` antes de apagar uma linha — nem impede que
       db_owner (a identidade do App Pool) contorne DENY de permissao
       convencional, porque aqui nao ha DENY nenhum sendo usado, so o
       gatilho. O que fecharia essa lacuna de verdade e' uma copia da trilha
       fora do alcance de quem administra o servidor (nivel 2 do plano de
       protecao de dados, ainda nao construido) — ver
       docs/PROTECAO-DE-DADOS.md, secao 4.

   POR QUE A FAIXA DO CHECK JA NASCE ATE 21 (nao so ate 13)
       TipoDeAcesso (Domain/Enums) reserva 10-19 para o ciclo de vida do
       acesso tecnico autorizado (PR seguinte do mesmo plano) e 20-29 para
       eventos de cifra (rotacao de chave, conclusao da cifra de dados
       legados) — nenhum dos dois ja e gravado por este binario. Alargar a
       CHECK agora, de uma vez, evita mais uma migration so para isso quando
       esses PRs chegarem; os valores intermediarios (4-9, 14-19, 22-29)
       ficam reservados, nao usados.

   POR QUE NAO PRECISA DE BACKFILL
       RegistrosDeAcesso nasce vazia (nao ha como reconstruir leituras
       passadas que nunca foram registradas). Nenhuma linha existente de
       EventosDeSeguranca tem Tipo = 13 (o valor so passa a existir a partir
       do primeiro login bem-sucedido depois deste binario subir).

   COMPATIBILIDADE DE DEPLOY
       Compativel com o binario ANTIGO rodando: ele nao conhece
       RegistrosDeAcesso (nao grava nem le) e nunca grava Tipo = 13 em
       EventosDeSeguranca — as duas mudancas sao transparentes para ele. O
       binario NOVO (RegistradorDeAcesso, guardas de AtendimentoVer,
       LoginRealizado) precisa desta migration aplicada antes de subir —
       o primeiro INSERT em RegistrosDeAcesso ou com Tipo = 13 falharia
       contra um schema sem esta migration.

   IDEMPOTENCIA
       CREATE TABLE e CREATE TRIGGER guardados por IF NOT EXISTS (o trigger
       via sys.triggers, com EXEC de SQL dinamico porque CREATE TRIGGER
       precisa ser a unica instrucao do proprio lote). DROP + ADD da CHECK
       constraint, sempre para a MESMA definicao final (1..13) — mesmo padrao
       de 037/044.
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

IF OBJECT_ID(N'dbo.EventosDeSeguranca', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Clientes', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Atendentes', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* -----------------------------------------------------------------------------
   1. RegistrosDeAcesso
   -------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.RegistrosDeAcesso', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RegistrosDeAcesso
    (
        Id                            UNIQUEIDENTIFIER NOT NULL,
        TenantId                      UNIQUEIDENTIFIER NOT NULL,

        -- Quem leu, quando foi alguem de dentro do cartorio. Nulo quando foi
        -- um ator externo (AtorExterno).
        AtendenteId                   UNIQUEIDENTIFIER NULL,

        -- Identidade de quem leu FORA do modelo Atendente/Tenant comum —
        -- mesmo papel que EventosDeSeguranca.AtorExterno. Nao preenchido
        -- ainda por nenhum binario; reservado.
        AtorExterno                   NVARCHAR(120)    NULL,

        -- 1 LeituraDeConversa, 2 DownloadDeAnexo, 3 LeituraDeFichaDeCliente,
        -- 10-13 ciclo de vida do acesso tecnico, 20-21 eventos de cifra.
        Tipo                          TINYINT          NOT NULL,

        ConversaId                    UNIQUEIDENTIFIER NULL,
        ClienteId                     UNIQUEIDENTIFIER NULL,
        MensagemId                    UNIQUEIDENTIFIER NULL,

        -- Preenchida quando a leitura aconteceu sob acesso tecnico
        -- autorizado. Sem FK de proposito: a tabela que ela aponta
        -- (AutorizacoesDeAcessoTecnico) so chega numa migration seguinte do
        -- mesmo plano — a coluna ja nasce aqui para nao exigir outro ALTER
        -- so para acrescenta-la.
        AutorizacaoDeAcessoTecnicoId  UNIQUEIDENTIFIER NULL,

        IpDeOrigem                    VARCHAR(45)      NULL,
        OcorridoEm                    DATETIME2(3)     NOT NULL,

        CriadoEm                      DATETIME2(3)     NOT NULL,
        AtualizadoEm                  DATETIME2(3)     NOT NULL,

        CONSTRAINT PK_RegistrosDeAcesso PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_RegistrosDeAcesso_Tenants
            FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
        CONSTRAINT FK_RegistrosDeAcesso_Atendentes
            FOREIGN KEY (AtendenteId) REFERENCES dbo.Atendentes (Id),
        CONSTRAINT FK_RegistrosDeAcesso_Conversas
            FOREIGN KEY (ConversaId) REFERENCES dbo.Conversas (Id),
        CONSTRAINT FK_RegistrosDeAcesso_Clientes
            FOREIGN KEY (ClienteId) REFERENCES dbo.Clientes (Id),
        CONSTRAINT FK_RegistrosDeAcesso_Mensagens
            FOREIGN KEY (MensagemId) REFERENCES dbo.Mensagens (Id),
        CONSTRAINT CK_RegistrosDeAcesso_Tipo
            CHECK (Tipo IN (1, 2, 3, 10, 11, 12, 13, 20, 21))
    );

    -- Consulta principal da tela: os acessos mais recentes de um tenant.
    CREATE INDEX IX_RegistrosDeAcesso_Tenant_Ocorrido
        ON dbo.RegistrosDeAcesso (TenantId, OcorridoEm);

    -- "Quem abriu ESTA conversa" — usado no detalhe do atendimento.
    CREATE INDEX IX_RegistrosDeAcesso_Tenant_Conversa
        ON dbo.RegistrosDeAcesso (TenantId, ConversaId);

    -- "O que ESTA pessoa leu" — filtro por atendente na tela.
    CREATE INDEX IX_RegistrosDeAcesso_Tenant_Atendente_Ocorrido
        ON dbo.RegistrosDeAcesso (TenantId, AtendenteId, OcorridoEm);
END
GO

/* -----------------------------------------------------------------------------
   2. Gatilho somente-insercao — ver o comentario de cabecalho para o limite
      honesto disto.
   -------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_RegistrosDeAcesso_SomenteInsercao')
BEGIN
    EXEC(N'
    CREATE TRIGGER TR_RegistrosDeAcesso_SomenteInsercao
    ON dbo.RegistrosDeAcesso
    INSTEAD OF UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;
        RAISERROR(''RegistrosDeAcesso e somente-insercao — UPDATE e DELETE nao sao permitidos.'', 16, 1);
    END
    ');
END
GO

/* -----------------------------------------------------------------------------
   3. CK_EventosDeSeguranca_Tipo — libera 13 (LoginRealizado)
   -------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_EventosDeSeguranca_Tipo')
    ALTER TABLE dbo.EventosDeSeguranca DROP CONSTRAINT CK_EventosDeSeguranca_Tipo;
GO

ALTER TABLE dbo.EventosDeSeguranca ADD CONSTRAINT CK_EventosDeSeguranca_Tipo CHECK (Tipo BETWEEN 1 AND 13);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'045-registros-de-acesso.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'045-registros-de-acesso.sql',
                N'Blindagem LGPD: cria dbo.RegistrosDeAcesso (trilha de leitura, somente-insercao via gatilho) e alarga CK_EventosDeSeguranca_Tipo para 1..13, liberando LoginRealizado (13) — gravado por AutenticacaoService a cada login bem-sucedido. Binario com guardas de AtendimentoVer/RegistradorDeAcesso exige esta migration antes de subir.');
END
GO

COMMIT TRANSACTION;
GO

SELECT OBJECT_DEFINITION(object_id) AS CK_EventosDeSeguranca_Tipo
FROM sys.check_constraints
WHERE name = N'CK_EventosDeSeguranca_Tipo';
GO

PRINT 'Migracao 045 concluida. RegistrosDeAcesso criada (se ausente); nenhuma linha existente foi alterada.';
GO
