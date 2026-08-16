using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

public interface IProcessadorDeWebhook
{
    /// <summary>Confere o segredo compartilhado da URL do webhook.</summary>
    bool TokenConfere(string? token);

    /// <summary>Processa o evento. Evento desconhecido e ignorado, nao e erro.</summary>
    Task ProcessarAsync(PayloadDoWebhook payload, CancellationToken ct = default);
}

/// <summary>
/// Ponte entre o payload cru da Evolution e o Atendimento.
/// </summary>
/// <remarks>
/// E aqui que o formato do terceiro morre: daqui para dentro o sistema so fala o
/// vocabulario do proprio dominio.
/// </remarks>
public class ProcessadorDeWebhook(
    CrmDbContext db,
    IRecebimentoDeWhatsApp recebimento,
    IOptions<OpcoesDoCanalWhatsApp> opcoes,
    ILogger<ProcessadorDeWebhook> log) : IProcessadorDeWebhook
{
    private readonly OpcoesDoCanalWhatsApp _opcoes = opcoes.Value;

    /// <remarks>
    /// Comparacao de tempo fixo: comparar segredo com <c>==</c> vaza o tamanho do
    /// prefixo correto pelo tempo de resposta. O custo aqui e nenhum e o habito
    /// vale mais que a economia.
    /// </remarks>
    public bool TokenConfere(string? token)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(_opcoes.TokenDoWebhook))
        {
            return false;
        }

        var recebido = System.Text.Encoding.UTF8.GetBytes(token);
        var esperado = System.Text.Encoding.UTF8.GetBytes(_opcoes.TokenDoWebhook);

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(recebido, esperado);
    }

    public async Task ProcessarAsync(PayloadDoWebhook payload, CancellationToken ct = default)
    {
        var evento = payload.Event?.ToLowerInvariant();

        // Evento que nao tratamos nao e erro: a Evolution manda dezenas de tipos, e
        // devolver falha faria o gateway reentregar em laco algo que nunca vamos
        // querer.
        if (evento is not ("messages.upsert" or "messages.update" or "connection.update"))
        {
            return;
        }

        if (evento == "connection.update")
        {
            log.LogInformation(
                "Canal WhatsApp da instancia {Instancia}: {Estado}",
                payload.Instance, TradutorDePayload.LerEstadoDaConexao(payload.Data) ?? "?");

            return;
        }

        var tenantId = await ResolverTenantAsync(payload.Instance, ct);

        if (evento == "messages.upsert")
        {
            if (TradutorDePayload.LerMensagem(payload.Data) is not { } mensagem)
            {
                // Eco da nossa propria mensagem, conversa de grupo, ou tipo sem
                // traducao util. Descarte deliberado — ver o README do modulo.
                return;
            }

            await recebimento.ReceberAsync(
                tenantId,
                new MensagemDoCanal(
                    mensagem.TelefoneE164,
                    mensagem.NomeDoPerfil,
                    mensagem.Conteudo,
                    mensagem.Tipo,
                    mensagem.IdExterno),
                ct);

            return;
        }

        if (TradutorDePayload.LerStatus(payload.Data) is { } status)
        {
            await recebimento.AtualizarEntregaAsync(tenantId, status.IdExterno, status.Status, ct);
        }
    }

    /// <summary>
    /// Descobre a serventia pelo nome da instancia.
    /// </summary>
    /// <remarks>
    /// O webhook chega sem token, entao nao ha claim de onde ler o TenantId. Uma
    /// instancia pertence a uma serventia (indice unico em
    /// <c>Tenants.WhatsappInstancia</c>), e e esse vinculo que faz o roteamento.
    ///
    /// Instancia desconhecida levanta 404 em vez de assumir "o unico tenant": um
    /// gateway repareado com outro nome entregaria mensagem de um numero errado no
    /// atendimento do cartorio, e falhar alto e o comportamento seguro.
    /// </remarks>
    private async Task<Guid> ResolverTenantAsync(string? instancia, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instancia))
        {
            throw CanalWhatsAppServiceException.InstanciaSemTenant("(vazia)");
        }

        var tenantId = await db.Tenants.AsNoTracking()
            .Where(t => t.WhatsappInstancia == instancia)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        return tenantId ?? throw CanalWhatsAppServiceException.InstanciaSemTenant(instancia);
    }
}
