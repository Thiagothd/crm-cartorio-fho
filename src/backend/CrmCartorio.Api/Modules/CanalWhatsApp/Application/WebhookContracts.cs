using System.Text.Json;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>
/// Envelope cru do webhook da Evolution.
/// </summary>
/// <remarks>
/// `Data` fica como <see cref="JsonElement"/> de proposito: o formato do
/// terceiro muda entre versoes do gateway, e desserializar em tipo fechado faria
/// uma mudanca de campo derrubar o recebimento inteiro. Aqui a leitura e
/// defensiva e o que nao reconhecemos e ignorado, nao explode.
/// </remarks>
public record PayloadDoWebhook(string? Event, string? Instance, JsonElement Data);

/// <summary>Mensagem que chegou do cliente, ja traduzida para o nosso vocabulario.</summary>
/// <param name="TipoMimeDaMidia">
/// Como o WhatsApp declarou o arquivo. Chega com parametro com frequencia
/// (<c>audio/ogg; codecs=opus</c>) — quem consome normaliza.
/// </param>
public record MensagemRecebidaDoCanal(
    string TelefoneE164,
    string? NomeDoPerfil,
    string Conteudo,
    TipoMensagem Tipo,
    string IdExterno,
    string? UrlDaMidia,
    string? NomeDoArquivo,
    string? TipoMimeDaMidia)
{
    /// <summary>Traz arquivo para baixar do gateway.</summary>
    public bool TemMidia => Tipo != TipoMensagem.Texto;
}

/// <summary>Mudanca de estado de entrega de uma mensagem que NOS enviamos.</summary>
public record StatusDeEntregaDoCanal(string IdExterno, StatusEntrega Status);

