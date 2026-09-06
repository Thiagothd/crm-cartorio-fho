/* =============================================================================
   CRM-Cartorio — Analise da IA: vocabulario correto de fallback, motivo da nao
   resposta e correlacao com o catalogo
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-05
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 001-schema.sql, 027-provedores-de-ia.sql, 021-versao-do-schema.sql

   O QUE FAZ
       dbo.ChamadasDeIa += TipoDeFallback      TINYINT          NOT NULL DEFAULT (0)
       dbo.ChamadasDeIa += RodadaId            UNIQUEIDENTIFIER NULL
       dbo.ChamadasDeIa += MotivoDaNaoResposta TINYINT          NULL
       dbo.ChamadasDeIa += TipoServicoId       UNIQUEIDENTIFIER NULL
       dbo.ChamadasDeIa += SetorId             UNIQUEIDENTIFIER NULL
       dbo.ChamadasDeIa += MensagemId          UNIQUEIDENTIFIER NULL
       + CK_ChamadasDeIa_TipoDeFallback
       + CK_ChamadasDeIa_MotivoDaNaoResposta
       Recria IX_ChamadasDeIa_Tenant_Iniciada com INCLUDE (cobre a tela do
       cartorio sem key lookup); cria IX_ChamadasDeIa_Iniciada (a consulta da
       Central, cross-tenant, sem essa coluna a esquerda nao existia indice
       nenhum) e IX_ChamadasDeIa_Conversa (drill-down por atendimento).
       Faz backfill de TipoDeFallback e MotivoDaNaoResposta a partir do estado
       ja gravado, antes de travar as duas colunas com CHECK.

   O PROBLEMA QUE ISTO RESOLVE
       Hoje "fallback" e contado como "linha de falha do Gemini" — mas quando o
       Gemini e pulado PREVENTIVAMENTE (desligado por config, disjuntor aberto,
       margem de cota atingida) nenhuma linha do Gemini chega a ser gravada, e
       o Haiku atende sozinho. Resultado ao vivo: a tela mostra "Caidas no
       Haiku: 1" ao lado de "Nenhum fallback no periodo — o Gemini respondeu
       tudo", ao mesmo tempo, sobre o mesmo periodo. O campo que ja distingue
       os dois casos corretamente (FoiFallback) e lido do banco e nunca usado.

       TipoDeFallback fecha essa lacuna com um vocabulario que vira GROUP BY,
       nao parse de texto: 0 nao e fallback nenhum; 1 e o Gemini que discou e
       falhou (executado); 2, 3 e 4 sao os tres motivos de pular o Gemini sem
       discar (preventivo); 5 e residuo de linha gravada antes desta migracao,
       que nao da para reclassificar com certeza.

       MotivoDaNaoResposta guarda POR QUE a IA respondeu mas nao soube — hoje
       o modelo ja devolve esse motivo, mas so vai para o ILogger e nunca e
       persistido. E a maior lacuna de valor do produto: sem isto nao ha como
       responder "o que falta cadastrar no meu catalogo".

       TipoServicoId, SetorId e MensagemId dao o eixo de assunto — QUAL
       servico/setor estava associado a conversa NO INSTANTE da chamada (nao
       o estado final da conversa, que pode ter mudado depois) — e o ponteiro
       para reabrir a pergunta na tela do cartorio.

   POR QUE MotivoDaNaoResposta E CODIGO FECHADO, NAO TEXTO LIVRE
       O texto que o modelo devolve para "por que nao sei" e gerado sobre a
       PERGUNTA do cliente — nada impede que ele cite o que foi perguntado.
       Persistir esse texto quebraria a garantia que esta tabela sustenta desde
       a 027 (nunca guarda conteudo de mensagem nem dado do cliente), e a
       Central de IA le esta tabela SEM filtro de tenant — um texto assim
       vazaria contexto de cliente de um cartorio para a tela de outro.
       Por isso o contrato do modelo passa a exigir uma categoria de lista
       fechada (ver MotivoDaNaoRespostaDeIa.cs); so o codigo e persistido, e
       "nao classificado" (0) e o piso seguro para quem nao a devolveu.

   POR QUE TipoServicoId, SetorId E MensagemId NAO TEM FOREIGN KEY
       Mesmo raciocinio ja registrado na 027 para ConversaId: esta e uma
       tabela de log, gravada dentro do fluxo de resposta ao cliente, e nao
       pode travar nenhuma rotina de manutencao futura sobre Conversas,
       Mensagens, TiposServico ou Setores. E tambem nao pode falhar em gravar
       a chamada de IA por causa de uma FK — perder a linha e pior do que
       perder a referencia.

   MensagemId E O UNICO CAMPO NOVO QUE MUDA O PERFIL DE PRIVACIDADE DA TABELA
       Deixa de ser "100% metadado tecnico" para virar "metadado + uma chave
       de join para o conteudo do cliente" (a mensagem ja existe no CRM; isto
       nao cria dado novo). Como a Central le sem filtro de tenant, a projecao
       da Central NUNCA seleciona MensagemId, ConversaId nem MotivoDoFallback
       (texto livre) — only os campos tecnicos. Ver Modules/CentralDeIa.

   POR QUE OS INDICES MUDAM
       IX_ChamadasDeIa_Tenant_Iniciada (TenantId, IniciadaEm) sem INCLUDE
       obriga a um key lookup por linha para pegar Provedor/Sucesso/tokens/
       custo — e o indice mais usado da tela do cartorio. Recriado com
       INCLUDE, a consulta fica covering.

       IX_ChamadasDeIa_Iniciada (IniciadaEm) e novo: a Central filtra so por
       periodo, cross-tenant, e nenhum indice existente tem IniciadaEm a
       esquerda sem TenantId ou ChaveDeProjeto na frente — hoje aquela
       consulta so resolve por scan.

       IX_ChamadasDeIa_Conversa (ConversaId) filtrado e novo: drill-down "todas
       as chamadas desta conversa", hoje sem indice de apoio nenhum.

       Nenhum indice novo por TipoServicoId ou RodadaId: o predicado seletivo
       de toda consulta desta tabela e sempre (TenantId, periodo) ou so
       periodo; agrupar por servico ou contar RodadaId distinto sai do INCLUDE
       dos indices acima. Cada indice a mais pesa no caminho quente — esta
       tabela e escrita de forma sincrona, dentro da resposta ao cliente.

   BACKFILL — RETROATIVO HONESTO
       DescreverFalha (Application/OrquestradorDeProvedores.cs) sempre produz
       "Gemini: {Falha} (...) — ..." para o fallback EXECUTADO (Gemini discou
       e falhou); nenhum motivo preventivo comeca com esse prefixo. Os tres
       motivos preventivos tem texto congelado e conhecido (ver PoliticaDeSelecao
       e ContadorDeCotaDoGemini). O que nao casar com nenhum prefixo conhecido
       vira 5 (Indeterminado) — aparece como numero PROPRIO em qualquer tela
       futura, nunca somado a "executado" nem a "preventivo".

       RodadaId, TipoServicoId, SetorId e MensagemId NAO tem backfill nenhum —
       nao ha como reconstruir de forma confiavel qual pergunta cada tentativa
       antiga respondia. Ficam NULL, e e assim que devem ficar: qualquer tela
       que consumir estes campos precisa tratar NULL como "anterior a esta
       instrumentacao", nunca como zero ou "nao identificado" silencioso.

   IDEMPOTENCIA
       Cada coluna, cada CHECK e cada indice so entram quando ainda nao
       existem. O backfill so atualiza linhas que ainda estao no valor padrao
       (TipoDeFallback = 0 com FoiFallback = 1; MotivoDaNaoResposta NULL com
       Sucesso = 1 e PodeResponder = 0) — rodar de novo nao encontra nada para
       corrigir.
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

IF OBJECT_ID(N'dbo.ChamadasDeIa', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* --- 1. As colunas ---------------------------------------------------------- */

