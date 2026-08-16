using CrmCartorio.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <inheritdoc cref="IPareamentoDoCanal"/>
public class PareamentoDoCanal(
    CrmDbContext db,
    IEvolutionClient gateway,
    IOptions<OpcoesDoCanalWhatsApp> opcoes,
    ILogger<PareamentoDoCanal> log) : IPareamentoDoCanal
{
    private readonly OpcoesDoCanalWhatsApp _opcoes = opcoes.Value;

    public async Task<SituacaoDoCanal> PrepararAsync(
        Guid tenantId,
        string urlBaseDaApi,
        CancellationToken ct = default)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return Desligado();
        }

        var urlDoWebhook =
            $"{urlBaseDaApi.TrimEnd('/')}/api/whatsapp/webhook?token={Uri.EscapeDataString(_opcoes.TokenDoWebhook)}";

        var estado = await gateway.GarantirInstanciaAsync(urlDoWebhook, ct);

        await VincularInstanciaAsync(tenantId, ct);

        return Traduzir(estado);
    }

    public async Task<SituacaoDoCanal> ConsultarAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return Desligado();
        }

        var instancia = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.WhatsappInstancia)
            .FirstOrDefaultAsync(ct);

        var estado = await gateway.ObterEstadoAsync(ct);

        return Traduzir(estado) with { Instancia = instancia };
    }

    /// <summary>
    /// Grava qual instancia pertence a esta serventia.
    /// </summary>
    /// <remarks>
    /// E o que permite o webhook — que chega sem token — descobrir o tenant. Sem
    /// este vinculo a mensagem chega e nao tem para onde ir.
    /// </remarks>
    private async Task VincularInstanciaAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant is null || tenant.WhatsappInstancia == _opcoes.NomeDaInstancia)
        {
            return;
        }

        tenant.WhatsappInstancia = _opcoes.NomeDaInstancia;
        tenant.AtualizadoEm = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Instancia {Instancia} vinculada a serventia {TenantId}.",
            _opcoes.NomeDaInstancia, tenantId);
    }

    private SituacaoDoCanal Desligado() =>
        new(false, false, "desligado", null, null);

    private SituacaoDoCanal Traduzir(EstadoDaInstancia estado) =>
        new(true, estado.Conectado, estado.Estado, estado.QrCodeBase64, _opcoes.NomeDaInstancia);
}
