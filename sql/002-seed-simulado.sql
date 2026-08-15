/* =============================================================================
   CRM-Cartorio — Configuracao de demonstracao da Fase 1
   -----------------------------------------------------------------------------
   Atualizado em  2026-08-13 — dados reais da serventia (perfil oficial do CNJ),
                  substituindo as suposicoes da versao anterior.
   Depende de ... 001-schema.sql

   O QUE ESTE SCRIPT INSERE
       Tenant, atendentes, setores, etapas de funil, catalogo de servicos, SLAs,
       mensagens automaticas e respostas rapidas.

   O QUE ELE NAO INSERE
       Clientes, conversas e mensagens ficticias. Esses vem do semeador da
       aplicacao (Seed:Habilitado = true), porque dependem de datas relativas
       ao momento da demonstracao — uma conversa "atrasada" gravada em SQL fixo
       envelhece e deixa de fazer sentido em duas semanas.

   IDENTIFICADORES
       Os GUIDs abaixo sao os mesmos de
       Infrastructure/Persistence/Seeding/IdentificadoresDeDemonstracao.cs e
       CatalogoDeDemonstracao.cs. Se mudar aqui, mude la tambem.

   ATENCAO — SENHA
       Todos os atendentes nascem com a senha:  cartorio2026
       E senha de DEMONSTRACAO. Trocar antes de qualquer uso real.

   PENDENCIAS DE CONFIRMACAO (docs/REQUISITOS.md, secao 5)
       - CNPJ informado tem 13 digitos; CNPJ tem 14.
       - Telefone informado tem 10 digitos; celular com DDD tem 11.
       - Nomes de "Carla Menezes" e "Paula Ribeiro" sao FICTICIOS.
       - "Notificacao extrajudicial" pode nao ser atribuicao desta serventia.
       - O TEXTO das mensagens automaticas e das respostas rapidas e RASCUNHO.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

DECLARE @agora     DATETIME2(3)     = SYSUTCDATETIME();
DECLARE @tenantId  UNIQUEIDENTIFIER = '0199B2C0-0000-7000-8000-000000000001';

/* Hash PBKDF2-HMAC-SHA256, 210.000 iteracoes, salt proprio.
   Formato: pbkdf2.sha256$<iteracoes>$<salt b64>$<hash b64> */
DECLARE @senhaHash VARCHAR(500) =
    'pbkdf2.sha256$210000$SQI4QIoE1PHwXFgiPn8a7A==$k3NxLT2V5SnEp2xBpTR2IauRcKyqq9qfwulk7GMjSMg=';

/* -----------------------------------------------------------------------------
   1. Tenant — dados oficiais da serventia
   -----------------------------------------------------------------------------
   Nome oficial ... Cartorio Antonio Campos Nogueira
   Fantasia ....... Cartorio de Registro Civil e Tabelionato de Notas
   CNS ............ 04.679-7
   Endereco ....... Praca Sao Francisco, 70, Centro, Gameleiras/MG, 39505-000
   Contato ........ (38) 99977-891 | cartoriogameleiras@gmail.com
   Horario ........ 2a a 6a, 8h as 17h
   Jurisdicao ..... inclui o Distrito de Jacu das Piranhas
   Titular ........ Helena Campos Nogueira
   Substituto ..... Rafael Campos Nogueira

   O modelo da Fase 1 nao tem colunas para CNS, endereco, horario nem jurisdicao.
   Esses dados ficam registrados aqui e em docs/CONTEXTO.md; entram no banco
   quando houver tela de configuracao da serventia (nao e escopo desta fase).
   -------------------------------------------------------------------------- */
INSERT INTO dbo.Tenants
    (Id, RazaoSocial, NomeFantasia, Cnpj,
     WhatsappBusinessAccountId, WhatsappPhoneNumberId, WhatsappNumeroExibicao,
     Ativo, CriadoEm, AtualizadoEm)
VALUES
    (@tenantId,
     N'Cartório Antonio Campos Nogueira',
     N'Cartório de Registro Civil e Tabelionato de Notas',

     -- Informado como 21.35.122/6000-10: 13 digitos, um a menos que os 14 de um
     -- CNPJ. Gravado como veio, para conferencia contra a pagina oficial.
     '2135122600010',

     NULL,                      -- WABA ID: so na Fase 2
     NULL,                      -- Phone Number ID: so na Fase 2

     -- Informado como (38) 99977-891: 10 digitos; celular com DDD tem 11.
     '+55 38 99977-891',

     1, @agora, @agora);

