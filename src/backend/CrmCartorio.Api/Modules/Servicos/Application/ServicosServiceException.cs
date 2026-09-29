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

    public static ServicosServiceException OrdemInvalida() =>
        new("ordem_invalida", "A posição no menu deve ficar entre 0 e 999.", 400);

    public static ServicosServiceException ServicoPaiInvalido(string motivo) =>
        new("servico_pai_invalido", motivo, 400);

    public static ServicosServiceException MensagemLonga() =>
        new("mensagem_longa", "A mensagem do serviço passa de 12000 caracteres.", 400);

    public static ServicosServiceException ParteLonga(int parte, int total) =>
        new("mensagem_longa",
            total == 1
                ? "A mensagem passa de 4000 caracteres, o máximo de uma mensagem no WhatsApp. Divida em partes com uma linha só com ---."
                : $"A parte {parte} da mensagem passa de 4000 caracteres, o máximo de uma mensagem no WhatsApp. Divida mais com uma linha só com ---.",
            400);

    public static ServicosServiceException MarcadorDesconhecido(IReadOnlyList<string> marcadores) =>
        new("marcador_desconhecido",
            $"Marcador desconhecido na mensagem do serviço: {string.Join(", ", marcadores)}.", 400);

    public static ServicosServiceException SetorInvalido() =>
        new("setor_invalido", "O setor informado nao existe neste cartorio.", 400);
}
