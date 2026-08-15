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

    public DateTime? UltimoAcessoEm { get; set; }

    public bool PodeGerirCadastros =>
        Perfil is PerfilDeAtendente.Supervisor or PerfilDeAtendente.Administrador;

    public bool PodeGerirAtendentes =>
        Perfil is PerfilDeAtendente.Administrador;

    /// <summary>Transferir ou encerrar conversa de terceiro exige supervisao.</summary>
    public bool PodeOperarConversaDeTerceiro =>
        Perfil is PerfilDeAtendente.Supervisor or PerfilDeAtendente.Administrador;
}
