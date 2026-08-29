using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

namespace CrmCartorio.Api.Modules.Usuarios.Domain.Entities;

/// <summary>
/// Usuario do sistema — funcionario da serventia. Nao confundir com Cliente,
/// que e o contato final do WhatsApp e nunca acessa o sistema.
/// </summary>
public class Atendente : EntidadeBase, IEntidadeDeTenant
{
    public Guid TenantId { get; set; }

    /// <summary>Exibido junto de cada mensagem que este atendente envia (RF-007).</summary>
    public required string Nome { get; set; }

    /// <summary>Login. Unico por tenant, armazenado em minusculas.</summary>
    public required string Email { get; set; }

    /// <summary>PBKDF2 com salt por usuario. Nunca senha em texto claro.</summary>
    public required string SenhaHash { get; set; }

    /// <summary>Permissao no sistema. O que a pessoa PODE FAZER aqui dentro.</summary>
    public PerfilDeAtendente Perfil { get; set; } = PerfilDeAtendente.Atendente;

    /// <summary>
    /// Funcao na serventia. O que a pessoa E no cartorio — informacao funcional, que
    /// existe fora do software e nao concede permissao nenhuma. Ver
    /// <see cref="CargoNaServentia"/> para o porque de ser separado do perfil.
    /// </summary>
    public CargoNaServentia Cargo { get; set; } = CargoNaServentia.NaoInformado;

    /// <summary>Pre-selecao de setor na triagem. Opcional.</summary>
    public Guid? SetorPadraoId { get; set; }

    /// <summary>Desligamento corta o acesso sem apagar o historico de mensagens.</summary>
    public bool Ativo { get; set; } = true;

    /// <summary>
    /// SEC-01: sobe a cada troca de senha, desativacao ou mudanca de Perfil. Todo
    /// JWT emitido carrega a versao vigente no instante da emissao, e o token so
    /// continua valido enquanto ela bater com este valor — e o mecanismo de
    /// revogacao de sessao. Nunca volta para tras, nem na reativacao. Ver
    /// <see cref="CrmCartorio.Api.Infrastructure.Security.IValidadorDeSessao"/>.
    /// </summary>
    public int TokenVersion { get; set; } = 1;

    /// <summary>
    /// SEC-02: falhas de login seguidas desde o ultimo sucesso ou desbloqueio.
    /// Ao chegar em <c>5</c>, volta a zero e abre um novo bloqueio — o ciclo se
    /// conta de novo do zero a cada bloqueio, nao acumula para sempre.
    /// </summary>
    public int FalhasDeLoginConsecutivas { get; set; }

    /// <summary>
    /// SEC-02: quantos bloqueios seguidos ja aconteceram SEM login bem-sucedido
    /// no meio. Escolhe o tempo do proximo bloqueio na progressao 1, 2, 4, 8, 16
    /// e 30 minutos; para no 6 (teto de 30 min). Um login bem-sucedido zera isto —
    /// e por isso a progressao recomeca em 1 minuto para quem de fato voltou a
    /// entrar.
    /// </summary>
    public byte NivelDeBloqueio { get; set; }

    /// <summary>
    /// SEC-02: enquanto estiver preenchido e no futuro, o login e recusado —
    /// <b>mesmo com a senha correta</b>. So a passagem do tempo ou uma
    /// redefinicao de senha pelo administrador liberam a conta; acertar a senha
    /// nao fura bloqueio vigente. Ver <c>AutenticacaoService.AutenticarAsync</c>.
    /// </summary>
    public DateTime? BloqueadoAte { get; set; }

    public DateTime? UltimoAcessoEm { get; set; }

    public bool PodeGerirCadastros =>
        Perfil is PerfilDeAtendente.Supervisor or PerfilDeAtendente.Administrador;

    public bool PodeGerirAtendentes =>
        Perfil is PerfilDeAtendente.Administrador;

    /// <summary>Transferir ou encerrar conversa de terceiro exige supervisao.</summary>
    public bool PodeOperarConversaDeTerceiro =>
        Perfil is PerfilDeAtendente.Supervisor or PerfilDeAtendente.Administrador;
}
