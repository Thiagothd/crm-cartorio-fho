/* =============================================================================
   CRM-Cartorio — Resposta dada pelo celular entra no atendimento
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-23
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 021-versao-do-schema.sql,
                  028-atendimento-humano.sql, 029-idempotencia-webhook-whatsapp.sql

   O QUE FAZ
       1. Mensagens.EnviadaPeloAparelho BIT NOT NULL DEFAULT (0)
       2. Conversas.AtendidaPeloAparelhoEm DATETIME2(3) NULL
       3. CK_Mensagens_AtendenteObrigatorio AMPLIADO: mensagem de atendente sem
          AtendenteId passa a ser aceita SOMENTE com EnviadaPeloAparelho = 1
       4. CK_Mensagens_EnviadaPeloAparelho (nova): a marca so existe no formato
          exato da resposta pelo celular
       5. UQ_Mensagens_Tenant_IdExterno_Aparelho (novo, filtrado): idempotencia
          da resposta pelo celular contra reentrega do gateway

   O PROBLEMA QUE ISTO RESOLVE
       Quem responde um cliente direto pelo WhatsApp do celular (ou WhatsApp Web)
       nao aparecia no atendimento: o webhook descartava toda mensagem "enviada
       por mim", porque o eco do que o proprio painel manda chega com a mesma
       marca. O registro da serventia ficava furado, e o robo continuava
       respondendo por cima de quem ja estava atendendo pelo celular.

       Agora a resposta pelo celular entra no fio (sem atendente identificado — o
       aparelho nao tem login) e tem o mesmo efeito de um atendente assumir pelo
       painel: a automacao conversacional fica muda (Conversas.EmAtendimentoHumano,
       028). Sem dono no painel, a conversa fica marcada como atendida pelo
       celular (AtendidaPeloAparelhoEm) e continua em aberto para quem quiser
       assumir.

   POR QUE AMPLIAR O CK_Mensagens_AtendenteObrigatorio
       A resposta pelo celular e GENTE da serventia falando (AutorTipo = 2):
       "aguardando resposta do cartorio", tempo de primeira resposta e
       inatividade precisam enxergar esta mensagem como a de qualquer atendente.
       Mas o sistema nao sabe QUEM respondeu. A marca EnviadaPeloAparelho e o que
       torna a falta do atendente uma informacao, e nao um defeito — por isso a
       excecao exige a marca, e a CHECK nova exige que a marca so apareca nesse
       formato (atendente nulo, nunca automatica, sempre com id do WhatsApp).

   SEGURO COM O BINARIO ANTERIOR RODANDO
       As duas colunas sao aditivas; a coluna nova de Mensagens nasce com DEFAULT
       (0), entao todo INSERT do binario anterior (que nao a conhece) a grava 0.
       A CHECK ampliada e estritamente mais permissiva: toda linha que satisfazia
       a versao antiga satisfaz a nova. A CHECK nova e o indice novo so olham
       linhas com a marca = 1, que o binario anterior nunca produz. Ver o contrato
       em deploy/migration-compatibility.json.

   IDEMPOTENCIA
       Colunas, constraints e indice so entram quando ainda nao existem; a CHECK
       ampliada so e recriada se a definicao atual ainda nao for a nova.
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

IF OBJECT_ID(N'dbo.Mensagens', N'U') IS NULL
   OR COL_LENGTH(N'dbo.Conversas', N'EmAtendimentoHumano') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores (inclusive a 028) antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1 e 2. As colunas -----------------------------------------------------
   Cada uma no proprio lote: o SQL Server resolve nome de coluna na compilacao
   do LOTE, entao uma coluna criada aqui so e visivel a partir do proximo GO,
   mesmo dentro da mesma transacao (mesma observacao da 028).
   ------------------------------------------------------------------------- */

