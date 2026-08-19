using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Arquivo que veio junto com a mensagem, ja baixado do gateway.
/// </summary>
/// <remarks>
/// Chega em BYTES, e nao como URL para o Atendimento buscar. A midia do WhatsApp
/// esta criptografada nos servidores da Meta e so o gateway sabe decifrar — quem
/// conhece o gateway e o slice CanalWhatsApp, entao e ele que baixa. Passar uma
/// URL aqui obrigaria o Atendimento a falar com o terceiro.
/// </remarks>
public record ArquivoDoCanal(byte[] Conteudo, string? Nome, string? TipoMime);

/// <summary>Mensagem que chegou do canal, no vocabulario do Atendimento.</summary>
/// <param name="TelefoneE164">Somente digitos, com codigo do pais.</param>
/// <param name="NomeDoPerfil">Nome do WhatsApp. Vira o nome do cliente novo.</param>
/// <param name="IdExterno">Id da mensagem no WhatsApp, para casar com o status depois.</param>
/// <param name="Arquivo">
/// Nulo na imensa maioria. Tambem nulo quando o download falhou: a mensagem entra
/// no fio mesmo assim, com a legenda, porque saber que o cliente mandou algo vale
/// mais do que nao registrar nada.
/// </param>
public record MensagemDoCanal(
    string TelefoneE164,
    string? NomeDoPerfil,
    string Conteudo,
    TipoMensagem Tipo,
    string IdExterno,
    ArquivoDoCanal? Arquivo = null,
    string? FotoPerfilUrl = null);

/// <summary>
/// Porta de ENTRADA do canal real: transforma mensagem de fora em atendimento.
/// </summary>
/// <remarks>
/// <b>Recebe o tenant explicitamente</b>, e essa e a diferenca que justifica a
/// porta existir. O restante do slice descobre o tenant por
/// <c>IContextoDoUsuario</c>, que le o JWT; o webhook do gateway nao carrega
/// token nenhum. Mesma situacao do <see cref="IVarredorDePrazos"/>, que roda em
/// servico de fundo — e mesma solucao: quem chama informa o tenant.
///
/// Na Fase 1 este papel era do <c>MensagemService.SimularRecebimentoAsync</c>,
/// que exigia uma conversa ja existente e vinha do painel. Aqui a mensagem chega
/// de alguem que pode nem ser cliente ainda, entao a porta resolve tudo: cliente,
/// conversa, protocolo, boas-vindas e triagem.
/// </remarks>
public interface IRecebimentoDeWhatsApp
{
    /// <summary>
    /// Registra a mensagem, criando cliente e conversa quando necessario.
    /// </summary>
    /// <returns>O id da conversa que recebeu a mensagem.</returns>
    Task<Guid> ReceberAsync(Guid tenantId, MensagemDoCanal mensagem, CancellationToken ct = default);

    /// <summary>
    /// Promove o estado de entrega de uma mensagem que ESTE sistema enviou.
    /// </summary>
    /// <remarks>
    /// So anda para frente (Enviada -> Entregue -> Lida). O WhatsApp entrega
    /// confirmacoes fora de ordem com frequencia, e sem essa trava um
    /// DELIVERY_ACK atrasado rebaixaria uma mensagem que o cliente ja leu.
    /// </remarks>
    Task AtualizarEntregaAsync(
        Guid tenantId,
        string idExternoWhatsapp,
        StatusEntrega status,
        CancellationToken ct = default);
}
