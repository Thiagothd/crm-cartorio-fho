using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.Servicos.Domain.Enums;

namespace CrmCartorio.Api.Infrastructure.Persistence.Seeding;

/// <summary>
/// Conteudo de configuracao da demonstracao: setores, etapas e catalogo de servicos.
/// </summary>
/// <remarks>
/// Setores refletem as atribuicoes REAIS da serventia, confirmadas no perfil do
/// CNJ: Tabelionato de Notas e Registro Civil das Pessoas Naturais. Nada mais.
///
/// Etapas e prazos continuam sendo SUGESTAO de mercado e aguardam validacao do
/// cartório parceiro (docs/REQUISITOS.md, secao 5). Como sao linhas de tabela e nao codigo,
/// ele renomeia, reordena e apaga tudo pela tela de configuracao.
/// </remarks>
public static class CatalogoDeDemonstracao
{
    public record SetorSemeado(Guid Id, string Nome, string Descricao, int Ordem);

    public record EtapaSemeada(Guid Id, string Nome, string Descricao, int Ordem, string Cor, bool EhFinal);

    public record ServicoSemeado(
        Guid Id,
        Guid PrazoId,
        string Nome,
        CategoriaServico Categoria,
        string? Codigo,
        int PrazoHoras,
        int PercentualAlerta,
        bool ApenasDiasUteis,
        string? Descricao = null,
        Guid? SetorId = null,
        string? DocumentosNecessarios = null);

    private static Guid IdDeServico(int n) => new($"0199b2c0-0004-7000-8000-{n:D12}");

    private static Guid IdDePrazo(int n) => new($"0199b2c0-0005-7000-8000-{n:D12}");

    /// <summary>Marca servico cuja atribuicao ainda depende de confirmacao do cliente.</summary>
    private const string AguardandoConfirmacao =
        "AGUARDANDO CONFIRMACAO: verificar com o cartório parceiro se este ato e atribuicao desta serventia.";

    /// <summary>
    /// As duas atribuicoes da serventia. Registro de Imoveis, Titulos e Documentos
    /// e Protesto foram removidos: nao sao competencia deste cartorio.
    /// </summary>
    // Nome e descricao COM ACENTO: sao rotulos que aparecem em toda tela do painel,
    // e "Captacao do servico" numa interface que se apresenta como livro de registro
    // custa credibilidade. O vinculo das regras de mensagem automatica e por Id, nao
    // por nome, entao corrigir a grafia nao quebra nada.
    public static readonly SetorSemeado[] Setores =
    [
        new(IdentificadoresDeDemonstracao.Setores.Notas,
            "Tabelionato de Notas",
            "Escrituras, procurações, reconhecimento de firma e autenticação.", 1),

        new(IdentificadoresDeDemonstracao.Setores.RegistroCivil,
            "Registro Civil",
            "Nascimento, casamento, óbito, segundas vias e averbações.", 2)
    ];

    // A Cor de cada etapa e a cor de TINTA da paleta de etapas
    // (docs/DESIGN.md, secao 2.2). O fundo e a borda saem dela no frontend.
    public static readonly EtapaSemeada[] Etapas =
    [
        new(IdentificadoresDeDemonstracao.Etapas.AtendimentoInicial,
            "Atendimento inicial",
            "Triagem: identifica quem é a pessoa e qual é a demanda.",
            1, "#2E5170", false),

        new(IdentificadoresDeDemonstracao.Etapas.Captacao,
            "Captação do serviço",
            "Identifica o tipo de ato e coleta a documentação necessária.",
            2, "#6B3F73", false),

        new(IdentificadoresDeDemonstracao.Etapas.EmProcessamento,
            "Em processamento / exigência",
            "Análise e pendências — inclusive espera de terceiro.",
            3, "#8A6516", false),

        new(IdentificadoresDeDemonstracao.Etapas.ProntoEntrega,
            "Pronto / entrega",
            "Ato concluído e cliente avisado.",
            4, "#3D5C3A", true)
    ];

