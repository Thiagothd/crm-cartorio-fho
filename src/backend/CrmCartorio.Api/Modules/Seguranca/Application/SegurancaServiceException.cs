using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Seguranca.Application;

public class SegurancaServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static SegurancaServiceException SemPermissao() =>
        new("sem_permissao", "Voce nao tem permissao para esta acao.", 403);

    // ---------------------------------------------------- Acesso tecnico autorizado

    public static SegurancaServiceException MotivoObrigatorio() =>
        new("motivo_obrigatorio", "Descreva o motivo do pedido de acesso.", 400);

    public static SegurancaServiceException EscopoDeConversaExigeConversaId() =>
        new("escopo_de_conversa_exige_conversa_id", "Escopo de uma conversa exige informar qual conversa.", 400);

    public static SegurancaServiceException ConversaNaoEncontrada() =>
        new("conversa_nao_encontrada", "Conversa nao encontrada.", 404);

    public static SegurancaServiceException DuracaoInvalida(int maximoMinutos) =>
        new("duracao_invalida", $"Duracao deve ser entre 1 e {maximoMinutos} minutos.", 400);

    public static SegurancaServiceException JaExisteSolicitacaoEmAberto() =>
        new("ja_existe_solicitacao_em_aberto",
            "Ja existe um pedido de acesso tecnico pendente ou ativo para esta conta.", 409);

    public static SegurancaServiceException AutorizacaoNaoEncontrada() =>
        new("autorizacao_nao_encontrada", "Autorizacao de acesso tecnico nao encontrada.", 404);

    public static SegurancaServiceException AutorizacaoNaoEstaPendente() =>
        new("autorizacao_nao_esta_pendente", "Este pedido ja foi decidido.", 409);

    public static SegurancaServiceException AutorizacaoNaoPodeSerRevogada() =>
        new("autorizacao_nao_pode_ser_revogada", "So uma autorizacao aprovada e ainda nao revogada pode ser revogada.", 409);

    public static SegurancaServiceException ContaNaoEhTecnica() =>
        new("conta_nao_e_tecnica", "Esta conta nao e uma conta tecnica.", 403);
}
