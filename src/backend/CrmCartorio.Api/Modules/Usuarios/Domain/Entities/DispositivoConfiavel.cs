using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Usuarios.Domain.Entities;

/// <summary>
/// SEC-03 — "confiar neste dispositivo por 30 dias" no segundo fator do
/// login. Uma linha por navegador que aceitou a opção, nunca por conta.
/// </summary>
/// <remarks>
/// Mesma disciplina de <see cref="DesafioDeMfa"/>: o banco nunca guarda o
/// token em claro, só o hash (<see cref="HashDoToken"/>) de quem apresenta o
/// cookie. <see cref="TokenVersionNoRegistro"/> é o mesmo mecanismo de
/// <c>DesafioDeMfa.TokenVersionNoDesafio</c>/<c>Atendente.MfaPendenteTokenVersion</c>
/// — um snapshot da <see cref="Entities.Atendente.TokenVersion"/> vigente no
/// momento em que o dispositivo foi marcado confiável. Qualquer evento que já
/// sobe essa versão hoje (troca de senha, reset/desativação/troca de MFA,
/// break-glass, mudança de Perfil/Cargo/Ativo) invalida este registro sem
/// nenhum código novo de revogação — é reconferido a cada uso em
/// <c>AutenticacaoService.AutenticarAsync</c>.
///
/// <see cref="ExpiraEm"/> é fixado no momento da concessão e NUNCA estendido
/// — teto duro de 30 dias, mesmo com uso diário. O hash é ROTACIONADO a cada
/// uso bem-sucedido (uso único por rotação): um cookie roubado, usado uma
/// vez, para de funcionar tanto para quem roubou quanto para o dono legítimo
/// — o próximo login de qualquer um dos dois exige o segundo fator de novo,
/// o que é o sinal de alerta correto.
/// </remarks>
public class DispositivoConfiavel : EntidadeBase
{
    public required Guid TenantId { get; set; }

    public required Guid AtendenteId { get; set; }

    /// <summary>SHA-256 hex do token opaco de 256 bits devolvido como cookie HttpOnly. Nunca o valor em si.</summary>
    public required string HashDoToken { get; set; }

    /// <summary>Snapshot de <c>Atendente.TokenVersion</c> na concessão — ver o `remarks` da classe.</summary>
    public required int TokenVersionNoRegistro { get; set; }

    /// <summary>Fixo desde a concessão. Rotação de uso NUNCA estende este valor.</summary>
    public required DateTime ExpiraEm { get; set; }

    /// <summary>Nulo enquanto ativo. Marcado por "esquecer dispositivos confiáveis".</summary>
    public DateTime? RevogadoEm { get; set; }

    public DateTime? UltimoUsoEm { get; set; }

    public string? IpDeOrigem { get; set; }
}
