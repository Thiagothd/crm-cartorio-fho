using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;

namespace CrmCartorio.Api.Infrastructure.Auditoria;

/// <summary>
/// Porta transversal para registrar quem leu o quê — ver
/// <c>Modules/Seguranca/Domain/Entities/RegistroDeAcesso</c>. Fica em
/// <c>Infrastructure</c>, não em <c>Modules/Seguranca/Application</c>,
/// porque é chamado de dentro de outros módulos (Atendimento, Clientes) —
/// mesmo motivo de <c>INotificadorDeAtendimento</c> morar em
/// <c>Infrastructure/Realtime</c> (docs/CONVENCOES.md 7.1: um módulo não referencia o
/// <c>Application</c> interno de outro).
/// </summary>
public interface IRegistradorDeAcesso
{
    /// <summary>
    /// Registra uma leitura. Falha (propaga exceção) se a gravação falhar —
    /// de propósito: a leitura que chamou isto deve falhar junto
    /// (fail-closed), nunca silenciar a ausência de trilha.
    /// </summary>
    /// <param name="autorizacaoDeAcessoTecnicoId">
    /// Só para os tipos de ciclo de vida do acesso técnico (10-13): o ator
    /// (quem pede/aprova/nega/revoga) não está necessariamente OPERANDO sob
    /// a autorização ainda — pedir e aprovar acontecem ANTES dela
    /// existir/valer, então <c>ctx.AutorizacaoDeAcessoTecnicoId</c> (a
    /// autorização vigente da sessão de quem chama) não serve aqui. Quando
    /// omitido, cai no comportamento padrão (lido do contexto).
    /// </param>
    Task RegistrarAsync(
        TipoDeAcesso tipo,
        Guid? conversaId = null,
        Guid? clienteId = null,
        Guid? mensagemId = null,
        Guid? autorizacaoDeAcessoTecnicoId = null,
        CancellationToken ct = default);
}
