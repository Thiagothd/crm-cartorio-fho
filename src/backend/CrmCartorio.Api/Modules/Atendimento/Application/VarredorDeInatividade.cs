using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <inheritdoc cref="IVarredorDeInatividade"/>
public class VarredorDeInatividade(
    CrmDbContext db,
    IDisparadorDeMensagemAutomatica automaticas,
    INotificadorDeAtendimento notificador,
    IEnviadorDeCanal canal,
    ILogger<VarredorDeInatividade> log) : IVarredorDeInatividade
{
    /// <summary>Maximo de conversas tratadas por passada, por fase e por tenant.</summary>
    /// <remarks>
    /// Mesmo teto e mesmo motivo de <see cref="VarredorDePrazos"/>: ligar o recurso
    /// numa serventia com meses de historico encontraria tudo parado de uma vez. O
    /// que sobra entra na passada seguinte, e o corte vai para o log — limite
    /// silencioso passaria a impressao de que tudo foi coberto.
    /// </remarks>
    private const int LoteMaximo = 200;

    /// <summary>
    /// Fuso do cliente do cartorio, para a janela de horario.
    /// </summary>
    /// <remarks>
    /// O sistema inteiro grava <c>DateTime.UtcNow</c>, mas a janela que interessa e
    /// a do celular de quem recebe a mensagem. Sem a conversao, "das 8 as 18" seria
    /// das 5h as 15h em Brasilia.
    /// </remarks>
    private static readonly TimeZoneInfo FusoDoCartorio = ResolverFuso();

    private sealed record ConfiguracaoDoTenant(
        Guid TenantId,
        int MinutosParaPerguntar,
        int MinutosParaEncerrar,
        int? HoraInicial,
        int? HoraFinal);

    public async Task<int> ExecutarAsync(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        // O tempo e configuracao POR TENANT, entao o criterio nao pode ser uma
        // consulta global unica. Ler primeiro a lista curta de quem configurou e o
        // que permite manter o filtro por conversa como comparacao simples de datas
        // — servida por indice — em vez de aritmetica de data dentro do WHERE.
        //
        // Nulo em qualquer um dos dois minutos = recurso desligado. Mesma disciplina
        // de Tenant.WhatsappModoDeRecebimento: nulo nao e zero nem padrao implicito,
        // e "ninguem decidiu isto".
        var configurados = await db.Tenants.AsNoTracking()
            .Where(t => t.Ativo
                     && t.MinutosParaPerguntarEncerramento != null
                     && t.MinutosParaEncerrarSemResposta != null)
            .Select(t => new ConfiguracaoDoTenant(
                t.Id,
                t.MinutosParaPerguntarEncerramento!.Value,
                t.MinutosParaEncerrarSemResposta!.Value,
                t.EncerramentoHoraInicial,
                t.EncerramentoHoraFinal))
            .ToListAsync(ct);

        if (configurados.Count == 0)
        {
            return 0;
        }

        var total = 0;

        foreach (var configuracao in configurados)
        {
            ct.ThrowIfCancellationRequested();

            // Fase 2 antes da fase 1. Os dois conjuntos sao disjuntos pelo marcador,
            // entao a ordem nao muda o resultado — mas terminar o que ja estava
            // pendente antes de comecar coisa nova deixa o log em ordem cronologica.
            total += await EncerrarSemRespostaAsync(configuracao, agora, ct);
            total += await PerguntarAsync(configuracao, agora, ct);
        }

        return total;
    }

    // ------------------------------------------------------------------ fase 1

    private async Task<int> PerguntarAsync(
        ConfiguracaoDoTenant configuracao,
        DateTime agora,
        CancellationToken ct)
    {
        if (!DentroDaJanela(configuracao, agora))
        {
            return 0;
        }

        // Sem texto cadastrado nao ha pergunta — e sem pergunta a fase 2 nunca
        // acontece, porque ela so ve conversa que ja recebeu o marcador. E o
        // comportamento certo: encerrar em silencio, sem nunca ter avisado, seria
        // pior do que nao encerrar. Chave geral de mensagens automaticas
        // desligada conta como "sem texto": o disparador nao geraria a pergunta,
        // e reivindicar para desfazer a cada passada so gastaria transacao. A
        // fase 2 NAO e barrada — conversa ja perguntada antes de desligar ainda
        // fecha (em silencio, a despedida e cortesia), em vez de ficar no limbo.
        var temRegra = await db.MensagensAutomaticas.AsNoTracking()
            .AnyAsync(m => m.TenantId == configuracao.TenantId
                        && m.Ativo
                        && m.Gatilho == GatilhoDeMensagem.ConfirmacaoDeEncerramento
                        && db.Tenants.Any(t => t.Id == m.TenantId && t.MensagensAutomaticasHabilitadas), ct);

        if (!temRegra)
        {
            return 0;
        }

        var limite = agora.AddMinutes(-configuracao.MinutosParaPerguntar);

        // NAO filtra por PrazoLimite/SLA. A primeira versao (2026-08-22) pulava
        // conversa com prazo vivo, para nao fechar um protocolo "em producao". Um
        // teste ao vivo em 2026-08-23 mostrou o efeito colateral: todo servico com
        // SLA cadastrado (a maioria) trava a conversa aberta pelos dias inteiros do
        // prazo, e se o cliente sumir nesse meio tempo ela nunca fecha — exatamente
        // o bug que este varredor existe para consertar, so que adiado. Decisao do
        // Thiago: o protocolo em si nao desaparece (Status=Encerrada e soft-close,
        // o historico continua), entao nao ha razao para o CANAL DE WHATSAPP ficar
        // preso ao prazo do ato. Se o cliente voltar so para outro assunto, ele
        // precisa cair em atendimento novo, nao na conversa velha do ato anterior.
        var candidatas = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == configuracao.TenantId
                     && c.Status == StatusConversa.Ativa
                     && c.PerguntaDeEncerramentoEnviadaEm == null
                     && c.UltimaMensagemEm < limite

                     // Robo pausado: quem pausou cuida da conversa, e o robo
                     // aparecer para perguntar "posso encerrar?" desfaria a pausa.
                     && c.RoboPausadoEm == null

                     // Conversa de demonstracao nunca entra. Ela nao sai pelo
                     // gateway (o enviador filtra por canal), entao a pergunta
                     // ficaria so no fio — e tres passadas depois a base que o
                     // cartório parceiro usa para ver o produto teria se desmontado sozinha.
                     && c.Canal == CanalOrigem.WhatsApp

                     // O cartorio esta devendo resposta quando a ULTIMA mensagem do fio
                     // foi do cliente. MensagensNaoLidas mede "nenhum humano abriu no
                     // CRM", nao "o cartorio nao respondeu" — so zera quando um humano
                     // abre a conversa, o cliente encerra ou esta propria varredura
                     // fecha (NUNCA quando a IA responde). Consequencia: toda conversa
                     // atendida so pela IA, sem humano nunca abrir — o caso mais comum
                     // num canal com IA na frente — ficava com o contador positivo para
                     // sempre e nunca entrava aqui. Confirmado em producao em
                     // 2026-09-03: cliente reabria a conversa velha ja passada da
                     // triagem em vez de cair no menu de boas-vindas. Perguntar "quer
                     // encerrar?" para quem escreveu por ultimo e nunca foi atendido e
                     // o pior uso possivel do canal.
                     && db.Mensagens
                            .Where(m => m.ConversaId == c.Id)
                            .OrderByDescending(m => m.EnviadaEm)
                            .Select(m => m.AutorTipo)
                            .FirstOrDefault() != AutorTipo.Cliente)
            .OrderBy(c => c.UltimaMensagemEm)
            .Take(LoteMaximo)
            .ToListAsync(ct);

        RegistrarCorte(candidatas.Count, "pergunta de encerramento", configuracao.TenantId);

        var perguntadas = 0;

        foreach (var conversa in candidatas)
        {
            ct.ThrowIfCancellationRequested();

            if (await PerguntarUmaAsync(conversa, agora, ct))
            {
                perguntadas++;
            }
        }

        return perguntadas;
    }

    /// <remarks>
    /// Mesma disciplina de <see cref="VarredorDePrazos"/>: o marcador e gravado por
    /// UPDATE condicional dentro de uma transacao, e quem afetar zero linhas desiste.
    /// E o que impede duas instancias de IIS de perguntarem a mesma coisa duas vezes.
    /// </remarks>
    private async Task<bool> PerguntarUmaAsync(Conversa conversa, DateTime agora, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        var reivindicada = await db.Conversas
            .Where(c => c.Id == conversa.Id
                     && c.Status == StatusConversa.Ativa
                     && c.PerguntaDeEncerramentoEnviadaEm == null
                     && c.RoboPausadoEm == null

                     // Fecha a corrida com o webhook. O recebimento carrega a
                     // Conversa RASTREADA e salva com RowVersion; se a varredura
                     // escrevesse entre o load e o SaveChanges dele, o webhook
                     // levantaria DbUpdateConcurrencyException, devolveria 500 ao
                     // gateway e a reentrega encontraria a conversa ja mexida.
                     // Comparar o carimbo faz a varredura desistir em silencio, que
                     // e o lado certo de perder essa disputa.
                     && c.UltimaMensagemEm == conversa.UltimaMensagemEm)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.PerguntaDeEncerramentoEnviadaEm, agora)
                .SetProperty(c => c.AtualizadoEm, agora), ct);

        if (reivindicada == 0)
        {
            await transacao.RollbackAsync(ct);
            return false;
        }

        var geradas = await automaticas.EnfileirarAsync(
            conversa, GatilhoDeMensagem.ConfirmacaoDeEncerramento, ct: ct);

        if (geradas.Count == 0)
        {
            // A regra foi desativada entre a checagem e agora. Desfaz o marcador:
            // sem isso a conversa ficaria "ja perguntada" sem nunca ter recebido a
            // pergunta, e seria encerrada em silencio absoluto pela fase 2.
            await transacao.RollbackAsync(ct);
            return false;
        }

        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        // Depois do commit, nunca dentro da transacao: segurar o lock da conversa
        // pelo tempo de uma chamada de rede travaria quem estiver atendendo.
        await canal.EnviarPendentesAsync(conversa.Id, ct);

        await NotificarMensagemAsync(conversa, geradas[0], ct);

        return true;
    }

    // ------------------------------------------------------------------ fase 2

    private async Task<int> EncerrarSemRespostaAsync(
        ConfiguracaoDoTenant configuracao,
        DateTime agora,
        CancellationToken ct)
    {
        var limite = agora.AddMinutes(-configuracao.MinutosParaEncerrar);

        var candidatas = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == configuracao.TenantId
                     && c.Status == StatusConversa.Ativa
                     && c.PerguntaDeEncerramentoEnviadaEm != null
                     && c.PerguntaDeEncerramentoEnviadaEm < limite
                     && c.RoboPausadoEm == null

                     // BUG-ATD-01: a Fase 1 (pergunta) continua saindo mesmo
                     // com atendente na conversa — decisao separada,
                     // registrada em REQUISITOS.md SS5.8, nao mexida aqui. Mas
                     // a Fase 2 (encerrar sozinha por silencio) nao pode
                     // fechar uma conversa que um humano e responsavel por
                     // ela agora — nem quem assumiu, nem quem esta na fila
                     // humana depois de liberada (LiberarAsync nao desliga
                     // EmAtendimentoHumano de proposito).
                     //
                     // EXCECAO (2026-09-23, decisao do Thiago): conversa atendida
                     // SO pelo celular, sem dono no painel. Ninguem no painel a
                     // encerraria, e ela ficaria aberta para sempre. Passado o
                     // tempo, fecha; o proximo contato do cliente volta ao robo.
                     && (!c.EmAtendimentoHumano
                         || (c.AtendidaPeloAparelhoEm != null && c.AtendenteId == null)))
            .OrderBy(c => c.PerguntaDeEncerramentoEnviadaEm)
            .Take(LoteMaximo)
            .ToListAsync(ct);

        RegistrarCorte(candidatas.Count, "encerramento por inatividade", configuracao.TenantId);

        var encerradas = 0;

        foreach (var conversa in candidatas)
        {
            ct.ThrowIfCancellationRequested();

            if (await EncerrarUmaAsync(conversa, agora, ct))
            {
                encerradas++;
            }
        }

        return encerradas;
    }

    /// <remarks>
    /// <b>Espelha <see cref="AplicadorDePosAtendimento.EncerrarPeloCliente"/>, mas
    /// nao pode chama-lo:</b> aquele escreve na entidade rastreada, e
    /// <see cref="Conversa"/> carrega <see cref="Conversa.RowVersion"/> — a varredura
    /// roda fora de requisicao HTTP e nao pode disputar o token com quem esta
    /// atendendo. SE MEXER LA, MEXA AQUI.
    ///
    /// <b>Divergencia deliberada, desde 2026-09-04:</b> este caminho dispara
    /// <see cref="GatilhoDeMensagem.EncerramentoPorInatividade"/> e drena a fila
    /// inteira com <c>EnviarPendentesAsync</c> — correto aqui, porque quem
    /// dispara e um relogio e so chega a este ponto depois de a Fase 1 ja ter
    /// perguntado. <see cref="Atendimento.Application.ConversaService.EncerrarAsync"/>
    /// dispara <see cref="GatilhoDeMensagem.EncerramentoPeloAtendente"/> e envia
    /// SO a mensagem gerada — la quem aciona e uma pessoa, que pode encerrar
    /// qualquer conversa a qualquer momento, inclusive uma com backlog parado ha
    /// semanas; drenar a fila naquele caminho reabriria atendimento fantasma.
    /// </remarks>
    private async Task<bool> EncerrarUmaAsync(Conversa conversa, DateTime agora, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        var encerrada = await db.Conversas
            .Where(c => c.Id == conversa.Id
                     && c.Status == StatusConversa.Ativa
                     && c.PerguntaDeEncerramentoEnviadaEm == conversa.PerguntaDeEncerramentoEnviadaEm
                     && c.RoboPausadoEm == null

                     // Mesma corrida da fase 1, e aqui ela e mais grave: encerrar
                     // uma conversa no exato instante em que o cliente respondeu
                     // transformaria a resposta dele em primeiro contato de uma
                     // conversa nova.
                     && c.UltimaMensagemEm == conversa.UltimaMensagemEm

                     // Reconferida no proprio UPDATE: um atendente que assumiu
                     // entre a leitura das candidatas e aqui tira a conversa do
                     // encerramento automatico.
                     && (!c.EmAtendimentoHumano
                         || (c.AtendidaPeloAparelhoEm != null && c.AtendenteId == null)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, StatusConversa.Encerrada)
                .SetProperty(c => c.EncerradaEm, agora)
                .SetProperty(c => c.EncerradaPorAtendenteId, (Guid?)null)
                .SetProperty(c => c.MotivoEncerramento, "Encerrado automaticamente por inatividade do cliente.")
                .SetProperty(c => c.MensagensNaoLidas, 0)
                .SetProperty(c => c.AguardandoTriagem, false)
                .SetProperty(c => c.AguardandoEscolhaDeServico, false)
                .SetProperty(c => c.AguardandoMenuDeContinuacao, false)
                .SetProperty(c => c.ServicoDoSubmenuId, (Guid?)null)
                .SetProperty(c => c.AguardandoDadosDoServico, false)
                .SetProperty(c => c.DadosDoServicoRecebidosEm, (DateTime?)null)
                .SetProperty(c => c.AguardandoDuvidaDoServico, false)
                .SetProperty(c => c.AguardandoComprovante, false)
                .SetProperty(c => c.AguardandoRespostaInteligente, false)
                .SetProperty(c => c.TentativasRespostaInteligente, 0)
                .SetProperty(c => c.PerguntaDeEncerramentoEnviadaEm, (DateTime?)null)
                .SetProperty(c => c.AtualizadoEm, agora), ct);

        if (encerrada == 0)
        {
            await transacao.RollbackAsync(ct);
            return false;
        }

        // DIVERGENCIA DELIBERADA do VarredorDePrazos, que faz rollback quando o
        // gatilho nao gera mensagem: la a mensagem E o efeito: sem ela o aviso nao
        // existiu. Aqui o efeito e FECHAR, e a mensagem e cortesia. Desfazer o
        // encerramento por falta de texto deixaria a conversa presa exatamente no
        // limbo que este varredor existe para desfazer.
        await automaticas.EnfileirarAsync(
            conversa, GatilhoDeMensagem.EncerramentoPorInatividade, ct: ct);

        db.Mensagens.Add(new Mensagem
        {
            TenantId = conversa.TenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Sistema,
            Conteudo = "Atendimento encerrado automaticamente por inatividade.",
            Tipo = TipoMensagem.Texto,
            StatusEntrega = StatusEntrega.Entregue,
            EnviadaEm = agora,
            LidaEm = agora
        });

        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        await canal.EnviarPendentesAsync(conversa.Id, ct);

        await NotificarEncerramentoAsync(conversa, ct);

        log.LogInformation(
            "Conversa {ConversaId} encerrada por inatividade (pergunta enviada em {PerguntaEm}).",
            conversa.Id, conversa.PerguntaDeEncerramentoEnviadaEm);

        return true;
    }

    // ------------------------------------------------------------------ apoio

    /// <summary>
    /// A pergunta so sai dentro da janela configurada. Sem janela, qualquer hora.
    /// </summary>
    /// <remarks>
    /// Vale so para PERGUNTAR. Encerrar acontece a qualquer hora: e silencioso do
    /// ponto de vista do incomodo, e a mensagem que sai junto e a consequencia de
    /// uma pergunta que o cliente ja viu.
    ///
    /// Janela invertida (ex.: 22 as 6) e lida como atravessando a meia-noite, em vez
    /// de recusada — quem digitar isso quis dizer isso.
    /// </remarks>
    private static bool DentroDaJanela(ConfiguracaoDoTenant configuracao, DateTime agoraUtc)
    {
        if (configuracao.HoraInicial is not { } inicio || configuracao.HoraFinal is not { } fim)
        {
            return true;
        }

        var hora = TimeZoneInfo.ConvertTimeFromUtc(agoraUtc, FusoDoCartorio).Hour;

        return inicio <= fim
            ? hora >= inicio && hora < fim
            : hora >= inicio || hora < fim;
    }

    /// <remarks>
    /// O identificador de fuso muda entre Windows e Linux, e o build roda nos dois
    /// (servidor Windows, runner Ubuntu do GitHub Actions). Sem o segundo nome, o
    /// verificador quebraria no CI com TimeZoneNotFoundException; sem o UTC no fim,
    /// um container sem base de fusos derrubaria a varredura inteira por causa de
    /// uma janela de horario.
    /// </remarks>
    private static TimeZoneInfo ResolverFuso()
    {
        foreach (var identificador in new[] { "E. South America Standard Time", "America/Sao_Paulo" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(identificador);
            }
            catch (Exception excecao) when (excecao is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Tenta o proximo nome.
            }
        }

        return TimeZoneInfo.Utc;
    }

    private void RegistrarCorte(int encontradas, string fase, Guid tenantId)
    {
        if (encontradas == LoteMaximo)
        {
            log.LogWarning(
                "Varredura de {Fase} atingiu o lote de {Lote} conversas no tenant {TenantId}. " +
                "O restante entra na proxima passada.",
                fase, LoteMaximo, tenantId);
        }
    }

    /// <remarks>
    /// Nao usa IConversaService de proposito, pelo mesmo motivo de
    /// <see cref="VarredorDePrazos"/>: aquele servico le TenantId de
    /// IContextoDoUsuario, que aqui nao existe.
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
                "Pergunta de encerramento gravada na conversa {ConversaId}, mas a notificacao em tempo real falhou",
                conversa.Id);
        }
    }

    /// <remarks>
    /// Evento proprio, e nao <c>MensagemRecebidaAsync</c> como na fase 1: aqui o
    /// Status mudou, e o painel tem tratamento dedicado para tirar a conversa da
    /// inbox. Sem isto ela so sumiria no proximo refresh.
    /// </remarks>
    private async Task NotificarEncerramentoAsync(Conversa conversa, CancellationToken ct)
    {
        try
        {
            var projecao = await ProjecaoDeConversa
                .De(db, db.Conversas.AsNoTracking().Where(c => c.Id == conversa.Id))
                .FirstAsync(ct);

            await notificador.ConversaEncerradaAsync(
                conversa.TenantId, projecao.ParaResposta(DateTime.UtcNow), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(
                ex,
                "Conversa {ConversaId} encerrada por inatividade, mas a notificacao em tempo real falhou",
                conversa.Id);
        }
    }
}