/* -----------------------------------------------------------------------------
   2. Setores — eixo de ROTEAMENTO
   -----------------------------------------------------------------------------
   APENAS DUAS ATRIBUICOES. A serventia e de Notas + Registro Civil.
   Registro de Imoveis, Titulos e Documentos e Protesto foram REMOVIDOS: estavam
   na versao anterior por suposicao de oficio unico, que a consulta ao CNJ negou.
   -------------------------------------------------------------------------- */
INSERT INTO dbo.Setores (Id, TenantId, Nome, Descricao, Ordem, Ativo, CriadoEm, AtualizadoEm)
VALUES
    ('0199B2C0-0001-7000-8000-000000000001', @tenantId, N'Tabelionato de Notas',
     N'Escrituras, procurações, reconhecimento de firma e autenticação.', 1, 1, @agora, @agora),

    ('0199B2C0-0001-7000-8000-000000000002', @tenantId, N'Registro Civil',
     N'Nascimento, casamento, óbito, segundas vias e averbações.', 2, 1, @agora, @agora);

/* -----------------------------------------------------------------------------
   3. Etapas do funil — eixo de PROGRESSAO
   AGUARDA VALIDACAO DO CARTÓRIO PARCEIRO (REQUISITOS.md, pendencias 2 e 8).
   A coluna Cor guarda a cor de TINTA da etapa, conforme a paleta de etapas de
   docs/DESIGN.md, secao 2.2. O fundo e a borda de cada pilula sao derivados
   dessa tinta no frontend — nao ha coluna para eles.
   -------------------------------------------------------------------------- */
INSERT INTO dbo.EtapasFunil (Id, TenantId, Nome, Descricao, Ordem, Cor, EhFinal, Ativo, CriadoEm, AtualizadoEm)
VALUES
    -- azul acinzentado
    ('0199B2C0-0002-7000-8000-000000000001', @tenantId, N'Atendimento inicial',
     N'Triagem: identifica quem é a pessoa e qual é a demanda.', 1, '#2E5170', 0, 1, @agora, @agora),

    -- roxo suave
    ('0199B2C0-0002-7000-8000-000000000002', @tenantId, N'Captação do serviço',
     N'Identifica o tipo de ato e coleta a documentação necessária.', 2, '#6B3F73', 0, 1, @agora, @agora),

    -- ambar
    ('0199B2C0-0002-7000-8000-000000000003', @tenantId, N'Em processamento / exigência',
     N'Análise e pendências — inclusive espera de terceiro.', 3, '#8A6516', 0, 1, @agora, @agora),

    -- verde. Esta etapa tem EhFinal = 1 e por isso tambem recebe o selo
    -- "ATO PRONTO" na interface; a cor nao substitui o selo, convive com ele.
    ('0199B2C0-0002-7000-8000-000000000004', @tenantId, N'Pronto / entrega',
     N'Ato concluído e cliente avisado.', 4, '#3D5C3A', 1, 1, @agora, @agora);

/* -----------------------------------------------------------------------------
   4. Atendentes
   -----------------------------------------------------------------------------
   DUAS COLUNAS INDEPENDENTES, e a distincao importa:

       Perfil ... PERMISSAO no sistema. 1 Atendente, 2 Supervisor, 3 Administrador
       Cargo .... FUNCAO na serventia.  0 NaoInformado, 1 Titular, 2 Substituto,
                  3 EscreventeAutorizado, 4 Escrevente, 5 Auxiliar

   Carla e o exemplo de por que foram separadas: cargo de escrevente autorizado
   com perfil de supervisao no painel. Fundidas numa coluna so, ela apareceria
   como "Supervisor" — cargo que nao existe em cartorio.

   Helena e Rafael sao nomes ficticios (titular e substituto).
   Carla e Paula sao NOMES FICTICIOS — existem para demonstrar a trava de
   conversa, que precisa de mais de um atendente na mesma fila. Substituir pelos
   nomes ficticios de demonstracao.
   -------------------------------------------------------------------------- */
INSERT INTO dbo.Atendentes
    (Id, TenantId, Nome, Email, SenhaHash, Perfil, Cargo, SetorPadraoId, Ativo, CriadoEm, AtualizadoEm)