/// <summary>
/// Traduz o payload da Evolution para os tipos acima.
/// </summary>
/// <remarks>
/// Vive dentro do slice e nao sai dele: o Atendimento nunca vê um campo da
/// Evolution. Trocar de gateway amanha mexe aqui e em mais lugar nenhum.
/// </remarks>
public static class TradutorDePayload
{
    /// <summary>
    /// Extrai a mensagem do cliente, ou <c>null</c> quando o evento nao deve virar
    /// mensagem no fio.
    /// </summary>
    /// <remarks>
    /// Devolve null — em vez de lancar — em tres casos que sao NORMAIS, nao erro:
    /// <list type="bullet">
    ///   <item><b>fromMe</b>: eco da nossa propria mensagem enviada. Gravar isso
    ///   duplicaria no fio cada resposta do atendente.</item>
    ///   <item><b>Grupo</b> (<c>@g.us</c>): o numero do piloto e pessoal e esta em
    ///   grupos de familia. Conversa de grupo nao e atendimento de cartorio, e
    ///   criar protocolo para cada uma poluiria a inbox com a vida do Thiago.</item>
    ///   <item><b>Tipo nao suportado</b>: figurinha, localizacao, contato.</item>
    /// </list>
    /// </remarks>
    public static MensagemRecebidaDoCanal? LerMensagem(JsonElement data)
    {
        if (!data.TryGetProperty("key", out var chave))
        {
            return null;
        }

        if (chave.TryGetProperty("fromMe", out var deMim)
            && deMim.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        var remoteJid = Texto(chave, "remoteJid");
        if (string.IsNullOrWhiteSpace(remoteJid) || remoteJid.Contains("@g.us"))
        {
            return null;
        }

        var idExterno = Texto(chave, "id");
        if (string.IsNullOrWhiteSpace(idExterno))
        {
            return null;
        }

        var telefone = SomenteDigitos(remoteJid.Split('@')[0]);
        if (telefone.Length is < 10 or > 15)
        {
            return null;
        }

        var nomeDoPerfil = Texto(data, "pushName");

        if (!data.TryGetProperty("message", out var mensagem)
            || mensagem.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return LerConteudo(mensagem) is { } conteudo
            ? new MensagemRecebidaDoCanal(
                telefone,
                string.IsNullOrWhiteSpace(nomeDoPerfil) ? null : nomeDoPerfil,
                conteudo.Texto,
                conteudo.Tipo,
                idExterno,
                conteudo.Url,
                conteudo.NomeDoArquivo,
                conteudo.TipoMime)
            : null;
    }

    /// <summary>
    /// Le a mudanca de estado de entrega, ou <c>null</c> se o evento nao trouxer
    /// um estado que nos interessa.
    /// </summary>
    /// <remarks>
    /// So promove para frente: PENDING e SERVER_ACK sao ruido — a mensagem ja
    /// nasce Enviada do nosso lado assim que o gateway aceita.
    /// </remarks>
    public static StatusDeEntregaDoCanal? LerStatus(JsonElement data)
    {
        var idExterno = Texto(data, "keyId");

        if (string.IsNullOrWhiteSpace(idExterno)
            && data.TryGetProperty("key", out var chave))
        {
            idExterno = Texto(chave, "id");
        }

        if (string.IsNullOrWhiteSpace(idExterno))
        {
            return null;
        }

        return Texto(data, "status")?.ToUpperInvariant() switch
        {
            "DELIVERY_ACK" => new StatusDeEntregaDoCanal(idExterno, StatusEntrega.Entregue),
            "READ" or "PLAYED" => new StatusDeEntregaDoCanal(idExterno, StatusEntrega.Lida),
            "ERROR" => new StatusDeEntregaDoCanal(idExterno, StatusEntrega.Falha),
            _ => null
        };
    }

    /// <summary>Estado da conexao (`open`, `close`, `connecting`).</summary>
    public static string? LerEstadoDaConexao(JsonElement data) =>
        Texto(data, "state") ?? Texto(data, "connection");

    private record Conteudo(
        string Texto,
        TipoMensagem Tipo,
        string? Url,
        string? NomeDoArquivo,
        string? TipoMime);

    private static Conteudo? LerConteudo(JsonElement mensagem)
    {
        // Texto simples e o caso esmagadoramente comum no balcao.
        if (Texto(mensagem, "conversation") is { Length: > 0 } simples)
        {
            return new Conteudo(simples, TipoMensagem.Texto, null, null, null);
        }

        if (mensagem.TryGetProperty("extendedTextMessage", out var estendida)
            && Texto(estendida, "text") is { Length: > 0 } textoEstendido)
        {
            return new Conteudo(textoEstendido, TipoMensagem.Texto, null, null, null);
        }

        // Legenda vira o Conteudo da mensagem. Uma foto de RG com "esse aqui ta
        // certo?" escrito junto perderia a pergunta se so guardassemos o arquivo —
        // e a pergunta e o que o atendente precisa ler.
        if (mensagem.TryGetProperty("imageMessage", out var imagem))
        {
            var legenda = Texto(imagem, "caption");
            return new Conteudo(
                string.IsNullOrWhiteSpace(legenda) ? "[imagem]" : legenda,
                TipoMensagem.Imagem,
                Texto(imagem, "url"),
                null,
                Texto(imagem, "mimetype"));
        }

        if (mensagem.TryGetProperty("documentMessage", out var documento))
        {
            var nome = Texto(documento, "fileName");
            var legenda = Texto(documento, "caption");

            return new Conteudo(
                string.IsNullOrWhiteSpace(legenda)
                    ? (string.IsNullOrWhiteSpace(nome) ? "[documento]" : nome)
                    : legenda,
                TipoMensagem.Documento,
                Texto(documento, "url"),
                nome,
                Texto(documento, "mimetype"));
        }

        // Documento com legenda chega neste envelope em versoes recentes.
        if (mensagem.TryGetProperty("documentWithCaptionMessage", out var comLegenda)
            && comLegenda.TryGetProperty("message", out var interna))
        {
            return LerConteudo(interna);
        }

        if (mensagem.TryGetProperty("audioMessage", out var audio))
        {
            return new Conteudo(
                "[áudio]",
                TipoMensagem.Audio,
                Texto(audio, "url"),
                null,
                Texto(audio, "mimetype"));
        }

        if (mensagem.TryGetProperty("videoMessage", out var video))
        {
            var legenda = Texto(video, "caption");
            return new Conteudo(
                string.IsNullOrWhiteSpace(legenda) ? "[vídeo]" : legenda,
                TipoMensagem.Video,
                Texto(video, "url"),
                null,
                Texto(video, "mimetype"));
        }

        return null;
    }

    private static string? Texto(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object
        && elemento.TryGetProperty(propriedade, out var valor)
        && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;

    private static string SomenteDigitos(string valor) =>
        new(valor.Where(char.IsDigit).ToArray());
}
