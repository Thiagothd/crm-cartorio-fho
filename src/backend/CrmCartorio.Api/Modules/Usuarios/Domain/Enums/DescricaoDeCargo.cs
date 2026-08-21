namespace CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

/// <summary>
/// O de-para de <see cref="CargoNaServentia"/> para o rótulo exibido.
/// </summary>
/// <remarks>
/// Mora no <c>Domain</c>, não no <c>Application</c>, de propósito: o módulo de
/// Indicadores também precisa deste rótulo para a aba de Atendentes, e a regra
/// de arquitetura (CLAUDE.md 7.1) proíbe um módulo referenciar o
/// <c>Application</c> interno de outro. Tipo de <c>Domain</c> é compartilhável —
/// é o mesmo motivo pelo qual <c>StatusConversa</c> e <c>TipoDePessoa</c> são
/// lidos de fora dos módulos que os declaram.
///
/// A alternativa era duplicar o switch em Indicadores, e aí "Escrevente
/// autorizado" viraria duas verdades que divergem no primeiro ajuste de palavra
/// — exatamente o que o comentário original de <c>DescreverCargo</c> queria
/// evitar quando o tornou público.
/// </remarks>
public static class DescricaoDeCargo
{
    public static string De(CargoNaServentia cargo) => cargo switch
    {
        CargoNaServentia.Titular => "Titular",
        CargoNaServentia.Substituto => "Substituto",
        CargoNaServentia.EscreventeAutorizado => "Escrevente autorizado",
        CargoNaServentia.Escrevente => "Escrevente",
        CargoNaServentia.Auxiliar => "Auxiliar",
        _ => "Não informado"
    };
}