VALUES
    -- Titular da serventia
    ('0199B2C0-0003-7000-8000-000000000005', @tenantId, N'Helena Campos Nogueira',
     N'helena@cartoriogameleiras.com.br', @senhaHash, 3, 1, NULL, 1, @agora, @agora),

    -- Substituto
    ('0199B2C0-0003-7000-8000-000000000001', @tenantId, N'Rafael Campos Nogueira',
     N'rafael@cartoriogameleiras.com.br', @senhaHash, 3, 2, NULL, 1, @agora, @agora),

    -- FICTICIA
    ('0199B2C0-0003-7000-8000-000000000002', @tenantId, N'Carla Menezes',
     N'carla@cartoriogameleiras.com.br', @senhaHash, 2, 3,
     '0199B2C0-0001-7000-8000-000000000002', 1, @agora, @agora),

    -- FICTICIA
    ('0199B2C0-0003-7000-8000-000000000003', @tenantId, N'Paula Ribeiro',
     N'paula@cartoriogameleiras.com.br', @senhaHash, 1, 4,
     '0199B2C0-0001-7000-8000-000000000001', 1, @agora, @agora);

/* -----------------------------------------------------------------------------
   5. Catalogo de servicos
   Categoria: 1 Notas, 2 RegistroCivil, 9 Outros
   (3 RegistroImoveis, 4 TitulosDocumentos e 5 Protesto nao sao usados —
    o enum mantem os valores, mas nao ha ato dessas categorias nesta serventia)

   A escritura de compra e venda FICA: lavrar a escritura e ato do tabeliao. O que
   nao e atribuicao daqui e o REGISTRO dela na matricula, que corre no Registro de
   Imoveis. Por isso sairam do catalogo: "Registro de imovel", "Averbacao em
   matricula", "Certidao de matricula" e "Certidao de onus reais".

   AGUARDA VALIDACAO DO CARTÓRIO PARCEIRO (REQUISITOS.md, pendencia 4).
   -------------------------------------------------------------------------- */
