using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Configuracoes.Application;

public class ConfiguracoesServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static ConfiguracoesServiceException SetorNaoEncontrado() =>
        new("setor_nao_encontrado", "Setor nao encontrado.", 404);

    public static ConfiguracoesServiceException EtapaNaoEncontrada() =>
        new("etapa_nao_encontrada", "Etapa nao encontrada.", 404);

    public static ConfiguracoesServiceException NomeObrigatorio() =>
        new("nome_obrigatorio", "Informe o nome.", 400);

    public static ConfiguracoesServiceException NomeEmUso(string nome) =>
        new("nome_em_uso", $"Ja existe um registro chamado \"{nome}\".", 409);

    public static ConfiguracoesServiceException CorInvalida() =>
        new("cor_invalida", "Informe a cor no formato #RRGGBB.", 400);

    public static ConfiguracoesServiceException SemPermissao() =>
        new("sem_permissao", "Seu perfil nao permite alterar cadastros.", 403);

    /// <summary>
    /// Desativar a ultima etapa ativa deixaria o funil vazio e novas conversas
    /// nasceriam sem etapa nenhuma.
    /// </summary>
    public static ConfiguracoesServiceException UltimaEtapaAtiva() =>
        new("ultima_etapa_ativa",
            "O funil precisa de ao menos uma etapa ativa.", 409);

    // Mensagens automaticas ---------------------------------------------------

    public static ConfiguracoesServiceException MensagemAutomaticaNaoEncontrada() =>
        new("mensagem_automatica_nao_encontrada", "Mensagem automatica nao encontrada.", 404);

    public static ConfiguracoesServiceException ConteudoObrigatorio() =>
        new("conteudo_obrigatorio", "Escreva o texto da mensagem.", 400);

    /// <summary>
    /// Gatilho invalido chegando pelo corpo da requisicao — enum fora da faixa.
    /// </summary>
    public static ConfiguracoesServiceException GatilhoInvalido() =>
        new("gatilho_invalido", "Gatilho desconhecido.", 400);

    public static ConfiguracoesServiceException EtapaAlvoObrigatoria() =>
        new("etapa_alvo_obrigatoria",
            "Escolha a etapa que dispara a mensagem.", 400);

    /// <summary>
    /// Barra o marcador digitado errado ANTES de gravar. E a unica chance de pegar
    /// isso: um "{NomeClinte}" nao daria erro em lugar nenhum — sairia literal na
    /// mensagem que o cliente do cartorio recebe.
    /// </summary>
    public static ConfiguracoesServiceException MarcadorDesconhecido(IEnumerable<string> marcadores) =>
        new("marcador_desconhecido",
            $"Marcador nao reconhecido: {string.Join(", ", marcadores)}. " +
            "Confira a lista de marcadores disponiveis abaixo do campo.", 400);

    // Respostas rapidas -------------------------------------------------------

    public static ConfiguracoesServiceException RespostaRapidaNaoEncontrada() =>
        new("resposta_rapida_nao_encontrada", "Resposta rapida nao encontrada.", 404);

    public static ConfiguracoesServiceException AtalhoObrigatorio() =>
        new("atalho_obrigatorio", "Informe o atalho.", 400);

    /// <summary>
    /// O atalho e digitado no meio do atendimento, depois de "/". Espaco encerraria a
    /// digitacao do atalho e o seletor nunca encontraria a resposta.
    /// </summary>
    public static ConfiguracoesServiceException AtalhoComEspaco() =>
        new("atalho_com_espaco", "O atalho nao pode ter espaco.", 400);

    public static ConfiguracoesServiceException AtalhoEmUso(string atalho) =>
        new("atalho_em_uso", $"Ja existe uma resposta rapida com o atalho \"/{atalho}\".", 409);

    public static ConfiguracoesServiceException TituloObrigatorio() =>
        new("titulo_obrigatorio", "Informe o titulo.", 400);
}
