using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

public class AtendimentoServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static AtendimentoServiceException ConversaNaoEncontrada() =>
        new("conversa_nao_encontrada", "Conversa nao encontrada.", 404);

    public static AtendimentoServiceException ClienteNaoEncontrado() =>
        new("cliente_nao_encontrado", "Cliente nao encontrado.", 404);

    public static AtendimentoServiceException AtendenteNaoEncontrado() =>
        new("atendente_nao_encontrado", "Atendente nao encontrado.", 404);

    public static AtendimentoServiceException SetorNaoEncontrado() =>
        new("setor_nao_encontrado", "Setor nao encontrado.", 404);

    public static AtendimentoServiceException EtapaNaoEncontrada() =>
        new("etapa_nao_encontrada", "Etapa nao encontrada.", 404);

    public static AtendimentoServiceException TipoServicoNaoEncontrado() =>
        new("tipo_servico_nao_encontrado", "Tipo de servico nao encontrado.", 404);

    public static AtendimentoServiceException ConversaEncerrada() =>
        new("conversa_encerrada",
            "A conversa esta encerrada. Reabra antes de continuar o atendimento.", 409);

    public static AtendimentoServiceException ConversaJaAtiva() =>
        new("conversa_ja_ativa", "A conversa ja esta ativa.", 409);

    public static AtendimentoServiceException ConversaSemResponsavel() =>
        new("conversa_sem_responsavel",
            "Assuma a conversa antes de executar esta acao.", 409);

    public static AtendimentoServiceException NaoEhOResponsavel(string responsavel) =>
        new("nao_eh_o_responsavel",
            $"Esta conversa esta em atendimento por {responsavel}.", 403);

    public static AtendimentoServiceException MensagemVazia() =>
        new("mensagem_vazia", "A mensagem nao pode ser vazia.", 400);

    public static AtendimentoServiceException MensagemCitadaInvalida() =>
        new("mensagem_citada_invalida",
            "A mensagem citada nao pertence a esta conversa.", 400);

    public static AtendimentoServiceException ClienteJaTemConversaAtiva(string protocolo) =>
        new("cliente_ja_tem_conversa_ativa",
            $"Este cliente ja tem a conversa {protocolo} em aberto.", 409)
        {
            Detalhes = { ["protocolo"] = protocolo }
        };

    public static AtendimentoServiceException NadaParaMover() =>
        new("nada_para_mover", "Informe uma etapa ou um setor de destino.", 400);
}

/// <summary>
/// Dois atendentes tentaram assumir a mesma conversa. E o cenario que o produto
/// existe para evitar, entao o erro carrega o nome de quem ganhou a corrida —
/// mensagem util no painel, nao apenas "conflito".
/// </summary>
public class ConflitoDeAtribuicaoException : ExcecaoDeAplicacao
{
    public ConflitoDeAtribuicaoException(Guid conversaId, Guid atendenteId, string atendenteNome)
        : base("conversa_ja_assumida",
               $"Esta conversa acabou de ser assumida por {atendenteNome}.",
               409)
    {
        ConversaId = conversaId;
        AtendenteId = atendenteId;
        AtendenteNome = atendenteNome;

        // O painel usa esses campos para atualizar a linha na hora, sem recarregar.
        Detalhes["conversaId"] = conversaId;
        Detalhes["atendenteId"] = atendenteId;
        Detalhes["atendenteNome"] = atendenteNome;
    }

    public Guid ConversaId { get; }

    public Guid AtendenteId { get; }

    public string AtendenteNome { get; }
}