INSERT INTO dbo.TiposServico (Id, TenantId, Nome, Codigo, Categoria, Descricao, Ativo, CriadoEm, AtualizadoEm)
VALUES
    -- Registro Civil das Pessoas Naturais
    ('0199B2C0-0004-7000-8000-000000000001', @tenantId, N'Certidão de nascimento', 'RC-NASC', 2, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000002', @tenantId, N'Certidão de casamento', 'RC-CAS', 2, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000003', @tenantId, N'Certidão de óbito', 'RC-OBI', 2, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000004', @tenantId, N'Segunda via de certidão', 'RC-2VIA', 2, NULL, 1, @agora, @agora),

    -- Tabelionato de Notas
    ('0199B2C0-0004-7000-8000-000000000005', @tenantId, N'Reconhecimento de firma', 'NOT-FIRMA', 1, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000006', @tenantId, N'Autenticação de documento', 'NOT-AUTEN', 1, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000007', @tenantId, N'Escritura pública de compra e venda', 'NOT-ECV', 1, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000008', @tenantId, N'Escritura pública de doação', 'NOT-EDOA', 1, NULL, 1, @agora, @agora),
    ('0199B2C0-0004-7000-8000-000000000009', @tenantId, N'Procuração pública', 'NOT-PROC', 1, NULL, 1, @agora, @agora),

    -- Pendente de confirmacao: costuma correr no Registro de Titulos e Documentos
    ('0199B2C0-0004-7000-8000-000000000014', @tenantId, N'Notificação extrajudicial', 'TD-NOTIF', 9,
     N'AGUARDANDO CONFIRMAÇÃO: verificar com o cartório parceiro se este ato é atribuição desta serventia.',
     1, @agora, @agora);

/* -----------------------------------------------------------------------------
   6. SLAs — prazo em HORAS
   PRAZOS SAO CHUTE PLAUSIVEL, NAO DADO REAL.
   AGUARDA VALIDACAO DO CARTÓRIO PARCEIRO (REQUISITOS.md, pendencia 5).
   -------------------------------------------------------------------------- */
INSERT INTO dbo.PrazosServico
    (Id, TenantId, TipoServicoId, PrazoHoras, PercentualAlerta, ApenasDiasUteis, Ativo, CriadoEm, AtualizadoEm)
VALUES
    ('0199B2C0-0005-7000-8000-000000000001', @tenantId, '0199B2C0-0004-7000-8000-000000000001',  24, 75, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000002', @tenantId, '0199B2C0-0004-7000-8000-000000000002',  24, 75, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000003', @tenantId, '0199B2C0-0004-7000-8000-000000000003',  24, 75, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000004', @tenantId, '0199B2C0-0004-7000-8000-000000000004',  48, 80, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000005', @tenantId, '0199B2C0-0004-7000-8000-000000000005',   2, 70, 0, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000006', @tenantId, '0199B2C0-0004-7000-8000-000000000006',   2, 70, 0, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000007', @tenantId, '0199B2C0-0004-7000-8000-000000000007', 240, 80, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000008', @tenantId, '0199B2C0-0004-7000-8000-000000000008', 240, 80, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000009', @tenantId, '0199B2C0-0004-7000-8000-000000000009',  48, 75, 1, 1, @agora, @agora),
    ('0199B2C0-0005-7000-8000-000000000014', @tenantId, '0199B2C0-0004-7000-8000-000000000014', 120, 80, 1, 1, @agora, @agora);

/* -----------------------------------------------------------------------------
   7. Mensagens automaticas
   Gatilho: 1 NovoAtendimento, 2 EtapaAlterada, 3 PrazoProximoVencimento,
            4 PrazoVencido, 5 TriagemSemCorrespondencia

   TODOS OS TEXTOS SAO RASCUNHO. Existem para o cartório parceiro ver o mecanismo funcionando e
   dizer como o cartorio realmente fala com o cliente (REQUISITOS.md, pendencia 15).

   Duas nascem DESATIVADAS de proposito — os dois avisos de prazo. Contar ao cliente
   que o cartorio estourou o proprio prazo e decisao de postura comercial, nao
   detalhe tecnico; o cartório parceiro liga na tela quando decidir.

   Sem acento, como o resto deste arquivo: o mesmo conteudo esta em
   Infrastructure/Persistence/Seeding/CatalogoDeDemonstracao.cs e as duas versoes
   precisam bater caractere a caractere. Na tela o campo aceita acento — e NVARCHAR.
   -------------------------------------------------------------------------- */
DECLARE @lf CHAR(1) = CHAR(10);

INSERT INTO dbo.MensagensAutomaticas
    (Id, TenantId, Gatilho, EtapaAlvoId, Conteudo, Ativo, CriadoEm, AtualizadoEm)
VALUES
    -- {MenuSetores} e resolvido pelos setores ATIVOS no instante do disparo: setor
    -- adicionado ou desativado depois muda o menu sozinho, sem tocar neste texto.
    ('0199B2C0-0006-7000-8000-000000000001', @tenantId, 1, NULL,
     N'Olá, {NomeCliente}! Você está falando com o {NomeCartorio}.' + @lf + @lf +
     N'Para encaminharmos seu atendimento ao setor responsável, responda com o número da opção desejada:' + @lf + @lf +
     N'{MenuSetores}' + @lf + @lf +
     N'Se preferir, descreva sua solicitação que um de nossos atendentes dará continuidade.',
     1, @agora, @agora),

    -- Fallback da triagem. UMA tentativa e o suficiente: repetir o menu ate o
    -- cliente acertar prenderia quem escreveu "bom dia" num loop de robo.
    ('0199B2C0-0006-7000-8000-000000000002', @tenantId, 5, NULL,
     N'Obrigado pelo contato, {NomeCliente}. Um de nossos atendentes assumirá sua solicitação em instantes.' + @lf + @lf +
     N'Seu atendimento foi registrado sob o protocolo {Protocolo}. Guarde esse número para acompanhamento.',
     1, @agora, @agora),

    -- Aviso de conclusao, preso a etapa final POR ID: renomear ou reordenar o funil
    -- nao quebra a regra.
    ('0199B2C0-0006-7000-8000-000000000003', @tenantId, 2, '0199B2C0-0002-7000-8000-000000000004',
     N'{NomeCliente}, seu atendimento foi concluído.' + @lf + @lf +
     N'Protocolo: {Protocolo}' + @lf +
     N'Serviço: {Servico}' + @lf + @lf +
     N'A retirada pode ser feita no {NomeCartorio}, na Praça São Francisco, nº 70, Centro, Gameleiras/MG, de segunda a sexta-feira, das 8h às 17h.' + @lf + @lf +
     N'Pedimos que apresente um documento de identificação com foto no balcão.',
     1, @agora, @agora),

    ('0199B2C0-0006-7000-8000-000000000004', @tenantId, 3, NULL,
     N'{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) segue em andamento e se aproxima do prazo previsto para conclusão.' + @lf + @lf +
     N'Assim que houver novidade, comunicaremos por aqui. Se precisar de alguma informação, basta responder a esta mensagem.',
     0, @agora, @agora),

    ('0199B2C0-0006-7000-8000-000000000005', @tenantId, 4, NULL,
     N'{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) ultrapassou o prazo inicialmente previsto e está com prioridade em nossa fila.' + @lf + @lf +
     N'Pedimos desculpas pelo transtorno. Assim que houver definição, informaremos por aqui.',
     0, @agora, @agora);

/* -----------------------------------------------------------------------------
   8. Respostas rapidas — atalho "/" da caixa de resposta
   Nada sai daqui sozinho: o texto entra no campo e o atendente revisa antes de
   enviar. Tambem sao rascunho, especialmente a de emolumentos, que fala de valor.
   -------------------------------------------------------------------------- */
INSERT INTO dbo.RespostasRapidas
    (Id, TenantId, Atalho, Titulo, Conteudo, Ativo, CriadoEm, AtualizadoEm)
VALUES
    ('0199B2C0-0007-7000-8000-000000000001', @tenantId, 'horario', N'Horário e endereço',
     N'O {NomeCartorio} atende de segunda a sexta-feira, das 8h às 17h, na Praça São ' +
     N'Francisco, nº 70, Centro, Gameleiras/MG, CEP 39505-000.',
     1, @agora, @agora),

    ('0199B2C0-0007-7000-8000-000000000002', @tenantId, 'prazo', N'Situação do protocolo',
     N'Seu atendimento está registrado sob o protocolo {Protocolo}, referente a: {Servico}.' + @lf + @lf +
     N'Assim que houver novidade, informaremos por aqui.',
     1, @agora, @agora),

    ('0199B2C0-0007-7000-8000-000000000003', @tenantId, 'valor', N'Emolumentos',
     N'Os valores dos atos seguem a Tabela de Emolumentos do Tribunal de Justiça de ' +
     N'Minas Gerais, definida por lei estadual — não é preço fixado pelo cartório.' + @lf + @lf +
     N'Vou verificar o valor exato aplicável ao seu caso e retorno em seguida.',
     1, @agora, @agora),

    ('0199B2C0-0007-7000-8000-000000000004', @tenantId, 'firma', N'Reconhecimento de firma',
     N'Para o reconhecimento de firma, é necessário que sua ficha de assinatura esteja ' +
     N'aberta neste cartório.' + @lf + @lf +
     N'Caso ainda não possua, basta comparecer com documento de identificação original ' +
     N'com foto para abertura na hora.',
     1, @agora, @agora),

    ('0199B2C0-0007-7000-8000-000000000005', @tenantId, 'escritura', N'Documentos para escritura',
     N'Para lavrarmos uma escritura pública, precisamos de:' + @lf + @lf +
     N'- RG e CPF das partes (e do cônjuge, quando casado)' + @lf +
     N'- Certidão de casamento atualizada, se for o caso' + @lf +
     N'- Comprovante de endereço' + @lf +
     N'- Documentação do bem objeto do ato' + @lf + @lf +
     N'A relação varia conforme o tipo de escritura. Confirmo os detalhes do seu caso e retorno.',
     1, @agora, @agora),

    ('0199B2C0-0007-7000-8000-000000000006', @tenantId, 'certidao', N'Dados para emitir certidão',
     N'Para localizarmos o registro e emitirmos a certidão, precisamos de:' + @lf + @lf +
     N'- Nome completo do registrado' + @lf +
     N'- Data do fato (nascimento, casamento ou óbito)' + @lf +
     N'- Nome dos pais, se souber' + @lf + @lf +
     N'Com esses dados fazemos a busca no livro e informamos o prazo de emissão.',
     1, @agora, @agora);

GO

COMMIT TRANSACTION;
GO

PRINT 'Configuracao de demonstracao inserida. Senha de todos os usuarios: cartorio2026';
GO