    /// <summary>
    /// Somente atos de Tabelionato de Notas e Registro Civil.
    /// </summary>
    /// <remarks>
    /// A escritura de compra e venda FICA: lavrar a escritura e ato do tabeliao de
    /// notas. O que nao e atribuicao desta serventia e o REGISTRO dessa escritura
    /// na matricula, que corre no Registro de Imoveis — por isso "Registro de
    /// imovel", "Averbacao em matricula", "Certidao de matricula" e "Certidao de
    /// onus reais" sairam do catalogo.
    /// </remarks>
    /// <summary>
    /// Checklist de documento por servico. Texto CADASTRO (cliente nao le direto no
    /// codigo — vira {DocumentosNecessarios} nas mensagens automaticas), calibrado
    /// contra pratica cartoraria comum, nao dado juridico caso a caso. Suposicao
    /// defensavel (docs/REQUISITOS.md, 5.7), editavel pelo cartório parceiro na tela de Servicos.
    /// </summary>
    private const string DocsCertidao =
        "Documento de identificação com foto de quem solicita. Se a certidão for de outra pessoa, informe também o nome completo e, se possível, a data do ato ou o número do registro.";

    private const string DocsHabilitacaoCasamento =
        "RG e CPF dos dois noivos, certidão de nascimento (ou de casamento anterior já com a averbação de divórcio ou óbito) e comprovante de residência. É preciso também indicar duas testemunhas maiores de idade, com documento de identificação.";

    private const string DocsAverbacao =
        "Documento de identificação com foto de quem solicita, o registro original a ser averbado e o documento que comprova o fato — sentença, escritura ou certidão, conforme o caso.";

    private const string DocsRegistroNascimento =
        "Declaração de Nascido Vivo (DNV) fornecida pela maternidade ou hospital, documento de identificação dos pais e comprovante de residência.";

    private const string DocsRegistroObito =
        "Declaração de Óbito fornecida pelo médico ou pelo IML, documento de identificação do falecido, quando houver, e de quem está declarando.";

    private const string DocsFirma =
        "Documento de identificação original com foto. É preciso ter ficha de assinatura já aberta neste cartório — sem isso, é necessário comparecer pessoalmente antes.";

    private const string DocsAutenticacao =
        "Documento original e a cópia a ser autenticada.";

    private const string DocsEscrituraCompraVenda =
        "RG e CPF das partes, certidão atualizada do imóvel e comprovante de residência. Certidões negativas adicionais podem ser exigidas conforme o caso — confirme com o cartório antes de reunir a documentação.";

    private const string DocsEscrituraDoacao =
        "RG e CPF do doador e do donatário, documento do bem a ser doado e comprovante de residência.";

    private const string DocsProcuracao =
        "RG e CPF de quem outorga a procuração. Se possível, informe também nome completo e CPF de quem vai receber os poderes.";

    private const string DocsAtaNotarial =
        "Documento de identificação com foto e a descrição do fato a ser constatado pelo tabelião.";

    private const string DocsTestamento =
        "RG e CPF de quem testa, documento de identificação de duas testemunhas, e a relação dos bens e dos herdeiros.";

    private const string DocsInventario =
        "RG e CPF de todos os herdeiros, certidão de óbito, certidão de casamento do falecido (se houver) e documentos dos bens a partilhar. Também é exigida certidão negativa de débitos do ITCMD.";

    private const string DocsDivorcio =
        "RG e CPF de ambos os cônjuges e certidão de casamento. O divórcio consensual em cartório só é possível sem filho menor ou incapaz do casal.";

    private const string DocsUniaoEstavel =
        "RG, CPF e comprovante de residência de ambos, e duas testemunhas com documento de identificação.";

