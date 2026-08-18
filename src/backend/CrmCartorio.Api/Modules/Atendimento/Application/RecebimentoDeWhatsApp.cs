using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <inheritdoc cref="IRecebimentoDeWhatsApp"/>
public class RecebimentoDeWhatsApp(
    CrmDbContext db,
    IGeradorDeProtocolo protocolos,
    IDisparadorDeMensagemAutomatica automaticas,
    INotificadorDeAtendimento notificador,
    IEnviadorDeCanal canal,
    IArmazenamentoDeAnexos anexos,
    IRespostaInteligenteService respostaInteligente,
    IOptions<OpcoesDaIA> opcoesDaIA,
    ILogger<RecebimentoDeWhatsApp> log) : IRecebimentoDeWhatsApp
{
    private readonly OpcoesDaIA _opcoesDaIA = opcoesDaIA.Value;

    public async Task<Guid> ReceberAsync(
        Guid tenantId,
        MensagemDoCanal mensagem,
        CancellationToken ct = default)
    {
        // O gateway reentrega o mesmo evento quando nao recebe 200 a tempo. Sem
        // esta checagem, uma reentrega viraria mensagem duplicada no fio — e, no
        // primeiro contato, um segundo protocolo para o mesmo "bom dia".
        var jaRegistrada = await db.Mensagens.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.IdExternoWhatsapp == mensagem.IdExterno)
            .Select(m => (Guid?)m.ConversaId)
            .FirstOrDefaultAsync(ct);

        if (jaRegistrada is { } existente)
        {
            log.LogInformation(
                "Mensagem {IdExterno} ja registrada; reentrega do gateway ignorada.",
                mensagem.IdExterno);

            return existente;
        }

        var agora = DateTime.UtcNow;

        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        var cliente = await ResolverClienteAsync(tenantId, mensagem, agora, ct);
        var (conversa, ehNova) = await ResolverConversaAsync(tenantId, cliente, agora, ct);

        // Grava cliente e conversa ANTES de enfileirar as automaticas. O
        // disparador resolve {NomeCliente} por CONSULTA ao banco, e um cliente que
        // so existe no change tracker nao aparece nela — o resultado era um
        // "Ola, !" na primeira mensagem que o cliente recebe do cartorio.
        //
        // Continua tudo na mesma transacao: se algo falhar adiante, nada fica.
        await db.SaveChangesAsync(ct);

        var recebida = new Mensagem
        {
            TenantId = tenantId,
            ConversaId = conversa.Id,
            AutorTipo = AutorTipo.Cliente,
            AtendenteId = null,
            Conteudo = mensagem.Conteudo,
            Tipo = mensagem.Tipo,

            // Chegou de verdade: Entregue, nao Enviada. Quem entrega para nos e o
            // WhatsApp, e ele so entrega o que ja chegou.
            StatusEntrega = StatusEntrega.Entregue,
            IdExternoWhatsapp = mensagem.IdExterno,
            EnviadaEm = agora
        };

        await GravarAnexoAsync(recebida, mensagem.Arquivo, agora, ct);

        db.Mensagens.Add(recebida);

        conversa.UltimaMensagemEm = agora;
        conversa.MensagensNaoLidas += 1;

        if (ehNova)
        {
            // Conversa recem-aberta: sai o menu, e so. Aplicar triagem aqui
            // responderia a pergunta com a MESMA mensagem que a provocou — o
            // "bom dia" nunca casa com opcao nenhuma, e o cliente receberia o
            // menu e o "vou encaminhar para um atendente" na mesma tacada, sem
            // chance de escolher.
            await EnfileirarBoasVindasAsync(conversa, ct);
        }
        else if (TriagemAutomatica.PediuMenu(mensagem.Conteudo))
        {
            // Checado ANTES de qualquer flag — achado ao vivo, 2026-08-18: so
            // dentro de AplicadorDePosAtendimento deixava a saida de emergencia
            // refem de AguardandoRespostaInteligente estar desligada, o que
            // deixou de ser garantido quando a IA passou a manter essa flag
            // ligada mesmo sem saber responder (mesmo dia). Ver
            // AplicadorDePosAtendimento.ReabrirDoZeroAsync.
            await AplicadorDePosAtendimento.ReabrirDoZeroAsync(
                db, automaticas, conversa, agora.AddMilliseconds(1), ct);
        }
        else if (conversa.AguardandoTriagem)
        {
            // Um milissegundo a frente, e nao o mesmo `agora`: o fio ordena por
            // EnviadaEm e desempata por Id, mas o SQL Server compara
            // uniqueidentifier numa ordem de bytes propria que NAO e a ordem
            // cronologica do Guid v7. Com timestamp igual, a resposta da triagem
            // podia aparecer acima da mensagem que a provocou — observado no fio.
            //
            // Separar o instante torna a ordem determinista sem depender de como
            // o banco compara GUID.
            await AplicadorDeTriagem.AplicarAsync(
                db, automaticas, respostaInteligente, _opcoesDaIA,
                conversa, mensagem.Conteudo, agora.AddMilliseconds(1), log, ct);
        }
        else if (conversa.AguardandoEscolhaDeServico)
        {
            // Mesmo ajuste de um milissegundo dos outros ramos, e pelo mesmo motivo.
            await AplicadorDeEscolhaDeServico.AplicarAsync(
                db, automaticas, respostaInteligente, _opcoesDaIA,
                conversa, mensagem.Conteudo, agora.AddMilliseconds(1), log, ct);
        }
        else if (conversa.AguardandoRespostaInteligente)
        {
            // Mesmo ajuste de um milissegundo do ramo da triagem acima, e pelo
            // mesmo motivo: a resposta da IA tem de aparecer DEPOIS da mensagem que
            // a provocou, e Guid v7 sozinho nao garante isso com timestamp igual.
            await AplicadorDeRespostaInteligente.AplicarAsync(
                db, respostaInteligente, automaticas, _opcoesDaIA,
                conversa, mensagem.Conteudo, agora.AddMilliseconds(1), log, ct);
        }
        else
        {
            // Nenhuma flag de fluxo ligada — o atendimento estruturado ja
            // terminou. Sem este ramo a mensagem so era gravada e nada respondia
            // (achado ao vivo, 2026-08-17): agora e sempre tratada, nem que seja
            // pela IA. Mesmo ajuste de um milissegundo dos outros ramos.
            await AplicadorDePosAtendimento.AplicarAsync(
                db, automaticas, respostaInteligente, _opcoesDaIA,
                conversa, mensagem.Conteudo, agora.AddMilliseconds(1), log, ct);
        }

        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        log.LogInformation(
            "WhatsApp: mensagem de {Telefone} registrada na conversa {ConversaId} ({Origem}).",
            mensagem.TelefoneE164, conversa.Id, ehNova ? "conversa nova" : "conversa existente");

        // Boas-vindas, menu de triagem e o aviso de encaminhamento saem AQUI —
        // depois do commit, nunca dentro da transacao. Uma chamada de rede presa
        // numa transacao aberta segura o lock da conversa pelo tempo do gateway.
        await canal.EnviarPendentesAsync(conversa.Id, ct);

        await NotificarAsync(tenantId, conversa.Id, ct);

        return conversa.Id;
    }

    public async Task AtualizarEntregaAsync(
        Guid tenantId,
        string idExternoWhatsapp,
        StatusEntrega status,
        CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        // So anda para frente. O WhatsApp entrega confirmacoes fora de ordem com
        // frequencia; sem o filtro por StatusEntrega, um DELIVERY_ACK atrasado
        // rebaixaria para "Entregue" uma mensagem que o cliente ja leu.
        //
        // ExecuteUpdate e nao entidade rastreada: Conversa carrega RowVersion, e o
        // caminho do webhook nao pode colidir com quem esta atendendo.
        var afetadas = status switch
        {
            StatusEntrega.Entregue => await db.Mensagens
                .Where(m => m.TenantId == tenantId
                         && m.IdExternoWhatsapp == idExternoWhatsapp
                         && m.StatusEntrega != StatusEntrega.Lida
                         && m.StatusEntrega != StatusEntrega.Entregue)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.StatusEntrega, StatusEntrega.Entregue)
                    .SetProperty(m => m.AtualizadoEm, agora), ct),

            StatusEntrega.Lida => await db.Mensagens
                .Where(m => m.TenantId == tenantId
                         && m.IdExternoWhatsapp == idExternoWhatsapp
                         && m.StatusEntrega != StatusEntrega.Lida)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.StatusEntrega, StatusEntrega.Lida)
                    .SetProperty(m => m.LidaEm, agora)
                    .SetProperty(m => m.AtualizadoEm, agora), ct),

            StatusEntrega.Falha => await db.Mensagens
                .Where(m => m.TenantId == tenantId
                         && m.IdExternoWhatsapp == idExternoWhatsapp
                         && m.StatusEntrega != StatusEntrega.Lida
                         && m.StatusEntrega != StatusEntrega.Entregue)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.StatusEntrega, StatusEntrega.Falha)
                    .SetProperty(m => m.AtualizadoEm, agora), ct),

            _ => 0
        };

        if (afetadas == 0)
        {
            return;
        }

        var conversaId = await db.Mensagens.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.IdExternoWhatsapp == idExternoWhatsapp)
            .Select(m => (Guid?)m.ConversaId)
            .FirstOrDefaultAsync(ct);

        if (conversaId is { } id)
        {
            await NotificarAsync(tenantId, id, ct);
        }
    }

    /// <summary>
    /// Grava o anexo em disco e carimba os campos na mensagem.
    /// </summary>
    /// <remarks>
    /// <b>Falha aqui nao derruba a mensagem.</b> Tipo fora da lista de
    /// permitidos, arquivo grande demais, disco cheio — em todos, a mensagem
    /// entra no fio com a legenda e sem o arquivo. O oposto seria perder a
    /// mensagem do cliente por causa de um anexo, e uma figurinha recusada
    /// silenciaria uma pergunta de verdade.
    ///
    /// O <c>Tipo</c> passa a ser o que a lista de permitidos diz, e nao o que o
    /// WhatsApp declarou: e a mesma informacao vinda de uma fonte que nos
    /// controlamos.
    ///
    /// Arquivo gravado com a transacao aberta pode virar orfao em disco se o
    /// commit falhar adiante. Preferido ao contrario — linha apontando para
    /// arquivo que nunca existiu quebra o fio; arquivo sem linha so ocupa espaco.
    /// </remarks>
    private async Task GravarAnexoAsync(
        Mensagem mensagem,
        ArquivoDoCanal? arquivo,
        DateTime agora,
        CancellationToken ct)
    {
        if (arquivo is null || arquivo.Conteudo.Length == 0)
        {
            return;
        }

        try
        {
            var gravado = await anexos.GravarAsync(
                mensagem.TenantId,
                mensagem.Id,
                agora,
                arquivo.Conteudo,
                arquivo.Nome,
                arquivo.TipoMime,
                ct);

            mensagem.ArquivoCaminho = gravado.CaminhoRelativo;
            mensagem.ArquivoNome = gravado.Nome;
            mensagem.ArquivoTipoMime = gravado.TipoMime;
            mensagem.ArquivoTamanhoBytes = gravado.TamanhoBytes;
            mensagem.Tipo = gravado.Tipo;
        }
        catch (Exception falha) when (falha is not OperationCanceledException)
        {
            log.LogWarning(
                falha,
                "Anexo da mensagem {IdExterno} nao foi gravado; a mensagem entra sem arquivo.",
                mensagem.IdExternoWhatsapp);
        }
    }

    /// <summary>
    /// Acha o cliente pelo telefone ou cria um com o nome do perfil do WhatsApp.
    /// </summary>
    /// <remarks>
    /// O telefone e a identidade (README de Clientes): unico por tenant, guardado
    /// em E.164 sem simbolos. Nome nasce do perfil e e corrigido pelo atendente
    /// quando a identificacao real aparece — exigir qualificacao no primeiro "bom
    /// dia" travaria o atendimento.
    /// </remarks>
    private async Task<Cliente> ResolverClienteAsync(
        Guid tenantId,
        MensagemDoCanal mensagem,
        DateTime agora,
        CancellationToken ct)
    {
        var cliente = await db.Clientes
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId && c.TelefoneWhatsapp == mensagem.TelefoneE164, ct);

        if (cliente is not null)
        {
            // Cliente cadastrado sem nome util ("Contato", vazio) ganha o nome do
            // perfil. Nome ja preenchido NAO e sobrescrito: pode ter sido corrigido
            // a mao por um atendente, e o perfil do WhatsApp e do cliente, nao nosso.
            if (!string.IsNullOrWhiteSpace(mensagem.NomeDoPerfil)
                && string.IsNullOrWhiteSpace(cliente.Nome))
            {
                cliente.Nome = mensagem.NomeDoPerfil;
                cliente.AtualizadoEm = agora;
            }

            return cliente;
        }

        cliente = new Cliente
        {
            TenantId = tenantId,
            Nome = string.IsNullOrWhiteSpace(mensagem.NomeDoPerfil)
                ? mensagem.TelefoneE164
                : mensagem.NomeDoPerfil,
            TelefoneWhatsapp = mensagem.TelefoneE164,
            Ativo = true,
            CriadoEm = agora,
            AtualizadoEm = agora
        };

        db.Clientes.Add(cliente);

        log.LogInformation("WhatsApp: cliente novo criado para {Telefone}.", mensagem.TelefoneE164);

        return cliente;
    }

    /// <summary>
    /// Reaproveita a conversa ativa do cliente ou abre uma nova.
    /// </summary>
    /// <remarks>
    /// Reaproveitar e o comportamento certo: cada mensagem nova do mesmo cliente
    /// abrindo um protocolo faria uma conversa de cinco linhas virar cinco
    /// atendimentos. Conversa encerrada NAO e reaberta automaticamente — o cliente
    /// que volta depois de encerrado tem um assunto novo, e o historico anterior
    /// continua consultavel pelo protocolo.
    /// </remarks>
    private async Task<(Conversa Conversa, bool EhNova)> ResolverConversaAsync(
        Guid tenantId,
        Cliente cliente,
        DateTime agora,
        CancellationToken ct)
    {
        var ativa = await db.Conversas
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId
                  && c.ClienteId == cliente.Id
                  && c.Status == StatusConversa.Ativa, ct);

        if (ativa is not null)
        {
            return (ativa, false);
        }

        var etapaInicial = await db.EtapasFunil.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Ativo)
            .OrderBy(e => e.Ordem)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

        var protocolo = await protocolos.GerarAsync(tenantId, agora, ct);

        var conversa = new Conversa
        {
            TenantId = tenantId,
            ClienteId = cliente.Id,
            Protocolo = protocolo,
            EtapaId = etapaInicial,
            Status = StatusConversa.Ativa,

            // O que separa esta conversa das 11 de demonstracao. E por este campo
            // que o envio decide se sai pelo gateway ou fica so no painel.
            Canal = CanalOrigem.WhatsApp,
            IniciadaEm = agora,
            UltimaMensagemEm = agora
        };

        db.Conversas.Add(conversa);

        db.HistoricoEtapas.Add(new HistoricoEtapa
        {
            TenantId = tenantId,
            ConversaId = conversa.Id,
            TipoMudanca = TipoMudancaHistorico.Abertura,
            EtapaNovaId = etapaInicial,
            AtendenteId = null,
            OcorridoEm = agora,
            Observacao = $"Atendimento aberto pelo WhatsApp com o protocolo {protocolo}."
        });

        return (conversa, true);
    }

    /// <summary>
    /// Boas-vindas com o menu de triagem.
    /// </summary>
    /// <remarks>
    /// Roda depois de o cliente ja estar gravado, e depois da mensagem dele
    /// entrar na fila: o disparador resolve {NomeCliente} por consulta ao banco, e
    /// a ordem de insercao e o que faz o menu aparecer DEPOIS do "bom dia" no fio.
    ///
    /// A flag depende de o menu ter REALMENTE saido: sem regra cadastrada nao ha
    /// menu, e esperar resposta de uma pergunta que ninguem fez faria a proxima
    /// mensagem do cliente receber um fallback vindo do nada.
    /// </remarks>
    private async Task EnfileirarBoasVindasAsync(Conversa conversa, CancellationToken ct)
    {
        var boasVindas = await automaticas.EnfileirarAsync(
            conversa, GatilhoDeMensagem.NovoAtendimento, ct: ct);

        conversa.AguardandoTriagem = boasVindas.Count > 0;
    }

    /// <summary>
    /// Empurra a conversa para os paineis abertos.
    /// </summary>
    /// <remarks>
    /// Nao usa <c>IConversaService</c> de proposito — aquele servico le TenantId de
    /// <c>IContextoDoUsuario</c>, que aqui nao existe. As projecoes estaticas
    /// recebem o tenant explicitamente e servem aos dois mundos.
    ///
    /// Falha de notificacao nao pode desfazer o que ja foi gravado: a mensagem
    /// aparece na proxima leitura do painel de qualquer forma.
    /// </remarks>
    private async Task NotificarAsync(Guid tenantId, Guid conversaId, CancellationToken ct)
    {
        try
        {
            var projecao = await ProjecaoDeConversa
                .De(db, db.Conversas.AsNoTracking().Where(c => c.Id == conversaId))
                .FirstAsync(ct);

            await notificador.ConversaAtualizadaAsync(
                tenantId, projecao.ParaResposta(DateTime.UtcNow), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(
                ex,
                "Mensagem gravada na conversa {ConversaId}, mas a notificacao em tempo real falhou",
                conversaId);
        }
    }
}
