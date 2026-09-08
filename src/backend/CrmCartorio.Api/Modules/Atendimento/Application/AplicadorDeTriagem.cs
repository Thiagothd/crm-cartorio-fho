using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Le a resposta do cliente ao menu de triagem e roteia o atendimento.
/// </summary>
/// <remarks>
/// Vive fora do <see cref="MensagemService"/> porque agora tem DOIS chamadores: a
/// simulacao pelo painel (Fase 1) e o webhook do WhatsApp real (Fase 2). Duplicar
/// a regra faria as duas divergirem na primeira mudanca, e a divergencia so
/// apareceria na conversa de um cliente de verdade.
///
/// Nao conhece <c>IContextoDoUsuario</c>: o webhook roda sem requisicao HTTP e sem
/// token, entao o tenant vem sempre da propria conversa. Mesma disciplina do
/// <see cref="IDisparadorDeMensagemAutomatica"/> e do <see cref="IVarredorDePrazos"/>.
///
/// <b>Enfileira, nao grava.</b> Quem chamou decide o <c>SaveChanges</c> — e o que
/// mantem a resposta da triagem na mesma transacao que criou a conversa.
/// </remarks>
public static class AplicadorDeTriagem
{
    /// <summary>
    /// Aplica a triagem sobre a conversa. A flag sai em TODOS os caminhos.
    /// </summary>
    /// <remarks>
    /// Uma unica tentativa e o suficiente, e isso e o requisito, nao um detalhe:
    /// repetir o menu ate o cliente acertar prende quem escreveu "bom dia" num
    /// loop de robo — o pior comportamento possivel num canal de cartorio, e o
    /// motivo pelo qual gente desiste de atendimento por WhatsApp.
    /// </remarks>
    /// <returns>
    /// <c>true</c> quando a triagem reconheceu um setor; <c>false</c> quando caiu
    /// para atendimento humano ou nao havia o que fazer.
    /// </returns>
    public static async Task<bool> AplicarAsync(
        CrmDbContext db,
        IDisparadorDeMensagemAutomatica automaticas,
        IRespostaInteligenteService ia,
        OpcoesDaIA opcoesDaIA,
        Conversa conversa,
        string conteudo,
        DateTime agora,
        ILogger log,
        CancellationToken ct,
        Guid? mensagemAtualId = null)
    {
        conversa.AguardandoTriagem = false;

        // Alguem da serventia roteou a conversa enquanto o menu estava no ar. A
        // escolha da pessoa vale mais que a do robo, e nao ha o que avisar ao
        // cliente: um atendente ja esta com o caso.
        if (conversa.SetorId is not null)
        {
            return false;
        }

        var setores = await automaticas.SetoresDoMenuAsync(conversa.TenantId, ct);
        var escolhido = TriagemAutomatica.InterpretarResposta(conteudo, setores);

        if (escolhido is null)
        {
            // Fase 2B: antes de escalar direto para humano, tenta a Resposta
            // Inteligente. AplicadorDeRespostaInteligente decide sozinho — IA
            // desligada, sem resposta, ou pedido de humano caem todos no MESMO
            // TriagemSemCorrespondencia de sempre; a diferenca e so a chance de
            // resolver sem esperar um atendente.
            log.LogInformation(
                "Triagem da conversa {ConversaId} sem correspondencia no menu — tentando a Resposta Inteligente",
                conversa.Id);

            await AplicadorDeRespostaInteligente.AplicarAsync(
                db, ia, automaticas, opcoesDaIA, conversa, conteudo, agora, log, ct, mensagemAtualId);

            return false;
        }

        conversa.SetorId = escolhido;

        var nomeDoSetor = setores.First(s => s.Id == escolhido).Nome;

        // AtendenteId nulo no historico e o que o modelo definiu para "mudanca
        // automatica" (MODELO-DADOS.md, 3.10). A trilha registra que foi o sistema,
        // nao um atendente cujo nome nao caberia aqui.
        db.HistoricoEtapas.Add(new HistoricoEtapa
        {
            TenantId = conversa.TenantId,
            ConversaId = conversa.Id,
            TipoMudanca = TipoMudancaHistorico.Setor,
            SetorNovoId = escolhido,
            AtendenteId = null,
            OcorridoEm = agora,
            Observacao = "Setor definido pela triagem automatica do primeiro contato."
        });

        // Anotacao interna, nao mensagem ao cliente: EhAutomatica fica falso. Quem
        // abrir a conversa depois precisa saber por que ela esta neste setor sem
        // ninguem ter mexido.
        db.Mensagens.Add(new Mensagem
        {
            TenantId = conversa.TenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Sistema,
            Conteudo = $"Triagem automatica encaminhou o atendimento ao setor {nomeDoSetor}.",
            Tipo = TipoMensagem.Texto,
            StatusEntrega = StatusEntrega.Entregue,
            EnviadaEm = agora,
            LidaEm = agora
        });

        // So convida a escolher servico se o setor tiver algum vinculado
        // (TipoServico.SetorId) — sem isso o menu de 2o nivel sairia vazio.
        // Servico ainda pode ser capturado pelo painel ou pela IA.
        var servicosDoSetor = await automaticas.ServicosDoMenuAsync(conversa.TenantId, escolhido.Value, ct);

        if (servicosDoSetor.Count > 0)
        {
            await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.TriagemComCorrespondencia, ct: ct);
            conversa.AguardandoEscolhaDeServico = true;
        }

        log.LogInformation(
            "Triagem da conversa {ConversaId} encaminhou ao setor {SetorId}", conversa.Id, escolhido);

        return true;
    }
}
