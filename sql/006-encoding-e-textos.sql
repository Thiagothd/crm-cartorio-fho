/* =============================================================================
   CRM-Cartorio — Correcao de encoding e redacao dos textos
   -----------------------------------------------------------------------------
   Gerado em .... 2026-08-14
   Uso .......... SOMENTE em banco de DESENVOLVIMENTO (CRM_Cartorio_Dev)
   Depende de ... 001 a 005

   POR QUE ESTE SCRIPT EXISTE
       Nenhum .sql do repositorio tinha BOM UTF-8. Sem o BOM, o sqlcmd le o arquivo
       como ANSI e cada caractere acentuado vira dois no banco — medido:
       LEN(N'Ola — cae') devolvia 15 em vez de 9.

       Resultado pratico: todo texto nao-ASCII ja inserido esta corrompido. A
       descricao da etapa "Em processamento" gravou "pendencias â?" inclusive" em
       vez de "pendências — inclusive", e o mesmo aconteceu com a mensagem de
       triagem do 005.

       A PARTIR DAQUI TODOS OS .SQL DO REPOSITORIO TEM BOM UTF-8. Se voce editar
       um deles, preserve o BOM — VS Code mostra "UTF-8 with BOM" na barra inferior.

   O QUE FAZ
       1. Repara o texto corrompido de EtapasFunil e MensagensAutomaticas.
       2. Corrige a grafia dos cadastros: nome e descricao de setor, etapa e tipo
          de servico passam a ter acento. Sao rotulos que aparecem em toda tela, e
          "Captacao do servico" destoa de um produto que se apresenta como livro de
          registro. O vinculo das regras automaticas e por Id, entao renomear e
          seguro.
       3. Substitui os textos das mensagens automaticas e das respostas rapidas
          pela redacao profissional, com os dados oficiais da serventia
          (docs/CONTEXTO.md, secao 2).
       4. Acrescenta duas respostas rapidas: /escritura e /certidao.

   O QUE NAO FAZ
       Nao apaga conversa, mensagem ou cliente. Nao mexe em Ativo: os dois avisos
       de prazo continuam desativados, como o cartório parceiro decidir depois.

   IDEMPOTENCIA
       Todos os UPDATE sao por Id e gravam valor final — rodar de novo nao muda
       nada. Os INSERT sao guardados por NOT EXISTS.

   ATENCAO
       A redacao adotada e SUPOSICAO defensavel, nao texto aprovado
       (docs/REQUISITOS.md, secao 5.7). O cartório parceiro reescreve tudo pela tela.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME() NOT LIKE '%_Dev%' AND DB_NAME() NOT LIKE '%Dev%'
BEGIN
    RAISERROR('Recusado: o nome do banco atual nao parece ser de desenvolvimento. Revise antes de rodar.', 16, 1);
    RETURN;
END
GO

IF OBJECT_ID(N'dbo.MensagensAutomaticas', N'U') IS NULL
BEGIN
    RAISERROR('Rode 005-mensagens-automaticas.sql antes deste script.', 16, 1);
    RETURN;
END
GO

/* Trava de seguranca do proprio encoding: se este arquivo estiver sendo lido como
   ANSI, o literal abaixo nao tera 9 caracteres e o script se recusa a rodar, em vez
   de gravar mojibake por cima do mojibake que veio consertar. */
