using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

public class UsuariosServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    /// <summary>
    /// Mensagem unica para e-mail inexistente, senha errada e usuario desativado.
    /// Diferenciar os tres entregaria a um atacante a lista de e-mails validos.
    /// </summary>
    // Acento no texto EXIBIDO, sem acento no `codigo` — a regra 7.2 do
    // CLAUDE.md: identificador de codigo sem acento, mensagem de usuario com.
    // Nao diz QUAL dos dois errou, e e' deliberado: apontar "e-mail nao existe"
    // confirmaria para quem tenta adivinhar que aquele endereco tem conta aqui.
    public static UsuariosServiceException CredenciaisInvalidas() =>
        new("credenciais_invalidas", "E-mail ou senha inválidos.", 401);

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

    // ------------------------------------------------------------- SEC-03

    /// <summary>
    /// Desafio de MFA (etapa 2 do login, ou sessao de matricula) desconhecido,
    /// expirado, ja consumido ou com tentativas esgotadas. Uma unica mensagem
    /// para os quatro casos: nao ha risco de enumeracao igual ao de
    /// <see cref="CredenciaisInvalidas"/> (a etapa 1 ja exigiu senha certa para
    /// chegar aqui), mas nao ha por que revelar qual dos quatro aconteceu.
    /// </summary>
    public static UsuariosServiceException DesafioMfaInvalido() =>
        new("desafio_mfa_invalido", "Codigo invalido, expirado ou sessao encerrada. Faca login novamente.", 401);

    public static UsuariosServiceException CodigoMfaInvalido() =>
        new("codigo_mfa_invalido", "Codigo invalido.", 401);

    public static UsuariosServiceException MfaBloqueado() =>
        new("mfa_bloqueado", "Muitas tentativas de codigo. Aguarde antes de tentar novamente.", 401);

    public static UsuariosServiceException MfaObrigatorioNaoMatriculado() =>
        new("mfa_obrigatorio_nao_matriculado",
            "Este acesso exige verificacao em duas etapas. Procure um administrador para matricular seu autenticador.",
            401);

    /// <remarks>
    /// Único gatilho hoje: conta em rematrícula obrigatória após reset de
    /// emergência (break-glass) tentando reativar o MFA pelo fluxo
    /// voluntário. Qualquer perfil — inclusive Titular e Administrador —
    /// pode ativar o MFA voluntariamente fora dessa situação; o código e o
    /// nome do método sobrevivem por compatibilidade com o fluxo de
    /// matrícula/rematrícula por ticket, que continua existindo.
    /// </remarks>
    public static UsuariosServiceException MatriculaPrivilegiadaExigeTicket() =>
        new("matricula_privilegiada_exige_ticket",
            "Esta conta está em rematrícula obrigatória após um reset de emergência — a reativação do MFA exige o fluxo de matrícula com ticket fora de banda.",
            409);

    public static UsuariosServiceException MfaJaAtivo() =>
        new("mfa_ja_ativo", "A verificacao em duas etapas ja esta ativa.", 409);

    public static UsuariosServiceException MfaNaoAtivo() =>
        new("mfa_nao_ativo", "A verificacao em duas etapas nao esta ativa.", 409);

    public static UsuariosServiceException MfaObrigatorioNaoPodeSerDesativado() =>
        new("mfa_obrigatorio_nao_pode_ser_desativado",
            "A politica da serventia exige verificacao em duas etapas para este perfil — nao e possivel desativar.", 409);

    public static UsuariosServiceException TicketInvalido() =>
        new("ticket_invalido", "Ticket de matricula invalido, expirado ou ja usado.", 401);

    public static UsuariosServiceException ContaNaoPrivilegiada() =>
        new("conta_nao_privilegiada", "Este fluxo e exclusivo para contas privilegiadas.", 400);

    public static UsuariosServiceException ApenasTitularReseteiaTitular() =>
        new("apenas_titular_reseta_titular",
            "So um administrador que tambem seja Titular pode resetar o MFA de outro Titular.", 403);

    public static UsuariosServiceException MotivoObrigatorio() =>
        new("motivo_obrigatorio", "Informe o motivo desta acao.", 400);

    public static UsuariosServiceException FlipAbortadoContasSemMfa(IReadOnlyList<string> pendentes) =>
        new UsuariosServiceException(
            "politica_mfa_contas_pendentes",
            "Existem contas privilegiadas sem MFA ativo. Matricule todas antes de exigir a politica.",
            409)
        { Detalhes = { ["contasPendentes"] = pendentes } };

    /// <summary>
    /// SEC-03 (revisao independente do PR #46, bloqueadores 10/11) — o estado
    /// da conta (TokenVersion, Perfil/Cargo, Ativo) mudou entre a validacao da
    /// autorizacao (senha/step-up/desafio) e o instante final do commit. Falha
    /// fechado: nunca devolve segredo, QR, ticket ou efetiva a mutacao — quem
    /// chamou precisa recomecar do zero (nova senha, novo step-up).
    /// </summary>
    public static UsuariosServiceException OperacaoDesatualizada() =>
        new("operacao_desatualizada",
            "O estado da conta mudou durante esta operacao. Recarregue e tente novamente.", 409);
}
