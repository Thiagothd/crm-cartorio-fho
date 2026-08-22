using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
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
    private static readonly SemaphoreSlim TravaDePareamento = new(1, 1);
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

        await TravaDePareamento.WaitAsync(ct);

        try
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
                ?? throw CanalWhatsAppServiceException.InstanciaSemTenant(
                    _opcoes.NomeDaInstancia);

            var urlDoWebhook =
                $"{urlBaseDaApi.TrimEnd('/')}/api/whatsapp/webhook?token={Uri.EscapeDataString(_opcoes.TokenDoWebhook)}";

            // GarantirInstancia tambem confirma o webhook. O QR nunca e devolvido
            // ao navegador antes de o tenant estar vinculado a esta instancia.
            var estado = await gateway.GarantirInstanciaAsync(urlDoWebhook, ct);

            await VincularInstanciaAsync(tenant, ct);

            if (!estado.Conectado && estado.QrCodeBase64 is null)
            {
                estado = await gateway.IniciarConexaoAsync(ct);
            }

            return Traduzir(estado);
        }
        finally
        {
            TravaDePareamento.Release();
        }
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

        // Uma base clonada pode conservar o nome da instancia de outro
        // ambiente. Nessa situacao nao se declara o canal operacional nem se
        // consulta o gateway; o POST explicito corrige o vinculo com seguranca.
        if (!_opcoes.InstanciaConfere(instancia))
        {
            return new SituacaoDoCanal(
                true, false, "nao_vinculado", null, instancia);
        }

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
    private async Task VincularInstanciaAsync(Tenant tenant, CancellationToken ct)
    {
        if (tenant.WhatsappInstancia == _opcoes.NomeDaInstancia)
        {
            return;
        }

        tenant.WhatsappInstancia = _opcoes.NomeDaInstancia;
        tenant.AtualizadoEm = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Instancia {Instancia} vinculada a serventia {TenantId}.",
            _opcoes.NomeDaInstancia, tenant.Id);
    }

    private SituacaoDoCanal Desligado() =>
        new(false, false, "desligado", null, null);

    private SituacaoDoCanal Traduzir(EstadoDaInstancia estado) =>
        new(true, estado.Conectado, estado.Estado, estado.QrCodeBase64, _opcoes.NomeDaInstancia);
}
