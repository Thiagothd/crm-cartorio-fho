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
    IEvolutionClient gateway,
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
        // Habilitado=false precisa desligar ENTRADA e saida. O webhook pode ter
        // ficado registrado no gateway de uma ativacao anterior; sem esta guarda,
        // desligar o canal parava as respostas, mas ainda criava Cliente, Conversa
        // e Mensagem a cada POST que continuasse chegando.
        if (!_opcoes.EstaConfigurado)
        {
            log.LogInformation(
                "Webhook da instancia {Instancia} ignorado: canal WhatsApp desligado ou incompleto.",
                payload.Instance ?? "(vazia)");

            return;
        }

        if (!_opcoes.InstanciaConfere(payload.Instance))
        {
            // Falha fechada e devolve 200 pelo controller: não processa o evento
            // cruzado e também não induz o gateway a repeti-lo indefinidamente.
            log.LogWarning(
                "Webhook da instancia {Instancia} ignorado; este ambiente aceita apenas {Esperada}.",
                payload.Instance ?? "(vazia)",
                _opcoes.NomeDaInstancia);

            return;
        }

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

        if (evento == "messages.upsert")
        {
            if (TradutorDePayload.LerMensagem(payload.Data) is not { } mensagem)
            {
                // Eco da nossa propria mensagem, conversa de grupo, ou tipo sem
                // traducao util. Descarte deliberado — ver o README do modulo.
                return;
            }

            if (!_opcoes.NumeroPermitido(mensagem.TelefoneE164))
            {
                // Piloto restrito a numero de teste (CanalWhatsApp:NumerosDeTeste).
                // Descartada ANTES de qualquer coisa — nao vira Cliente nem
                // Conversa, exatamente como o eco e a mensagem de grupo acima.
                log.LogInformation(
                    "Piloto restrito a numero de teste: mensagem de {Telefone} descartada.",
                    mensagem.TelefoneE164);

                return;
            }

            // Só consulta o banco depois que formato, instância e número
            // passaram pelas travas. Um contato fora da allowlist não produz
            // nem leitura de tenant, muito menos Cliente/Conversa/Mensagem.
            var tenantDaMensagemId = await ResolverTenantAsync(payload.Instance, ct);

            var fotoPerfilUrl = await ObterFotoPerfilSeNecessarioAsync(
                tenantDaMensagemId,
                mensagem.TelefoneE164,
                ct);

            await recebimento.ReceberAsync(
                tenantDaMensagemId,
                new MensagemDoCanal(
                    mensagem.TelefoneE164,
                    mensagem.NomeDoPerfil,
                    mensagem.Conteudo,
                    mensagem.Tipo,
                    mensagem.IdExterno,
                    await BaixarArquivoAsync(mensagem, ct),
                    fotoPerfilUrl),
                ct);

            return;
        }

        var tenantDoStatusId = await ResolverTenantAsync(payload.Instance, ct);

        if (TradutorDePayload.LerStatus(payload.Data) is { } status)
        {
            await recebimento.AtualizarEntregaAsync(
                tenantDoStatusId,
                status.IdExterno,
                status.Status,
                ct);
        }
    }

    /// <summary>
    /// Baixa a midia do gateway, ou <c>null</c> quando nao ha o que baixar.
    /// </summary>
    /// <remarks>
    /// Roda ANTES de gravar a mensagem, e nao depois, porque o fio nunca deve
    /// mostrar um anexo que ainda esta chegando: o atendente clicaria e receberia
    /// erro. O custo e o webhook demorar o tempo do download — aceitavel, porque
    /// reentrega do gateway ja cai na guarda por IdExterno do recebimento.
    ///
    /// <b>Falha nao interrompe.</b> Se o download nao vier, a mensagem entra
    /// mesmo assim com a legenda: o atendente ver "o cliente mandou uma foto, e
    /// ela nao chegou" vale muito mais do que a mensagem sumir por causa de uma
    /// midia. Quem chama recebe null e segue.
    /// </remarks>
    private async Task<ArquivoDoCanal?> BaixarArquivoAsync(
        MensagemRecebidaDoCanal mensagem,
        CancellationToken ct)
    {
        if (!mensagem.TemMidia)
        {
            return null;
        }

        try
        {
            var bytes = await gateway.BaixarMidiaAsync(mensagem.IdExterno, ct);

            if (bytes is null || bytes.Length == 0)
            {
                log.LogWarning(
                    "Midia da mensagem {IdExterno} nao veio do gateway; a mensagem entra sem anexo.",
                    mensagem.IdExterno);

                return null;
            }

            return new ArquivoDoCanal(bytes, mensagem.NomeDoArquivo, mensagem.TipoMimeDaMidia);
        }
        catch (Exception falha) when (falha is not OperationCanceledException)
        {
            log.LogError(
                falha,
                "Falha ao baixar a midia da mensagem {IdExterno}; a mensagem entra sem anexo.",
                mensagem.IdExterno);

            return null;
        }
    }

    private async Task<string?> ObterFotoPerfilSeNecessarioAsync(
        Guid tenantId,
        string telefoneE164,
        CancellationToken ct)
    {
        try
        {
            var cliente = await db.Clientes.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.TelefoneWhatsapp == telefoneE164)
                .Select(c => new { c.FotoPerfilUrl, c.FotoPerfilAtualizadaEm })
                .FirstOrDefaultAsync(ct);

            var agora = DateTime.UtcNow;
            if (cliente is null || cliente.FotoPerfilAtualizadaEm is null || cliente.FotoPerfilAtualizadaEm < agora.AddHours(-24))
            {
                return await gateway.ObterFotoPerfilUrlAsync(telefoneE164, ct);
            }

            return cliente.FotoPerfilUrl;
        }
        catch (Exception falha) when (falha is not OperationCanceledException)
        {
            log.LogDebug(falha, "Falha ao resolver foto de perfil para {Telefone}.", telefoneE164);
            return null;
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
