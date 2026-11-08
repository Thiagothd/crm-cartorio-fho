/* =============================================================================
   CRM-Cartorio — Controle de versao do schema
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-18
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql

   O QUE FAZ
       Cria dbo.VersaoDoSchema e carimba 001 a 020 como ja aplicados. A partir
       daqui, todo script novo registra a si mesmo na ultima linha.

   POR QUE ISSO EXISTE
       Ate 2026-08-18 havia UM banco (CRM_Cartorio_Dev, na maquina do
       desenvolvedor) e a resposta para "esse script ja rodou aqui?" era a
       memoria de quem rodou. Com a subida ao servidor passam a ser TRES bancos
       — producao, teste e desenvolvimento — e memoria deixa de servir: um
       script aplicado em teste e esquecido em producao produz duas bases com o
       mesmo nome e comportamentos diferentes, e o sintoma aparece semanas
       depois, na conversa de um cliente.

       Nao e uma ferramenta de migracao. Continua valendo a regra 1 do
       docs/CONVENCOES.md: nada roda contra banco sem aprovacao explicita, e o .sql
       continua sendo revisado a mao. Esta tabela so responde o que ja foi
       feito.

   TRAVA DE AMBIENTE — MUDANCA DE CONVENCAO
       Os scripts 004 a 020 recusam rodar quando DB_NAME() nao contem "Dev".
       Aquilo protegia bem quando so existia banco de desenvolvimento, mas
       bloquearia exatamente o que agora e legitimo: aplicar em producao.

       Deste script em diante a trava passa a ser por LISTA DE NOMES CONHECIDOS.
       E mais estrita, nao menos: '%Dev%' aceitaria qualquer banco de terceiro
       com "Dev" no nome — e este servidor hospeda bancos de outros sistemas.

   IDEMPOTENCIA
       Tabela criada so se faltar; cada carimbo inserido so se ausente. Rodar
       duas vezes nao duplica nada.
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

IF OBJECT_ID(N'dbo.Conversas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 001-schema.sql antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VersaoDoSchema
    (
        -- Nome do arquivo, exatamente como esta em sql/. E a chave porque e o
        -- que uma pessoa procura: "o 017 rodou aqui?".
        Script      NVARCHAR(120) NOT NULL,

        AplicadoEm  DATETIME2(3)  NOT NULL
            CONSTRAINT DF_VersaoDoSchema_AplicadoEm  DEFAULT (SYSUTCDATETIME()),

        -- Quem rodou. Em auditoria de cartorio, "quando" sem "quem" vale pouco.
        AplicadoPor NVARCHAR(128) NOT NULL
            CONSTRAINT DF_VersaoDoSchema_AplicadoPor DEFAULT (SUSER_SNAME()),

        Observacao  NVARCHAR(400) NULL,

        CONSTRAINT PK_VersaoDoSchema PRIMARY KEY CLUSTERED (Script)
    );
END
GO

/* --- Carimbo retroativo de 001 a 020 -------------------------------------
   A data gravada e a de HOJE, nao a de quando cada um rodou de verdade — essa
   informacao nao existe em lugar nenhum, e inventar seria pior que admitir.
   A Observacao diz isso explicitamente para quem consultar depois.          */

DECLARE @retroativo NVARCHAR(400) =
    N'Carimbado retroativamente em 2026-08-18. A data real de aplicacao nao foi registrada — este banco e anterior ao controle de versao.';

;WITH Anteriores(Script) AS
(
    SELECT N'001-schema.sql'                           UNION ALL
    SELECT N'002-seed-simulado.sql'                    UNION ALL
    SELECT N'003-cores-de-etapa.sql'                   UNION ALL
    SELECT N'004-qualificacao-clientes-e-resposta.sql' UNION ALL
    SELECT N'005-mensagens-automaticas.sql'            UNION ALL
    SELECT N'006-encoding-e-textos.sql'                UNION ALL
    SELECT N'007-cargo-do-atendente.sql'               UNION ALL
    SELECT N'008-aparencia-da-conversa.sql'            UNION ALL
    SELECT N'009-acoes-de-mensagem.sql'                UNION ALL
    SELECT N'010-canal-whatsapp.sql'                   UNION ALL
    SELECT N'011-anexos-de-mensagem.sql'               UNION ALL
    SELECT N'012-resposta-inteligente.sql'             UNION ALL
    SELECT N'013-catalogo-atos-candidatos.sql'         UNION ALL
    SELECT N'014-servico-por-setor-e-checklist.sql'    UNION ALL
    SELECT N'015-checklist-e-mensagens-de-servico.sql' UNION ALL
    SELECT N'016-emoji-nas-mensagens.sql'              UNION ALL
    SELECT N'017-piso-da-ia-schema.sql'                UNION ALL
    SELECT N'018-piso-da-ia-conteudo.sql'              UNION ALL
    SELECT N'019-resposta-sem-correspondencia-schema.sql'   UNION ALL
    SELECT N'020-resposta-sem-correspondencia-conteudo.sql'
)
INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
SELECT a.Script, @retroativo
FROM Anteriores a
WHERE NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema v WHERE v.Script = a.Script);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'021-versao-do-schema.sql')
    INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
    VALUES (N'021-versao-do-schema.sql', N'Cria o proprio controle de versao.');
GO

COMMIT TRANSACTION;
GO

SELECT Script, AplicadoEm, AplicadoPor FROM dbo.VersaoDoSchema ORDER BY Script;
GO

PRINT 'Migracao 021 concluida.';
GO
