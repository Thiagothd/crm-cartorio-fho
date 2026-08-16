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
}
