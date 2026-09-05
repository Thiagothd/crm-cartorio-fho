namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// SEC-03 — leitura/escrita do cookie HttpOnly de "dispositivo confiável".
/// </summary>
/// <remarks>
/// Mesmo padrão de <see cref="IContextoDoUsuario"/> — uma abstração fina
/// sobre <c>IHttpContextAccessor</c>, injetada na camada de Application, para
/// os controllers nunca precisarem tocar <c>HttpContext</c>/cookie
/// diretamente (regra de "controller fino, zero regra de negócio").
///
/// Diferença deliberada de <see cref="IContextoDoUsuario"/>: aquele só faz
/// sentido com um usuário JÁ autenticado (lê claims do JWT). Este precisa
/// funcionar ANTES de existir sessão — é lido logo na etapa 1 do login
/// (<c>[AllowAnonymous]</c>) — então nunca depende de
/// <c>HttpContext.User.Identity.IsAuthenticated</c>.
/// </remarks>
public interface ICookieDeDispositivoConfiavel
{
    /// <summary>O token em claro apresentado pelo navegador, se houver. Nunca lança.</summary>
    string? Ler();

    /// <summary>
    /// Grava o cookie. <paramref name="expiraEm"/> é sempre o
    /// <c>DispositivoConfiavel.ExpiraEm</c> já persistido — nunca escolhido
    /// aqui, para o cookie do navegador nunca durar mais que o registro no
    /// banco que ele representa.
    /// </summary>
    void Definir(string tokenEmClaro, DateTime expiraEm);

    /// <summary>Remove o cookie deste navegador. Não afeta o banco — quem revoga de verdade é o registro.</summary>
    void Limpar();
}
