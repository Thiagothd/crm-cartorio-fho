/* =============================================================================
   CRM-Cartorio — Conhecimento inicial da IA (universal + rascunhos)
   -----------------------------------------------------------------------------
   Gerado em .... 2026-09-04
   Uso .......... CRM_Cartorio, CRM_Cartorio_Teste, CRM_Cartorio_Dev
   Depende de ... 039-conhecimento-de-ia.sql

   O QUE FAZ
       Insere 5 itens em dbo.ConhecimentosDeIa para o tenant do piloto:
         3 ATIVOS   — conhecimento genuinamente universal (vale pra qualquer
                      cartorio, nao so este), pra a tela nao nascer vazia e a
                      IA ja ter grounding util desde o primeiro dia.
         2 RASCUNHO (Ativo=0) — placeholders de conhecimento ESPECIFICO da
                      serventia, que dependem de confirmacao do cartório parceiro. Ficam
                      visiveis na tela, mas fora do grounding ate serem
                      revisados e ativados manualmente.

   POR QUE SO ISTO E' "UNIVERSAL"
       O catalogo de servicos (TiposServico.DocumentosNecessarios/Prazo) ja
       cobre documento e prazo POR SERVICO, e a RespostaRapida "/horario" ja
       cobre endereco/horario deste cartorio especificamente. O que sobra
       como realmente universal — verdadeiro pra qualquer serventia, sem
       chute de regra local — e pouco: documento de identidade generico,
       emolumento variavel por Estado, e o texto padrao de LGPD.
       Decisao do Thiago (2026-09-04): preencher com a melhor suposicao
       defensavel documentada como tal (regra 13 do docs/CONVENCOES.md), sem deixar a
       tela nova vazia, e ajustar com o cartório parceiro depois.

   RISCO QUE O ITEM DE EMOLUMENTO EXISTE PRA CONTER
       Sem orientacao explicita, um modelo de linguagem tende a "chutar" um
       valor plausivel quando perguntado o preco de um servico — exatamente
       o risco que este cadastro existe para conter. O PromptDeSistema ja
       proibe inventar; dar a instrucao CERTA no grounding e melhor do que
       so proibir a errada.

   ORIGEM DOS REGISTROS
       CriadoPorAtendenteId fica NULO — migracao retroativa, nao cadastro
       manual (mesmo padrao documentado no XML-doc de ConhecimentoDeIa.cs).

   IDEMPOTENCIA
       Guardado por um IF EXISTS no primeiro Id novo, mesmo padrao de 018.
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

IF OBJECT_ID(N'dbo.ConhecimentosDeIa', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode 039-conhecimento-de-ia.sql antes deste script.', 16, 1);
    RETURN;
END
GO

IF EXISTS (SELECT 1 FROM dbo.ConhecimentosDeIa WHERE Id = '0199B2C0-0008-7000-8000-000000000001')
BEGIN
    PRINT 'Migracao 040 ja aplicada — nada a fazer.';
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

DECLARE @agora    DATETIME2(3)     = SYSUTCDATETIME();
DECLARE @tenantId UNIQUEIDENTIFIER = '0199B2C0-0000-7000-8000-000000000001';

INSERT INTO dbo.ConhecimentosDeIa
    (Id, TenantId, Titulo, Conteudo, Tema, TipoServicoId, VigenteDesde, VigenteAte,
     Versao, CriadoPorAtendenteId, AtualizadoPorAtendenteId, Ativo, CriadoEm, AtualizadoEm)
VALUES
    -- ATIVO 1/3 — documento de identidade, universal a qualquer ato
    ('0199B2C0-0008-7000-8000-000000000001', @tenantId,
     N'Documento de identidade e CPF exigidos em qualquer atendimento presencial',
     N'Para qualquer ato no cartório é necessário apresentar documento oficial de identificação com foto (RG, CNH ou passaporte) e CPF, ambos originais. Sem esses documentos o atendimento presencial não pode ser realizado.',
     N'Documentação geral', NULL, NULL, NULL, 1, NULL, NULL, 1, @agora, @agora),

    -- ATIVO 2/3 — emolumento variavel, evita a IA chutar preco
    ('0199B2C0-0008-7000-8000-000000000002', @tenantId,
     N'Valor do serviço (emolumento) varia por ato e é tabelado pelo Estado',
     N'O valor cobrado por cada ato (emolumento) segue a tabela oficial do Tribunal de Justiça do Estado e varia conforme o serviço solicitado. Não é possível informar um valor genérico pelo chat; para saber o valor exato de um serviço específico, encaminhe o cliente a um atendente.',
     N'Valores e emolumentos', NULL, NULL, NULL, 1, NULL, NULL, 1, @agora, @agora),

    -- ATIVO 3/3 — LGPD, texto padrao
    ('0199B2C0-0008-7000-8000-000000000003', @tenantId,
     N'Uso dos dados pessoais no atendimento pelo WhatsApp',
     N'Os dados fornecidos pelo cliente neste canal são usados apenas para realizar o atendimento solicitado, conforme a Lei Geral de Proteção de Dados (LGPD), e não são compartilhados com terceiros além do necessário para prestar o serviço.',
     N'Dados pessoais e LGPD', NULL, NULL, NULL, 1, NULL, NULL, 1, @agora, @agora),

    -- RASCUNHO 1/2 — atribuicao ainda incerta (docs/REQUISITOS.md, secao 5)
    ('0199B2C0-0008-7000-8000-000000000004', @tenantId,
     N'[RASCUNHO] Este cartório realiza Notificação Extrajudicial?',
     N'[RASCUNHO — aguardando confirmação do cartório parceiro, ver docs/REQUISITOS.md, seção 5] Ainda não está confirmado se a Notificação Extrajudicial é atribuição desta serventia. Não ativar este item, nem afirmar que o serviço é oferecido, antes dessa confirmação.',
     N'Notificação Extrajudicial', NULL, NULL, NULL, 1, NULL, NULL, 0, @agora, @agora),

    -- RASCUNHO 2/2 — regra local de atendimento presencial, ainda nao definida
    ('0199B2C0-0008-7000-8000-000000000005', @tenantId,
     N'[RASCUNHO] Atendimento presencial exige agendamento prévio?',
     N'[RASCUNHO — aguardando confirmação do cartório parceiro] Ainda não está confirmado se o atendimento presencial no balcão exige agendamento prévio ou é por ordem de chegada. Preencher e ativar depois de confirmar com o titular ou substituto.',
     N'Agendamento', NULL, NULL, NULL, 1, NULL, NULL, 0, @agora, @agora);
GO

IF OBJECT_ID(N'dbo.VersaoDoSchema', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.VersaoDoSchema WHERE Script = N'040-conhecimento-de-ia-conteudo-inicial.sql')
        INSERT INTO dbo.VersaoDoSchema (Script, Observacao)
        VALUES (N'040-conhecimento-de-ia-conteudo-inicial.sql',
                N'Conteudo inicial de dbo.ConhecimentosDeIa: 3 itens universais ativos (documento+CPF, emolumento variavel, LGPD) e 2 rascunhos inativos aguardando confirmacao do cartório parceiro (Notificacao Extrajudicial, agendamento). Suposicao defensavel, editavel pela tela (regra 13 do docs/CONVENCOES.md).');
END
GO

COMMIT TRANSACTION;
GO

SELECT Titulo, Tema, Ativo FROM dbo.ConhecimentosDeIa WHERE TenantId = '0199B2C0-0000-7000-8000-000000000001' ORDER BY Ativo DESC, Tema;
GO

PRINT 'Migracao 040 concluida.';
GO
