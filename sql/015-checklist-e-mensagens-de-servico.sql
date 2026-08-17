/* =============================================================================
   CRM-Cartorio — Checklist de documento + mensagens do fluxo de servico
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-17
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql, 002-seed-simulado.sql, 013-catalogo-atos-candidatos.sql,
                  014-servico-por-setor-e-checklist.sql

   O QUE FAZ
       Amplia CK_MensagensAutomaticas_Gatilho (001-schema.sql) para aceitar os
       gatilhos 6 e 7, novos nesta fase — sem isso o INSERT abaixo falha com
       violacao de CHECK. Preenche TiposServico.DocumentosNecessarios para os 19
       servicos do catalogo e insere as 3 mensagens automaticas novas da Fase 2D:
       o convite para escolher o servico dentro do setor
       (TriagemComCorrespondencia), a confirmacao com checklist e prazo quando o
       servico e identificado (ServicoEscolhido), e o aviso de "em analise" na
       etapa que ainda ficava sem mensagem (EtapaAlterada / Em processamento).

   ESPELHO OBRIGATORIO
       O mesmo conteudo esta em
       Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs — os dois
       precisam bater caractere a caractere, mesma disciplina de 002.

   SUPOSICAO DEFENSAVEL
       O checklist de documento e calibrado contra pratica cartoraria comum, nao
       dado juridico caso a caso (docs/REQUISITOS.md, 5.7) — editavel pelo cartório parceiro
       na tela de Servicos.

   IDEMPOTENCIA
       A constraint e sempre DROP + ADD, o que da no mesmo resultado rodando uma
       ou dez vezes. O UPDATE do checklist so toca linha ainda com
       DocumentosNecessarios nulo, para nao sobrescrever edicao manual feita
       entre uma execucao e outra. O INSERT das mensagens e guardado por um IF
       NOT EXISTS no primeiro Id novo.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.TiposServico', N'U') IS NULL OR OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* Constraint antiga so aceitava 1-5 (001-schema.sql, quando so existiam os
   gatilhos da Fase 1/2B). DROP + ADD e idempotente por natureza: rodar de novo
   so recria a mesma regra. */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MensagensAutomaticas_Gatilho')
    ALTER TABLE dbo.MensagensAutomaticas DROP CONSTRAINT CK_MensagensAutomaticas_Gatilho;
GO

ALTER TABLE dbo.MensagensAutomaticas ADD CONSTRAINT CK_MensagensAutomaticas_Gatilho
    CHECK (Gatilho IN (1, 2, 3, 4, 5, 6, 7));
GO

DECLARE @agora    DATETIME2(3)     = SYSUTCDATETIME();
DECLARE @tenantId UNIQUEIDENTIFIER = '0199B2C0-0000-7000-8000-000000000001';
DECLARE @lf       CHAR(1)          = CHAR(10);

/* -----------------------------------------------------------------------------
   1. Checklist de documento por servico — so entra onde ainda esta nulo.
   -------------------------------------------------------------------------- */
UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'Documento de identificação com foto de quem solicita. Se a certidão for de outra pessoa, informe também o nome completo e, se possível, a data do ato ou o número do registro.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id IN ('0199B2C0-0004-7000-8000-000000000001', '0199B2C0-0004-7000-8000-000000000002',
             '0199B2C0-0004-7000-8000-000000000003', '0199B2C0-0004-7000-8000-000000000004');

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'Documento de identificação original com foto. É preciso ter ficha de assinatura já aberta neste cartório — sem isso, é necessário comparecer pessoalmente antes.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000005';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'Documento original e a cópia a ser autenticada.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000006';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'RG e CPF das partes, certidão atualizada do imóvel e comprovante de residência. Certidões negativas adicionais podem ser exigidas conforme o caso — confirme com o cartório antes de reunir a documentação.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000007';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'RG e CPF do doador e do donatário, documento do bem a ser doado e comprovante de residência.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000008';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'RG e CPF de quem outorga a procuração. Se possível, informe também nome completo e CPF de quem vai receber os poderes.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000009';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'RG e CPF dos dois noivos, certidão de nascimento (ou de casamento anterior já com a averbação de divórcio ou óbito) e comprovante de residência. É preciso também indicar duas testemunhas maiores de idade, com documento de identificação.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000010';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'Documento de identificação com foto de quem solicita, o registro original a ser averbado e o documento que comprova o fato — sentença, escritura ou certidão, conforme o caso.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000011';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'Declaração de Nascido Vivo (DNV) fornecida pela maternidade ou hospital, documento de identificação dos pais e comprovante de residência.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000012';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'Declaração de Óbito fornecida pelo médico ou pelo IML, documento de identificação do falecido, quando houver, e de quem está declarando.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000013';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'Documento de identificação com foto e a descrição do fato a ser constatado pelo tabelião.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000015';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'RG e CPF de quem testa, documento de identificação de duas testemunhas, e a relação dos bens e dos herdeiros.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000016';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'RG e CPF de todos os herdeiros, certidão de óbito, certidão de casamento do falecido (se houver) e documentos dos bens a partilhar. Também é exigida certidão negativa de débitos do ITCMD.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000017';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'RG e CPF de ambos os cônjuges e certidão de casamento. O divórcio consensual em cartório só é possível sem filho menor ou incapaz do casal.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000018';

UPDATE dbo.TiposServico SET DocumentosNecessarios =
    N'RG, CPF e comprovante de residência de ambos, e duas testemunhas com documento de identificação.',
    AtualizadoEm = @agora
WHERE TenantId = @tenantId AND DocumentosNecessarios IS NULL
  AND Id = '0199B2C0-0004-7000-8000-000000000019';

/* -----------------------------------------------------------------------------
   2. Mensagens automaticas novas
   -------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.MensagensAutomaticas WHERE Id = '0199B2C0-0006-7000-8000-000000000006')
BEGIN
    INSERT INTO dbo.MensagensAutomaticas
        (Id, TenantId, Gatilho, EtapaAlvoId, Conteudo, Ativo, CriadoEm, AtualizadoEm)
    VALUES
        -- Gatilho 6 = TriagemComCorrespondencia
        ('0199B2C0-0006-7000-8000-000000000006', @tenantId, 6, NULL,
         N'Certo, {NomeCliente}! Qual destes serviços você precisa?' + @lf + @lf +
         N'{MenuServicos}' + @lf + @lf +
         N'Se preferir, descreva o que precisa que um de nossos atendentes dará continuidade.',
         1, @agora, @agora),

        -- Gatilho 7 = ServicoEscolhido
        ('0199B2C0-0006-7000-8000-000000000007', @tenantId, 7, NULL,
         N'Perfeito, {NomeCliente}! Localizamos seu atendimento de {Servico}, protocolo {Protocolo}.' + @lf + @lf +
         N'Documentos necessários:' + @lf + N'{DocumentosNecessarios}' + @lf + @lf +
         N'Prazo previsto: {PrazoServico}.' + @lf + @lf +
         N'Assim que possível, providencie a documentação e traga ao {NomeCartorio}. Qualquer dúvida, é só responder por aqui.',
         1, @agora, @agora),

        -- Gatilho 2 = EtapaAlterada, etapa "Em processamento / exigência"
        ('0199B2C0-0006-7000-8000-000000000008', @tenantId, 2, '0199b2c0-0002-7000-8000-000000000003',
         N'{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) está em análise.' + @lf + @lf +
         N'Prazo previsto: {PrazoServico}. Se houver alguma pendência, entraremos em contato por aqui.',
         1, @agora, @agora);
END
GO

COMMIT TRANSACTION;
GO

SELECT Nome, LEFT(DocumentosNecessarios, 40) AS InicioDoChecklist FROM dbo.TiposServico ORDER BY Nome;
GO

PRINT 'Migracao 015 concluida.';
GO
