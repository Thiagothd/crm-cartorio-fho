using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Gera as mensagens automaticas de um gatilho no fio de uma conversa.
/// </summary>
/// <remarks>
/// NAO depende de <c>IContextoDoUsuario</c>, e isso e deliberado: o gatilho de prazo
/// vem de um servico de fundo, onde nao existe requisicao HTTP nem token para ler
/// TenantId. O tenant vem da propria conversa.
/// </remarks>
public interface IDisparadorDeMensagemAutomatica
{
    /// <summary>
    /// Adiciona as mensagens do gatilho ao contexto — <b>sem gravar</b>. Quem chamou
    /// decide o momento do <c>SaveChanges</c>.
    /// </summary>
    /// <remarks>
    /// Enfileirar em vez de gravar existe para que a mensagem de boas-vindas entre na
    /// MESMA transacao que cria a conversa e gera o protocolo. Se o disparador
    /// gravasse por conta propria, um erro depois deixaria uma mensagem de
    /// boas-vindas orfa, apontando para uma conversa que nao existe.
    /// </remarks>
    /// <param name="etapaAlvoId">
    /// Etapa que acabou de receber a conversa. Usado apenas com
    /// <see cref="GatilhoDeMensagem.EtapaAlterada"/>; ignorado nos demais.
    /// </param>
    Task<IReadOnlyList<Mensagem>> EnfileirarAsync(
        Conversa conversa,
        GatilhoDeMensagem gatilho,
        Guid? etapaAlvoId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Setores ativos do tenant, na ordem em que aparecem no menu de triagem.
    /// </summary>
    /// <remarks>
    /// Vive aqui, e nao em cada chamador, porque e a mesma lista que preenche
    /// {MenuSetores} e que interpreta a resposta do cliente. Uma fonte so.
    /// </remarks>
    Task<IReadOnlyList<TriagemAutomatica.OpcaoDeMenu>> SetoresDoMenuAsync(
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Servicos ativos do setor, na ordem em que aparecem no menu de 2o nivel —
    /// mesmo papel de <see cref="SetoresDoMenuAsync"/>, um nivel abaixo.
    /// </summary>
    Task<IReadOnlyList<TriagemAutomatica.OpcaoDeMenu>> ServicosDoMenuAsync(
        Guid tenantId,
        Guid setorId,
        CancellationToken ct = default);

    /// <summary>Os serviços de dentro de <paramref name="servicoPaiId"/>, na ordem do menu.</summary>
    Task<IReadOnlyList<TriagemAutomatica.OpcaoDeMenu>> ServicosDoSubmenuAsync(
        Guid tenantId,
        Guid servicoPaiId,
        CancellationToken ct = default);

    /// <summary>
    /// Gera, como mensagem automática, um texto que não vem de regra cadastrada — o
    /// texto próprio de um serviço. Mesmos marcadores e mesma chave geral de
    /// <see cref="EnfileirarAsync"/>.
    /// </summary>
    Task<IReadOnlyList<Mensagem>> EnfileirarTextoAsync(
        Conversa conversa,
        string modelo,
        string origem,
        CancellationToken ct = default);
}