IF COL_LENGTH(N'dbo.Mensagens', N'EnviadaPeloAparelho') IS NULL
    ALTER TABLE dbo.Mensagens ADD EnviadaPeloAparelho BIT NOT NULL
        CONSTRAINT DF_Mensagens_EnviadaPeloAparelho DEFAULT (0);
GO

IF COL_LENGTH(N'dbo.Conversas', N'AtendidaPeloAparelhoEm') IS NULL
    ALTER TABLE dbo.Conversas ADD AtendidaPeloAparelhoEm DATETIME2(3) NULL;
GO

/* --- 3. CK_Mensagens_AtendenteObrigatorio, ampliada -------------------------
   DROP + ADD no mesmo commit, como 030/041/044 fizeram com outras CHECK. So
   AMPLIA: a unica linha nova aceita e atendente sem AtendenteId COM a marca.
   ------------------------------------------------------------------------- */

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                WHERE name = 'CK_Mensagens_AtendenteObrigatorio'
                  AND definition LIKE '%EnviadaPeloAparelho%')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Mensagens_AtendenteObrigatorio')
        ALTER TABLE dbo.Mensagens DROP CONSTRAINT CK_Mensagens_AtendenteObrigatorio;

    ALTER TABLE dbo.Mensagens WITH CHECK ADD CONSTRAINT CK_Mensagens_AtendenteObrigatorio
        CHECK (
            (AutorTipo <> 2 AND AtendenteId IS NULL)
            OR
            (AutorTipo = 2 AND (AtendenteId IS NOT NULL OR EnviadaPeloAparelho = 1))
        );
END
GO

/* --- 4. CK_Mensagens_EnviadaPeloAparelho ----------------------------------- */

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Mensagens_EnviadaPeloAparelho')
    ALTER TABLE dbo.Mensagens WITH CHECK ADD CONSTRAINT CK_Mensagens_EnviadaPeloAparelho
        CHECK (
            EnviadaPeloAparelho = 0
            OR
            (AutorTipo = 2 AND AtendenteId IS NULL AND EhAutomatica = 0 AND IdExternoWhatsapp IS NOT NULL)
        );
GO

/* --- 5. Idempotencia da resposta pelo celular -------------------------------
   Filtrado pela marca, e NAO por AutorTipo = 2: as mensagens que o proprio
   painel envia tambem guardam o id, gravado pela fila de saida DEPOIS do envio,
   e nao podem disputar unicidade com nada. Nasce vazio.
   ------------------------------------------------------------------------- */

IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = 'UQ_Mensagens_Tenant_IdExterno_Aparelho'
                  AND object_id = OBJECT_ID(N'dbo.Mensagens'))
    CREATE UNIQUE INDEX UQ_Mensagens_Tenant_IdExterno_Aparelho
        ON dbo.Mensagens (TenantId, IdExternoWhatsapp)
        WHERE EnviadaPeloAparelho = 1;
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'051-resposta-pelo-celular.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'051-resposta-pelo-celular.sql',
                N'Resposta dada pelo celular entra no atendimento: Mensagens.EnviadaPeloAparelho, Conversas.AtendidaPeloAparelhoEm, CK_Mensagens_AtendenteObrigatorio ampliada so para a marca, CK_Mensagens_EnviadaPeloAparelho e indice unico filtrado de idempotencia. Aditiva, sem backfill.');
END
GO

COMMIT TRANSACTION;
GO

SELECT c.name AS Coluna, OBJECT_NAME(c.object_id) AS Tabela, t.name AS Tipo, c.is_nullable AS AceitaNulo
  FROM sys.columns c
  JOIN sys.types t ON t.user_type_id = c.user_type_id
 WHERE (c.object_id = OBJECT_ID(N'dbo.Mensagens') AND c.name = N'EnviadaPeloAparelho')
    OR (c.object_id = OBJECT_ID(N'dbo.Conversas') AND c.name = N'AtendidaPeloAparelhoEm');
GO

PRINT 'Migracao 051 concluida.';
GO
