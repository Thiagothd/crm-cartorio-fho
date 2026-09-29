using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Le a resposta do cliente ao menu de 2o nivel — qual servico, dentro do setor
/// que a triagem ja reconheceu — e grava <see cref="Conversa.TipoServicoId"/>.
/// </summary>
/// <remarks>
/// Mesma forma de <see cref="AplicadorDeTriagem"/> e pelo mesmo motivo: ctx-free,
/// porque o webhook do WhatsApp real chama isso sem token nenhum, e os dois
/// chamadores (simulacao do painel e webhook) precisam da MESMA regra.
///
/// <b>Nunca deixa o cliente sem resposta.</b> Sem correspondencia, ou setor ainda
/// sem nenhum servico vinculado, cai na Resposta Inteligente antes de escalar
/// para humano — mesmo principio de contingencia do resto do sistema.
/// </remarks>
public static class AplicadorDeEscolhaDeServico
{
    public static async Task AplicarAsync(
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
        conversa.AguardandoEscolhaDeServico = false;

        if (conversa.SetorId is not { } setorId)
        {
            // Defensivo: so liga a flag depois de gravar SetorId (AplicadorDeTriagem),
            // mas sem setor nao ha menu de servico para interpretar.
            log.LogWarning(
                "Conversa {ConversaId} aguardava escolha de servico sem SetorId — encaminhando para a Resposta Inteligente.",
                conversa.Id);

            await AplicadorDeRespostaInteligente.AplicarAsync(
                db, ia, automaticas, opcoesDaIA, conversa, conteudo, agora, log, ct, mensagemAtualId);
            return;
        }

        // O menu no ar e o do submenu, se houver um aberto; senao, o do setor. Ele se
        // fecha aqui de qualquer jeito: a flag dura uma mensagem, e um submenu que
        // ninguem esta mais lendo nao pode decidir a escolha seguinte.
        var submenu = conversa.ServicoDoSubmenuId;
        conversa.ServicoDoSubmenuId = null;

        var servicos = submenu is { } paiId
            ? await automaticas.ServicosDoSubmenuAsync(conversa.TenantId, paiId, ct)
            : await automaticas.ServicosDoMenuAsync(conversa.TenantId, setorId, ct);
        var escolhido = TriagemAutomatica.InterpretarResposta(conteudo, servicos);

        if (escolhido is null)
        {
            log.LogInformation(
                "Escolha de servico da conversa {ConversaId} sem correspondencia — tentando a Resposta Inteligente",
                conversa.Id);

            await AplicadorDeRespostaInteligente.AplicarAsync(
                db, ia, automaticas, opcoesDaIA, conversa, conteudo, agora, log, ct, mensagemAtualId);
            return;
        }

        conversa.TipoServicoId = escolhido;

        var nomeDoServico = servicos.First(s => s.Id == escolhido).Nome;

        // Mesma anotacao interna de AplicadorDeTriagem, e pelo mesmo motivo: quem
        // abrir a conversa depois precisa saber por que o servico ja esta preenchido
        // sem nenhum atendente ter mexido.
        db.Mensagens.Add(new Mensagem
        {
            TenantId = conversa.TenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Sistema,
            Conteudo = $"Triagem automatica identificou o servico {nomeDoServico}.",
            Tipo = TipoMensagem.Texto,
            StatusEntrega = StatusEntrega.Entregue,
            EnviadaEm = agora,
            LidaEm = agora
        });

        var textos = await db.TiposServico.AsNoTracking()
            .Where(t => t.Id == escolhido && t.TenantId == conversa.TenantId)
            .Select(t => new { t.MensagemAoEscolher, t.MensagemAposDados })
            .FirstAsync(ct);

        // Servico com submenu (ex.: "Segunda via de certidao"): abre a lista de
        // dentro dele e espera outra escolha. O texto proprio do servico, se houver,
        // e a pergunta; senao, a mesma regra do menu do setor.
        if ((await automaticas.ServicosDoSubmenuAsync(conversa.TenantId, escolhido.Value, ct)).Count > 0)
        {
            conversa.ServicoDoSubmenuId = escolhido;

            var pergunta = string.IsNullOrWhiteSpace(textos.MensagemAoEscolher)
                ? await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.TriagemComCorrespondencia, ct: ct)
                : await automaticas.EnfileirarTextoAsync(conversa, textos.MensagemAoEscolher, $"submenu de {nomeDoServico}", ct);

            conversa.AguardandoEscolhaDeServico = pergunta.Count > 0;
            if (pergunta.Count == 0) conversa.ServicoDoSubmenuId = null;

            log.LogInformation(
                "Escolha de servico da conversa {ConversaId} abriu o submenu de {TipoServicoId}", conversa.Id, escolhido);
            return;
        }

        var aoEscolher = string.IsNullOrWhiteSpace(textos.MensagemAoEscolher)
            ? await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.ServicoEscolhido, ct: ct)
            : await automaticas.EnfileirarTextoAsync(conversa, textos.MensagemAoEscolher, $"servico {nomeDoServico}", ct);

        // Servico que pede dados e responde com instrucao (ex.: certidao → Pix): a
        // proxima mensagem do cliente sao os dados. Sem menu de continuacao no meio,
        // que confundiria quem esta digitando nome e CPF.
        if (!string.IsNullOrWhiteSpace(textos.MensagemAposDados) && aoEscolher.Count > 0)
        {
            conversa.AguardandoDadosDoServico = true;

            log.LogInformation(
                "Escolha de servico da conversa {ConversaId} identificou {TipoServicoId} e aguarda os dados", conversa.Id, escolhido);
            return;
        }

        // Fase 2E.1: o atendimento estruturado termina aqui, mas a conversa nao
        // pode terminar muda. Oferece continuar em vez de esperar o cliente
        // adivinhar o que fazer depois do checklist.
        await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.MenuDeContinuacao, ct: ct);
        conversa.AguardandoMenuDeContinuacao = true;

        log.LogInformation(
            "Escolha de servico da conversa {ConversaId} identificou {TipoServicoId}", conversa.Id, escolhido);
    }
}
