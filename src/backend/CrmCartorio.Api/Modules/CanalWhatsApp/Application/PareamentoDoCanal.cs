using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Entities;
using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <inheritdoc cref="IPareamentoDoCanal"/>
public class PareamentoDoCanal(
    CrmDbContext db,
    IEvolutionClient gateway,
    IRegrasDoCanal regras,
    ControleDeTentativasDePareamento controleDeTentativas,
    IOptions<OpcoesDoCanalWhatsApp> opcoes,
    ILogger<PareamentoDoCanal> log) : IPareamentoDoCanal
{
    private static readonly SemaphoreSlim TravaDePareamento = new(1, 1);
    private readonly OpcoesDoCanalWhatsApp _opcoes = opcoes.Value;

    /// <remarks>
    /// Fixo de propósito: um campo livre aqui transformaria a tela num
    /// disparador de WhatsApp na mão de quem tem sessão de administrador.
    /// </remarks>
    private const string TextoDoTeste =
        "Mensagem de teste do CRM Cartório — se você recebeu isto, o canal está conectado e funcionando.";

    public async Task<SituacaoDoCanal> PrepararAsync(
        Guid tenantId,
        string urlBaseDaApi,
        CancellationToken ct = default)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return Desligado();
        }

        return await GerarNovoQrAsync(tenantId, urlBaseDaApi, ct);
    }

    public async Task<SituacaoDoCanal> ConsultarAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return Desligado();
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        // Uma base clonada pode conservar o nome da instancia de outro
        // ambiente. Nessa situacao nao se declara o canal operacional nem se
        // consulta o gateway; o POST explicito corrige o vinculo com seguranca.
        if (!_opcoes.InstanciaConfere(tenant?.WhatsappInstancia))
        {
            return new SituacaoDoCanal(true, false, "nao_vinculado", null, tenant?.WhatsappInstancia);
        }

        var estado = await gateway.ObterEstadoAsync(ct);

        // So busca o perfil na PRIMEIRA vez que ve o canal aberto sem numero
        // gravado — nao a cada poll de 5s da tela. Cobre tanto o pareamento feito
        // agora quanto uma reconexao que o webhook connection.update perdeu.
        if (estado.Conectado && tenant is not null && tenant.WhatsappNumeroExibicao is null)
        {
            await PersistirPerfilConectadoAsync(tenant, ct);
        }

        return Traduzir(estado, tenant);
    }

    public async Task<SituacaoDoCanal> TrocarNumeroAsync(
        Guid tenantId,
        Guid atendenteId,
        string urlBaseDaApi,
        CancellationToken ct = default)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return Desligado();
        }

        await FecharSessaoAtualAsync(tenantId, atendenteId, MotivoDaConexao.Troca, ct);
        await LimparNumeroDoTenantAsync(tenantId, ct);
        await gateway.DesconectarAsync(ct);

        // A instancia e o webhook nao mudam — so a sessao pareada dentro dela.
        // Nenhuma conversa existente e tocada por isto.
        return await GerarNovoQrAsync(tenantId, urlBaseDaApi, ct);
    }

    public async Task<SituacaoDoCanal> DesconectarAsync(
        Guid tenantId,
        Guid atendenteId,
        CancellationToken ct = default)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return Desligado();
        }

        await FecharSessaoAtualAsync(tenantId, atendenteId, MotivoDaConexao.Desconexao, ct);
        await LimparNumeroDoTenantAsync(tenantId, ct);
        await gateway.DesconectarAsync(ct);

        var estado = await gateway.ObterEstadoAsync(ct);
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        return Traduzir(estado, tenant);
    }

    public async Task<TesteDeEnvioResponse> TestarEnvioAsync(
        Guid tenantId, string telefone, CancellationToken ct = default)
    {
        if (!_opcoes.EstaConfigurado)
        {
            throw CanalWhatsAppServiceException.CanalDesligado();
        }

        var destino = NormalizadorDeTelefone.ParaE164(telefone);

        var veredicto = await regras.PodeFalarComAsync(tenantId, destino, ct);

        if (!veredicto.Permitido)
        {
            return new TesteDeEnvioResponse(
                false,
                veredicto.Motivo == MotivoDoDescarte.Bloqueado
                    ? "Este número está na lista de bloqueados."
                    : "Este número não está na lista de liberados.");
        }

        await gateway.EnviarTextoAsync(destino, TextoDoTeste, ct);

        return new TesteDeEnvioResponse(true, null);
    }

    public async Task<IReadOnlyList<ConexaoResponse>> HistoricoDeConexoesAsync(
        Guid tenantId, int limite = 20, CancellationToken ct = default) =>
        await db.CanalWhatsappConexoes.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.ConectadoEm)
            .Take(limite)
            .Select(c => new ConexaoResponse(
                c.Numero,
                c.PerfilNome,
                c.ConectadoEm,
                c.DesconectadoEm,
                db.Atendentes
                    .Where(a => a.Id == c.AtendenteId)
                    .Select(a => (string?)a.Nome)
                    .FirstOrDefault(),
                c.Motivo))
            .ToListAsync(ct);

    /// <remarks>
    /// Nucleo comum de <see cref="PrepararAsync"/> e <see cref="TrocarNumeroAsync"/>:
    /// os dois pedem um QR novo ao gateway. <see cref="TravaDePareamento"/>
    /// serializa chamadas concorrentes (dois cliques, duas abas abertas);
    /// <see cref="GarantirIntervaloDeTentativa"/> espaca tentativas no tempo — o
    /// bloqueio temporario da Meta contra pareamento repetido em sequencia.
    /// <c>GarantirInstanciaAsync</c> nunca devolve QR: so
    /// <c>IniciarConexaoAsync</c> pode, e so quando a sessao estiver mesmo
    /// fechada — chama-lo com a sessao "connecting" substituiria a tentativa em
    /// andamento (conflict: replaced) e impediria o pareamento de estabilizar.
    /// </remarks>
    private async Task<SituacaoDoCanal> GerarNovoQrAsync(Guid tenantId, string urlBaseDaApi, CancellationToken ct)
    {
        GarantirIntervaloDeTentativa();

        await TravaDePareamento.WaitAsync(ct);

        try
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
                ?? throw CanalWhatsAppServiceException.InstanciaSemTenant(_opcoes.NomeDaInstancia);

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

            return Traduzir(estado, tenant);
        }
        finally
        {
            TravaDePareamento.Release();
        }
    }

    private void GarantirIntervaloDeTentativa()
    {
        var restante = controleDeTentativas.SegundosRestantes(_opcoes.NomeDaInstancia);

        if (restante > 0)
        {
            throw CanalWhatsAppServiceException.PareamentoMuitoRecente(restante);
        }

        controleDeTentativas.RegistrarTentativa(_opcoes.NomeDaInstancia);
    }

    /// <summary>
    /// Busca numero/nome/foto no gateway e grava no tenant, abrindo a linha de
    /// conexao se nenhuma estiver em curso.
    /// </summary>
    /// <remarks>
    /// A guarda de "nenhuma em curso" torna isto idempotente com o que
    /// <c>ProcessadorDeWebhook.RegistrarConexaoAsync</c> ja pode ter aberto a
    /// partir do evento <c>connection.update</c> — qual dos dois chega primeiro
    /// nao importa, so um cria a linha.
    /// </remarks>
    private async Task PersistirPerfilConectadoAsync(Tenant tenant, CancellationToken ct)
    {
        var perfil = await gateway.ObterPerfilDaInstanciaAsync(ct);

        tenant.WhatsappNumeroExibicao = perfil?.Numero;
        tenant.WhatsappPerfilNome = perfil?.Nome;
        tenant.WhatsappPerfilFotoUrl = perfil?.FotoUrl;
        tenant.WhatsappConectadoEm = DateTime.UtcNow;

        var jaAberta = await db.CanalWhatsappConexoes.AnyAsync(
            c => c.TenantId == tenant.Id && c.DesconectadoEm == null, ct);

        if (!jaAberta)
        {
            db.CanalWhatsappConexoes.Add(new ConexaoDoCanal
            {
                TenantId = tenant.Id,
                Numero = perfil?.Numero,
                PerfilNome = perfil?.Nome,
                ConectadoEm = tenant.WhatsappConectadoEm.Value,
                Motivo = MotivoDaConexao.Pareamento
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Fecha a linha de conexao em curso, se houver, com o motivo e quem pediu.</summary>
    private async Task FecharSessaoAtualAsync(
        Guid tenantId, Guid atendenteId, MotivoDaConexao motivo, CancellationToken ct)
    {
        var aberta = await db.CanalWhatsappConexoes
            .Where(c => c.TenantId == tenantId && c.DesconectadoEm == null)
            .OrderByDescending(c => c.ConectadoEm)
            .FirstOrDefaultAsync(ct);

        if (aberta is null)
        {
            return;
        }

        aberta.DesconectadoEm = DateTime.UtcNow;
        aberta.Motivo = motivo;
        aberta.AtendenteId = atendenteId;

        await db.SaveChangesAsync(ct);
    }

    private async Task LimparNumeroDoTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant is null)
        {
            return;
        }

        tenant.WhatsappNumeroExibicao = null;
        tenant.WhatsappPerfilNome = null;
        tenant.WhatsappPerfilFotoUrl = null;
        tenant.WhatsappConectadoEm = null;

        await db.SaveChangesAsync(ct);
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

    private SituacaoDoCanal Traduzir(EstadoDaInstancia estado, Tenant? tenant) =>
        new(
            true,
            estado.Conectado,
            estado.Estado,
            estado.QrCodeBase64,
            _opcoes.NomeDaInstancia,
            tenant?.WhatsappNumeroExibicao,
            tenant?.WhatsappPerfilNome,
            tenant?.WhatsappPerfilFotoUrl,
            tenant?.WhatsappConectadoEm);
}
