namespace CrmCartorio.Api.Infrastructure.Security;

/// <inheritdoc cref="ICookieDeDispositivoConfiavel"/>
public class CookieDeDispositivoConfiavel(IHttpContextAccessor acessor, IHostEnvironment ambiente)
    : ICookieDeDispositivoConfiavel
{
    /// <summary>
    /// Nome do cookie. Opaco de propósito — não carrega e-mail, id nem nada
    /// que identifique a conta para quem só olha o navegador.
    /// </summary>
    internal const string Nome = "crm_dispositivo_confiavel";

    /// <summary>
    /// SÓ as duas rotas que leem/escrevem este cookie (login etapa 1 e etapa
    /// 2) ficam sob este caminho. Sem isto o navegador anexaria a credencial
    /// em TODA chamada da API, ampliando a superfície sem nenhum ganho.
    /// </summary>
    internal const string Caminho = "/api/autenticacao";

    public string? Ler()
    {
        var cookie = acessor.HttpContext?.Request.Cookies[Nome];
        return string.IsNullOrWhiteSpace(cookie) ? null : cookie;
    }

    public void Definir(string tokenEmClaro, DateTime expiraEm)
    {
        var contexto = acessor.HttpContext;
        if (contexto is null)
        {
            return;
        }

        contexto.Response.Cookies.Append(Nome, tokenEmClaro, MontarOpcoes(expiraEm));
    }

    public void Limpar()
    {
        var contexto = acessor.HttpContext;
        if (contexto is null)
        {
            return;
        }

        // Precisa repetir Path/Secure/SameSite: o navegador só apaga um cookie
        // quando os atributos batem com os da gravação. Por isso a exclusão
        // usa a MESMA rotina de opções da gravação — nunca uma cópia à parte,
        // que poderia divergir num ajuste futuro e deixar cookie órfão vivo.
        contexto.Response.Cookies.Delete(Nome, MontarOpcoes(DateTime.UtcNow));
    }

    /// <summary>
    /// Fonte única dos atributos do cookie — gravação, rotação e exclusão
    /// passam todas por aqui.
    /// </summary>
    /// <remarks>
    /// <b>Por que <c>Secure</c> NÃO olha <c>Request.IsHttps</c>:</b> a
    /// infraestrutura real termina TLS na Cloudflare, e o IIS pode receber a
    /// requisição em HTTP puro na perna interna. Ali <c>Request.IsHttps</c> é
    /// <c>false</c> mesmo com o navegador falando HTTPS — e o cookie de
    /// confiança sairia SEM <c>Secure</c>, viajando em claro se alguém
    /// alcançasse o mesmo host por HTTP. Confiar em
    /// <c>X-Forwarded-Proto</c> resolveria, mas exigiria
    /// <c>ForwardedHeaders</c> global com lista de proxies confiáveis — uma
    /// mudança de pipeline muito maior do que este cookie justifica, e que
    /// afetaria toda a API.
    ///
    /// A decisão é a mais conservadora possível: <c>Secure</c> é ligado
    /// SEMPRE, e o único ambiente que pode abrir mão dele é
    /// <c>Development</c> — nomeado explicitamente, nunca por inferência.
    /// Qualquer ambiente novo (Teste, Production, Staging, um nome ainda não
    /// inventado) nasce seguro por omissão. É o inverso do erro clássico:
    /// aqui esquecer de configurar deixa o cookie MAIS protegido, não menos.
    ///
    /// Development precisa da exceção porque o painel roda em
    /// <c>http://localhost:5173</c> contra a API em <c>http://localhost:5210</c>:
    /// com <c>Secure</c> fixo, o navegador descartaria o cookie em silêncio e
    /// o recurso simplesmente não funcionaria em desenvolvimento.
    /// </remarks>
    private CookieOptions MontarOpcoes(DateTime expiraEm) => new()
    {
        // JavaScript nunca lê nem escreve: é o que impede um XSS de exfiltrar
        // a credencial de dispositivo confiável.
        HttpOnly = true,

        // Nunca emitido sem Secure fora de Development — ver o comentário
        // acima. Não depende do esquema da requisição.
        Secure = !ambiente.IsDevelopment(),

        // O cookie só precisa viajar em requisição do próprio painel para a
        // própria API. `Strict` corta qualquer envio originado de outro site.
        SameSite = SameSiteMode.Strict,

        Path = Caminho,

        // Mesma expiração do registro no banco — o cookie nunca sobrevive ao
        // que ele representa.
        Expires = new DateTimeOffset(DateTime.SpecifyKind(expiraEm, DateTimeKind.Utc))
    };
}