IF COL_LENGTH(N'dbo.ChamadasDeIa', N'TipoDeFallback') IS NULL
    -- 0 NaoEhFallback, 1 Executado, 2 PreventivoDesligado, 3 PreventivoDisjuntor,
    -- 4 PreventivoMargemDeCota, 5 Indeterminado (linha anterior a esta migracao).
    ALTER TABLE dbo.ChamadasDeIa ADD TipoDeFallback TINYINT NOT NULL
        CONSTRAINT DF_ChamadasDeIa_TipoDeFallback DEFAULT (0);
GO

IF COL_LENGTH(N'dbo.ChamadasDeIa', N'RodadaId') IS NULL
    -- Mesmo valor nas duas tentativas de uma pergunta (a que falhou e a que
    -- resolveu) — sem isto nao da para distinguir "tentativa" de "pergunta".
    ALTER TABLE dbo.ChamadasDeIa ADD RodadaId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.ChamadasDeIa', N'MotivoDaNaoResposta') IS NULL
    -- 0 NaoClassificado, 1 PrecoNaoCadastrado, 2 PrazoNaoCadastrado,
    -- 3 DocumentoNaoCadastrado, 4 ServicoForaDoCatalogo,
    -- 5 OrientacaoJuridicaEspecifica, 6 ForaDoEscopoDaServentia,
    -- 7 PerguntaAmbigua, 8 Outro. So preenchido quando Sucesso=1 e
    -- PodeResponder=0 — ver CK_ChamadasDeIa_MotivoDaNaoResposta abaixo.
    ALTER TABLE dbo.ChamadasDeIa ADD MotivoDaNaoResposta TINYINT NULL;
