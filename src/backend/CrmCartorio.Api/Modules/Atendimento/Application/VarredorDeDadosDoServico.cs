using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <inheritdoc cref="IVarredorDeDadosDoServico"/>
public class VarredorDeDadosDoServico(
    CrmDbContext db,
    IDisparadorDeMensagemAutomatica automaticas,
    INotificadorDeAtendimento notificador,
    IEnviadorDeCanal canal,
    ILogger<VarredorDeDadosDoServico> log) : IVarredorDeDadosDoServico
{
    /// <summary>Máximo de conversas tratadas por passada e por tenant.</summary>
    private const int LoteMaximo = 100;

    public async Task<int> ExecutarAsync(TimeSpan silencio, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var limite = agora - silencio;

        var tenants = await db.Tenants.AsNoTracking()
            .Where(t => t.Ativo)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var total = 0;

        foreach (var tenantId in tenants)
        {
            ct.ThrowIfCancellationRequested();

            var candidatas = await db.Conversas.AsNoTracking()
                .Where(c => c.TenantId == tenantId
                         && c.Status == StatusConversa.Ativa
                         && c.AguardandoDadosDoServico
                         && c.DadosDoServicoRecebidosEm != null
                         && !c.EmAtendimentoHumano && c.RoboPausadoEm == null
                         && c.UltimaMensagemEm <= limite

                         // Mensagem do cliente aceita e ainda não processada (fila do
                         // webhook, ou nova tentativa depois de falha) também é
                         // digitação: responder antes dela seria responder no meio.
                         && !db.Mensagens.Any(m => m.ConversaId == c.Id
                                                && m.AutorTipo == AutorTipo.Cliente
                                                && m.EstadoProcessamentoEntrada != null
                                                && m.EstadoProcessamentoEntrada != EstadoProcessamentoEntradaWhatsapp.Processada))
                .OrderBy(c => c.UltimaMensagemEm)
                .Select(c => c.Id)
                .Take(LoteMaximo)
                .ToListAsync(ct);

            foreach (var conversaId in candidatas)
            {
                ct.ThrowIfCancellationRequested();

                if (await ResponderUmaAsync(conversaId, limite, agora, ct))
                {
                    total++;
                }
            }
        }

        return total;
    }

    /// <remarks>
    /// A conversa é carregada RASTREADA e salva com o RowVersion: se o webhook gravou
    /// uma mensagem nova entre a leitura e o SaveChanges, o salvamento falha, esta
    /// passada desiste e a janela de silêncio recomeça — que é o lado certo de perder
    /// a disputa. O webhook, se perder ele, descarta a própria automação sem erro
    /// (ver <c>ProcessadorDeEntradaWhatsapp.SalvarSemRepetirEfeitosAsync</c>).
    /// </remarks>
    private async Task<bool> ResponderUmaAsync(Guid conversaId, DateTime limite, DateTime agora, CancellationToken ct)
    {
        var conversa = await db.Conversas
            .FirstOrDefaultAsync(c => c.Id == conversaId
                                   && c.Status == StatusConversa.Ativa
                                   && c.AguardandoDadosDoServico
                                   && c.DadosDoServicoRecebidosEm != null
                                   && !c.EmAtendimentoHumano && c.RoboPausadoEm == null
                                   && c.UltimaMensagemEm <= limite, ct);

        if (conversa is null)
        {
            return false;
        }

        var geradas = await AplicadorDeDadosDoServico.ResponderAosDadosAsync(db, automaticas, conversa, agora, log, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }

        // Depois do commit: segurar a conversa pelo tempo de uma chamada de rede
        // travaria quem estiver atendendo.
        await canal.EnviarPendentesAsync(conversa.Id, ct);

        foreach (var mensagem in geradas)
        {
            await NotificarMensagemAsync(conversa, mensagem, ct);
        }

        return true;
    }

    /// <remarks>
    /// Mesmo desenho de <see cref="VarredorDeInatividade"/>: não usa IConversaService,
    /// que lê o TenantId de IContextoDoUsuario, e aqui não há usuário.
    /// </remarks>
    private async Task NotificarMensagemAsync(Conversa conversa, Mensagem mensagem, CancellationToken ct)
    {
        try
        {
            var clienteNome = await db.Clientes.AsNoTracking()
                .Where(c => c.Id == conversa.ClienteId)
                .Select(c => c.Nome)
                .FirstOrDefaultAsync(ct) ?? string.Empty;

            var resposta = await ProjecaoDeMensagem
                .ObterUmaAsync(db, db.Mensagens.AsNoTracking().Where(m => m.Id == mensagem.Id), clienteNome, ct);

            var projecao = await ProjecaoDeConversa
                .De(db, db.Conversas.AsNoTracking().Where(c => c.Id == conversa.Id))
                .FirstAsync(ct);

            await notificador.MensagemRecebidaAsync(
                conversa.TenantId, resposta, projecao.ParaResposta(DateTime.UtcNow), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(
                ex,
                "Resposta aos dados do servico gravada na conversa {ConversaId}, mas a notificacao em tempo real falhou",
                conversa.Id);
        }
    }
}
