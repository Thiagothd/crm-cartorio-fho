using System.Net;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// SEC-02: descobre o IP real de quem chamou, para servir de chave de particao do
/// limite de tentativas de login.
/// </summary>
/// <remarks>
/// <b>Por que nao basta <c>RemoteIpAddress</c>:</b> em producao o trafego chega
/// pela borda da Cloudflare e entra no IIS atraves do <c>cloudflared.exe</c>, que
/// roda na PROPRIA maquina (ver docs/INSTALACAO.md, secoes 1 e 5). Do ponto de
/// vista do IIS, portanto, toda requisicao publica vem de <c>127.0.0.1</c> — sem
/// tratar o cabecalho, o mundo inteiro dividiria um unico balde de limite e a
/// protecao por IP nao existiria na pratica.
///
/// <b>Por que nao <c>ForwardedHeadersMiddleware</c>:</b> ele resolve
/// <c>X-Forwarded-For</c>, que aqui nao e a fonte de verdade. Quem termina a borda
/// e a Cloudflare, e ela sempre SOBRESCREVE <c>CF-Connecting-IP</c> com o IP real
/// do visitante — um valor que o cliente nao consegue forjar atravessando o tunel.
///
/// <b>Onde esta a desconfianca:</b> o cabecalho so e aceito quando o peer imediato
/// ja e loopback, ou seja, quando a conexao so pode ter vindo do <c>cloudflared</c>
/// local. Vindo de qualquer outro lugar, o cabecalho e ignorado por completo e vale
/// o IP da conexao. Falha para o lado seguro nos dois sentidos: um cabecalho
/// forjado nunca vira chave, e uma deteccao errada de proxy no maximo junta
/// requisicoes no mesmo balde (limite mais rigido), nunca afrouxa o limite.
/// </remarks>
public static class ResolvedorDeIpDoCliente
{
    /// <summary>Cabecalho que a borda da Cloudflare preenche com o IP do visitante.</summary>
    public const string CabecalhoDaCloudflare = "CF-Connecting-IP";

    /// <summary>
    /// Usado quando nao ha IP de conexao algum — cenario de teste em memoria e
    /// transportes sem endereco remoto. Vira uma chave de particao como outra
    /// qualquer, entao continua havendo limite.
    /// </summary>
    public const string Desconhecido = "desconhecido";

    public static string Resolver(HttpContext http)
    {
        var remoto = http.Connection.RemoteIpAddress;

        // TryParse e nao o texto cru do cabecalho: sem isso, qualquer string que um
        // atacante conseguisse injetar viraria chave de particao — cada valor
        // diferente abriria um balde novo de 10 tentativas, o que transformaria o
        // proprio limitador num jeito de nao ter limite.
        if (remoto is not null
            && IPAddress.IsLoopback(remoto)
            && http.Request.Headers.TryGetValue(CabecalhoDaCloudflare, out var valores)
            && IPAddress.TryParse(valores.ToString(), out var ipReal))
        {
            // ToString() do IPAddress ja parseado, nao o texto recebido: normaliza
            // as varias grafias do mesmo endereco IPv6 (abreviacoes, zeros a
            // esquerda, maiusculas) para uma unica chave — do contrario, o mesmo
            // cliente teria varios baldes so trocando a formatacao.
            return ipReal.ToString();
        }

        return remoto?.ToString() ?? Desconhecido;
    }
}