GO

IF COL_LENGTH(N'dbo.ChamadasDeIa', N'TipoServicoId') IS NULL
    -- Snapshot de Conversas.TipoServicoId no INSTANTE da chamada — sem FK,
    -- mesmo motivo de ConversaId (ver 027). Nulo quando a conversa ainda nao
    -- tinha servico definido nesse momento.
    ALTER TABLE dbo.ChamadasDeIa ADD TipoServicoId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.ChamadasDeIa', N'SetorId') IS NULL
    -- Snapshot de Conversas.SetorId no instante da chamada. Sem FK, mesmo motivo.
    ALTER TABLE dbo.ChamadasDeIa ADD SetorId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.ChamadasDeIa', N'MensagemId') IS NULL
    -- A mensagem do cliente que originou esta chamada. Sem FK. Unico campo
    -- novo que aproxima esta tabela de conteudo do cliente — ver nota de
    -- privacidade no cabecalho. A Central de IA nunca projeta esta coluna.
    ALTER TABLE dbo.ChamadasDeIa ADD MensagemId UNIQUEIDENTIFIER NULL;
GO

/* --- 2. Backfill --------------------------------------------------------------

   Precisa de GO separado dos ALTER TABLE acima: o SQL Server so enxerga uma
   coluna criada por ALTER TABLE a partir do proximo lote, mesmo dentro da
   mesma transacao (BEGIN TRANSACTION nao fecha lote, so GO fecha).

   TipoDeFallback: so toca linha com FoiFallback = 1 (as demais ja nascem
   corretas no valor padrao 0). A ordem dos CASE importa — do prefixo mais
   especifico para o mais generico nao seria necessario aqui porque os quatro
   prefixos sao mutuamente exclusivos, mas a clausula ELSE cobre qualquer
   texto historico que nao bata com nenhum — vira Indeterminado, nunca um dos
   quatro valores especificos por suposicao. ------------------------------- */

UPDATE dbo.ChamadasDeIa
   SET TipoDeFallback =
       CASE
           WHEN MotivoDoFallback LIKE N'Gemini: %'                THEN 1 -- Executado
           WHEN MotivoDoFallback LIKE N'Gemini desligado%'        THEN 2 -- PreventivoDesligado
           WHEN MotivoDoFallback LIKE N'Disjuntor do Gemini%'     THEN 3 -- PreventivoDisjuntor
           WHEN MotivoDoFallback LIKE N'Margem de seguranca%'
             OR MotivoDoFallback LIKE N'Margem de segurança%'
             OR MotivoDoFallback LIKE N'Franquia nao cadastrada%'
             OR MotivoDoFallback LIKE N'Franquia não cadastrada%' THEN 4 -- PreventivoMargemDeCota
           ELSE 5 -- Indeterminado
       END
 WHERE FoiFallback = 1
   AND TipoDeFallback = 0;
GO

-- MotivoDaNaoResposta: toda linha gravada antes desta migracao com
-- Sucesso=1 e PodeResponder=0 nao tinha como classificar o motivo — entra
-- como NaoClassificado (0), o mesmo piso que o codigo novo usa quando o
-- provedor nao devolve categoria nenhuma.
UPDATE dbo.ChamadasDeIa
   SET MotivoDaNaoResposta = 0
 WHERE Sucesso = 1
   AND PodeResponder = 0
   AND MotivoDaNaoResposta IS NULL;
GO

/* --- 3. Os CHECKs -------------------------------------------------------------

   So podem ser adicionados DEPOIS do backfill acima, senao a validacao contra
   dado existente (WITH CHECK) falha e a migracao inteira aborta (XACT_ABORT). */