    public static readonly ServicoSemeado[] Servicos =
    [
        // --- Registro Civil das Pessoas Naturais ---
        new(IdDeServico(1), IdDePrazo(1), "Certidão de nascimento", CategoriaServico.RegistroCivil, "RC-NASC", 24, 75, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.RegistroCivil, DocumentosNecessarios: DocsCertidao),
        new(IdDeServico(2), IdDePrazo(2), "Certidão de casamento", CategoriaServico.RegistroCivil, "RC-CAS", 24, 75, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.RegistroCivil, DocumentosNecessarios: DocsCertidao),
        new(IdDeServico(3), IdDePrazo(3), "Certidão de óbito", CategoriaServico.RegistroCivil, "RC-OBI", 24, 75, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.RegistroCivil, DocumentosNecessarios: DocsCertidao),
        new(IdDeServico(4), IdDePrazo(4), "Segunda via de certidão", CategoriaServico.RegistroCivil, "RC-2VIA", 48, 80, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.RegistroCivil, DocumentosNecessarios: DocsCertidao),

        // --- Tabelionato de Notas ---
        new(IdDeServico(5), IdDePrazo(5), "Reconhecimento de firma", CategoriaServico.Notas, "NOT-FIRMA", 2, 70, false,
            SetorId: IdentificadoresDeDemonstracao.Setores.Notas, DocumentosNecessarios: DocsFirma),
        new(IdDeServico(6), IdDePrazo(6), "Autenticação de documento", CategoriaServico.Notas, "NOT-AUTEN", 2, 70, false,
            SetorId: IdentificadoresDeDemonstracao.Setores.Notas, DocumentosNecessarios: DocsAutenticacao),
        new(IdDeServico(7), IdDePrazo(7), "Escritura pública de compra e venda", CategoriaServico.Notas, "NOT-ECV", 240, 80, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.Notas, DocumentosNecessarios: DocsEscrituraCompraVenda),
        new(IdDeServico(8), IdDePrazo(8), "Escritura pública de doação", CategoriaServico.Notas, "NOT-EDOA", 240, 80, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.Notas, DocumentosNecessarios: DocsEscrituraDoacao),
        new(IdDeServico(9), IdDePrazo(9), "Procuração pública", CategoriaServico.Notas, "NOT-PROC", 48, 75, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.Notas, DocumentosNecessarios: DocsProcuracao),

        // --- Registro Civil: atos candidatos (docs/REQUISITOS.md, 5.6) ---
        // Atribuicao ja confirmada (sao Registro Civil das Pessoas Naturais); o que
        // falta e o cartório parceiro validar prazo e se o nome/redacao batem com o uso daqui.
        // Habilitacao de casamento usa 360h (15 dias) porque o prazo minimo de
        // proclamas e LEGAL (Lei 6.015/73, art. 67), nao suposicao solta.
        new(IdDeServico(10), IdDePrazo(10), "Habilitação de casamento", CategoriaServico.RegistroCivil, "RC-HABIL", 360, 80, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.RegistroCivil, DocumentosNecessarios: DocsHabilitacaoCasamento),
        new(IdDeServico(11), IdDePrazo(11), "Averbação em registro civil", CategoriaServico.RegistroCivil, "RC-AVERB", 48, 75, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.RegistroCivil, DocumentosNecessarios: DocsAverbacao),
        new(IdDeServico(12), IdDePrazo(12), "Registro de nascimento", CategoriaServico.RegistroCivil, "RC-REGNASC", 2, 70, false,
            SetorId: IdentificadoresDeDemonstracao.Setores.RegistroCivil, DocumentosNecessarios: DocsRegistroNascimento),
        new(IdDeServico(13), IdDePrazo(13), "Registro de óbito", CategoriaServico.RegistroCivil, "RC-REGOBI", 2, 70, false,
            SetorId: IdentificadoresDeDemonstracao.Setores.RegistroCivil, DocumentosNecessarios: DocsRegistroObito),

        // --- Pendente de confirmacao ---
        // Notificacao extrajudicial costuma correr no Registro de Titulos e
        // Documentos, que nao e atribuicao desta serventia. Fica listado, fora de
        // categoria e marcado, para o cartório parceiro dizer se atendem isso ou nao. Sem
        // SetorId de proposito: a propria atribuicao esta em duvida, entao nao entra
        // em nenhum menu automatico ate ser confirmado.
        new(IdDeServico(14), IdDePrazo(14), "Notificação extrajudicial", CategoriaServico.Outros, "TD-NOTIF", 120, 80, true,
            AguardandoConfirmacao),

        // --- Tabelionato de Notas: atos candidatos (docs/REQUISITOS.md, 5.6) ---
        // Ata notarial fica entre Procuracao e Escritura em complexidade. Inventario
        // e partilha e o ato mais longo do catalogo de proposito: reune inventario
        // extrajudicial de verdade (certidoes, ITCMD, anuencia de todos os herdeiros).
        new(IdDeServico(15), IdDePrazo(15), "Ata notarial", CategoriaServico.Notas, "NOT-ATA", 72, 75, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.Notas, DocumentosNecessarios: DocsAtaNotarial),
        new(IdDeServico(16), IdDePrazo(16), "Testamento público", CategoriaServico.Notas, "NOT-TESTAM", 240, 80, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.Notas, DocumentosNecessarios: DocsTestamento),
        new(IdDeServico(17), IdDePrazo(17), "Escritura de inventário e partilha", CategoriaServico.Notas, "NOT-EINV", 480, 80, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.Notas, DocumentosNecessarios: DocsInventario),
        new(IdDeServico(18), IdDePrazo(18), "Escritura de divórcio consensual", CategoriaServico.Notas, "NOT-EDIV", 240, 80, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.Notas, DocumentosNecessarios: DocsDivorcio),
        new(IdDeServico(19), IdDePrazo(19), "Escritura declaratória de união estável", CategoriaServico.Notas, "NOT-EUNI", 240, 80, true,
            SetorId: IdentificadoresDeDemonstracao.Setores.Notas, DocumentosNecessarios: DocsUniaoEstavel)
    ];

