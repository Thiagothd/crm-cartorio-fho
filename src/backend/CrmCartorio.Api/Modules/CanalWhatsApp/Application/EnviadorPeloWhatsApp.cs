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
    // Um claim sem chamada externa iniciada pode ser retomado depois deste prazo.
    // Cinco minutos e deliberadamente conservador: validacoes e leitura de anexo
    // acontecem antes do claim, portanto o intervalo protege apenas trabalho local curto.
    private static readonly TimeSpan ExpiracaoDoClaim = TimeSpan.FromMinutes(5);

    private readonly OpcoesDoCanalWhatsApp _opcoes = opcoes.Value;

    /// <remarks>
    /// Mesmo vinculo que o webhook usa para rotear a entrada
    /// (<c>Tenants.WhatsappInstancia</c> + a instancia deste ambiente): o que
    /// entra e o que sai precisam concordar sobre qual serventia o numero atende.
    /// </remarks>
    public async Task<bool> AtendeOTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return false;
        }

        var instancia = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.WhatsappInstancia)
            .FirstOrDefaultAsync(ct);

        return _opcoes.InstanciaConfere(instancia);
    }

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
                m.AutorTipo,
                m.EhAutomatica,
                m.GeradaPorIa,
                AtendenteNome = db.Atendentes
                    .Where(a => a.Id == m.AtendenteId)
                    .Select(a => a.Nome)
                    .FirstOrDefault(),
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
            await MarcarFalhaAntesDoDisparoAsync(mensagemId, ct);
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

        byte[]? conteudoDoArquivo = null;
        string? nomeDoArquivo = null;
        string? tipoMime = null;
        string? legenda = null;

        // Ler e validar o arquivo antes do claim: uma falha local comprovadamente
        // anterior ao POST continua retentavel e nunca deixa a linha presa.
        if (alvo.ArquivoCaminho is { Length: > 0 } caminho)
        {
            try
            {
                (conteudoDoArquivo, nomeDoArquivo, tipoMime, legenda) =
                    await PrepararArquivoAsync(
                        caminho,
                        alvo.ArquivoNome,
                        alvo.ArquivoTipoMime,
                        alvo.Conteudo,
                        ct);
            }
            catch (Exception falha) when (falha is not OperationCanceledException)
            {
                log.LogError(falha,
                    "Falha local antes do envio da mensagem {MensagemId} pelo WhatsApp.", mensagemId);
                await MarcarFalhaAntesDoDisparoAsync(mensagemId, ct);
                return null;
            }
        }

        if (await TentarAdquirirClaimAsync(mensagemId, ct) is not { } claimAdquiridoEm)
        {
            return null;
        }

        if (!await MarcarDisparoAsync(mensagemId, claimAdquiridoEm, ct))
        {
            log.LogWarning(
                "Mensagem {MensagemId} perdeu o estado Enviando antes do disparo e nao sera enviada.",
                mensagemId);
            return null;
        }

        // Decoracao de autoria SO aqui, no call site do gateway — nunca gravada.
        // Ver o remark de AssinaturaDeAutoria para o porque: se entrasse no
        // banco, vazaria para o painel, para a citacao, para o encaminhamento e
        // para o historico que alimenta a IA.
        var autoria = new AutoriaDaMensagem(alvo.AutorTipo, alvo.EhAutomatica, alvo.GeradaPorIa, alvo.AtendenteNome);

        try
        {
            var idExterno = conteudoDoArquivo is not null
                ? await gateway.EnviarMidiaAsync(
                    alvo.Telefone,
                    conteudoDoArquivo,
                    tipoMime!,
                    nomeDoArquivo!,
                    AssinaturaDeAutoria.DecorarLegenda(legenda, autoria),
                    ct)
                : await gateway.EnviarTextoAsync(
                    alvo.Telefone, AssinaturaDeAutoria.Decorar(alvo.Conteudo, autoria), ct);

            await MarcarEnviadaAsync(mensagemId, claimAdquiridoEm, idExterno, ct);

            return idExterno;
        }
        catch (Exception falha)
        {
            log.LogError(
                falha,
                "Resultado ambiguo ao enviar a mensagem {MensagemId}: o disparo foi iniciado, " +
                "mas nao e possivel confirmar se a Evolution aceitou. A mensagem permanecera Enviando.",
                mensagemId);

            if (falha is OperationCanceledException) throw;
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
                     && (m.StatusEntrega == StatusEntrega.Pendente
                         || m.StatusEntrega == StatusEntrega.Falha
                         || m.StatusEntrega == StatusEntrega.Enviando))
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

            // Uma anterior ainda Enviando e barreira: pode estar em voo ou ter
            // resultado ambiguo. Uma Falha tambem nao pode ser ultrapassada.
            var statusAtual = await db.Mensagens.AsNoTracking()
                .Where(m => m.Id == id)
                .Select(m => m.StatusEntrega)
                .FirstOrDefaultAsync(ct);

            if (statusAtual is StatusEntrega.Enviando or StatusEntrega.Falha)
            {
                break;
            }
        }
    }

    public async Task<ResultadoNoCanal> CorrigirAsync(
        Guid mensagemId,
        string novoConteudo,
        CancellationToken ct = default)
    {
        if (await AlvoDaCorrecaoAsync(mensagemId, ct) is not { } alvo)
        {
            return ResultadoNoCanal.NaoSeAplica;
        }

        // Mesma guarda da saida, e pelo mesmo motivo: editar entrega TEXTO NOVO ao
        // aparelho do cliente. Um numero que saiu da regra do canal nao volta a
        // receber conteudo por esta porta.
        if (await regras.PodeFalarComAsync(alvo.TenantId, alvo.Telefone, ct) is { Permitido: false } veredicto)
        {
            log.LogInformation(
                "Correcao da mensagem {MensagemId} nao enviada: numero fora da regra do canal ({Motivo}).",
                mensagemId, veredicto.Motivo);

            return ResultadoNoCanal.NaoSeAplica;
        }

        // A autoria e a de QUEM ESCREVEU, nao a de quem corrigiu: o cabecalho diz
        // ao cliente com quem ele esta falando, e trocar esse nome numa correcao
        // reescreveria a conversa dele.
        var autoria = new AutoriaDaMensagem(
            alvo.AutorTipo, alvo.EhAutomatica, alvo.GeradaPorIa, alvo.AtendenteNome);

        var aceitou = await gateway.EditarTextoAsync(
            alvo.Telefone,
            alvo.IdExternoWhatsapp,
            AssinaturaDeAutoria.Decorar(novoConteudo, autoria),
            ct);

        return aceitou ? ResultadoNoCanal.Feito : ResultadoNoCanal.Recusado;
    }

    public async Task<ResultadoNoCanal> ApagarParaTodosAsync(
        Guid mensagemId,
        CancellationToken ct = default)
    {
        if (await AlvoDaCorrecaoAsync(mensagemId, ct) is not { } alvo)
        {
            return ResultadoNoCanal.NaoSeAplica;
        }

        // Sem a guarda de IRegrasDoCanal, ao contrario da correcao — e deliberado.
        // Apagar nao ENTREGA nada ao cliente: retira o que ja esta no aparelho
        // dele. Um numero que saiu da allowlist depois do envio continua com a
        // mensagem errada na tela, e recusar o apagamento aqui protegeria a regra
        // do canal as custas da unica coisa que o atendente ainda pode fazer.
        var aceitou = await gateway.ApagarParaTodosAsync(alvo.Telefone, alvo.IdExternoWhatsapp, ct);

        return aceitou ? ResultadoNoCanal.Feito : ResultadoNoCanal.Recusado;
    }

    /// <summary>
    /// Dados de uma mensagem que ja saiu, para corrigir ou apagar no aparelho do
    /// cliente. <c>null</c> quando o canal nao se aplica.
    /// </summary>
    /// <remarks>
    /// Repete o GATE de <see cref="EnviarAsync"/> — canal configurado, conversa de
    /// WhatsApp, telefone presente. Conversa de demonstracao nunca teve id externo
    /// e cai fora ja pela ausencia dele; o gate explicito por canal fica assim
    /// mesmo, para o dia em que existir outro canal.
    /// </remarks>
    private async Task<AlvoDaCorrecao?> AlvoDaCorrecaoAsync(Guid mensagemId, CancellationToken ct)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return null;
        }

        var alvo = await db.Mensagens.AsNoTracking()
            .Where(m => m.Id == mensagemId)
            .Select(m => new
            {
                m.IdExternoWhatsapp,
                m.AutorTipo,
                m.EhAutomatica,
                m.GeradaPorIa,
                AtendenteNome = db.Atendentes
                    .Where(a => a.Id == m.AtendenteId)
                    .Select(a => a.Nome)
                    .FirstOrDefault(),
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

        if (alvo is null
            || alvo.Canal != CanalOrigem.WhatsApp
            || string.IsNullOrWhiteSpace(alvo.IdExternoWhatsapp)
            || string.IsNullOrWhiteSpace(alvo.Telefone))
        {
            return null;
        }

        return new AlvoDaCorrecao(
            alvo.IdExternoWhatsapp,
            alvo.Telefone,
            alvo.TenantId,
            alvo.AutorTipo,
            alvo.EhAutomatica,
            alvo.GeradaPorIa,
            alvo.AtendenteNome);
    }

    private sealed record AlvoDaCorrecao(
        string IdExternoWhatsapp,
        string Telefone,
        Guid TenantId,
        AutorTipo AutorTipo,
        bool EhAutomatica,
        bool GeradaPorIa,
        string? AtendenteNome);

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
    private async Task<(byte[] Conteudo, string Nome, string TipoMime, string? Legenda)> PrepararArquivoAsync(
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

        return (
            bytes,
            nomeDoArquivo,
            string.IsNullOrWhiteSpace(tipoMime) ? "application/octet-stream" : tipoMime,
            legenda);
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
    private async Task<DateTime?> TentarAdquirirClaimAsync(Guid mensagemId, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        var expiradoAntesDe = agora - ExpiracaoDoClaim;

        // Recuperacao lazy exclusivamente do caso seguro: o processo adquiriu o
        // claim, mas ainda nao marcou o inicio da chamada externa.
        await db.Mensagens
            .Where(m => m.Id == mensagemId
                     && m.StatusEntrega == StatusEntrega.Enviando
                     && m.EnvioDisparadoEm == null
                     && m.EnvioIniciadoEm < expiradoAntesDe)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.StatusEntrega, StatusEntrega.Falha)
                .SetProperty(m => m.EnvioIniciadoEm, (DateTime?)null)
                .SetProperty(m => m.AtualizadoEm, agora), ct);

        var afetadas = await db.Mensagens
            .Where(m => m.Id == mensagemId
                     && (m.StatusEntrega == StatusEntrega.Pendente
                         || m.StatusEntrega == StatusEntrega.Falha))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.StatusEntrega, StatusEntrega.Enviando)
                .SetProperty(m => m.EnvioIniciadoEm, agora)
                .SetProperty(m => m.EnvioDisparadoEm, (DateTime?)null)
                .SetProperty(m => m.AtualizadoEm, agora), ct);

        return afetadas == 1 ? agora : null;
    }

    private async Task<bool> MarcarDisparoAsync(
        Guid mensagemId,
        DateTime claimAdquiridoEm,
        CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        return await db.Mensagens
            .Where(m => m.Id == mensagemId
                     && m.StatusEntrega == StatusEntrega.Enviando
                     && m.EnvioIniciadoEm == claimAdquiridoEm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.EnvioDisparadoEm, agora)
                .SetProperty(m => m.AtualizadoEm, agora), ct) == 1;
    }

    private async Task MarcarEnviadaAsync(
        Guid mensagemId,
        DateTime claimAdquiridoEm,
        string idExterno,
        CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        await db.Mensagens
            .Where(m => m.Id == mensagemId
                     && m.StatusEntrega == StatusEntrega.Enviando
                     && m.EnvioIniciadoEm == claimAdquiridoEm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.StatusEntrega, StatusEntrega.Enviada)
                .SetProperty(m => m.IdExternoWhatsapp, idExterno)
                .SetProperty(m => m.EnvioIniciadoEm, (DateTime?)null)
                .SetProperty(m => m.EnvioDisparadoEm, (DateTime?)null)
                .SetProperty(m => m.AtualizadoEm, agora), ct);
    }

    private async Task MarcarFalhaAntesDoDisparoAsync(Guid mensagemId, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        await db.Mensagens
            .Where(m => m.Id == mensagemId
                     && (m.StatusEntrega == StatusEntrega.Pendente
                         || m.StatusEntrega == StatusEntrega.Falha))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.StatusEntrega, StatusEntrega.Falha)
                .SetProperty(m => m.AtualizadoEm, agora), ct);
    }
}