IF OBJECT_ID(N'dbo.CK_ChamadasDeIa_TipoDeFallback', N'C') IS NULL
    ALTER TABLE dbo.ChamadasDeIa WITH CHECK ADD CONSTRAINT CK_ChamadasDeIa_TipoDeFallback
        CHECK
        (
            (FoiFallback = 0 AND TipoDeFallback = 0)
            OR (FoiFallback = 1 AND TipoDeFallback BETWEEN 1 AND 5)
        );
GO

IF OBJECT_ID(N'dbo.CK_ChamadasDeIa_MotivoDaNaoResposta', N'C') IS NULL
    ALTER TABLE dbo.ChamadasDeIa WITH CHECK ADD CONSTRAINT CK_ChamadasDeIa_MotivoDaNaoResposta
        CHECK
        (
            (Sucesso = 1 AND PodeResponder = 0 AND MotivoDaNaoResposta BETWEEN 0 AND 8)
            OR (NOT (Sucesso = 1 AND PodeResponder = 0) AND MotivoDaNaoResposta IS NULL)
        );
GO

/* --- 4. Indices ----------------------------------------------------------- */

-- Recriado com INCLUDE: a tela do cartorio deixa de precisar de key lookup
-- para cada linha do periodo. DROP + CREATE (em vez de IF NOT EXISTS) porque
-- o indice ja existe desde a 027 com forma diferente — recriar de novo numa
-- proxima execucao do script custa so um pouco de CPU, nunca produz forma errada.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChamadasDeIa_Tenant_Iniciada' AND object_id = OBJECT_ID(N'dbo.ChamadasDeIa'))
    DROP INDEX IX_ChamadasDeIa_Tenant_Iniciada ON dbo.ChamadasDeIa;
GO

CREATE INDEX IX_ChamadasDeIa_Tenant_Iniciada ON dbo.ChamadasDeIa (TenantId, IniciadaEm)
    INCLUDE (Provedor, Sucesso, PodeResponder, TipoDeFalha, TipoDeFallback, RodadaId,
             TipoServicoId, SetorId, MotivoDaNaoResposta, LatenciaMs, CustoEstimadoUsd,
             TokensEntrada, TokensSaida, TokensPensamento);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChamadasDeIa_Iniciada' AND object_id = OBJECT_ID(N'dbo.ChamadasDeIa'))
    CREATE INDEX IX_ChamadasDeIa_Iniciada ON dbo.ChamadasDeIa (IniciadaEm)
        INCLUDE (TenantId, Provedor, Sucesso, PodeResponder, TipoDeFalha, TipoDeFallback,
                 RodadaId, LatenciaMs, CustoEstimadoUsd);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChamadasDeIa_Conversa' AND object_id = OBJECT_ID(N'dbo.ChamadasDeIa'))
    CREATE INDEX IX_ChamadasDeIa_Conversa ON dbo.ChamadasDeIa (ConversaId)
        INCLUDE (IniciadaEm, Sucesso, PodeResponder, TipoDeFallback)
        WHERE ConversaId IS NOT NULL;
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'042-analise-da-ia.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'042-analise-da-ia.sql',
                N'ChamadasDeIa ganha TipoDeFallback (executado x preventivo, corrige bug "Caidas no Haiku" vs "nenhum fallback"), MotivoDaNaoResposta (categoria fechada do que falta no catalogo), RodadaId (correlaciona as duas tentativas de uma pergunta) e snapshot TipoServicoId/SetorId/MensagemId. Colunas de correlacao ficam NULL em linha antiga. Indices recriados para cartorio e Central sem scan.');
END
GO

COMMIT TRANSACTION;
GO

SELECT COL_LENGTH(N'dbo.ChamadasDeIa', N'TipoDeFallback')      AS TipoDeFallback,
       COL_LENGTH(N'dbo.ChamadasDeIa', N'RodadaId')            AS RodadaId,
       COL_LENGTH(N'dbo.ChamadasDeIa', N'MotivoDaNaoResposta') AS MotivoDaNaoResposta,
       COL_LENGTH(N'dbo.ChamadasDeIa', N'TipoServicoId')       AS TipoServicoId,
       COL_LENGTH(N'dbo.ChamadasDeIa', N'SetorId')             AS SetorId,
       COL_LENGTH(N'dbo.ChamadasDeIa', N'MensagemId')          AS MensagemId;
GO

SELECT TipoDeFallback, COUNT(*) AS Total
  FROM dbo.ChamadasDeIa
 WHERE FoiFallback = 1
 GROUP BY TipoDeFallback
 ORDER BY TipoDeFallback;
GO

PRINT 'Migracao 042 concluida.';
GO
