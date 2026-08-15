using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Servicos.Application;

public class ServicosServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static ServicosServiceException NaoEncontrado() =>
        new("tipo_servico_nao_encontrado", "Tipo de servico nao encontrado.", 404);

    public static ServicosServiceException NomeObrigatorio() =>
        new("nome_obrigatorio", "Informe o nome do servico.", 400);

    public static ServicosServiceException NomeEmUso(string nome) =>
        new("nome_em_uso", $"Ja existe um servico chamado \"{nome}\".", 409);

    public static ServicosServiceException PrazoInvalido() =>
        new("prazo_invalido", "O prazo deve ser de pelo menos 1 hora.", 400);

    public static ServicosServiceException PercentualAlertaInvalido() =>
        new("percentual_alerta_invalido",
            "O percentual de alerta deve ficar entre 1 e 99.", 400);

    public static ServicosServiceException SemPermissao() =>
        new("sem_permissao", "Seu perfil nao permite alterar cadastros.", 403);
}
