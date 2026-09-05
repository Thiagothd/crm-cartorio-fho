/* =============================================================================
   CRM-Cartorio — dispositivos confiaveis (SEC-03)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-01
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql, 034-mfa-totp.sql

   O QUE FAZ
       dbo.DispositivosConfiaveis (tabela nova) — "confiar neste dispositivo
       por 30 dias" no segundo fator do login. Uma linha por NAVEGADOR que
       aceitou a opcao, nunca por conta.

   O PROBLEMA QUE ISTO RESOLVE
       Hoje o MFA pede o codigo do autenticador em TODO login, inclusive do
       mesmo computador de trabalho, minutos depois. Esta tabela guarda so o
       HASH de um token opaco de 256 bits devolvido ao navegador como cookie
       HttpOnly — nunca o valor em claro, mesmo padrao ja usado por
       DesafiosDeMfa (034-mfa-totp.sql).

       A SENHA continua obrigatoria em todo login. O que o dispositivo
       confiavel dispensa e SOMENTE o segundo fator.

   COMO O CODIGO USA ISTO
       AutenticacaoService.AutenticarAsync (etapa 1), quando a conta tem MFA
       ativo, procura o cookie ANTES de abrir um desafio: se o hash bate, o
       dispositivo nao esta revogado nem expirado, e TokenVersionNoRegistro
       ainda e igual ao Atendentes.TokenVersion atual, o login conclui sem
       segundo fator — reconfirmando Ativo/MfaAtivo/TokenVersion no mesmo
       instante da escrita (mesmo "porteiro unico" do resto do modulo) e
       ROTACIONANDO o hash na mesma transacao. Qualquer falha cai no fluxo
       normal de MFA (fail closed).

       AutenticacaoService.ConcluirLoginMfaAsync (etapa 2) grava a linha nova
       quando o chamador pede ConfiarNesteDispositivo, na MESMA transacao que
       ja conclui o login.

       MfaService.EsquecerDispositivosConfiaveisAsync marca RevogadoEm em
       lote, para a acao "esquecer dispositivos confiaveis" da tela de
       Seguranca.

   POR QUE NAO EXISTE COLUNA/TABELA DE REVOGACAO POR EVENTO
       TokenVersionNoRegistro reaproveita o MESMO mecanismo de invalidacao
       automatica de DesafiosDeMfa.TokenVersionNoDesafio (034) e de
       Atendentes.MfaPendenteTokenVersion (035): troca de senha, reset de MFA,
       desativacao de MFA, troca de autenticador, break-glass e mudanca de
       Perfil/Cargo/Ativo JA sobem Atendentes.TokenVersion hoje. Como a linha
       guarda a versao vigente na concessao, todos esses eventos invalidam o
       dispositivo sozinhos, sem nenhuma bookkeeping nova.

       ExpiraEm e fixado na concessao e NUNCA estendido pela rotacao — teto
       duro de 30 dias, mesmo com uso diario.

   POR QUE NAO PRECISA DE BACKFILL
       Tabela nova, nasce vazia. Nenhum dispositivo e confiavel ate o primeiro
       login que marcar a caixa — o comportamento atual (sempre pedir o
       segundo fator) continua identico ate la.

   COMPATIBILIDADE DE DEPLOY
       Compativel com o binario ANTIGO rodando: ele ignora a tabela nova por
       completo. O binario NOVO CONSULTA e GRAVA dbo.DispositivosConfiaveis em
       AutenticarAsync, ConcluirLoginMfaAsync e ObterSituacaoAsync — nao deve
       ser iniciado antes desta migration estar aplicada, mesmo padrao de
       033..037 (ver docs/DEPLOY.md, secao 1.2).

   IDEMPOTENCIA
       A tabela so e criada quando ainda nao existe. Rodar o script de novo
       nao encontra nada para fazer.
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

IF OBJECT_ID(N'dbo.Atendentes', N'U') IS NULL OR OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.DesafiosDeMfa', N'U') IS NULL
BEGIN
    RAISERROR('034-mfa-totp.sql precisa ter rodado antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'dbo.DispositivosConfiaveis', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DispositivosConfiaveis
    (
        Id                     UNIQUEIDENTIFIER NOT NULL,
        TenantId               UNIQUEIDENTIFIER NOT NULL,
        AtendenteId            UNIQUEIDENTIFIER NOT NULL,

        -- SHA-256 (hex) do token opaco de 256 bits entregue ao navegador.
        -- Nunca o valor em claro, mesma disciplina de DesafiosDeMfa.
        HashDoToken            CHAR(64)         NOT NULL,

        -- Snapshot de Atendentes.TokenVersion na concessao. Reconferido a cada
        -- uso: e o que faz troca de senha, reset/desativacao/troca de MFA,
        -- break-glass e mudanca de Perfil/Cargo/Ativo invalidarem o
        -- dispositivo automaticamente.
        TokenVersionNoRegistro INT              NOT NULL,

        -- Fixo desde a concessao. A rotacao do hash a cada uso NUNCA estende
        -- este valor — teto duro de 30 dias.
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

    CREATE UNIQUE INDEX UQ_DispositivosConfiaveis_Hash
        ON dbo.DispositivosConfiaveis (HashDoToken);

    -- Contagem na tela de Seguranca e revogacao em lote ("esquecer
    -- dispositivos"): so os ainda ativos interessam.
    CREATE INDEX IX_DispositivosConfiaveis_Atendente_Ativos
        ON dbo.DispositivosConfiaveis (AtendenteId)
        WHERE RevogadoEm IS NULL;
END
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'038-dispositivos-confiaveis.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'038-dispositivos-confiaveis.sql',
                N'SEC-03: "confiar neste dispositivo por 30 dias" no segundo fator do login. Guarda so o hash do token, com rotacao a cada uso e teto duro de 30 dias. TokenVersionNoRegistro reaproveita a invalidacao ja existente (troca de senha, reset/desativacao/troca de MFA, break-glass, mudanca de Perfil/Cargo/Ativo). Binario novo exige esta migration antes de subir (docs/DEPLOY.md 1.2).');
END
GO

COMMIT TRANSACTION;
GO

SELECT OBJECT_ID(N'dbo.DispositivosConfiaveis', N'U') AS DispositivosConfiaveis;
GO

PRINT 'Migracao 038 concluida. Nenhuma conta foi afetada por esta migracao.';
GO
