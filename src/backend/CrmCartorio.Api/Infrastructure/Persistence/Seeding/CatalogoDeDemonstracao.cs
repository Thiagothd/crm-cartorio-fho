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
        string? Descricao = null);

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
    public static readonly ServicoSemeado[] Servicos =
    [
        // --- Registro Civil das Pessoas Naturais ---
        new(IdDeServico(1), IdDePrazo(1), "Certidão de nascimento", CategoriaServico.RegistroCivil, "RC-NASC", 24, 75, true),
        new(IdDeServico(2), IdDePrazo(2), "Certidão de casamento", CategoriaServico.RegistroCivil, "RC-CAS", 24, 75, true),
        new(IdDeServico(3), IdDePrazo(3), "Certidão de óbito", CategoriaServico.RegistroCivil, "RC-OBI", 24, 75, true),
        new(IdDeServico(4), IdDePrazo(4), "Segunda via de certidão", CategoriaServico.RegistroCivil, "RC-2VIA", 48, 80, true),

        // --- Tabelionato de Notas ---
        new(IdDeServico(5), IdDePrazo(5), "Reconhecimento de firma", CategoriaServico.Notas, "NOT-FIRMA", 2, 70, false),
        new(IdDeServico(6), IdDePrazo(6), "Autenticação de documento", CategoriaServico.Notas, "NOT-AUTEN", 2, 70, false),
        new(IdDeServico(7), IdDePrazo(7), "Escritura pública de compra e venda", CategoriaServico.Notas, "NOT-ECV", 240, 80, true),
        new(IdDeServico(8), IdDePrazo(8), "Escritura pública de doação", CategoriaServico.Notas, "NOT-EDOA", 240, 80, true),
        new(IdDeServico(9), IdDePrazo(9), "Procuração pública", CategoriaServico.Notas, "NOT-PROC", 48, 75, true),

        // --- Pendente de confirmacao ---
        // Notificacao extrajudicial costuma correr no Registro de Titulos e
        // Documentos, que nao e atribuicao desta serventia. Fica listado, fora de
        // categoria e marcado, para o cartório parceiro dizer se atendem isso ou nao.
        new(IdDeServico(14), IdDePrazo(14), "Notificação extrajudicial", CategoriaServico.Outros, "TD-NOTIF", 120, 80, true,
            AguardandoConfirmacao)
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
            "Olá, {NomeCliente}! Você está falando com o {NomeCartorio}.\n\n" +
            "Para encaminharmos seu atendimento ao setor responsável, responda com o " +
            "número da opção desejada:\n\n" +
            "{MenuSetores}\n\n" +
            "Se preferir, descreva sua solicitação que um de nossos atendentes dará continuidade.",
            true),

        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.TriagemSemResposta,
            GatilhoDeMensagem.TriagemSemCorrespondencia,
            null,
            "Obrigado pelo contato, {NomeCliente}. Um de nossos atendentes assumirá sua " +
            "solicitação em instantes.\n\n" +
            "Seu atendimento foi registrado sob o protocolo {Protocolo}. " +
            "Guarde esse número para acompanhamento.",
            true),

        // A etapa alvo e a etapa final semeada. Se o cartório parceiro renomear ou reordenar o
        // funil, a regra continua valendo: o vinculo e por Id, nao por nome.
        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.AtoConcluido,
            GatilhoDeMensagem.EtapaAlterada,
            IdentificadoresDeDemonstracao.Etapas.ProntoEntrega,
            "{NomeCliente}, seu atendimento foi concluído.\n\n" +
            "Protocolo: {Protocolo}\n" +
            "Serviço: {Servico}\n\n" +
            "A retirada pode ser feita no {NomeCartorio}, na Praça São Francisco, nº 70, " +
            "Centro, Gameleiras/MG, de segunda a sexta-feira, das 8h às 17h.\n\n" +
            "Pedimos que apresente um documento de identificação com foto no balcão.",
            true),

        new(IdentificadoresDeDemonstracao.MensagensAutomaticas.PrazoPerto,
            GatilhoDeMensagem.PrazoProximoVencimento,
            null,
            "{NomeCliente}, seu atendimento de protocolo {Protocolo} ({Servico}) segue em " +
            "andamento e se aproxima do prazo previsto para conclusão.\n\n" +
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
            "prazo inicialmente previsto e está com prioridade em nossa fila.\n\n" +
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
            "O {NomeCartorio} atende de segunda a sexta-feira, das 8h às 17h, na Praça São " +
            "Francisco, nº 70, Centro, Gameleiras/MG, CEP 39505-000."),

        new(IdentificadoresDeDemonstracao.RespostasRapidas.Prazo,
            "prazo",
            "Situação do protocolo",
            "Seu atendimento está registrado sob o protocolo {Protocolo}, referente a: " +
            "{Servico}.\n\nAssim que houver novidade, informaremos por aqui."),

        new(IdentificadoresDeDemonstracao.RespostasRapidas.Emolumentos,
            "valor",
            "Emolumentos",
            "Os valores dos atos seguem a Tabela de Emolumentos do Tribunal de Justiça de " +
            "Minas Gerais, definida por lei estadual — não é preço fixado pelo cartório.\n\n" +
            "Vou verificar o valor exato aplicável ao seu caso e retorno em seguida."),

        new(IdentificadoresDeDemonstracao.RespostasRapidas.Firma,
            "firma",
            "Reconhecimento de firma",
            "Para o reconhecimento de firma, é necessário que sua ficha de assinatura esteja " +
            "aberta neste cartório.\n\n" +
            "Caso ainda não possua, basta comparecer com documento de identificação original " +
            "com foto para abertura na hora."),

        new(IdentificadoresDeDemonstracao.RespostasRapidas.Escritura,
            "escritura",
            "Documentos para escritura",
            "Para lavrarmos uma escritura pública, precisamos de:\n\n" +
            "- RG e CPF das partes (e do cônjuge, quando casado)\n" +
            "- Certidão de casamento atualizada, se for o caso\n" +
            "- Comprovante de endereço\n" +
            "- Documentação do bem objeto do ato\n\n" +
            "A relação varia conforme o tipo de escritura. Confirmo os detalhes do seu caso e retorno."),

        new(IdentificadoresDeDemonstracao.RespostasRapidas.Certidao,
            "certidao",
            "Dados para emitir certidão",
            "Para localizarmos o registro e emitirmos a certidão, precisamos de:\n\n" +
            "- Nome completo do registrado\n" +
            "- Data do fato (nascimento, casamento ou óbito)\n" +
            "- Nome dos pais, se souber\n\n" +
            "Com esses dados fazemos a busca no livro e informamos o prazo de emissão.")
    ];
}