IF LEN(N'Olá — çãé') <> 9
BEGIN
    RAISERROR('Recusado: este arquivo nao esta sendo lido como UTF-8. Confirme o BOM ou rode com sqlcmd -f 65001.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

DECLARE @agora    DATETIME2(3)     = SYSUTCDATETIME();
DECLARE @tenantId UNIQUEIDENTIFIER = '0199B2C0-0000-7000-8000-000000000001';
DECLARE @lf       CHAR(1)          = CHAR(10);

/* -----------------------------------------------------------------------------
   1. Setores — grafia
   -------------------------------------------------------------------------- */
UPDATE dbo.Setores SET
    Descricao    = N'Escrituras, procurações, reconhecimento de firma e autenticação.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0001-7000-8000-000000000001';

UPDATE dbo.Setores SET
    Descricao    = N'Nascimento, casamento, óbito, segundas vias e averbações.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0001-7000-8000-000000000002';

/* -----------------------------------------------------------------------------
   2. Etapas — grafia e reparo do travessao corrompido
   -------------------------------------------------------------------------- */
UPDATE dbo.EtapasFunil SET
    Descricao    = N'Triagem: identifica quem é a pessoa e qual é a demanda.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0002-7000-8000-000000000001';

UPDATE dbo.EtapasFunil SET
    Nome         = N'Captação do serviço',
    Descricao    = N'Identifica o tipo de ato e coleta a documentação necessária.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0002-7000-8000-000000000002';

UPDATE dbo.EtapasFunil SET
    Nome         = N'Em processamento / exigência',
    Descricao    = N'Análise e pendências — inclusive espera de terceiro.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0002-7000-8000-000000000003';

UPDATE dbo.EtapasFunil SET
    Descricao    = N'Ato concluído e cliente avisado.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0002-7000-8000-000000000004';

/* -----------------------------------------------------------------------------
   3. Tenant e atendentes — grafia dos nomes proprios
   Os nomes do titular e do substituto sao ficticios; os das duas
   escreventes seguem FICTICIOS (REQUISITOS.md, pendencia 5).
   -------------------------------------------------------------------------- */
UPDATE dbo.Tenants SET
    RazaoSocial  = N'Cartório Antonio Campos Nogueira',
    NomeFantasia = N'Cartório de Registro Civil e Tabelionato de Notas',
    AtualizadoEm = @agora
WHERE Id = @tenantId;

UPDATE dbo.Atendentes SET
    Nome = N'Helena Campos Nogueira', AtualizadoEm = @agora
WHERE Id = '0199B2C0-0003-7000-8000-000000000005';

UPDATE dbo.Atendentes SET
    Nome = N'Rafael Campos Nogueira', AtualizadoEm = @agora
WHERE Id = '0199B2C0-0003-7000-8000-000000000001';

/* -----------------------------------------------------------------------------
   4. Tipos de servico — grafia
   -------------------------------------------------------------------------- */
UPDATE dbo.TiposServico SET Nome = N'Certidão de nascimento',              AtualizadoEm = @agora WHERE Id = '0199B2C0-0004-7000-8000-000000000001';
UPDATE dbo.TiposServico SET Nome = N'Certidão de casamento',               AtualizadoEm = @agora WHERE Id = '0199B2C0-0004-7000-8000-000000000002';
UPDATE dbo.TiposServico SET Nome = N'Certidão de óbito',                   AtualizadoEm = @agora WHERE Id = '0199B2C0-0004-7000-8000-000000000003';
UPDATE dbo.TiposServico SET Nome = N'Segunda via de certidão',             AtualizadoEm = @agora WHERE Id = '0199B2C0-0004-7000-8000-000000000004';
UPDATE dbo.TiposServico SET Nome = N'Autenticação de documento',           AtualizadoEm = @agora WHERE Id = '0199B2C0-0004-7000-8000-000000000006';
UPDATE dbo.TiposServico SET Nome = N'Escritura pública de compra e venda', AtualizadoEm = @agora WHERE Id = '0199B2C0-0004-7000-8000-000000000007';
UPDATE dbo.TiposServico SET Nome = N'Escritura pública de doação',         AtualizadoEm = @agora WHERE Id = '0199B2C0-0004-7000-8000-000000000008';
UPDATE dbo.TiposServico SET Nome = N'Procuração pública',                  AtualizadoEm = @agora WHERE Id = '0199B2C0-0004-7000-8000-000000000009';

UPDATE dbo.TiposServico SET
    Nome      = N'Notificação extrajudicial',
    Descricao = N'AGUARDANDO CONFIRMAÇÃO: verificar com o cartório parceiro se este ato é atribuição desta serventia.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0004-7000-8000-000000000014';

/* -----------------------------------------------------------------------------
   5. Mensagens automaticas — redacao profissional
   Endereco e horario sao os dados oficiais confirmados (CONTEXTO.md, secao 2).
   Ativo NAO e tocado: os dois avisos de prazo continuam desativados.
   -------------------------------------------------------------------------- */
UPDATE dbo.MensagensAutomaticas SET
    Conteudo = N'Olá, {NomeCliente}! Você está falando com o {NomeCartorio}.' + @lf + @lf +
               N'Para encaminharmos seu atendimento ao setor responsável, responda com o número da opção desejada:' + @lf + @lf +
               N'{MenuSetores}' + @lf + @lf +
               N'Se preferir, descreva sua solicitação que um de nossos atendentes dará continuidade.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0006-7000-8000-000000000001';

UPDATE dbo.MensagensAutomaticas SET
    Conteudo = N'Obrigado pelo contato, {NomeCliente}. Um de nossos atendentes assumirá sua solicitação em instantes.' + @lf + @lf +
               N'Seu atendimento foi registrado sob o protocolo {Protocolo}. Guarde esse número para acompanhamento.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0006-7000-8000-000000000002';

UPDATE dbo.MensagensAutomaticas SET
    Conteudo = N'{NomeCliente}, seu atendimento foi concluído.' + @lf + @lf +
               N'Protocolo: {Protocolo}' + @lf +
               N'Serviço: {Servico}' + @lf + @lf +
               N'A retirada pode ser feita no {NomeCartorio}, na Praça São Francisco, nº 70, Centro, Gameleiras/MG, de segunda a sexta-feira, das 8h às 17h.' + @lf + @lf +
               N'Pedimos que apresente um documento de identificação com foto no balcão.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0006-7000-8000-000000000003';

UPDATE dbo.MensagensAutomaticas SET
    Conteudo = N'{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) segue em andamento e se aproxima do prazo previsto para conclusão.' + @lf + @lf +
               N'Assim que houver novidade, comunicaremos por aqui. Se precisar de alguma informação, basta responder a esta mensagem.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0006-7000-8000-000000000004';

UPDATE dbo.MensagensAutomaticas SET
    Conteudo = N'{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) ultrapassou o prazo inicialmente previsto e está com prioridade em nossa fila.' + @lf + @lf +
               N'Pedimos desculpas pelo transtorno. Assim que houver definição, informaremos por aqui.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0006-7000-8000-000000000005';

/* -----------------------------------------------------------------------------
   6. Respostas rapidas — redacao profissional
   -------------------------------------------------------------------------- */
UPDATE dbo.RespostasRapidas SET
    Titulo   = N'Horário e endereço',
    Conteudo = N'O {NomeCartorio} atende de segunda a sexta-feira, das 8h às 17h, na Praça São Francisco, nº 70, Centro, Gameleiras/MG, CEP 39505-000.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0007-7000-8000-000000000001';

UPDATE dbo.RespostasRapidas SET
    Titulo   = N'Situação do protocolo',
    Conteudo = N'Seu atendimento está registrado sob o protocolo {Protocolo}, referente a: {Servico}.' + @lf + @lf +
               N'Assim que houver novidade, informaremos por aqui.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0007-7000-8000-000000000002';

UPDATE dbo.RespostasRapidas SET
    Titulo   = N'Emolumentos',
    Conteudo = N'Os valores dos atos seguem a Tabela de Emolumentos do Tribunal de Justiça de Minas Gerais, definida por lei estadual — não é preço fixado pelo cartório.' + @lf + @lf +
               N'Vou verificar o valor exato aplicável ao seu caso e retorno em seguida.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0007-7000-8000-000000000003';

UPDATE dbo.RespostasRapidas SET
    Titulo   = N'Reconhecimento de firma',
    Conteudo = N'Para o reconhecimento de firma, é necessário que sua ficha de assinatura esteja aberta neste cartório.' + @lf + @lf +
               N'Caso ainda não possua, basta comparecer com documento de identificação original com foto para abertura na hora.',
    AtualizadoEm = @agora
WHERE Id = '0199B2C0-0007-7000-8000-000000000004';

IF NOT EXISTS (SELECT 1 FROM dbo.RespostasRapidas WHERE Id = '0199B2C0-0007-7000-8000-000000000005')
    INSERT INTO dbo.RespostasRapidas (Id, TenantId, Atalho, Titulo, Conteudo, Ativo, CriadoEm, AtualizadoEm)
    VALUES ('0199B2C0-0007-7000-8000-000000000005', @tenantId, 'escritura', N'Documentos para escritura',
        N'Para lavrarmos uma escritura pública, precisamos de:' + @lf + @lf +
        N'- RG e CPF das partes (e do cônjuge, quando casado)' + @lf +
        N'- Certidão de casamento atualizada, se for o caso' + @lf +
        N'- Comprovante de endereço' + @lf +
        N'- Documentação do bem objeto do ato' + @lf + @lf +
        N'A relação varia conforme o tipo de escritura. Confirmo os detalhes do seu caso e retorno.',
        1, @agora, @agora);

IF NOT EXISTS (SELECT 1 FROM dbo.RespostasRapidas WHERE Id = '0199B2C0-0007-7000-8000-000000000006')
    INSERT INTO dbo.RespostasRapidas (Id, TenantId, Atalho, Titulo, Conteudo, Ativo, CriadoEm, AtualizadoEm)
    VALUES ('0199B2C0-0007-7000-8000-000000000006', @tenantId, 'certidao', N'Dados para emitir certidão',
        N'Para localizarmos o registro e emitirmos a certidão, precisamos de:' + @lf + @lf +
        N'- Nome completo do registrado' + @lf +
        N'- Data do fato (nascimento, casamento ou óbito)' + @lf +
        N'- Nome dos pais, se souber' + @lf + @lf +
        N'Com esses dados fazemos a busca no livro e informamos o prazo de emissão.',
        1, @agora, @agora);
GO

COMMIT TRANSACTION;
GO

/* Conferencia: nenhuma linha pode conter o padrao de mojibake. */
SELECT
    (SELECT COUNT(*) FROM dbo.EtapasFunil        WHERE Descricao LIKE N'%â%' OR Nome LIKE N'%â%') AS EtapasCorrompidas,
    (SELECT COUNT(*) FROM dbo.MensagensAutomaticas WHERE Conteudo LIKE N'%â%')                     AS MensagensCorrompidas,
    (SELECT COUNT(*) FROM dbo.RespostasRapidas   WHERE Conteudo LIKE N'%â%')                       AS RespostasCorrompidas,
    (SELECT COUNT(*) FROM dbo.RespostasRapidas)                                                    AS TotalRespostas,
    (SELECT COUNT(*) FROM dbo.MensagensAutomaticas WHERE Ativo = 1)                                AS RegrasAtivas;
GO

PRINT 'Migracao 006 concluida. Os avisos de prazo continuam DESATIVADOS.';
PRINT 'A redacao adotada e suposicao documentada — o cartório parceiro reescreve pela tela.';
GO
