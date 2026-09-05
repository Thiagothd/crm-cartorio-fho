using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

namespace CrmCartorio.Api.Modules.Usuarios.Domain.Entities;

/// <summary>
/// SEC-03: um desafio de MFA em aberto — segunda etapa de login, sessao de
/// matricula, ou o ticket fora de banda em si (<see cref="TipoDeDesafioMfa.Ticket"/>).
/// </summary>
/// <remarks>
/// Estado persistido no banco, nao um JWT de escopo reduzido: uso unico de
/// verdade exige uma linha que possa ser consumida atomicamente, e um desafio
/// precisa poder ser invalidado por um evento de seguranca que aconteceu DEPOIS
/// dele ter sido emitido — o que <see cref="TokenVersionNoDesafio"/> resolve.
///
/// Nunca guarda o codigo em si, so o hash de quem o apresenta
/// (<c>HashDoDesafio</c> na configuracao) — o desafio devolvido ao cliente e o
/// segredo; o banco so sabe reconhece-lo.
/// </remarks>
public class DesafioDeMfa : EntidadeBase
{
    public required Guid TenantId { get; set; }

    public required Guid AtendenteId { get; set; }

    /// <summary>SHA-256 do valor aleatorio devolvido ao cliente/portador. Nunca o valor em si.</summary>
    public required string HashDoDesafio { get; set; }

    public required TipoDeDesafioMfa Tipo { get; set; }

    /// <summary>
    /// Snapshot do <c>Atendente.TokenVersion</c> no instante da emissao. Qualquer
    /// evento que suba a versao (troca de senha, desativacao, mudanca de Perfil,
    /// reset de MFA) invalida este desafio na conclusao — ver
    /// <c>AutenticacaoService.ConcluirLoginMfaAsync</c>.
    /// </summary>
    public required int TokenVersionNoDesafio { get; set; }

    public required DateTime ExpiraEm { get; set; }

    /// <summary>
    /// Incrementado ANTES de validar o codigo (reserva de tentativa), nunca
    /// depois — e o que faz o teto de 5 valer mesmo se a validacao for
    /// interrompida no meio. Ver Modules/Usuarios/README.md, secao SEC-03.
    /// </summary>
    public int TentativasDeCodigo { get; set; }

    /// <summary>Nulo enquanto em aberto. So marcado no caminho de SUCESSO.</summary>
    public DateTime? ConsumidoEm { get; set; }

    public string? IpDeOrigem { get; set; }
}
