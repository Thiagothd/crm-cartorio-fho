/* =============================================================================
   CRM-Cartorio — Emoji pontual nas mensagens que o cliente le
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-17
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001-schema.sql, 002-seed-simulado.sql, 005-mensagens-automaticas.sql,
                  015-checklist-e-mensagens-de-servico.sql

   O QUE FAZ
       Atualiza o CONTEUDO das 8 mensagens automaticas e das 6 respostas rapidas
       ja cadastradas, acrescentando emoji pontual e com proposito (endereco,
       documento, prazo, confirmacao) — decisao do Thiago em 2026-08-17, depois
       de comparar com o fluxo de um concorrente real (docs/CONVENCOES.md regra 8,
       DESIGN.md secao 4).

   O QUE NAO MUDA
       Gatilho, EtapaAlvoId e Ativo de cada linha continuam exatamente como
       estavam — PrazoPerto e PrazoVencido continuam desativadas (decisao do
       cartório parceiro, docs/REQUISITOS.md 5.7), so o texto delas foi atualizado para
       quando ele ligar.

   ESPELHO OBRIGATORIO
       O mesmo conteudo esta em
       Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs — os dois
       precisam bater caractere a caractere, mesma disciplina de 002/015.

   IDEMPOTENCIA
       UPDATE por Id e sempre idempotente: rodar de novo grava o mesmo texto.
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

IF OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL OR OBJECT_ID(N'dbo.RespostasRapidas', N'U') IS NULL
BEGIN
    RAISERROR('Schema base ausente. Rode as migracoes anteriores antes deste script.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

DECLARE @agora DATETIME2(3) = SYSUTCDATETIME();
DECLARE @lf    CHAR(1)      = CHAR(10);

-- 1. Mensagens automaticas -------------------------------------------------

UPDATE dbo.MensagensAutomaticas SET AtualizadoEm = @agora, Conteudo =
    N'Olá, {NomeCliente}! 👋 Você está falando com o {NomeCartorio}.' + @lf + @lf +
    N'Para encaminharmos seu atendimento ao setor responsável, responda com o número da opção desejada:' + @lf + @lf +
    N'{MenuSetores}' + @lf + @lf +
    N'Se preferir, descreva sua solicitação que um de nossos atendentes dará continuidade.'
WHERE Id = '0199B2C0-0006-7000-8000-000000000001';

UPDATE dbo.MensagensAutomaticas SET AtualizadoEm = @agora, Conteudo =
    N'Obrigado pelo contato, {NomeCliente}! 🙏 Um de nossos atendentes assumirá sua solicitação em instantes.' + @lf + @lf +
    N'📋 Protocolo: {Protocolo}' + @lf +
    N'Guarde esse número para acompanhamento.'
WHERE Id = '0199B2C0-0006-7000-8000-000000000002';

UPDATE dbo.MensagensAutomaticas SET AtualizadoEm = @agora, Conteudo =
    N'{NomeCliente}, seu atendimento foi concluído! ✅' + @lf + @lf +
    N'📋 Protocolo: {Protocolo}' + @lf +
    N'📄 Serviço: {Servico}' + @lf + @lf +
    N'📍 A retirada pode ser feita no {NomeCartorio}, na Praça São Francisco, nº 70, Centro, Gameleiras/MG, de segunda a sexta-feira, das 8h às 17h.' + @lf + @lf +
    N'Pedimos que apresente um documento de identificação com foto no balcão.'
WHERE Id = '0199B2C0-0006-7000-8000-000000000003';

UPDATE dbo.MensagensAutomaticas SET AtualizadoEm = @agora, Conteudo =
    N'{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) segue em andamento e se aproxima do prazo previsto para conclusão. ⏳' + @lf + @lf +
    N'Assim que houver novidade, comunicaremos por aqui. Se precisar de alguma informação, basta responder a esta mensagem.'
WHERE Id = '0199B2C0-0006-7000-8000-000000000004';

UPDATE dbo.MensagensAutomaticas SET AtualizadoEm = @agora, Conteudo =
    N'{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) ultrapassou o prazo inicialmente previsto e está com prioridade em nossa fila. ⚠️' + @lf + @lf +
    N'Pedimos desculpas pelo transtorno. Assim que houver definição, informaremos por aqui.'
WHERE Id = '0199B2C0-0006-7000-8000-000000000005';

UPDATE dbo.MensagensAutomaticas SET AtualizadoEm = @agora, Conteudo =
    N'Certo, {NomeCliente}! ✅ Qual destes serviços você precisa?' + @lf + @lf +
    N'{MenuServicos}' + @lf + @lf +
    N'Se preferir, descreva o que precisa que um de nossos atendentes dará continuidade.'
WHERE Id = '0199B2C0-0006-7000-8000-000000000006';

UPDATE dbo.MensagensAutomaticas SET AtualizadoEm = @agora, Conteudo =
    N'Perfeito, {NomeCliente}! ✅ Localizamos seu atendimento de {Servico}, protocolo {Protocolo}.' + @lf + @lf +
    N'📄 Documentos necessários:' + @lf + N'{DocumentosNecessarios}' + @lf + @lf +
    N'⏱️ Prazo previsto: {PrazoServico}' + @lf + @lf +
    N'Assim que possível, providencie a documentação e traga ao {NomeCartorio}. Qualquer dúvida, é só responder por aqui.'
WHERE Id = '0199B2C0-0006-7000-8000-000000000007';

UPDATE dbo.MensagensAutomaticas SET AtualizadoEm = @agora, Conteudo =
    N'{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) está em análise. 🔎' + @lf + @lf +
    N'⏱️ Prazo previsto: {PrazoServico}' + @lf +
    N'Se houver alguma pendência, entraremos em contato por aqui.'
WHERE Id = '0199B2C0-0006-7000-8000-000000000008';

-- 2. Respostas rapidas ------------------------------------------------------

UPDATE dbo.RespostasRapidas SET AtualizadoEm = @agora, Conteudo =
    N'📍 O {NomeCartorio} atende de segunda a sexta-feira, das 8h às 17h, na Praça São Francisco, nº 70, Centro, Gameleiras/MG, CEP 39505-000.'
WHERE Id = '0199B2C0-0007-7000-8000-000000000001';

UPDATE dbo.RespostasRapidas SET AtualizadoEm = @agora, Conteudo =
    N'📋 Seu atendimento está registrado sob o protocolo {Protocolo}, referente a: {Servico}.' + @lf + @lf +
    N'Assim que houver novidade, informaremos por aqui.'
WHERE Id = '0199B2C0-0007-7000-8000-000000000002';

UPDATE dbo.RespostasRapidas SET AtualizadoEm = @agora, Conteudo =
    N'💰 Os valores dos atos seguem a Tabela de Emolumentos do Tribunal de Justiça de Minas Gerais, definida por lei estadual — não é preço fixado pelo cartório.' + @lf + @lf +
    N'Vou verificar o valor exato aplicável ao seu caso e retorno em seguida.'
WHERE Id = '0199B2C0-0007-7000-8000-000000000003';

UPDATE dbo.RespostasRapidas SET AtualizadoEm = @agora, Conteudo =
    N'✍️ Para o reconhecimento de firma, é necessário que sua ficha de assinatura esteja aberta neste cartório.' + @lf + @lf +
    N'Caso ainda não possua, basta comparecer com documento de identificação original com foto para abertura na hora.'
WHERE Id = '0199B2C0-0007-7000-8000-000000000004';

UPDATE dbo.RespostasRapidas SET AtualizadoEm = @agora, Conteudo =
    N'📄 Para lavrarmos uma escritura pública, precisamos de:' + @lf + @lf +
    N'- RG e CPF das partes (e do cônjuge, quando casado)' + @lf +
    N'- Certidão de casamento atualizada, se for o caso' + @lf +
    N'- Comprovante de endereço' + @lf +
    N'- Documentação do bem objeto do ato' + @lf + @lf +
    N'A relação varia conforme o tipo de escritura. Confirmo os detalhes do seu caso e retorno.'
WHERE Id = '0199B2C0-0007-7000-8000-000000000005';

UPDATE dbo.RespostasRapidas SET AtualizadoEm = @agora, Conteudo =
    N'📄 Para localizarmos o registro e emitirmos a certidão, precisamos de:' + @lf + @lf +
    N'- Nome completo do registrado' + @lf +
    N'- Data do fato (nascimento, casamento ou óbito)' + @lf +
    N'- Nome dos pais, se souber' + @lf + @lf +
    N'Com esses dados fazemos a busca no livro e informamos o prazo de emissão.'
WHERE Id = '0199B2C0-0007-7000-8000-000000000006';

COMMIT TRANSACTION;
GO

SELECT Gatilho, LEFT(Conteudo, 50) AS InicioDoTexto FROM dbo.MensagensAutomaticas ORDER BY Gatilho;
GO

PRINT 'Migracao 016 concluida.';
GO
