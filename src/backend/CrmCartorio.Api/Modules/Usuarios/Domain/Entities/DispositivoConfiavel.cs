using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Usuarios.Domain.Entities;

/// <summary>
/// SEC-03 — "confiar neste dispositivo por 30 dias" no segundo fator do
/// login. Cada linha é uma concessão de confiança de UM navegador para UMA
/// conta: o registro é amarrado a <see cref="TenantId"/> +
/// <see cref="AtendenteId"/> + <see cref="HashDoToken"/>. O mesmo navegador
/// usado por duas contas gera duas linhas, e a mesma conta acessada de dois
/// navegadores também — nenhuma das duas dispensa o segundo fator da outra.
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
/// uso bem-sucedido (uso único por rotação): o valor apresentado morre no
/// mesmo UPDATE que grava o sucessor.
///
/// O que isso dá, exatamente, quando o cookie é copiado: <b>quem apresentar o
/// token válido primeiro recebe a rotação e segue confiável; a cópia antiga
/// deixa de valer e cai para o segundo fator completo.</b> Não há aqui
/// detecção de roubo nem revogação dos dois lados — se o ladrão chegar antes,
/// é o dono legítimo que passa a ser desafiado. O ganho real é limitar a
/// janela: um cookie exfiltrado vale por um único uso, não pelos 30 dias.
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
