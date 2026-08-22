using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

public class CanalWhatsAppServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static CanalWhatsAppServiceException CanalDesligado() =>
        new("canal_desligado",
            "O canal de WhatsApp está desligado ou incompleto na configuração.", 503);

    public static CanalWhatsAppServiceException GatewayIndisponivel() =>
        new("gateway_indisponivel",
            "O gateway de WhatsApp não respondeu. Verifique se o contêiner está no ar.", 503);

    public static CanalWhatsAppServiceException WebhookNaoAutorizado() =>
        new("webhook_nao_autorizado", "Token do webhook inválido.", 401);

    /// <remarks>
    /// A instância pareada não corresponde a nenhum tenant. Acontece quando o
    /// gateway é repareado com outro nome e ninguém atualizou o cadastro.
    /// </remarks>
    public static CanalWhatsAppServiceException InstanciaSemTenant(string instancia) =>
        new("instancia_sem_tenant",
            $"A instância '{instancia}' não está vinculada a nenhuma serventia.", 404);

    public static CanalWhatsAppServiceException ModoInvalido() =>
        new("modo_invalido", "Modo de recebimento desconhecido.", 400);

    public static CanalWhatsAppServiceException TelefoneInvalido() =>
        new("telefone_invalido", "Telefone inválido. Confira o DDD e o número.", 400);

    public static CanalWhatsAppServiceException NumeroDuplicado(string telefone) =>
        new("numero_duplicado", $"O número {telefone} já está cadastrado nesta lista.", 409);

    public static CanalWhatsAppServiceException NumeroNaoEncontrado() =>
        new("numero_nao_encontrado", "Número não encontrado.", 404);

    /// <remarks>
    /// Devolvida pelo envio (painel ou teste) quando o destino está fora da
    /// regra do canal — o mesmo veredito que descarta no recebimento, do lado
    /// de quem está tentando mandar.
    /// </remarks>
    public static CanalWhatsAppServiceException NumeroBloqueado() =>
        new("numero_bloqueado", "Este número não está autorizado a falar com o canal.", 403);

    /// <remarks>
    /// A Meta bloqueia temporariamente 2-3 pareamentos seguidos em pouco tempo
    /// (docs/DEPLOY-WHATSAPP.md, seção 7). Recusar aqui evita gastar uma
    /// tentativa que sabemos que vai falhar.
    /// </remarks>
    public static CanalWhatsAppServiceException PareamentoMuitoRecente(int segundosRestantes)
    {
        var excecao = new CanalWhatsAppServiceException(
            "pareamento_muito_recente",
            $"Aguarde {segundosRestantes}s antes de gerar outro código — pareamentos repetidos em sequência são bloqueados temporariamente pela Meta.",
            429);

        // A tela usa isto para desenhar a contagem regressiva sem precisar
        // reanalisar o texto da mensagem.
        excecao.Detalhes["segundosRestantes"] = segundosRestantes;

        return excecao;
    }
}
