using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>
/// Adaptador de saida: entrega pelo gateway o que o atendente escreveu.
/// </summary>
/// <remarks>
/// Nunca lanca para quem chamou. A mensagem JA ESTA GRAVADA quando este servico
/// roda: derrubar a requisicao aqui faria o atendente ver erro sobre uma mensagem
/// que existe no fio, e clicar de novo duplicaria. Falha vira
/// <see cref="StatusEntrega.Falha"/>, que o fio ja sabe desenhar desde a Fase 1.
/// </remarks>
public class EnviadorPeloWhatsApp(
    CrmDbContext db,
    IEvolutionClient gateway,
    IOptions<OpcoesDoCanalWhatsApp> opcoes,
    ILogger<EnviadorPeloWhatsApp> log) : IEnviadorDeCanal
{
    private readonly OpcoesDoCanalWhatsApp _opcoes = opcoes.Value;

    public async Task<string?> EnviarAsync(Guid mensagemId, CancellationToken ct = default)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return null;
        }

        var alvo = await db.Mensagens.AsNoTracking()
            .Where(m => m.Id == mensagemId)
            .Select(m => new
            {
                m.Id,
                m.Conteudo,
                m.StatusEntrega,
                Canal = db.Conversas
                    .Where(c => c.Id == m.ConversaId)
                    .Select(c => c.Canal)
                    .FirstOrDefault(),
                Telefone = db.Conversas
                    .Where(c => c.Id == m.ConversaId)
                    .Select(c => db.Clientes
                        .Where(cl => cl.Id == c.ClienteId)
                        .Select(cl => cl.TelefoneWhatsapp)
                        .FirstOrDefault())
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        if (alvo is null)
        {
            return null;
        }

        // O GATE. Conversa de demonstracao continua exatamente como na Fase 1:
        // gravada no fio, sem sair para lugar nenhum. Sem isto, ligar o canal
        // faria as 11 conversas semeadas tentarem mandar mensagem para telefones
        // ficticios — e algumas delas sao numeros reais de terceiros.
        if (alvo.Canal != CanalOrigem.WhatsApp)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(alvo.Telefone))
        {
            log.LogWarning("Mensagem {MensagemId} sem telefone de destino.", mensagemId);
            await MarcarAsync(mensagemId, StatusEntrega.Falha, null, ct);
            return null;
        }

        try
        {
            var idExterno = await gateway.EnviarTextoAsync(alvo.Telefone, alvo.Conteudo, ct);

            await MarcarAsync(mensagemId, StatusEntrega.Enviada, idExterno, ct);

            return idExterno;
        }
        catch (Exception falha) when (falha is not OperationCanceledException)
        {
            log.LogError(
                falha, "Falha ao enviar a mensagem {MensagemId} pelo WhatsApp.", mensagemId);

            await MarcarAsync(mensagemId, StatusEntrega.Falha, null, ct);

            return null;
        }
    }

    public async Task EnviarPendentesAsync(Guid conversaId, CancellationToken ct = default)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return;
        }

        var ehWhatsApp = await db.Conversas.AsNoTracking()
            .AnyAsync(c => c.Id == conversaId && c.Canal == CanalOrigem.WhatsApp, ct);

        if (!ehWhatsApp)
        {
            return;
        }

        // So mensagem automatica ENDERECADA AO CLIENTE. A anotacao interna do fio
        // ("Conversa assumida por Fulano") tambem e Sistema, mas nasce Entregue e
        // com EhAutomatica falso — o cliente nunca deveria receber isso.
        var pendentes = await db.Mensagens.AsNoTracking()
            .Where(m => m.ConversaId == conversaId
                     && m.EhAutomatica
                     && m.StatusEntrega == StatusEntrega.Pendente)
            .OrderBy(m => m.EnviadaEm)
            .ThenBy(m => m.Id)
            .Select(m => m.Id)
            .ToListAsync(ct);

        foreach (var id in pendentes)
        {
            // Em serie, e nao em paralelo: o cliente le boas-vindas e menu na ordem
            // em que foram escritos, e disparar junto embaralharia os dois no
            // aparelho dele.
            await EnviarAsync(id, ct);
        }
    }

    /// <remarks>
    /// <c>ExecuteUpdate</c> e nao entidade rastreada: quem chamou ja fez o
    /// <c>SaveChanges</c> da mensagem, e recarregar a entidade aqui so criaria
    /// chance de conflito com quem estiver atendendo a mesma conversa.
    /// </remarks>
    private async Task MarcarAsync(
        Guid mensagemId,
        StatusEntrega status,
        string? idExterno,
        CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        await db.Mensagens
            .Where(m => m.Id == mensagemId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.StatusEntrega, status)
                .SetProperty(m => m.IdExternoWhatsapp, idExterno)
                .SetProperty(m => m.AtualizadoEm, agora), ct);
    }
}