    public record MensagemAutomaticaSemeada(
        Guid Id,
        GatilhoDeMensagem Gatilho,
        Guid? EtapaAlvoId,
        string Conteudo,
        bool Ativo);

    public record RespostaRapidaSemeada(Guid Id, string Atalho, string Titulo, string Conteudo);

    /// <summary>
    /// Regras de mensagem automatica da demonstracao.
    /// </summary>
    /// <remarks>
    /// COM ACENTO, ao contrario do resto do seed. Estas linhas nao sao rotulo de
    /// cadastro: sao o texto que o CLIENTE DO CARTORIO le no WhatsApp, e "Ola, seu
    /// servico esta pronto" e o tipo de descuido que uma serventia nao pode assinar.
    ///
    /// O mesmo conteudo esta em sql/002-seed-simulado.sql e as duas versoes precisam
    /// bater caractere a caractere. Por isso aquele arquivo — e todos os .sql do
    /// repositorio — passou a ser gravado com BOM UTF-8: sem o BOM, o sqlcmd le o
    /// arquivo como ANSI e cada acento vira dois caracteres no banco.
    ///
    /// Redacao adotada como SUPOSICAO defensavel (docs/REQUISITOS.md, 5.7). Endereco
    /// e horario sao os dados oficiais confirmados em docs/CONTEXTO.md, secao 2. O
    /// cartório parceiro reescreve tudo pela tela, sem release.
    /// </remarks>
    public static readonly MensagemAutomaticaSemeada[] MensagensAutomaticas =
    [
        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.BoasVindas,
            GatilhoDeMensagem.NovoAtendimento,
            null,
            "Olá, {NomeCliente}! 👋 Você está falando com o {NomeCartorio}.\n\n" +
            "Para encaminharmos seu atendimento ao setor responsável, responda com o " +
            "número da opção desejada:\n\n" +
            "{MenuSetores}\n\n" +
            "Se preferir, descreva sua solicitação que um de nossos atendentes dará continuidade.",
            true),

        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.TriagemSemResposta,
            GatilhoDeMensagem.TriagemSemCorrespondencia,
            null,
            "Obrigado pelo contato, {NomeCliente}! 🙏 Um de nossos atendentes assumirá sua " +
            "solicitação em instantes.\n\n" +
            "📋 Protocolo: {Protocolo}\n" +
            "Guarde esse número para acompanhamento.",
            true),

        // Fase 2D: dispara logo depois que a triagem reconhece o setor, perguntando
        // qual servico especifico — o passo que faltava e que o teste ao vivo expos
        // ("digitou 1 e nao aconteceu nada").
        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.TriagemComResposta,
            GatilhoDeMensagem.TriagemComCorrespondencia,
            null,
            "Certo, {NomeCliente}! ✅ Qual destes serviços você precisa?\n\n" +
            "{MenuServicos}\n\n" +
            "Se preferir, descreva o que precisa que um de nossos atendentes dará continuidade.",
            true),

        // Fase 2D: fecha o par com a mensagem acima — o cliente escolheu o servico,
        // e o checklist/prazo chegam sozinhos, sem gastar a IA nem esperar atendente.
        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.ServicoEscolhido,
            GatilhoDeMensagem.ServicoEscolhido,
            null,
            "Perfeito, {NomeCliente}! ✅ Localizamos seu atendimento de {Servico}, protocolo " +
            "{Protocolo}.\n\n" +
            "📄 Documentos necessários:\n{DocumentosNecessarios}\n\n" +
            "⏱️ Prazo previsto: {PrazoServico}\n\n" +
            "Assim que possível, providencie a documentação e traga ao {NomeCartorio}. " +
            "Qualquer dúvida, é só responder por aqui.",
            true),

        // Fase 2D: preenche a unica etapa do funil que ainda ficava sem mensagem.
        // Dispara na mudanca manual de etapa pelo atendente (ConversaService), o
        // mesmo mecanismo que ja move AtoConcluido abaixo.
        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.EmAndamento,
            GatilhoDeMensagem.EtapaAlterada,
            IdentificadoresDeDemonstracao.Etapas.EmProcessamento,
            "{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) está em análise. 🔎\n\n" +
            "⏱️ Prazo previsto: {PrazoServico}\n" +
            "Se houver alguma pendência, entraremos em contato por aqui.",
            true),

        // Fase 2E.1: dispara junto com ServicoEscolhido — o atendimento
        // estruturado terminou, mas a conversa nao pode terminar muda. E o
        // "else" da mensagem: sem isto, qualquer coisa que o cliente escrevesse
        // depois do checklist (ate um "obrigada") nao gerava resposta nenhuma —
        // achado ao vivo, 2026-08-17.
        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.MenuDeContinuacao,
            GatilhoDeMensagem.MenuDeContinuacao,
            null,
            "Posso ajudar em mais alguma coisa, {NomeCliente}?\n\n" +
            "1 - Ver outro serviço\n" +
            "2 - Trocar de setor\n" +
            "3 - Falar com um atendente\n" +
            "4 - Encerrar atendimento",
            true),

        // Fase 2E.1: encaminhamento para humano NO MEIO ou DEPOIS do atendimento —
        // nao no primeiro contato (esse continua sendo TriagemSemResposta). Texto
        // proprio para nao repetir "obrigado pelo contato" com quem o sistema ja
        // esta atendendo ha uma volta inteira de mensagens.
        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.PedidoDeAtendente,
            GatilhoDeMensagem.PedidoDeAtendente,
            null,
            "Certo, {NomeCliente}! Um de nossos atendentes vai continuar seu atendimento " +
            "(protocolo {Protocolo}) em instantes.",
            true),

        // Fase 2E.1: cliente escolheu encerrar pelo MenuDeContinuacao. So a
        // mensagem — nao fecha a Conversa de verdade ainda (ver o enum).
        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.Despedida,
            GatilhoDeMensagem.Despedida,
            null,
            "Foi um prazer atender você, {NomeCliente}! 👋\n\n" +
            "Seu protocolo {Protocolo} fica registrado — qualquer necessidade, é só mandar " +
            "mensagem por aqui.",
            true),

        // A etapa alvo e a etapa final semeada. Se o cartório parceiro renomear ou reordenar o
        // funil, a regra continua valendo: o vinculo e por Id, nao por nome.
        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.AtoConcluido,
            GatilhoDeMensagem.EtapaAlterada,
            IdentificadoresDeDemonstracao.Etapas.ProntoEntrega,
            "{NomeCliente}, seu atendimento foi concluído! ✅\n\n" +
            "📋 Protocolo: {Protocolo}\n" +
            "📄 Serviço: {Servico}\n\n" +
            "📍 A retirada pode ser feita no {NomeCartorio}, na Praça São Francisco, nº 70, " +
            "Centro, Gameleiras/MG, de segunda a sexta-feira, das 8h às 17h.\n\n" +
            "Pedimos que apresente um documento de identificação com foto no balcão.",
            true),

        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.PrazoPerto,
            GatilhoDeMensagem.PrazoProximoVencimento,
            null,
            "{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) segue em " +
            "andamento e se aproxima do prazo previsto para conclusão. ⏳\n\n" +
            "Assim que houver novidade, comunicaremos por aqui. " +
            "Se precisar de alguma informação, basta responder a esta mensagem.",
            false),

        // Semeada DESATIVADA de proposito. Avisar o cliente de que o cartorio
        // estourou o proprio prazo e decisao de postura comercial, nao detalhe
        // tecnico — o cartório parceiro liga na tela se quiser (docs/REQUISITOS.md, 5.7).
        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.PrazoVencido,
            GatilhoDeMensagem.PrazoVencido,
            null,
            "{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) ultrapassou o " +
            "prazo inicialmente previsto e está com prioridade em nossa fila. ⚠️\n\n" +
            "Pedimos desculpas pelo transtorno. Assim que houver definição, informaremos por aqui.",
            false)
    ];

    /// <summary>
    /// Respostas rapidas do dia a dia — o que a equipe mais repete no balcao e no
    /// WhatsApp. Mesma observacao de acento e de redacao provisoria acima.
    /// </summary>
    public static readonly RespostaRapidaSemeada[] RespostasRapidas =
    [
        new(IdentificadoresDeDemonstracao.RespostasRapidas.Horario,
            "horario",
            "Horário e endereço",
            "📍 O {NomeCartorio} atende de segunda a sexta-feira, das 8h às 17h, na Praça São " +
            "Francisco, nº 70, Centro, Gameleiras/MG, CEP 39505-000."),

        new(IdentificadoresDeDemonstracao.RespostasRapidas.Prazo,
            "prazo",
            "Situação do protocolo",
            "📋 Seu atendimento está registrado sob o protocolo {Protocolo}, referente a: " +
            "{Servico}.\n\nAssim que houver novidade, informaremos por aqui."),

        new(IdentificadoresDeDemonstracao.RespostasRapidas.Emolumentos,
            "valor",
            "Emolumentos",
            "💰 Os valores dos atos seguem a Tabela de Emolumentos do Tribunal de Justiça de " +
            "Minas Gerais, definida por lei estadual — não é preço fixado pelo cartório.\n\n" +
            "Vou verificar o valor exato aplicável ao seu caso e retorno em seguida."),

        new(IdentificadoresDeDemonstracao.RespostasRapidas.Firma,
            "firma",
            "Reconhecimento de firma",
            "✍️ Para o reconhecimento de firma, é necessário que sua ficha de assinatura esteja " +
            "aberta neste cartório.\n\n" +
            "Caso ainda não possua, basta comparecer com documento de identificação original " +
            "com foto para abertura na hora."),

        new(IdentificadoresDeDemonstracao.RespostasRapidas.Escritura,
            "escritura",
            "Documentos para escritura",
            "📄 Para lavrarmos uma escritura pública, precisamos de:\n\n" +
            "- RG e CPF das partes (e do cônjuge, quando casado)\n" +
            "- Certidão de casamento atualizada, se for o caso\n" +
            "- Comprovante de endereço\n" +
            "- Documentação do bem objeto do ato\n\n" +
            "A relação varia conforme o tipo de escritura. Confirmo os detalhes do seu caso e retorno."),

        new(IdentificadoresDeDemonstracao.RespostasRapidas.Certidao,
            "certidao",
            "Dados para emitir certidão",
            "📄 Para localizarmos o registro e emitirmos a certidão, precisamos de:\n\n" +
            "- Nome completo do registrado\n" +
            "- Data do fato (nascimento, casamento ou óbito)\n" +
            "- Nome dos pais, se souber\n\n" +
            "Com esses dados fazemos a busca no livro e informamos o prazo de emissão.")
    ];
}
