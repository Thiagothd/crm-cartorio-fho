using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <inheritdoc cref="IVarredorDePrazos"/>
public class VarredorDePrazos(
    CrmDbContext db,
    IDisparadorDeMensagemAutomatica automaticas,
    INotificadorDeAtendimento notificador,
    ILogger<VarredorDePrazos> log) : IVarredorDePrazos
{
    /// <summary>
    /// Maximo de conversas avisadas por passada, por gatilho.
    /// </summary>
    /// <remarks>
    /// Teto para a primeira execucao numa base que ja tem historico: sem ele, ligar a
    /// automacao numa serventia com meses de atendimento tentaria avisar tudo de uma
    /// vez. O que sobrar entra na passada seguinte, e o corte e registrado no log —
    /// limite silencioso passaria a impressao de que tudo foi coberto.
    /// </remarks>
    private const int LoteMaximo = 200;

    private enum Marcador
    {
        Alerta,
        Vencimento
    }

    public async Task<int> ExecutarAsync(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        // Alerta exige que o prazo AINDA nao tenha estourado. Se a conversa passou de
        // "em dia" direto para "atrasada" entre duas passadas — intervalo longo, API
        // parada a noite —, o aviso de "esta chegando" perdeu o sentido e so o de
        // vencido deve sair.
        var total = await AvisarAsync(
            GatilhoDeMensagem.PrazoProximoVencimento,
            Marcador.Alerta,
            c => c.Status == StatusConversa.Ativa
              && c.AvisoDeAlertaEnviadoEm == null
              && c.PrazoAlertaEm != null
              && c.PrazoLimite != null
              && agora >= c.PrazoAlertaEm
              && agora <= c.PrazoLimite,
            agora,
            ct);

        total += await AvisarAsync(
            GatilhoDeMensagem.PrazoVencido,
            Marcador.Vencimento,
            c => c.Status == StatusConversa.Ativa
              && c.AvisoDeVencimentoEnviadoEm == null
              && c.PrazoLimite != null
              && agora > c.PrazoLimite,
            agora,
            ct);

        return total;
    }

    private async Task<int> AvisarAsync(
        GatilhoDeMensagem gatilho,
        Marcador marcador,
        System.Linq.Expressions.Expression<Func<Conversa, bool>> criterio,
        DateTime agora,
        CancellationToken ct)
    {
        // Restringe aos tenants que TEM regra ativa para o gatilho. Sem isso, uma
        // serventia que decidiu nao avisar prazo automaticamente seria varrida a cada
        // passada para nao gerar nada — ou, pior, teria o marcador gravado sem
        // mensagem, e os avisos nunca sairiam se ela mudasse de ideia depois.
        var tenantsComRegra = await db.MensagensAutomaticas.AsNoTracking()
            .Where(m => m.Ativo && m.Gatilho == gatilho)
            .Select(m => m.TenantId)
            .Distinct()
            .ToListAsync(ct);

        if (tenantsComRegra.Count == 0)
        {
            return 0;
        }

        var candidatas = await db.Conversas.AsNoTracking()
            .Where(criterio)
            .Where(c => tenantsComRegra.Contains(c.TenantId))
            .OrderBy(c => c.PrazoLimite)
            .Take(LoteMaximo)
            .ToListAsync(ct);

        if (candidatas.Count == LoteMaximo)
        {
            log.LogWarning(
                "Varredura de {Gatilho} atingiu o lote de {Lote} conversas. O restante " +
                "entra na proxima passada.",
                gatilho, LoteMaximo);
        }

        var avisadas = 0;

        foreach (var conversa in candidatas)
        {
            ct.ThrowIfCancellationRequested();

            if (await AvisarUmaAsync(conversa, gatilho, marcador, agora, ct))
            {
                avisadas++;
            }
        }

        return avisadas;
    }

    /// <summary>
    /// Marca a conversa como avisada e gera a mensagem, ou nao faz nenhuma das duas.
    /// </summary>
    /// <remarks>
    /// A mesma disciplina da trava de conversa (MODELO-DADOS.md, 4.1): o marcador e
    /// gravado por UPDATE condicional, e quem afetar zero linhas desiste. E o que
    /// impede duas instancias de IIS de avisarem o mesmo cliente duas vezes.
    ///
    /// A transacao existe porque a ordem correta e travar antes de escrever: se o
    /// marcador fosse gravado fora dela e a insercao da mensagem falhasse, a conversa
    /// ficaria marcada como avisada sem nunca ter sido avisada — e nenhuma passada
    /// futura tentaria de novo.
    /// </remarks>
    private async Task<bool> AvisarUmaAsync(
        Conversa conversa,
        GatilhoDeMensagem gatilho,
        Marcador marcador,
        DateTime agora,
        CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        var reivindicada = marcador == Marcador.Alerta
            ? await db.Conversas
                .Where(c => c.Id == conversa.Id && c.AvisoDeAlertaEnviadoEm == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.AvisoDeAlertaEnviadoEm, agora)
                    .SetProperty(c => c.AtualizadoEm, agora), ct)
            : await db.Conversas
                .Where(c => c.Id == conversa.Id && c.AvisoDeVencimentoEnviadoEm == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.AvisoDeVencimentoEnviadoEm, agora)
                    .SetProperty(c => c.AtualizadoEm, agora), ct);

        if (reivindicada == 0)
        {
            await transacao.RollbackAsync(ct);
            return false;
        }

        var geradas = await automaticas.EnfileirarAsync(conversa, gatilho, ct: ct);

        if (geradas.Count == 0)
        {
            // A regra foi desativada entre a consulta de tenants e agora. Desfaz o
            // marcador: quando houver regra de novo, a conversa volta a ser candidata.
            await transacao.RollbackAsync(ct);
            return false;
        }

        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        await NotificarAsync(conversa, geradas[0], ct);

        return true;
    }

    /// <summary>
    /// Empurra a mensagem para os paineis abertos.
    /// </summary>
    /// <remarks>
    /// Nao usa IConversaService de proposito: aquele servico le TenantId de
    /// IContextoDoUsuario, que aqui nao existe — a varredura roda fora de requisicao
    /// HTTP, sem token. As projecoes estaticas recebem o tenant explicitamente e
    /// servem aos dois mundos.
    ///
    /// Uma notificacao por conversa, mesmo quando o gatilho gera duas mensagens: o
    /// painel reage invalidando a conversa inteira, entao a segunda chegaria na mesma
    /// recarga e o aviso extra seria trabalho sem efeito.
    /// </remarks>
    private async Task NotificarAsync(Conversa conversa, Mensagem mensagem, CancellationToken ct)
    {
        try
        {
            var clienteNome = await db.Clientes.AsNoTracking()
                .Where(c => c.Id == conversa.ClienteId)
                .Select(c => c.Nome)
                .FirstOrDefaultAsync(ct) ?? string.Empty;

            var resposta = await ProjecaoDeMensagem
                .De(db, db.Mensagens.AsNoTracking().Where(m => m.Id == mensagem.Id), clienteNome)
                .FirstAsync(ct);

            var projecao = await ProjecaoDeConversa
                .De(db, db.Conversas.AsNoTracking().Where(c => c.Id == conversa.Id))
                .FirstAsync(ct);

            await notificador.MensagemRecebidaAsync(
                conversa.TenantId, resposta, projecao.ParaResposta(DateTime.UtcNow), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A mensagem ja esta gravada. Falhar o aviso em tempo real nao pode
            // desfazer isso nem derrubar a varredura — o painel mostra a mensagem na
            // proxima leitura de qualquer forma.
            log.LogWarning(
                ex,
                "Aviso de prazo gravado na conversa {ConversaId}, mas a notificacao em tempo real falhou",
                conversa.Id);
        }
    }
}
