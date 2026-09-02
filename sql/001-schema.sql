/* =============================================================================
   CRM-Cartorio — Schema da Fase 1
   -----------------------------------------------------------------------------
   Status ....... NAO EXECUTADO. Revisar antes de rodar.
   Gerado em .... 2026-08-12
   Alvo ......... SQL Server 2019+
   Referencia ... docs/MODELO-DADOS.md

   Este script NAO cria o banco de dados. Crie a base manualmente e execute-o
   dentro dela:

       CREATE DATABASE CrmCartorio;
       GO
       USE CrmCartorio;
       GO

   Ordem de execucao:
       001-schema.sql          (este arquivo)
       002-seed-simulado.sql   (dados de demonstracao)

   Este arquivo e mantido ATUALIZADO: ja contem tudo que as migracoes 003 a 007
   acrescentaram. Base nova roda so 001 + 002; base que ja existe roda as migracoes.

   ENCODING: este arquivo e todos os demais .sql do repositorio sao gravados em
   UTF-8 COM BOM. Sem o BOM o sqlcmd le como ANSI e todo acento vira dois
   caracteres no banco. Ao editar, preserve o BOM.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

/* -----------------------------------------------------------------------------
   1. Tenants — cartorio cliente
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.Tenants
(
    Id                        UNIQUEIDENTIFIER NOT NULL,
    RazaoSocial               NVARCHAR(200)    NOT NULL,
    NomeFantasia              NVARCHAR(200)    NULL,
    Cnpj                      VARCHAR(14)      NOT NULL,

    -- Preenchidos apenas na Fase 2, quando a conta Meta estiver verificada.
    WhatsappBusinessAccountId VARCHAR(64)      NULL,
    WhatsappPhoneNumberId     VARCHAR(64)      NULL,
    WhatsappNumeroExibicao    VARCHAR(20)      NULL,

    -- Textura de fundo do fio de mensagens: 1 PapelLiso, 2 SeloRepetido,
    -- 3 PautaFina. Por tenant, nao por atendente — e a cara do cartorio.
    AparenciaDaConversa       TINYINT          NOT NULL CONSTRAINT DF_Tenants_AparenciaDaConversa DEFAULT (1),

    -- Encerramento automatico de conversa parada (024). Nulo nos minutos desliga o
    -- recurso; nulo nas duas horas significa "qualquer hora".
    MinutosParaPerguntarEncerramento INT        NULL,
    MinutosParaEncerrarSemResposta   INT        NULL,
    EncerramentoHoraInicial          INT        NULL,
    EncerramentoHoraFinal            INT        NULL,

    -- SEC-03: liga a exigencia de MFA para Administrador/Titular. So muda por
    -- uma operacao transacional que confirma matricula de todos os alvos e
    -- sobe o TokenVersion deles — nunca por UPDATE direto nesta coluna.
    MfaObrigatorioParaAdministradores BIT NOT NULL CONSTRAINT DF_Tenants_MfaObrigatorioParaAdministradores DEFAULT (0),

    Ativo                     BIT              NOT NULL CONSTRAINT DF_Tenants_Ativo DEFAULT (1),
    CriadoEm                  DATETIME2(3)     NOT NULL,
    AtualizadoEm              DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_Tenants PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT CK_Tenants_AparenciaDaConversa CHECK (AparenciaDaConversa IN (1, 2, 3)),
    CONSTRAINT CK_Tenants_MinutosParaPerguntarEncerramento CHECK
        (MinutosParaPerguntarEncerramento IS NULL OR MinutosParaPerguntarEncerramento BETWEEN 5 AND 43200),
    CONSTRAINT CK_Tenants_MinutosParaEncerrarSemResposta CHECK
        (MinutosParaEncerrarSemResposta IS NULL OR MinutosParaEncerrarSemResposta BETWEEN 5 AND 43200),
    CONSTRAINT CK_Tenants_EncerramentoJanela CHECK
    (
        (EncerramentoHoraInicial IS NULL AND EncerramentoHoraFinal IS NULL)
        OR (EncerramentoHoraInicial BETWEEN 0 AND 23
            AND EncerramentoHoraFinal BETWEEN 0 AND 23
            AND EncerramentoHoraInicial <> EncerramentoHoraFinal)
    )
);
GO

CREATE UNIQUE INDEX UQ_Tenants_Cnpj ON dbo.Tenants (Cnpj);
GO

/* -----------------------------------------------------------------------------
   2. Setores — eixo de ROTEAMENTO (configuravel por tenant)
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.Setores
(
    Id           UNIQUEIDENTIFIER NOT NULL,
    TenantId     UNIQUEIDENTIFIER NOT NULL,
    Nome         NVARCHAR(120)    NOT NULL,
    Descricao    NVARCHAR(300)    NULL,
    Ordem        INT              NOT NULL CONSTRAINT DF_Setores_Ordem DEFAULT (0),
    Ativo        BIT              NOT NULL CONSTRAINT DF_Setores_Ativo DEFAULT (1),
    CriadoEm     DATETIME2(3)     NOT NULL,
    AtualizadoEm DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_Setores PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Setores_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id)
);
GO

CREATE UNIQUE INDEX UQ_Setores_Tenant_Nome ON dbo.Setores (TenantId, Nome);
CREATE INDEX IX_Setores_Tenant_Ordem       ON dbo.Setores (TenantId, Ordem);
GO

/* -----------------------------------------------------------------------------
   3. EtapasFunil — eixo de PROGRESSAO (configuravel por tenant)
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.EtapasFunil
(
    Id           UNIQUEIDENTIFIER NOT NULL,
    TenantId     UNIQUEIDENTIFIER NOT NULL,
    Nome         NVARCHAR(120)    NOT NULL,
    Descricao    NVARCHAR(300)    NULL,
    Ordem        INT              NOT NULL CONSTRAINT DF_EtapasFunil_Ordem DEFAULT (0),
    Cor          VARCHAR(9)       NOT NULL CONSTRAINT DF_EtapasFunil_Cor DEFAULT ('#64748B'),
    EhFinal      BIT              NOT NULL CONSTRAINT DF_EtapasFunil_EhFinal DEFAULT (0),
    Ativo        BIT              NOT NULL CONSTRAINT DF_EtapasFunil_Ativo DEFAULT (1),
    CriadoEm     DATETIME2(3)     NOT NULL,
    AtualizadoEm DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_EtapasFunil PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_EtapasFunil_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id)
);
GO

CREATE UNIQUE INDEX UQ_EtapasFunil_Tenant_Nome ON dbo.EtapasFunil (TenantId, Nome);
CREATE INDEX IX_EtapasFunil_Tenant_Ordem       ON dbo.EtapasFunil (TenantId, Ordem);
GO

/* -----------------------------------------------------------------------------
   4. Atendentes — usuarios do sistema
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.Atendentes
(
    Id             UNIQUEIDENTIFIER NOT NULL,
    TenantId       UNIQUEIDENTIFIER NOT NULL,
    Nome           NVARCHAR(150)    NOT NULL,
    Email          NVARCHAR(160)    NOT NULL,

    -- Formato: pbkdf2.sha256$<iteracoes>$<salt b64>$<hash b64>. Nunca texto claro.
    SenhaHash      VARCHAR(500)     NOT NULL,

    -- PERMISSAO no sistema: 1 = Atendente, 2 = Supervisor, 3 = Administrador
    Perfil         TINYINT          NOT NULL CONSTRAINT DF_Atendentes_Perfil DEFAULT (1),

    -- FUNCAO na serventia, na estrutura da Lei 8.935/94:
    -- 0 NaoInformado, 1 Titular, 2 Substituto, 3 EscreventeAutorizado,
    -- 4 Escrevente, 5 Auxiliar
    --
    -- Coluna separada de Perfil de proposito. Cargo e o que a pessoa E no cartorio
    -- e nao concede nada; Perfil e o que ela PODE FAZER aqui dentro. Fundidos, um
    -- escrevente nao podia ter permissao de supervisao sem aparecer como
    -- "Supervisor", cargo que nao existe em cartorio.
    Cargo          TINYINT          NOT NULL CONSTRAINT DF_Atendentes_Cargo DEFAULT (0),

    SetorPadraoId  UNIQUEIDENTIFIER NULL,
    Ativo          BIT              NOT NULL CONSTRAINT DF_Atendentes_Ativo DEFAULT (1),

    -- SEC-01: sobe a cada troca de senha, desativacao ou mudanca de Perfil.
    -- Todo JWT carrega a versao vigente no instante em que foi emitido; o
    -- token so continua valido enquanto ela bater com esta coluna. Nunca
    -- volta para tras, nem quando o atendente e reativado.
    TokenVersion   INT              NOT NULL CONSTRAINT DF_Atendentes_TokenVersion DEFAULT (1),

    -- SEC-02 — backoff progressivo contra forca bruta no login.
    --
    -- FalhasDeLoginConsecutivas conta as falhas do ciclo atual; na 5a, zera e
    -- abre um bloqueio. NivelDeBloqueio conta quantos bloqueios ja houve SEM
    -- login bem-sucedido no meio e escolhe a duracao do proximo na progressao
    -- 1, 2, 4, 8, 16 e 30 minutos (teto 6 = 30 min). BloqueadoAte diz ate quando
    -- a conta esta travada. Um login bem-sucedido zera os tres.
    --
    -- Senha correta NAO fura bloqueio vigente, tentativa durante bloqueio nao o
    -- estende, e conta inativa nao acumula lockout. Ver
    -- Modules/Usuarios/Application/AutenticacaoService.cs.
    FalhasDeLoginConsecutivas INT     NOT NULL CONSTRAINT DF_Atendentes_FalhasDeLoginConsecutivas DEFAULT (0),
    NivelDeBloqueio           TINYINT NOT NULL CONSTRAINT DF_Atendentes_NivelDeBloqueio DEFAULT (0),
    BloqueadoAte              DATETIME2(3) NULL,

    -- SEC-03 — MFA (TOTP, RFC 6238) para contas privilegiadas.
    --
    -- MfaAtivo so vira 1 na CONFIRMACAO do 1o codigo — nunca ao gerar o
    -- segredo pendente. MfaSegredoCifrado/MfaSegredoPendenteCifrado sao
    -- AES-256-GCM (nonce+tag+ciphertext, formato versionado — ver
    -- Infrastructure/Security/CifraDeSegredoMfa.cs). MfaUltimoPassoUsado e o
    -- anti-replay (compare-and-set do ultimo passo TOTP de 30s aceito).
    -- MfaFalhasConsecutivas/MfaNivelDeBloqueio/MfaBloqueadoAte sao um
    -- contador PROPRIO, mesma progressao de NivelDeBloqueio (senha) mas nunca
    -- somado a ele — bloqueio de MFA e por CONTA, nao por desafio.
    -- MfaPendenteTokenVersion (migration 035) e a TokenVersion gravada no
    -- MESMO instante do segredo pendente — a confirmacao so conclui se ainda
    -- bater com a TokenVersion atual, fechando promocao/revogacao no meio da
    -- cerimonia de ativacao voluntaria ou troca de autenticador.
    MfaAtivo                  BIT              NOT NULL CONSTRAINT DF_Atendentes_MfaAtivo DEFAULT (0),
    MfaSegredoCifrado         VARBINARY(256)   NULL,
    MfaSegredoPendenteCifrado VARBINARY(256)   NULL,
    MfaPendenteExpiraEm       DATETIME2(3)     NULL,
    MfaPendenteTokenVersion   INT              NULL,
    MfaConfirmadoEm           DATETIME2(3)     NULL,
    MfaUltimoPassoUsado       BIGINT           NULL,
    MfaFalhasConsecutivas     INT              NOT NULL CONSTRAINT DF_Atendentes_MfaFalhasConsecutivas DEFAULT (0),
    MfaNivelDeBloqueio        TINYINT          NOT NULL CONSTRAINT DF_Atendentes_MfaNivelDeBloqueio DEFAULT (0),
    MfaBloqueadoAte           DATETIME2(3)     NULL,

    -- RematriculaObrigatoriaAposEmergencia (migration 036) — true quando o
    -- reset de emergencia (break-glass) removeu o MFA desta conta. Bloqueia
    -- login so-senha INDEPENDENTE da politica obrigatoria do tenant, ate a
    -- conta concluir uma matricula nova com o ticket que o proprio
    -- break-glass emitiu. Volta a 0 so na confirmacao bem-sucedida dessa
    -- matricula.
    RematriculaObrigatoriaAposEmergencia BIT NOT NULL CONSTRAINT DF_Atendentes_RematriculaObrigatoriaAposEmergencia DEFAULT (0),

    UltimoAcessoEm DATETIME2(3)     NULL,
    CriadoEm       DATETIME2(3)     NOT NULL,
    AtualizadoEm   DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_Atendentes PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Atendentes_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_Atendentes_Setores FOREIGN KEY (SetorPadraoId) REFERENCES dbo.Setores (Id),
    CONSTRAINT CK_Atendentes_Perfil CHECK (Perfil IN (1, 2, 3)),
    CONSTRAINT CK_Atendentes_Cargo  CHECK (Cargo  IN (0, 1, 2, 3, 4, 5)),
    CONSTRAINT CK_Atendentes_FalhasDeLogin   CHECK (FalhasDeLoginConsecutivas >= 0),
    CONSTRAINT CK_Atendentes_NivelDeBloqueio CHECK (NivelDeBloqueio BETWEEN 0 AND 6),
    CONSTRAINT CK_Atendentes_MfaFalhas CHECK (MfaFalhasConsecutivas >= 0),
    CONSTRAINT CK_Atendentes_MfaNivel  CHECK (MfaNivelDeBloqueio BETWEEN 0 AND 6),
    -- Invariante central: nunca existe conta com MfaAtivo=1 sem segredo.
    CONSTRAINT CK_Atendentes_MfaAtivoTemSegredo CHECK (MfaAtivo = 0 OR MfaSegredoCifrado IS NOT NULL)
);
GO

CREATE UNIQUE INDEX UQ_Atendentes_Tenant_Email ON dbo.Atendentes (TenantId, Email);
GO

/* -----------------------------------------------------------------------------
   5. Clientes — contatos finais do WhatsApp
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.Clientes
(
    Id                UNIQUEIDENTIFIER NOT NULL,
    TenantId          UNIQUEIDENTIFIER NOT NULL,
    Nome              NVARCHAR(200)    NOT NULL,

    -- 1 = Pessoa fisica, 2 = Pessoa juridica
    Tipo              TINYINT          NOT NULL CONSTRAINT DF_Clientes_Tipo DEFAULT (1),

    -- E.164 sem simbolos. E a identidade real do contato.
    TelefoneWhatsapp  VARCHAR(20)      NOT NULL,

    TelefoneAlternativo VARCHAR(20)    NULL,

    Documento         VARCHAR(14)      NULL,
    Email             NVARCHAR(160)    NULL,

    -- Qualificacao de pessoa fisica
    Rg                VARCHAR(20)      NULL,
    OrgaoEmissor      NVARCHAR(20)     NULL,
    UfOrgaoEmissor    VARCHAR(2)       NULL,
    DataNascimento    DATE             NULL,
    EstadoCivil       TINYINT          NOT NULL CONSTRAINT DF_Clientes_EstadoCivil DEFAULT (0),
    RegimeDeBens      TINYINT          NOT NULL CONSTRAINT DF_Clientes_RegimeDeBens DEFAULT (0),
    Profissao         NVARCHAR(100)    NULL,
    Nacionalidade     NVARCHAR(60)     NULL,
    Naturalidade      NVARCHAR(120)    NULL,
    NomeDoPai         NVARCHAR(200)    NULL,
    NomeDaMae         NVARCHAR(200)    NULL,

    -- Qualificacao de pessoa juridica
    RazaoSocial       NVARCHAR(200)    NULL,
    RepresentanteLegal NVARCHAR(200)   NULL,

    -- Endereco da parte
    Cep               VARCHAR(8)       NULL,
    Logradouro        NVARCHAR(200)    NULL,
    Numero            NVARCHAR(20)     NULL,
    Complemento       NVARCHAR(100)    NULL,
    Bairro            NVARCHAR(100)    NULL,
    Cidade            NVARCHAR(120)    NULL,
    Uf                VARCHAR(2)       NULL,

    Observacoes       NVARCHAR(1000)   NULL,
    FotoPerfilUrl     NVARCHAR(500)    NULL,
    FotoPerfilAtualizadaEm DATETIME2(3) NULL,

    -- Quando o sistema perguntou a este contato como quer ser chamado. Fato
    -- datado, nao estado: "o nome esta pendente" se calcula na leitura. Serve a
    -- uma coisa so — nao perguntar duas vezes.
    NomePerguntadoEm  DATETIME2(3)     NULL,

    -- De onde este cadastro veio: 1 Webhook, 2 Manual, 3 ImportacaoPlanilha,
    -- 4 ImportacaoWhatsapp. Nulo nos clientes de antes desta coluna — "nao
    -- sabemos" e a verdade para eles.
    OrigemDoCadastro  TINYINT          NULL,

    PrimeiroContatoEm DATETIME2(3)     NOT NULL,
    Ativo             BIT              NOT NULL CONSTRAINT DF_Clientes_Ativo DEFAULT (1),
    CriadoEm          DATETIME2(3)     NOT NULL,
    AtualizadoEm      DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_Clientes PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Clientes_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT CK_Clientes_Tipo CHECK (Tipo IN (1, 2)),
    CONSTRAINT CK_Clientes_EstadoCivil CHECK (EstadoCivil IN (0, 1, 2, 3, 4, 5, 6)),
    CONSTRAINT CK_Clientes_RegimeDeBens CHECK (RegimeDeBens IN (0, 1, 2, 3, 4, 5))
);
GO

CREATE UNIQUE INDEX UQ_Clientes_Tenant_Telefone ON dbo.Clientes (TenantId, TelefoneWhatsapp);
CREATE INDEX IX_Clientes_Tenant_Nome            ON dbo.Clientes (TenantId, Nome);
CREATE INDEX IX_Clientes_Tenant_Documento       ON dbo.Clientes (TenantId, Documento);
CREATE INDEX IX_Clientes_Tenant_Cidade          ON dbo.Clientes (TenantId, Cidade);
GO

/* -----------------------------------------------------------------------------
   6. TiposServico — catalogo de atos cartorarios
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.TiposServico
(
    Id           UNIQUEIDENTIFIER NOT NULL,
    TenantId     UNIQUEIDENTIFIER NOT NULL,
    Nome         NVARCHAR(160)    NOT NULL,
    Codigo       VARCHAR(30)      NULL,

    -- 1 Notas, 2 RegistroCivil, 3 RegistroImoveis, 4 TitulosDocumentos,
    -- 5 Protesto, 9 Outros
    Categoria    TINYINT          NOT NULL CONSTRAINT DF_TiposServico_Categoria DEFAULT (9),

    Descricao    NVARCHAR(500)    NULL,
    Ativo        BIT              NOT NULL CONSTRAINT DF_TiposServico_Ativo DEFAULT (1),
    CriadoEm     DATETIME2(3)     NOT NULL,
    AtualizadoEm DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_TiposServico PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_TiposServico_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT CK_TiposServico_Categoria CHECK (Categoria IN (1, 2, 3, 4, 5, 9))
);
GO

CREATE UNIQUE INDEX UQ_TiposServico_Tenant_Nome    ON dbo.TiposServico (TenantId, Nome);
CREATE INDEX IX_TiposServico_Tenant_Categoria      ON dbo.TiposServico (TenantId, Categoria);
GO

/* -----------------------------------------------------------------------------
   7. PrazosServico — SLA por tipo de servico (1:1)
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.PrazosServico
(
    Id               UNIQUEIDENTIFIER NOT NULL,
    TenantId         UNIQUEIDENTIFIER NOT NULL,
    TipoServicoId    UNIQUEIDENTIFIER NOT NULL,

    -- Em horas: reconhecimento de firma sai em minutos, registro de imovel leva
    -- dias. Hora e a menor unidade que cobre os dois sem virar dois campos.
    PrazoHoras       INT              NOT NULL,

    PercentualAlerta INT              NOT NULL CONSTRAINT DF_PrazosServico_Alerta DEFAULT (80),
    ApenasDiasUteis  BIT              NOT NULL CONSTRAINT DF_PrazosServico_DiasUteis DEFAULT (1),
    Ativo            BIT              NOT NULL CONSTRAINT DF_PrazosServico_Ativo DEFAULT (1),
    CriadoEm         DATETIME2(3)     NOT NULL,
    AtualizadoEm     DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_PrazosServico PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_PrazosServico_Tenants      FOREIGN KEY (TenantId)      REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_PrazosServico_TiposServico FOREIGN KEY (TipoServicoId) REFERENCES dbo.TiposServico (Id),
    CONSTRAINT CK_PrazosServico_PrazoHoras       CHECK (PrazoHoras > 0),
    CONSTRAINT CK_PrazosServico_PercentualAlerta CHECK (PercentualAlerta BETWEEN 1 AND 99)
);
GO

CREATE UNIQUE INDEX UQ_PrazosServico_TipoServico ON dbo.PrazosServico (TipoServicoId);
GO

/* -----------------------------------------------------------------------------
   8. Conversas — entidade central; e aqui que mora a trava
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.Conversas
(
    Id                      UNIQUEIDENTIFIER NOT NULL,
    TenantId                UNIQUEIDENTIFIER NOT NULL,
    ClienteId               UNIQUEIDENTIFIER NOT NULL,

    -- Formato AAAAMM-NNNN, unico por tenant.
    Protocolo               VARCHAR(20)      NOT NULL,

    -- NULL = na fila. Preenchido = travada para os demais atendentes.
    AtendenteId             UNIQUEIDENTIFIER NULL,

    SetorId                 UNIQUEIDENTIFIER NULL,
    EtapaId                 UNIQUEIDENTIFIER NULL,
    TipoServicoId           UNIQUEIDENTIFIER NULL,

    -- 1 = Ativa, 2 = Encerrada. NAO existe valor "excluida" (RF-015).
    Status                  TINYINT          NOT NULL CONSTRAINT DF_Conversas_Status DEFAULT (1),

    -- 1 = WhatsApp (Fase 2), 9 = Simulado (Fase 1)
    Canal                   TINYINT          NOT NULL CONSTRAINT DF_Conversas_Canal DEFAULT (9),

    IniciadaEm              DATETIME2(3)     NOT NULL,
    UltimaMensagemEm        DATETIME2(3)     NOT NULL,
    MensagensNaoLidas       INT              NOT NULL CONSTRAINT DF_Conversas_NaoLidas DEFAULT (0),

    -- Recebeu o menu de triagem e espera a escolha do setor. Dura UMA mensagem: a
    -- proxima coisa que o cliente escrever limpa a flag, tenha ela correspondido a
    -- uma opcao ou nao.
    AguardandoTriagem       BIT              NOT NULL CONSTRAINT DF_Conversas_AguardandoTriagem DEFAULT (0),

    -- O contato chegou sem nome usavel no perfil do WhatsApp e a conversa espera
    -- que ele diga como quer ser chamado, ANTES de receber o menu. Mesma
    -- disciplina acima: dura uma mensagem, responda ele um nome ou nao.
    AguardandoNome          BIT              NOT NULL CONSTRAINT DF_Conversas_AguardandoNome DEFAULT (0),

    PrazoIniciadoEm         DATETIME2(3)     NULL,
    PrazoLimite             DATETIME2(3)     NULL,
    PrazoAlertaEm           DATETIME2(3)     NULL,

    -- Quando cada aviso automatico de prazo saiu. Nao e "situacao de prazo
    -- persistida" — situacao envelhece, isto e um fato datado. E o que torna o
    -- disparo idempotente sob varredura periodica e com varias instancias de IIS.
    AvisoDeAlertaEnviadoEm      DATETIME2(3) NULL,
    AvisoDeVencimentoEnviadoEm  DATETIME2(3) NULL,

    -- Quando a pergunta "deseja encerrar?" saiu, por inatividade (024). Mesmo tipo
    -- de fato datado dos avisos acima, e acumula tres papeis de proposito: marcador
    -- idempotente da varredura, relogio da espera pela resposta, e a flag que o
    -- despacho consulta para tratar a proxima mensagem do cliente como resposta.
    PerguntaDeEncerramentoEnviadaEm DATETIME2(3) NULL,

    EncerradaEm             DATETIME2(3)     NULL,
    EncerradaPorAtendenteId UNIQUEIDENTIFIER NULL,
    MotivoEncerramento      NVARCHAR(500)    NULL,
    ReabertaEm              DATETIME2(3)     NULL,
    ReabertaPorAtendenteId  UNIQUEIDENTIFIER NULL,

    -- Concorrencia otimista. Cobre toda escrita sobre a conversa, nao so a
    -- atribuicao: etapa, setor, encerramento e transferencia tambem.
    RowVersion              ROWVERSION       NOT NULL,

    CriadoEm                DATETIME2(3)     NOT NULL,
    AtualizadoEm            DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_Conversas PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Conversas_Tenants        FOREIGN KEY (TenantId)      REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_Conversas_Clientes       FOREIGN KEY (ClienteId)     REFERENCES dbo.Clientes (Id),
    CONSTRAINT FK_Conversas_Atendentes     FOREIGN KEY (AtendenteId)   REFERENCES dbo.Atendentes (Id),
    CONSTRAINT FK_Conversas_Setores        FOREIGN KEY (SetorId)       REFERENCES dbo.Setores (Id),
    CONSTRAINT FK_Conversas_EtapasFunil    FOREIGN KEY (EtapaId)       REFERENCES dbo.EtapasFunil (Id),
    CONSTRAINT FK_Conversas_TiposServico   FOREIGN KEY (TipoServicoId) REFERENCES dbo.TiposServico (Id),
    CONSTRAINT FK_Conversas_Encerrada_Por  FOREIGN KEY (EncerradaPorAtendenteId) REFERENCES dbo.Atendentes (Id),
    CONSTRAINT FK_Conversas_Reaberta_Por   FOREIGN KEY (ReabertaPorAtendenteId)  REFERENCES dbo.Atendentes (Id),
    CONSTRAINT CK_Conversas_Status CHECK (Status IN (1, 2)),
    CONSTRAINT CK_Conversas_Canal  CHECK (Canal  IN (1, 9))
);
GO

CREATE UNIQUE INDEX UQ_Conversas_Tenant_Protocolo
    ON dbo.Conversas (TenantId, Protocolo);

-- Consulta padrao da inbox: ativas do tenant, mais recentes primeiro.
CREATE INDEX IX_Conversas_Inbox
    ON dbo.Conversas (TenantId, Status, UltimaMensagemEm DESC);

CREATE INDEX IX_Conversas_Atendente ON dbo.Conversas (TenantId, AtendenteId, Status);
CREATE INDEX IX_Conversas_Etapa     ON dbo.Conversas (TenantId, EtapaId, Status);
CREATE INDEX IX_Conversas_Setor     ON dbo.Conversas (TenantId, SetorId, Status);
CREATE INDEX IX_Conversas_Cliente   ON dbo.Conversas (TenantId, ClienteId);
CREATE UNIQUE INDEX UQ_Conversas_Tenant_Cliente_Canal_Ativa
    ON dbo.Conversas (TenantId, ClienteId, Canal)
    WHERE Status = 1;
CREATE INDEX IX_Conversas_Prazo     ON dbo.Conversas (TenantId, Status, PrazoLimite);

-- Varredura de prazo. Filtrados porque, em regime, quase toda conversa antiga ja
-- avisou — indexar essas seria carregar peso morto.
CREATE INDEX IX_Conversas_AvisoAlertaPendente
    ON dbo.Conversas (TenantId, PrazoAlertaEm)
    WHERE AvisoDeAlertaEnviadoEm IS NULL AND PrazoLimite IS NOT NULL;

CREATE INDEX IX_Conversas_AvisoVencimentoPendente
    ON dbo.Conversas (TenantId, PrazoLimite)
    WHERE AvisoDeVencimentoEnviadoEm IS NULL AND PrazoLimite IS NOT NULL;

-- Segunda fase da varredura de inatividade. A primeira nao ganha indice de
-- proposito: ela e servida por IX_Conversas_Inbox, e um filtrado por "IS NULL"
-- cobriria quase toda conversa ativa — seria a inbox indexada duas vezes.
CREATE INDEX IX_Conversas_EncerramentoPendente
    ON dbo.Conversas (TenantId, PerguntaDeEncerramentoEnviadaEm)
    WHERE PerguntaDeEncerramentoEnviadaEm IS NOT NULL;
GO

/* -----------------------------------------------------------------------------
   9. Mensagens — o fio da conversa. Nunca editadas, nunca apagadas.
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.Mensagens
(
    Id                UNIQUEIDENTIFIER NOT NULL,
    TenantId          UNIQUEIDENTIFIER NOT NULL,
    ConversaId        UNIQUEIDENTIFIER NOT NULL,

    -- 1 = Cliente, 2 = Atendente, 3 = Sistema
    AutorTipo         TINYINT          NOT NULL,

    -- RF-007: obrigatorio quando AutorTipo = 2. O cliente pediu para saber QUAL
    -- atendente respondeu, e a resposta e uma coluna, nao uma inferencia.
    AtendenteId       UNIQUEIDENTIFIER NULL,

    Conteudo          NVARCHAR(4000)   NOT NULL,

    -- 1 Texto, 2 Imagem, 3 Documento, 4 Audio, 5 Video (midia so na Fase 2)
    Tipo              TINYINT          NOT NULL CONSTRAINT DF_Mensagens_Tipo DEFAULT (1),

    -- 1 Pendente, 2 Enviada, 3 Entregue, 4 Lida, 5 Falha, 6 Enviando
    StatusEntrega     TINYINT          NOT NULL CONSTRAINT DF_Mensagens_Status DEFAULT (2),

    -- Claim persistente do envio. Disparado preenchido significa que a chamada
    -- externa pode ter comecado e, portanto, nao admite retry automatico cego.
    EnvioIniciadoEm   DATETIME2(3)     NULL,
    EnvioDisparadoEm  DATETIME2(3)     NULL,

    -- Gerada por regra de MensagemAutomatica. Combinada com AutorTipo = 3, separa a
    -- anotacao interna do fio ("Conversa assumida por Fulano", que o cliente nunca
    -- receberia) da mensagem endereçada AO CLIENTE. E o que a interface usa para
    -- desenhar o rotulo "Automatica" — sem ele, o atendente acharia que um colega
    -- escreveu aquilo.
    EhAutomatica      BIT              NOT NULL CONSTRAINT DF_Mensagens_EhAutomatica DEFAULT (0),

    EnviadaEm         DATETIME2(3)     NOT NULL,
    LidaEm            DATETIME2(3)     NULL,

    -- ID da mensagem na Meta. Preenchido apenas na Fase 2.
    IdExternoWhatsapp VARCHAR(80)      NULL,

    -- Fila duravel da entrada: 1 Pendente, 2 Processando, 3 Processada, 4 Falha.
    -- Nulo para mensagens que nao vieram do webhook.
    EstadoProcessamentoEntrada       TINYINT       NULL,
    ProcessamentoEntradaIniciadoEm   DATETIME2(3)  NULL,
    ProcessamentoEntradaConcluidoEm  DATETIME2(3)  NULL,
    ProcessamentoEntradaDisponivelEm DATETIME2(3)  NULL,
    TentativasProcessamentoEntrada   INT           NOT NULL CONSTRAINT DF_Mensagens_TentativasProcessamentoEntrada DEFAULT (0),
    ErroProcessamentoEntrada         NVARCHAR(1000) NULL,

    -- Mensagem original citada numa resposta. Sempre da mesma conversa,
    -- validado pelo servico antes de gravar.
    MensagemRespondidaId UNIQUEIDENTIFIER NULL,

    -- Fixada no topo da conversa. UMA por conversa: fixar outra desafixa a
    -- anterior, por regra de servico.
    FixadaEm             DATETIME2(3)     NULL,
    FixadaPorAtendenteId UNIQUEIDENTIFIER NULL,

    -- Ocultada do fio. A LINHA PERMANECE: nao existe exclusao de mensagem em
    -- caminho nenhum do sistema (RNF-008). Guardar quem ocultou e quando e o que
    -- separa "ocultar" de "apagar com outro nome".
    OcultaEm             DATETIME2(3)     NULL,
    OcultaPorAtendenteId UNIQUEIDENTIFIER NULL,

    -- Origem, quando esta mensagem e um encaminhamento. Guarda a referencia e nao
    -- um recorte do texto, como MensagemRespondidaId.
    EncaminhadaDeMensagemId UNIQUEIDENTIFIER NULL,

    CriadoEm          DATETIME2(3)     NOT NULL,
    AtualizadoEm      DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_Mensagens PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Mensagens_Tenants    FOREIGN KEY (TenantId)    REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_Mensagens_Conversas  FOREIGN KEY (ConversaId)  REFERENCES dbo.Conversas (Id),
    CONSTRAINT FK_Mensagens_Atendentes FOREIGN KEY (AtendenteId) REFERENCES dbo.Atendentes (Id),
    CONSTRAINT FK_Mensagens_MensagemRespondida FOREIGN KEY (MensagemRespondidaId) REFERENCES dbo.Mensagens (Id),
    CONSTRAINT FK_Mensagens_FixadaPor      FOREIGN KEY (FixadaPorAtendenteId)    REFERENCES dbo.Atendentes (Id),
    CONSTRAINT FK_Mensagens_OcultaPor      FOREIGN KEY (OcultaPorAtendenteId)    REFERENCES dbo.Atendentes (Id),
    CONSTRAINT FK_Mensagens_EncaminhadaDe  FOREIGN KEY (EncaminhadaDeMensagemId) REFERENCES dbo.Mensagens (Id),
    CONSTRAINT CK_Mensagens_AutorTipo     CHECK (AutorTipo     IN (1, 2, 3)),
    CONSTRAINT CK_Mensagens_Tipo          CHECK (Tipo          IN (1, 2, 3, 4, 5)),
    CONSTRAINT CK_Mensagens_StatusEntrega CHECK (StatusEntrega IN (1, 2, 3, 4, 5, 6)),
    CONSTRAINT CK_Mensagens_ProcessamentoEntrada CHECK
    (
        EstadoProcessamentoEntrada IS NULL OR
        (AutorTipo = 1 AND IdExternoWhatsapp IS NOT NULL AND EstadoProcessamentoEntrada IN (1, 2, 3, 4))
    ),

    -- Rede de seguranca do RF-007 no banco: mensagem de atendente sem AtendenteId
    -- nao entra, venha de onde vier (API, importacao, webhook da Fase 2).
    CONSTRAINT CK_Mensagens_AtendenteObrigatorio CHECK
    (
        (AutorTipo <> 2 AND AtendenteId IS NULL) OR
        (AutorTipo  = 2 AND AtendenteId IS NOT NULL)
    ),

    -- Mensagem automatica e sempre do sistema. Marcar como automatica algo que uma
    -- pessoa escreveu inverteria a autoria na tela.
    CONSTRAINT CK_Mensagens_AutomaticaEhDoSistema CHECK
    (
        (EhAutomatica = 0) OR (EhAutomatica = 1 AND AutorTipo = 3)
    )
);
GO

CREATE INDEX IX_Mensagens_Conversa_Enviada ON dbo.Mensagens (ConversaId, EnviadaEm);
CREATE INDEX IX_Mensagens_Tenant_Atendente ON dbo.Mensagens (TenantId, AtendenteId);
CREATE INDEX IX_Mensagens_MensagemRespondida ON dbo.Mensagens (MensagemRespondidaId) WHERE MensagemRespondidaId IS NOT NULL;
CREATE INDEX IX_Mensagens_Fixada ON dbo.Mensagens (ConversaId) WHERE FixadaEm IS NOT NULL;
CREATE UNIQUE INDEX UQ_Mensagens_Tenant_IdExterno_Cliente
    ON dbo.Mensagens (TenantId, IdExternoWhatsapp)
    WHERE IdExternoWhatsapp IS NOT NULL AND AutorTipo = 1;
CREATE INDEX IX_Mensagens_ProcessamentoEntrada
    ON dbo.Mensagens
       (EstadoProcessamentoEntrada, ProcessamentoEntradaDisponivelEm,
        ProcessamentoEntradaIniciadoEm, ConversaId, EnviadaEm)
    WHERE EstadoProcessamentoEntrada IN (1, 2, 4);
GO

/* -----------------------------------------------------------------------------
   10. HistoricoEtapas — trilha de etapa e setor. So cresce.
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.HistoricoEtapas
(
    Id              UNIQUEIDENTIFIER NOT NULL,
    TenantId        UNIQUEIDENTIFIER NOT NULL,
    ConversaId      UNIQUEIDENTIFIER NOT NULL,

    -- 1 Abertura, 2 Etapa, 3 Setor, 4 EtapaESetor
    TipoMudanca     TINYINT          NOT NULL,

    EtapaAnteriorId UNIQUEIDENTIFIER NULL,
    EtapaNovaId     UNIQUEIDENTIFIER NULL,
    SetorAnteriorId UNIQUEIDENTIFIER NULL,
    SetorNovoId     UNIQUEIDENTIFIER NULL,

    -- NULL quando a mudanca foi automatica.
    AtendenteId     UNIQUEIDENTIFIER NULL,

    OcorridoEm      DATETIME2(3)     NOT NULL,
    Observacao      NVARCHAR(500)    NULL,
    CriadoEm        DATETIME2(3)     NOT NULL,
    AtualizadoEm    DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_HistoricoEtapas PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_HistoricoEtapas_Tenants    FOREIGN KEY (TenantId)        REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_HistoricoEtapas_Conversas  FOREIGN KEY (ConversaId)      REFERENCES dbo.Conversas (Id),
    CONSTRAINT FK_HistoricoEtapas_EtapaAnt   FOREIGN KEY (EtapaAnteriorId) REFERENCES dbo.EtapasFunil (Id),
    CONSTRAINT FK_HistoricoEtapas_EtapaNova  FOREIGN KEY (EtapaNovaId)     REFERENCES dbo.EtapasFunil (Id),
    CONSTRAINT FK_HistoricoEtapas_SetorAnt   FOREIGN KEY (SetorAnteriorId) REFERENCES dbo.Setores (Id),
    CONSTRAINT FK_HistoricoEtapas_SetorNovo  FOREIGN KEY (SetorNovoId)     REFERENCES dbo.Setores (Id),
    CONSTRAINT FK_HistoricoEtapas_Atendentes FOREIGN KEY (AtendenteId)     REFERENCES dbo.Atendentes (Id),
    CONSTRAINT CK_HistoricoEtapas_TipoMudanca CHECK (TipoMudanca IN (1, 2, 3, 4))
);
GO

CREATE INDEX IX_HistoricoEtapas_Conversa_Ocorrido
    ON dbo.HistoricoEtapas (ConversaId, OcorridoEm);
GO

/* -----------------------------------------------------------------------------
   11. MensagensAutomaticas — modelos disparados por evento do atendimento
   -----------------------------------------------------------------------------
   Gatilho: 1 NovoAtendimento, 2 EtapaAlterada, 3 PrazoProximoVencimento,
            4 PrazoVencido, 5 TriagemSemCorrespondencia, 6 TriagemComCorrespondencia,
            7 ServicoEscolhido, 8 MenuDeContinuacao, 9 PedidoDeAtendente,
            10 Despedida, 11 RespostaSemCorrespondencia,
            12 ConfirmacaoDeEncerramento, 13 EncerramentoPorInatividade,
            14 PedidoDeNome

   Enum, e nao tabela, porque cada valor corresponde a um ponto de codigo que
   precisa existir para disparar. Ja o TEXTO da mensagem e QUAL etapa dispara sao
   cadastro: nenhuma frase que o cliente final le deveria exigir release para mudar.
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.MensagensAutomaticas
(
    Id           UNIQUEIDENTIFIER NOT NULL,
    TenantId     UNIQUEIDENTIFIER NOT NULL,
    Gatilho      TINYINT          NOT NULL,

    -- Obrigatorio no gatilho 2, nulo em todos os outros. Ver o CHECK abaixo.
    EtapaAlvoId  UNIQUEIDENTIFIER NULL,

    -- 1000, e nao os 4000 de Mensagens.Conteudo: o que fica aqui e o MODELO, e os
    -- marcadores ({NomeCliente}, {MenuSetores}, ...) crescem na substituicao.
    Conteudo     NVARCHAR(1000)   NOT NULL,

    Ativo        BIT              NOT NULL CONSTRAINT DF_MensagensAutomaticas_Ativo DEFAULT (1),
    CriadoEm     DATETIME2(3)     NOT NULL,
    AtualizadoEm DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_MensagensAutomaticas PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_MensagensAutomaticas_Tenants     FOREIGN KEY (TenantId)    REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_MensagensAutomaticas_EtapasFunil FOREIGN KEY (EtapaAlvoId) REFERENCES dbo.EtapasFunil (Id),
    CONSTRAINT CK_MensagensAutomaticas_Gatilho CHECK (Gatilho IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14)),

    -- Gatilho de etapa SEM etapa alvo dispararia mensagem ao cliente em toda
    -- movimentacao interna do funil; etapa alvo em gatilho que nao e de etapa e
    -- dado morto que engana quem le a tabela.
    CONSTRAINT CK_MensagensAutomaticas_EtapaAlvo CHECK
    (
        (Gatilho  = 2 AND EtapaAlvoId IS NOT NULL) OR
        (Gatilho <> 2 AND EtapaAlvoId IS NULL)
    )
);
GO

CREATE INDEX IX_MensagensAutomaticas_Tenant_Gatilho
    ON dbo.MensagensAutomaticas (TenantId, Gatilho, Ativo);
GO

/* -----------------------------------------------------------------------------
   12. RespostasRapidas — insercao assistida, nunca envio automatico
   -----------------------------------------------------------------------------
   Nada sai daqui sozinho: o atendente digita "/atalho", o texto entra no campo, ele
   le, edita se quiser e envia. E a diferenca de responsabilidade em relacao a
   tabela 11 — aquela o sistema assina, esta o atendente assina.
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.RespostasRapidas
(
    Id           UNIQUEIDENTIFIER NOT NULL,
    TenantId     UNIQUEIDENTIFIER NOT NULL,

    -- Sem a barra, em minusculas e sem espaco. VARCHAR porque e digitado no meio do
    -- atendimento e nao deveria exigir acento para acertar.
    Atalho       VARCHAR(30)      NOT NULL,

    Titulo       NVARCHAR(120)    NOT NULL,
    Conteudo     NVARCHAR(1000)   NOT NULL,
    Ativo        BIT              NOT NULL CONSTRAINT DF_RespostasRapidas_Ativo DEFAULT (1),
    CriadoEm     DATETIME2(3)     NOT NULL,
    AtualizadoEm DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_RespostasRapidas PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RespostasRapidas_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id)
);
GO

-- Dois atalhos iguais deixariam o seletor ambiguo: o atendente digita "/prazo", ve
-- duas linhas identicas e nao tem como escolher a certa.
CREATE UNIQUE INDEX UQ_RespostasRapidas_Tenant_Atalho
    ON dbo.RespostasRapidas (TenantId, Atalho);
GO

/* -----------------------------------------------------------------------------
   13. DesafiosDeMfa — SEC-03: segunda etapa de login, matricula privilegiada,
   e o proprio ticket fora de banda (Tipo = 3)
   -----------------------------------------------------------------------------
   Estado persistido, nao um JWT de escopo reduzido: uso unico de verdade exige
   uma linha que possa ser consumida atomicamente, e um desafio precisa poder
   ser invalidado por um evento de seguranca ocorrido DEPOIS da emissao —
   TokenVersionNoDesafio e a comparacao repetida na conclusao. O token em claro
   NUNCA e gravado, so o SHA-256 dele.
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.DesafiosDeMfa
(
    Id                    UNIQUEIDENTIFIER NOT NULL,
    TenantId              UNIQUEIDENTIFIER NOT NULL,
    AtendenteId           UNIQUEIDENTIFIER NOT NULL,
    HashDoDesafio         CHAR(64)         NOT NULL,

    -- 1 SegundoFator, 2 MatriculaPrivilegiada, 3 Ticket
    Tipo                  TINYINT          NOT NULL,

    TokenVersionNoDesafio INT              NOT NULL,
    ExpiraEm              DATETIME2(3)     NOT NULL,

    -- Incrementado ANTES de validar o codigo (reserva de tentativa) — e o
    -- proprio esgotamento em 5, sem passo separado de "inutilizar".
    TentativasDeCodigo    INT              NOT NULL CONSTRAINT DF_DesafiosDeMfa_Tentativas DEFAULT (0),

    -- Nulo enquanto em aberto. So marcado no caminho de SUCESSO.
    ConsumidoEm           DATETIME2(3)     NULL,
    IpDeOrigem            VARCHAR(45)      NULL,
    CriadoEm              DATETIME2(3)     NOT NULL,
    AtualizadoEm          DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_DesafiosDeMfa PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_DesafiosDeMfa_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_DesafiosDeMfa_Atendentes FOREIGN KEY (AtendenteId) REFERENCES dbo.Atendentes (Id),
    CONSTRAINT CK_DesafiosDeMfa_Tipo CHECK (Tipo IN (1, 2, 3)),
    CONSTRAINT CK_DesafiosDeMfa_Tentativas CHECK (TentativasDeCodigo >= 0)
);
GO

CREATE UNIQUE INDEX UQ_DesafiosDeMfa_Hash ON dbo.DesafiosDeMfa (HashDoDesafio);

-- Varredura de expirados: so os ainda em aberto interessam.
CREATE INDEX IX_DesafiosDeMfa_ExpiraEm_Abertos ON dbo.DesafiosDeMfa (ExpiraEm)
    WHERE ConsumidoEm IS NULL;
GO

/* -----------------------------------------------------------------------------
   14. CodigosDeRecuperacaoMfa — 10 por conta, 80 bits, uso unico
   -----------------------------------------------------------------------------
   16 caracteres Crockford Base32 (sem I/L/O/U). So o hash SHA-256 do codigo
   normalizado e gravado; o texto claro so existe na resposta da ativacao ou
   da regeneracao, uma unica vez.
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.CodigosDeRecuperacaoMfa
(
    Id           UNIQUEIDENTIFIER NOT NULL,
    TenantId     UNIQUEIDENTIFIER NOT NULL,
    AtendenteId  UNIQUEIDENTIFIER NOT NULL,
    HashDoCodigo CHAR(64)         NOT NULL,

    -- Nulo enquanto disponivel. Consumo e atomico e definitivo.
    UsadoEm      DATETIME2(3)     NULL,
    CriadoEm     DATETIME2(3)     NOT NULL,
    AtualizadoEm DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_CodigosDeRecuperacaoMfa PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_CodigosDeRecuperacaoMfa_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_CodigosDeRecuperacaoMfa_Atendentes FOREIGN KEY (AtendenteId) REFERENCES dbo.Atendentes (Id)
);
GO

CREATE UNIQUE INDEX UQ_CodigosDeRecuperacaoMfa_Atendente_Hash
    ON dbo.CodigosDeRecuperacaoMfa (AtendenteId, HashDoCodigo);
GO

/* -----------------------------------------------------------------------------
   15. EventosDeSeguranca — trilha append-only de fatos de seguranca de MFA
   -----------------------------------------------------------------------------
   Mesmo padrao de CanalWhatsappConexoes: existe para a tela (e uma
   investigacao) responderem "o que aconteceu e quando" sem depender do log do
   servidor. NUNCA grava segredo, codigo, ticket, senha ou desafio — so o
   FATO. Isto vale tambem para Motivo, texto livre digitado por um humano numa
   acao administrativa, nunca preenchido a partir de dado sensivel.
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.EventosDeSeguranca
(
    Id               UNIQUEIDENTIFIER NOT NULL,
    TenantId         UNIQUEIDENTIFIER NOT NULL,

    -- De quem este evento fala — a conta afetada.
    AtendenteId      UNIQUEIDENTIFIER NOT NULL,

    -- Quem executou, quando foi OUTRO atendente (reset, ativacao de politica).
    -- Nulo quando o proprio alvo agiu sobre si mesmo.
    AtorAtendenteId  UNIQUEIDENTIFIER NULL,

    -- Identidade de quem agiu FORA do sistema (acesso SSH que rodou a
    -- ferramenta de bootstrap, ou break-glass). So identidade, nunca motivo.
    AtorExterno      NVARCHAR(120)    NULL,

    -- 1 MfaAtivado, 2 MfaDesativado, 3 CodigoDeRecuperacaoUsado,
    -- 4 CodigosDeRecuperacaoRegenerados, 5 ResetAdministrativo,
    -- 6 ResetDeEmergencia, 7 BloqueioDeMfaPorFalhas,
    -- 8 TicketDeMatriculaEmitido, 9 TicketDeBootstrapEmitido,
    -- 10 PoliticaDeMfaAtivada, 11 TicketDeRematriculaReemitido (migration 037)
    Tipo             TINYINT          NOT NULL,

    OcorridoEm       DATETIME2(3)     NOT NULL,
    IpDeOrigem       VARCHAR(45)      NULL,
    Motivo           NVARCHAR(400)    NULL,
    CriadoEm         DATETIME2(3)     NOT NULL,
    AtualizadoEm     DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_EventosDeSeguranca PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_EventosDeSeguranca_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_EventosDeSeguranca_Atendentes FOREIGN KEY (AtendenteId) REFERENCES dbo.Atendentes (Id),
    CONSTRAINT CK_EventosDeSeguranca_Tipo CHECK (Tipo BETWEEN 1 AND 11)
);
GO

CREATE INDEX IX_EventosDeSeguranca_Tenant_Ocorrido ON dbo.EventosDeSeguranca (TenantId, OcorridoEm);
CREATE INDEX IX_EventosDeSeguranca_Atendente ON dbo.EventosDeSeguranca (AtendenteId);
GO

/* -----------------------------------------------------------------------------
   16. DispositivosConfiaveis — "confiar neste dispositivo por 30 dias" (SEC-03)
   -----------------------------------------------------------------------------
   Uma linha por NAVEGADOR que aceitou a opcao no segundo fator do login, nunca
   por conta. Guarda so o HASH do token opaco entregue como cookie HttpOnly.

   TokenVersionNoRegistro e a mesma tecnica de DesafiosDeMfa.TokenVersionNoDesafio:
   snapshot de Atendentes.TokenVersion na concessao, reconferido a cada uso —
   troca de senha, reset/desativacao/troca de MFA, break-glass e mudanca de
   Perfil/Cargo/Ativo ja sobem essa versao e invalidam o dispositivo sozinhos.

   ExpiraEm e fixo desde a concessao; a rotacao do hash a cada uso nunca o
   estende. A SENHA continua obrigatoria em todo login — so o segundo fator e
   dispensado. Ver migration 038 e Modules/Usuarios/README.md.
   -------------------------------------------------------------------------- */
