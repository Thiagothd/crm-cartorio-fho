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
    IArmazenamentoDeAnexos anexos,
    IRegrasDoCanal regras,
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
                m.ArquivoCaminho,
                m.ArquivoNome,
                m.ArquivoTipoMime,
                Canal = db.Conversas
                    .Where(c => c.Id == m.ConversaId)
                    .Select(c => c.Canal)
                    .FirstOrDefault(),
                TenantId = db.Conversas
                    .Where(c => c.Id == m.ConversaId)
                    .Select(c => c.TenantId)
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

        // A allowlist nao pode proteger so o webhook: o banco pode carregar
        // conversa WhatsApp antiga/importada, e sem a mesma guarda na saida um
        // clique do atendente ou uma automatica pendente alcancaria um contato
        // fora da regra mesmo sem ele conseguir iniciar conversa nova. Mesma
        // fonte do recebimento (IRegrasDoCanal), para os dois sentidos nunca
        // divergirem.
        //
        // Nao marca Falha: bloqueio por regra nao e falha tecnica do envio — e
        // decisao deliberada de quem configurou o canal. Misturar os dois no
        // mesmo StatusEntrega faria um bloqueio de politica parecer bug de
        // gateway na tela do atendente.
        if (await regras.PodeFalarComAsync(alvo.TenantId, alvo.Telefone, ct) is { Permitido: false } veredicto)
        {
            log.LogInformation(
                "Mensagem {MensagemId} nao enviada: numero {Telefone} fora da regra do canal ({Motivo}).",
                mensagemId, alvo.Telefone, veredicto.Motivo);

            return null;
        }

        try
        {
            var idExterno = alvo.ArquivoCaminho is { Length: > 0 } caminho
                ? await EnviarComArquivoAsync(
                    alvo.Telefone,
                    caminho,
                    alvo.ArquivoNome,
                    alvo.ArquivoTipoMime,
                    alvo.Conteudo,
                    ct)
                : await gateway.EnviarTextoAsync(alvo.Telefone, alvo.Conteudo, ct);

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

    /// <remarks>
    /// Apesar do nome, tambem retenta <see cref="StatusEntrega.Falha"/> — decisao
    /// do Thiago em 2026-08-17, depois de um envio real falhar (soluco da
    /// Evolution, nunca confirmado com certeza) e a conversa seguir adiante como
    /// se a mensagem tivesse saido: o cliente ficava esperando um menu que nunca
    /// chegou. Chamado nos mesmos 3 pontos de sempre (aviso de prazo, recebimento
    /// de mensagem, acao do atendente), entao um soluco passageiro se autocorrige
    /// sozinho na proxima chamada, sem exigir nada do cliente nem do atendente.
    /// Nao ha risco de martelar destino permanentemente invalido: cada retentativa
    /// so acontece quando este metodo e chamado de novo, nunca em loop apertado.
    /// </remarks>
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
                     && (m.StatusEntrega == StatusEntrega.Pendente || m.StatusEntrega == StatusEntrega.Falha))
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

    /// <summary>
    /// Le o arquivo do disco e manda pelo gateway.
    /// </summary>
    /// <remarks>
    /// A legenda so vai quando ha texto de gente. O conteudo de uma mensagem so
    /// de anexo e o rotulo do proprio arquivo — "[imagem]", ou o nome do PDF — e
    /// mandar isso como legenda escreveria "[imagem]" embaixo da imagem no
    /// aparelho do cliente.
    ///
    /// Arquivo sumido do disco levanta, e quem chamou converte em
    /// <see cref="StatusEntrega.Falha"/>: melhor o atendente ver que nao saiu do
    /// que o cliente receber uma mensagem vazia no lugar do documento.
    /// </remarks>
    private async Task<string> EnviarComArquivoAsync(
        string telefone,
        string caminho,
        string? nome,
        string? tipoMime,
        string conteudo,
        CancellationToken ct)
    {
        var bytes = await anexos.LerAsync(caminho, ct)
            ?? throw AtendimentoServiceException.AnexoNaoEncontrado();

        var nomeDoArquivo = string.IsNullOrWhiteSpace(nome) ? "anexo" : nome;

        var legenda = conteudo.Equals(nomeDoArquivo, StringComparison.Ordinal)
                      || RotulosDeAnexo.Contains(conteudo)
            ? null
            : conteudo;

        return await gateway.EnviarMidiaAsync(
            telefone,
            bytes,
            string.IsNullOrWhiteSpace(tipoMime) ? "application/octet-stream" : tipoMime,
            nomeDoArquivo,
            legenda,
            ct);
    }

    /// <summary>
    /// Textos que o tradutor usa quando o anexo veio sem legenda.
    /// </summary>
    /// <remarks>
    /// Espelham <c>TradutorDePayload.LerConteudo</c>. Mudar la sem mudar aqui faz
    /// o rotulo reaparecer como legenda no aparelho do cliente — visivel so em
    /// conversa real, entao vale a nota.
    /// </remarks>
    private static readonly HashSet<string> RotulosDeAnexo =
        new(StringComparer.Ordinal) { "[imagem]", "[documento]", "[áudio]", "[vídeo]" };

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
