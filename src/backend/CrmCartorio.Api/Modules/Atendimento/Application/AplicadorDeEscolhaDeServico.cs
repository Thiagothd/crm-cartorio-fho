using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;

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
        CancellationToken ct)
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
                db, ia, automaticas, opcoesDaIA, conversa, conteudo, agora, log, ct);
            return;
        }

        var servicos = await automaticas.ServicosDoMenuAsync(conversa.TenantId, setorId, ct);
        var escolhido = TriagemAutomatica.InterpretarResposta(conteudo, servicos);

        if (escolhido is null)
        {
            log.LogInformation(
                "Escolha de servico da conversa {ConversaId} sem correspondencia — tentando a Resposta Inteligente",
                conversa.Id);

            await AplicadorDeRespostaInteligente.AplicarAsync(
                db, ia, automaticas, opcoesDaIA, conversa, conteudo, agora, log, ct);
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

        await automaticas.EnfileirarAsync(conversa, GatilhoDeMensagem.ServicoEscolhido, ct: ct);

        log.LogInformation(
            "Escolha de servico da conversa {ConversaId} identificou {TipoServicoId}", conversa.Id, escolhido);
    }
}
