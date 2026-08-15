using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

public class UsuariosServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    /// <summary>
    /// Mensagem unica para e-mail inexistente, senha errada e usuario desativado.
    /// Diferenciar os tres entregaria a um atacante a lista de e-mails validos.
    /// </summary>
    public static UsuariosServiceException CredenciaisInvalidas() =>
        new("credenciais_invalidas", "E-mail ou senha invalidos.", 401);

    public static UsuariosServiceException AtendenteNaoEncontrado() =>
        new("atendente_nao_encontrado", "Atendente nao encontrado.", 404);

    public static UsuariosServiceException EmailEmUso(string email) =>
        new("email_em_uso", $"Ja existe um atendente com o e-mail {email}.", 409);

    public static UsuariosServiceException SenhaFraca() =>
        new("senha_fraca", "A senha deve ter ao menos 8 caracteres.", 400);

    public static UsuariosServiceException SenhaAtualIncorreta() =>
        new("senha_atual_incorreta", "A senha atual esta incorreta.", 400);

    public static UsuariosServiceException SemPermissao() =>
        new("sem_permissao", "Seu perfil nao permite esta acao.", 403);

    public static UsuariosServiceException NaoPodeDesativarASiMesmo() =>
        new("nao_pode_desativar_a_si_mesmo",
            "Voce nao pode desativar o proprio acesso.", 400);

    public static UsuariosServiceException TenantNaoEncontrado() =>
        new("tenant_nao_encontrado", "Cartorio nao encontrado.", 404);
}