CREATE TABLE dbo.DispositivosConfiaveis
(
    Id                     UNIQUEIDENTIFIER NOT NULL,
    TenantId               UNIQUEIDENTIFIER NOT NULL,
    AtendenteId            UNIQUEIDENTIFIER NOT NULL,
    HashDoToken            CHAR(64)         NOT NULL,
    TokenVersionNoRegistro INT              NOT NULL,
    ExpiraEm               DATETIME2(3)     NOT NULL,
    RevogadoEm             DATETIME2(3)     NULL,
    UltimoUsoEm            DATETIME2(3)     NULL,
    IpDeOrigem             VARCHAR(45)      NULL,
    CriadoEm               DATETIME2(3)     NOT NULL,
    AtualizadoEm           DATETIME2(3)     NOT NULL,

    CONSTRAINT PK_DispositivosConfiaveis PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_DispositivosConfiaveis_Tenants
        FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_DispositivosConfiaveis_Atendentes
        FOREIGN KEY (AtendenteId) REFERENCES dbo.Atendentes (Id)
);
GO

CREATE UNIQUE INDEX UQ_DispositivosConfiaveis_Hash
    ON dbo.DispositivosConfiaveis (HashDoToken);

CREATE INDEX IX_DispositivosConfiaveis_Atendente_Ativos
    ON dbo.DispositivosConfiaveis (AtendenteId)
    WHERE RevogadoEm IS NULL;
GO

PRINT 'Schema da Fase 1 criado.';
GO
