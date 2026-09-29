using CrmCartorio.Api.Infrastructure.Auditoria;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

public class MensagemService(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IConversaService conversas,
    INotificadorDeAtendimento notificador,
    IDisparadorDeMensagemAutomatica automaticas,
    IEnviadorDeCanal canal,
    IArmazenamentoDeAnexos anexos,
    IRespostaInteligenteService respostaInteligente,
    IOptions<OpcoesDaIA> opcoesDaIA,
    IRegistradorDeAcesso registrador,
    ILogger<MensagemService> log) : IMensagemService, IAcoesDeMensagem
{
    private readonly OpcoesDaIA _opcoesDaIA = opcoesDaIA.Value;

    /// <summary>
    /// Janela em que o WhatsApp aceita editar uma mensagem ja entregue.
    /// </summary>
    /// <remarks>
    /// O numero e deles, nao nosso: nao adianta configurar por tela nem alargar
    /// aqui, porque quem recusa do outro lado e o aparelho do cliente. Fica como
    /// constante para o painel poder esconder a acao quando a janela fechou, em
    /// vez de oferecer um botao que sempre falha.
    /// </remarks>
    public const int MinutosParaCorrigir = 15;

    /// <summary>
    /// Janela em que o WhatsApp aceita apagar para todos. Tambem e regra deles.
    /// </summary>
    /// <remarks>
    /// Conservador de proposito: o limite observado e maior (perto de dois dias e
    /// meio), e pedir no ultimo minuto daria recusa do gateway em vez de recusa
    /// explicada. Quem estourou os dois dias ainda pode ocultar do fio.
    /// </remarks>
    public const int DiasParaApagar = 2;

    /// <summary>Mesmo limite do texto em claro da coluna (ver MensagemConfiguration).</summary>
    private const int TamanhoMaximoDaMensagem = 4000;

    public async Task<IReadOnlyList<MensagemResponse>> ListarAsync(
        Guid conversaId,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.AtendimentoVer);
        GarantirEscopoDeConversa(conversaId);

        var tenantId = ctx.TenantId;

        var clienteNome = await db.Conversas.AsNoTracking()
            .Where(c => c.Id == conversaId && c.TenantId == tenantId)
            .Select(c => db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.Nome).FirstOrDefault())
            .FirstOrDefaultAsync(ct);

        if (clienteNome is null)
        {
            throw AtendimentoServiceException.ConversaNaoEncontrada();
        }

        await registrador.RegistrarAsync(TipoDeAcesso.LeituraDeConversa, conversaId: conversaId, ct: ct);

        return await ProjecaoDeMensagem
            .ListarAsync(
                db,
                db.Mensagens.AsNoTracking()
                    .Where(m => m.ConversaId == conversaId && m.TenantId == tenantId)
                    .OrderBy(m => m.EnviadaEm)
                    .ThenBy(m => m.Id),
                clienteNome,
                ct);
    }

    public async Task<MensagemResponse> EnviarAsync(
        Guid conversaId,
        EnviarMensagemRequest request,
        CancellationToken ct = default)
    {
        var conteudo = request.Conteudo?.Trim();
        if (string.IsNullOrWhiteSpace(conteudo))
        {
            throw AtendimentoServiceException.MensagemVazia();
        }

        var conversa = await PrepararEnvioAsync(conversaId, request.MensagemRespondidaId, ct);
        var agora = DateTime.UtcNow;

        var mensagem = new Mensagem
        {
            TenantId = conversa.TenantId,
            ConversaId = conversaId,
            AutorTipo = AutorTipo.Atendente,

            // RF-007: a mensagem carrega QUEM respondeu. Nao e inferido do dono da
            // conversa — se ela for transferida depois, o autor original permanece.
            AtendenteId = ctx.AtendenteId,

            Conteudo = conteudo,
            Tipo = TipoMensagem.Texto,
            MensagemRespondidaId = request.MensagemRespondidaId,

            // Conversa de WhatsApp nasce Pendente: o envio real acontece depois do
            // commit, e so entao vira Enviada com o id externo. Conversa simulada
            // continua nascendo Enviada — nao ha canal para confirmar coisa alguma,
            // e Pendente eterno mentiria no fio.
            StatusEntrega = conversa.Canal == CanalOrigem.WhatsApp
                ? StatusEntrega.Pendente
                : StatusEntrega.Enviada,
            EnviadaEm = agora
        };

        db.Mensagens.Add(mensagem);

        return await ConcluirEnvioAsync(mensagem, conversaId, agora, ct);
    }

    public async Task<MensagemResponse> EnviarAnexoAsync(
        Guid conversaId,
        EnviarAnexoRequest request,
        CancellationToken ct = default)
    {
        // Conferido ANTES de assumir a conversa: recusar um arquivo depois de
        // atribuir o atendimento a quem so tentou mandar um tipo invalido deixaria
        // a conversa presa com ele.
        if (request.Conteudo.Length == 0)
        {
            throw AtendimentoServiceException.AnexoVazio();
        }

        if (request.Conteudo.LongLength > anexos.TamanhoMaximoBytes)
        {
            throw AtendimentoServiceException.AnexoGrandeDemais(
                (int)(anexos.TamanhoMaximoBytes / (1024 * 1024)));
        }

        if (!anexos.TipoPermitido(request.TipoMime))
        {
            throw AtendimentoServiceException.AnexoDeTipoNaoAceito(request.TipoMime);
        }

        var conversa = await PrepararEnvioAsync(conversaId, request.MensagemRespondidaId, ct);
        var agora = DateTime.UtcNow;
        var legenda = request.Legenda?.Trim();

        var mensagem = new Mensagem
        {
            TenantId = conversa.TenantId,
            ConversaId = conversaId,
            AutorTipo = AutorTipo.Atendente,
            AtendenteId = ctx.AtendenteId,

            // Placeholder ate o arquivo ser gravado: Conteudo e NOT NULL e o nome
            // final so existe depois que o armazenamento resolve a extensao.
            Conteudo = string.IsNullOrWhiteSpace(legenda) ? "[documento]" : legenda,
            MensagemRespondidaId = request.MensagemRespondidaId,
            StatusEntrega = conversa.Canal == CanalOrigem.WhatsApp
                ? StatusEntrega.Pendente
                : StatusEntrega.Enviada,
            EnviadaEm = agora
        };

        // Aqui a falha SOBE, ao contrario do recebimento. Sao situacoes opostas:
        // no webhook, recusar o anexo perderia a mensagem do cliente; aqui, quem
        // anexou esta na frente da tela e precisa saber que o arquivo nao foi.
        var gravado = await anexos.GravarAsync(
            conversa.TenantId,
            mensagem.Id,
            agora,
            request.Conteudo,
            request.NomeDoArquivo,
            request.TipoMime,
            ct);

        mensagem.ArquivoCaminho = gravado.CaminhoRelativo;
        mensagem.ArquivoNome = gravado.Nome;
        mensagem.ArquivoTipoMime = gravado.TipoMime;
        mensagem.ArquivoTamanhoBytes = gravado.TamanhoBytes;
        mensagem.Tipo = gravado.Tipo;

        // Sem legenda, o fio mostra o nome do arquivo — mais util que "[documento]"
        // e igual ao que o recebimento faz com documento que chega sem legenda.
        if (string.IsNullOrWhiteSpace(legenda))
        {
            mensagem.Conteudo = gravado.Nome;
        }

        db.Mensagens.Add(mensagem);

        return await ConcluirEnvioAsync(mensagem, conversaId, agora, ct);
    }

    public async Task<AnexoParaDownload> ObterAnexoAsync(
        Guid conversaId,
        Guid mensagemId,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.AtendimentoVer);
        GarantirEscopoDeConversa(conversaId);

        // O WHERE amarra conversa E tenant. Sem isso, adivinhar o id de uma
        // mensagem entregaria o documento de um cliente de outra serventia.
        var alvo = await db.Mensagens.AsNoTracking()
            .Where(m => m.Id == mensagemId
                     && m.ConversaId == conversaId
                     && m.TenantId == ctx.TenantId)
            .Select(m => new { m.ArquivoCaminho, m.ArquivoNome, m.ArquivoTipoMime })
            .FirstOrDefaultAsync(ct)
            ?? throw AtendimentoServiceException.MensagemNaoEncontrada();

        if (string.IsNullOrWhiteSpace(alvo.ArquivoCaminho))
        {
            throw AtendimentoServiceException.AnexoNaoEncontrado();
        }

        // Registrado ANTES de ler o arquivo, nao depois: se o disco falhar
        // (AnexoNaoEncontrado, backup restaurado sem a pasta de anexos), o
        // pedido de acesso ao documento ja aconteceu e deve ficar na trilha
        // mesmo assim.
        await registrador.RegistrarAsync(
            TipoDeAcesso.DownloadDeAnexo, conversaId: conversaId, mensagemId: mensagemId, ct: ct);

        var conteudo = await anexos.LerAsync(alvo.ArquivoCaminho, ct)
            ?? throw AtendimentoServiceException.AnexoNaoEncontrado();

        var mime = alvo.ArquivoTipoMime ?? "application/octet-stream";

        return new AnexoParaDownload(
            conteudo,
            alvo.ArquivoNome ?? "anexo",
            mime,

            // SO imagem abre na tela. Qualquer outro tipo baixa, inclusive PDF:
            // exibir no dominio do painel um arquivo que o cliente escolheu e o
            // que transforma um anexo em codigo rodando com a sessao do atendente.
            mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Confere que da para escrever nesta conversa, e assume se ela estiver na fila.
    /// </summary>
    /// <remarks>
    /// Compartilhado por texto e anexo de proposito: duas copias destas regras
    /// divergiriam, e a divergencia seria justamente na trava que este produto
    /// existe para garantir.
    /// </remarks>
    private async Task<Conversa> PrepararEnvioAsync(
        Guid conversaId,
        Guid? mensagemRespondidaId,
        CancellationToken ct)
    {
        var tenantId = ctx.TenantId;

        var conversa = await db.Conversas
            .FirstOrDefaultAsync(c => c.Id == conversaId && c.TenantId == tenantId, ct)
            ?? throw AtendimentoServiceException.ConversaNaoEncontrada();

        if (conversa.Status != StatusConversa.Ativa)
        {
            throw AtendimentoServiceException.ConversaEncerrada();
        }

        // Conversa na fila: responder E assumir. Quem digitou e apertou enviar ja
        // decidiu atender — exigir um clique extra em "Assumir" antes disso e
        // cerimonia, e cerimonia no meio de um atendimento vira conversa perdida.
        //
        // A TRAVA NAO ENFRAQUECE. Reusa AssumirAsync, que e o mesmo UPDATE
        // condicional ja exercitado sob 12 chamadas simultaneas em
        // verificar-concorrencia.cs: se outro atendente assumiu um instante antes,
        // ele levanta ConflitoDeAtribuicaoException e a mensagem NAO e gravada.
        // O que muda e quando o UPDATE dispara, nao a garantia que ele da.
        //
        // AssumirAsync recarrega a entidade rastreada depois do ExecuteUpdate,
        // entao `conversa` ja sai daqui com o responsavel preenchido.
        if (conversa.AtendenteId is null)
        {
            await conversas.AssumirAsync(conversaId, ct);
        }

        // A trava do lado do envio continua: quem nao e o responsavel nao escreve
        // por cima de quem e. Sem isso, dois atendentes responderiam o mesmo
        // cliente ao mesmo tempo — o erro que este produto existe para eliminar.
        if (conversa.AtendenteId != ctx.AtendenteId && !ctx.Pode(PermissaoDoSistema.AtendimentoMexerNoDeTerceiro))
        {
            var responsavel = await db.Atendentes.AsNoTracking()
                .Where(a => a.Id == conversa.AtendenteId)
                .Select(a => a.Nome)
                .FirstOrDefaultAsync(ct) ?? "outro atendente";

            throw AtendimentoServiceException.NaoEhOResponsavel(responsavel);
        }

        // A mensagem citada tem de ser da MESMA conversa. Sem esta checagem daria
        // para citar mensagem de outro atendimento — e de outro cliente.
        if (mensagemRespondidaId is { } respondidaId)
        {
            var pertence = await db.Mensagens.AsNoTracking()
                .AnyAsync(m => m.Id == respondidaId
                            && m.ConversaId == conversaId
                            && m.TenantId == tenantId, ct);

            if (!pertence)
            {
                throw AtendimentoServiceException.MensagemCitadaInvalida();
            }
        }

        return conversa;
    }

    /// <summary>
    /// Grava, carimba a conversa, entrega pelo canal e avisa os paineis abertos.
    /// </summary>
    private async Task<MensagemResponse> ConcluirEnvioAsync(
        Mensagem mensagem,
        Guid conversaId,
        DateTime agora,
        CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);

        // UltimaMensagemEm sai por ExecuteUpdate, e NAO pela entidade rastreada.
        //
        // Conversa carrega RowVersion como token de concorrencia, entao gravar o
        // carimbo pela entidade faria dois envios simultaneos na mesma conversa
        // colidirem — o segundo levantaria DbUpdateConcurrencyException e viraria
        // 409 na cara de quem so respondeu um cliente. E conflito inventado: o
        // token existe para proteger DECISAO sobre a conversa (quem atende, que
        // etapa, encerrar), nao para arbitrar qual de dois relogios venceu.
        //
        // Para um carimbo de "ultima mensagem", o ultimo a escrever vencer e a
        // resposta certa, e e o que o UPDATE direto faz.
        //
        // A pergunta de encerramento pendente cai junto: o ATENDENTE respondeu, e
        // uma conversa que acabou de receber atencao humana nao pode fechar por
        // silencio minutos depois. Cabe no UPDATE que ja existe, sem consulta a
        // mais.
        await db.Conversas
            .Where(c => c.Id == conversaId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.UltimaMensagemEm, agora)
                .SetProperty(c => c.PerguntaDeEncerramentoEnviadaEm, (DateTime?)null)
                .SetProperty(c => c.AtualizadoEm, agora), ct);

        // Entrega de verdade, DEPOIS do commit. O adaptador decide pelo canal da
        // conversa: WhatsApp sai, simulada nao. Nao lanca — falha vira StatusEntrega
        // .Falha, porque a mensagem ja esta no fio e derrubar a requisicao aqui
        // faria o atendente clicar de novo e duplicar.
        await canal.EnviarAsync(mensagem.Id, ct);

        var resposta = await ObterUmaAsync(mensagem.Id, conversaId, ct);

        var resumo = await conversas.ObterResumoAsync(conversaId, ct);
        await notificador.MensagemRecebidaAsync(mensagem.TenantId, resposta, resumo, ct);

        return resposta;
    }

    public async Task<MensagemResponse> SimularRecebimentoAsync(
        Guid conversaId,
        SimularMensagemDeClienteRequest request,
        CancellationToken ct = default)
    {
        // Ferramenta de demonstracao — forja uma mensagem "do cliente" sem
        // WhatsApp real por tras. Fora de todos os 6 modelos de permissao de
        // proposito (Modules/Usuarios/Domain/CatalogoDePermissoes.cs); quem
        // precisar marca a chave a mao.
        if (!ctx.Pode(PermissaoDoSistema.AtendimentoSimularCliente))
        {
            throw AtendimentoServiceException.SemPermissao();
        }

        var conteudo = request.Conteudo?.Trim();
        if (string.IsNullOrWhiteSpace(conteudo))
        {
            throw AtendimentoServiceException.MensagemVazia();
        }

        var tenantId = ctx.TenantId;

        var conversa = await db.Conversas
            .FirstOrDefaultAsync(c => c.Id == conversaId && c.TenantId == tenantId, ct)
            ?? throw AtendimentoServiceException.ConversaNaoEncontrada();

        if (conversa.Status != StatusConversa.Ativa)
        {
            throw AtendimentoServiceException.ConversaEncerrada();
        }

        var agora = DateTime.UtcNow;

        // FASE 1: se o cliente esta respondendo, e porque leu o que foi enviado.
        // Promover as mensagens anteriores do atendente para Lida e o que permite
        // demonstrar os tiques de leitura sem WhatsApp real. Na Fase 2 isso sai:
        // quem move esse status passa a ser o webhook de status da Meta (RF-105),
        // que e a unica fonte honesta dessa informacao.
        await db.Mensagens
            .Where(m => m.ConversaId == conversaId
                     && m.TenantId == tenantId
                     && m.AutorTipo == AutorTipo.Atendente
                     && m.StatusEntrega != StatusEntrega.Lida
                     && m.StatusEntrega != StatusEntrega.Falha)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.StatusEntrega, StatusEntrega.Lida)
                .SetProperty(m => m.AtualizadoEm, agora), ct);

        var mensagem = new Mensagem
        {
            TenantId = tenantId,
            ConversaId = conversaId,
            AutorTipo = AutorTipo.Cliente,
            AtendenteId = null,
            Conteudo = conteudo,
            Tipo = TipoMensagem.Texto,
            StatusEntrega = StatusEntrega.Entregue,
            EnviadaEm = agora
        };

        db.Mensagens.Add(mensagem);

        conversa.UltimaMensagemEm = agora;
        conversa.MensagensNaoLidas += 1;

        // A mensagem do cliente ja esta na frente da fila de insercao, entao a
        // resposta da triagem aparece DEPOIS dela no fio — Guid v7 e sequencial, e o
        // fio ordena por EnviadaEm e depois por Id.
        //
        // "menu"/"voltar" e checado ANTES de qualquer flag — achado ao vivo,
        // 2026-08-18: checar isso so dentro de AplicadorDePosAtendimento deixava
        // a saida de emergencia refem de AguardandoRespostaInteligente estar
        // desligada, o que deixou de ser garantido quando a IA passou a manter
        // essa flag ligada mesmo sem saber responder (mesmo dia, ver
        // AplicadorDeRespostaInteligente). Cliente pedindo "menu" no meio de uma
        // troca com a IA ia direto para ela, que nao entende "menu" como comando.
        // BUG-ATD-01 — checado ANTES de qualquer outra coisa: a conversa ja
        // foi entregue a um humano (cliente pediu atendente, ou um atendente
        // assumiu por conta propria) e a automacao conversacional fica muda.
        // A mensagem ja foi gravada e os contadores acima ja foram
        // atualizados — so falta a notificacao no fim do metodo, que
        // continua incondicional.
        //
        // A pergunta de encerramento por inatividade e a UNICA excecao: se
        // havia uma pendente, o cliente escrever prova que ele esta vivo —
        // mesmo principio ja usado em ConcluirEnvioAsync quando o ATENDENTE
        // responde.
        //
        // SE MEXER AQUI, MEXA EM RecebimentoDeWhatsApp.ReceberAsync TAMBEM —
        // mesma cadeia de despacho, duplicada por enquanto.
        if (conversa.EmAtendimentoHumano)
        {
            if (conversa.PerguntaDeEncerramentoEnviadaEm is not null)
            {
                conversa.PerguntaDeEncerramentoEnviadaEm = null;
            }
        }
        else if (await AplicadorDeEncerramentoPorInatividade.TratarRespostaAsync(
                db, automaticas, conversa, conteudo, agora, ct))
        {
            // Primeiro de todos, ate antes do "menu": a pergunta de encerramento
            // por inatividade pode ter saido com a conversa em qualquer estado.
            //
            // CUIDADO: esta condicao tem efeito colateral quando devolve false —
            // ela ja cancelou a pergunta pendente, e a mensagem segue a cadeia
            // abaixo de proposito. Nao e um `if` puro.
        }
        else if (TriagemAutomatica.PediuMenu(conteudo))
        {
            await ReabrirMenuInicialAsync(conversa, agora, ct);
        }
        else if (conversa.AguardandoNome)
        {
            // Mesma posicao do webhook real (RecebimentoDeWhatsApp): abaixo do
            // "menu", acima das demais flags. O aplicador consome a mensagem em
            // todos os caminhos — o retorno nao e condicao de despacho.
            await AplicadorDePedidoDeNome.AplicarAsync(
                db, automaticas, conversa, conteudo, agora, log, ct);
        }
        else if (conversa.AguardandoTriagem)
        {
            await ResponderTriagemAsync(conversa, conteudo, agora, ct);
        }
        else if (conversa.AguardandoEscolhaDeServico)
        {
            await ContinuarEscolhaDeServicoAsync(conversa, conteudo, agora, ct);
        }
        else if (conversa.AguardandoDadosDoServico || conversa.AguardandoComprovante)
        {
            await AplicadorDeDadosDoServico.AplicarAsync(db, automaticas, conversa, agora, log, ct);
        }
        else if (conversa.AguardandoRespostaInteligente)
        {
            await ContinuarRespostaInteligenteAsync(conversa, conteudo, agora, ct);
        }
        else
        {
            // Nenhuma flag de fluxo ligada — o atendimento estruturado ja
            // terminou, ou esta conversa nunca passou pela triagem. Sem este
            // ramo a mensagem so era gravada e nada respondia (achado ao vivo,
            // 2026-08-17): agora e sempre tratada, nem que seja pela IA.
            await ContinuarPosAtendimentoAsync(conversa, conteudo, agora, ct);
        }

        await db.SaveChangesAsync(ct);

        var resposta = await ObterUmaAsync(mensagem.Id, conversaId, ct);

        var resumo = await conversas.ObterResumoAsync(conversaId, ct);
        await notificador.MensagemRecebidaAsync(tenantId, resposta, resumo, ct);

        return resposta;
    }

    /// <summary>
    /// Le a resposta do cliente ao menu de triagem.
    /// </summary>
    /// <remarks>
    /// A regra mora em <see cref="AplicadorDeTriagem"/> desde a Fase 2, porque o
    /// webhook do WhatsApp real precisa da MESMA triagem que a simulacao pelo
    /// painel. Duas copias divergiriam na primeira mudanca, e a divergencia so
    /// apareceria na conversa de um cliente de verdade.
    /// </remarks>
    private Task ResponderTriagemAsync(
        Conversa conversa,
        string conteudo,
        DateTime agora,
        CancellationToken ct) =>
        AplicadorDeTriagem.AplicarAsync(
            db, automaticas, respostaInteligente, _opcoesDaIA, conversa, conteudo, agora, log, ct);

    /// <summary>
    /// Segunda troca em diante da Resposta Inteligente, quando a conversa já está em
    /// sequência (ver <see cref="Conversa.AguardandoRespostaInteligente"/>).
    /// </summary>
    /// <remarks>
    /// Mesmo motivo de <see cref="ResponderTriagemAsync"/> para viver fora do
    /// serviço: <see cref="AplicadorDeRespostaInteligente"/> é a MESMA regra que o
    /// webhook do WhatsApp real chama em <c>RecebimentoDeWhatsApp</c>.
    /// </remarks>
    private Task ContinuarRespostaInteligenteAsync(
        Conversa conversa,
        string conteudo,
        DateTime agora,
        CancellationToken ct) =>
        AplicadorDeRespostaInteligente.AplicarAsync(
            db, respostaInteligente, automaticas, _opcoesDaIA, conversa, conteudo, agora, log, ct);

    /// <summary>
    /// Le a resposta do cliente ao menu de 2o nivel (qual servico dentro do setor).
    /// </summary>
    /// <remarks>
    /// Mesmo motivo de <see cref="ResponderTriagemAsync"/> para viver fora do
    /// servico: <see cref="AplicadorDeEscolhaDeServico"/> e a MESMA regra que o
    /// webhook do WhatsApp real chama em <c>RecebimentoDeWhatsApp</c>.
    /// </remarks>
    private Task ContinuarEscolhaDeServicoAsync(
        Conversa conversa,
        string conteudo,
        DateTime agora,
        CancellationToken ct) =>
        AplicadorDeEscolhaDeServico.AplicarAsync(
            db, automaticas, respostaInteligente, _opcoesDaIA, conversa, conteudo, agora, log, ct);

    /// <summary>
    /// Le a mensagem do cliente quando nenhuma flag de fluxo esta ligada.
    /// </summary>
    /// <remarks>
    /// Mesmo motivo dos outros Continuar*: <see cref="AplicadorDePosAtendimento"/>
    /// e a MESMA regra que o webhook do WhatsApp real chama em
    /// <c>RecebimentoDeWhatsApp</c>.
    /// </remarks>
    private Task ContinuarPosAtendimentoAsync(
        Conversa conversa,
        string conteudo,
        DateTime agora,
        CancellationToken ct) =>
        AplicadorDePosAtendimento.AplicarAsync(
            db, automaticas, respostaInteligente, _opcoesDaIA, conversa, conteudo, agora, log, ct);

    /// <summary>
    /// "menu"/"voltar" — reabre do zero, verificado ANTES de qualquer flag de
    /// fluxo. Ver <see cref="AplicadorDePosAtendimento.ReabrirDoZeroAsync"/>.
    /// </summary>
    private Task ReabrirMenuInicialAsync(Conversa conversa, DateTime agora, CancellationToken ct) =>
        AplicadorDePosAtendimento.ReabrirDoZeroAsync(db, automaticas, conversa, agora, ct);

    // ------------------------------------------------- acoes do menu de contexto

    public async Task<MensagemResponse> FixarAsync(
        Guid conversaId,
        Guid mensagemId,
        bool fixar,
        CancellationToken ct = default)
    {
        var conversa = await CarregarParaAcaoAsync(conversaId, ct);
        var agora = DateTime.UtcNow;

        var mensagem = await db.Mensagens
            .FirstOrDefaultAsync(m => m.Id == mensagemId
                                   && m.ConversaId == conversaId
                                   && m.TenantId == conversa.TenantId, ct)
            ?? throw AtendimentoServiceException.MensagemNaoEncontrada();

        if (fixar)
        {
            // Uma por conversa. Desafixa as demais no mesmo golpe, em vez de
            // acumular um mural no topo do fio — o lugar mais caro da tela.
            await db.Mensagens
                .Where(m => m.ConversaId == conversaId && m.FixadaEm != null && m.Id != mensagemId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.FixadaEm, (DateTime?)null)
                    .SetProperty(m => m.FixadaPorAtendenteId, (Guid?)null)
                    .SetProperty(m => m.AtualizadoEm, agora), ct);

            mensagem.FixadaEm = agora;
            mensagem.FixadaPorAtendenteId = ctx.AtendenteId;
        }
        else
        {
            mensagem.FixadaEm = null;
            mensagem.FixadaPorAtendenteId = null;
        }

        await db.SaveChangesAsync(ct);

        var resposta = await ObterUmaAsync(mensagem.Id, conversaId, ct);
        var resumo = await conversas.ObterResumoAsync(conversaId, ct);
        await notificador.MensagemRecebidaAsync(conversa.TenantId, resposta, resumo, ct);

        return resposta;
    }

    public async Task<MensagemResponse> CorrigirAsync(
        Guid conversaId,
        Guid mensagemId,
        CorrigirMensagemRequest request,
        CancellationToken ct = default)
    {
        var novoConteudo = request.Conteudo?.Trim();

        if (string.IsNullOrWhiteSpace(novoConteudo))
        {
            throw AtendimentoServiceException.MensagemVazia();
        }

        if (novoConteudo.Length > TamanhoMaximoDaMensagem)
        {
            throw AtendimentoServiceException.MensagemLongaDemais(TamanhoMaximoDaMensagem);
        }

        var conversa = await CarregarParaAcaoAsync(conversaId, ct);
        var mensagem = await CarregarParaCorrecaoAsync(conversa, mensagemId, ct);

        // So texto. O WhatsApp nao edita midia — a legenda de uma foto ja enviada
        // fica como esta, e o caminho para esse engano e apagar e mandar de novo.
        if (mensagem.Tipo != TipoMensagem.Texto || mensagem.TemArquivo)
        {
            throw AtendimentoServiceException.CorrecaoSoDeTexto();
        }

        if (novoConteudo == mensagem.Conteudo)
        {
            throw AtendimentoServiceException.CorrecaoSemMudanca();
        }

        var agora = DateTime.UtcNow;

        // A janela e do WhatsApp, nao nossa. Conferida aqui para o atendente
        // receber uma recusa que explica o motivo, em vez de um "o WhatsApp nao
        // aceitou" generico depois da ida ao gateway.
        if (agora - mensagem.EnviadaEm > TimeSpan.FromMinutes(MinutosParaCorrigir))
        {
            throw AtendimentoServiceException.JanelaDeCorrecaoExpirada(MinutosParaCorrigir);
        }

        var resultado = await canal.CorrigirAsync(mensagemId, novoConteudo, ct);

        if (resultado == ResultadoNoCanal.Recusado)
        {
            // Nada e gravado. O texto no aparelho do cliente continua o antigo, e um
            // fio que mostrasse o novo faria o atendente acreditar num conserto que
            // nao aconteceu.
            throw AtendimentoServiceException.CanalRecusouACorrecao();
        }

        // Guarda o que saiu da PRIMEIRA vez. Correcao da correcao nao sobrescreve:
        // o que interessa depois e o que o cliente recebeu antes de qualquer
        // conserto.
        mensagem.ConteudoOriginal ??= mensagem.Conteudo;
        mensagem.Conteudo = novoConteudo;
        mensagem.CorrigidaEm = agora;
        mensagem.CorrigidaPorAtendenteId = ctx.AtendenteId;

        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Mensagem {MensagemId} corrigida por {AtendenteId} na conversa {ConversaId} ({Canal}).",
            mensagemId, ctx.AtendenteId, conversaId, resultado);

        return await PublicarMensagemAtualizadaAsync(mensagemId, conversaId, conversa.TenantId, ct);
    }

    public async Task<MensagemResponse> ApagarParaTodosAsync(
        Guid conversaId,
        Guid mensagemId,
        CancellationToken ct = default)
    {
        var conversa = await CarregarParaAcaoAsync(conversaId, ct);
        var mensagem = await CarregarParaCorrecaoAsync(conversa, mensagemId, ct);

        var agora = DateTime.UtcNow;

        if (agora - mensagem.EnviadaEm > TimeSpan.FromDays(DiasParaApagar))
        {
            throw AtendimentoServiceException.JanelaDeApagamentoExpirada(DiasParaApagar);
        }

        var resultado = await canal.ApagarParaTodosAsync(mensagemId, ct);

        if (resultado == ResultadoNoCanal.Recusado)
        {
            throw AtendimentoServiceException.CanalRecusouACorrecao();
        }

        // O Conteudo permanece. A linha nao some do fio nem do banco: o atendente
        // ve a mensagem marcada como apagada, e continua sabendo o que foi dito ao
        // cliente antes de ser retirado.
        mensagem.ApagadaParaTodosEm = agora;
        mensagem.ApagadaParaTodosPorAtendenteId = ctx.AtendenteId;

        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Mensagem {MensagemId} apagada para todos por {AtendenteId} na conversa {ConversaId} ({Canal}).",
            mensagemId, ctx.AtendenteId, conversaId, resultado);

        return await PublicarMensagemAtualizadaAsync(mensagemId, conversaId, conversa.TenantId, ct);
    }

    /// <summary>
    /// Carrega a mensagem e aplica as travas comuns a corrigir e apagar.
    /// </summary>
    /// <remarks>
    /// As duas acoes partilham as mesmas quatro recusas, e separa-las em dois
    /// lugares faria uma divergir da outra no primeiro ajuste.
    /// </remarks>
    private async Task<Mensagem> CarregarParaCorrecaoAsync(
        Conversa conversa,
        Guid mensagemId,
        CancellationToken ct)
    {
        var mensagem = await db.Mensagens
            .FirstOrDefaultAsync(m => m.Id == mensagemId
                                   && m.ConversaId == conversa.Id
                                   && m.TenantId == conversa.TenantId, ct)
            ?? throw AtendimentoServiceException.MensagemNaoEncontrada();

        // Mensagem do cliente nao e nossa para mexer, e automatica/IA se conserta
        // no cadastro que a gerou — corrigir uma a uma no fio deixaria a regra
        // errada continuar disparando errado na proxima conversa.
        if (mensagem.AutorTipo != AutorTipo.Atendente)
        {
            throw AtendimentoServiceException.CorrecaoSoDeAtendente();
        }

        // Saiu do celular: quem escreveu esta com o aparelho na mao, e o conserto
        // e la. Pelo painel ninguem responde pelo autor — a mensagem nao tem
        // atendente, e a regra "so o autor corrige" nao teria a quem se aplicar.
        if (mensagem.EnviadaPeloAparelho)
        {
            throw AtendimentoServiceException.CorrecaoDeMensagemDoAparelho();
        }

        // Quem escreveu conserta o proprio texto. A mensagem sai assinada com o
        // nome do autor (AssinaturaDeAutoria), entao uma correcao feita por outra
        // pessoa mudaria, no aparelho do cliente, o que "Maria" disse — sem Maria
        // saber. Supervisao continua possivel: quem tem a permissao de mexer em
        // atendimento de terceiro passa.
        if (mensagem.AtendenteId != ctx.AtendenteId
            && !ctx.Pode(PermissaoDoSistema.AtendimentoMexerNoDeTerceiro))
        {
            var autor = await db.Atendentes.AsNoTracking()
                .Where(a => a.Id == mensagem.AtendenteId)
                .Select(a => a.Nome)
                .FirstOrDefaultAsync(ct) ?? "outro atendente";

            throw AtendimentoServiceException.CorrecaoSoDoAutor(autor);
        }

        if (mensagem.ApagadaParaTodosEm is not null)
        {
            throw AtendimentoServiceException.MensagemJaApagada();
        }

        // Ainda na fila de saida: sem id externo nao ha o que corrigir no aparelho
        // do cliente, e gravar o texto novo aqui produziria uma divergencia
        // silenciosa se o envio acontecesse um instante depois. Esperar alguns
        // segundos resolve — a fila de saida e rapida.
        if (mensagem.StatusEntrega is StatusEntrega.Pendente or StatusEntrega.Enviando
            || (conversa.Canal == CanalOrigem.WhatsApp
                && string.IsNullOrWhiteSpace(mensagem.IdExternoWhatsapp)))
        {
            throw AtendimentoServiceException.MensagemAindaSaindo();
        }

        return mensagem;
    }

    /// <summary>Recarrega a projecao e avisa o painel de quem estiver com a conversa aberta.</summary>
    private async Task<MensagemResponse> PublicarMensagemAtualizadaAsync(
        Guid mensagemId,
        Guid conversaId,
        Guid tenantId,
        CancellationToken ct)
    {
        var resposta = await ObterUmaAsync(mensagemId, conversaId, ct);
        var resumo = await conversas.ObterResumoAsync(conversaId, ct);
        await notificador.MensagemRecebidaAsync(tenantId, resposta, resumo, ct);

        return resposta;
    }

    public async Task<int> OcultarAsync(
        Guid conversaId,
        OcultarMensagensRequest request,
        CancellationToken ct = default)
    {
        var conversa = await CarregarParaAcaoAsync(conversaId, ct);

        if (request.MensagemIds.Count == 0)
        {
            return 0;
        }

        var agora = DateTime.UtcNow;
        var ids = request.MensagemIds;

        // ExecuteUpdate, e nao entidade rastreada: e operacao em lote vinda da
        // selecao multipla, e carregar N mensagens para mudar duas colunas seria
        // trabalho a toa. O WHERE amarra a conversa e o tenant — nao da para
        // ocultar mensagem de outro atendimento passando o Id na mao.
        var afetadas = await db.Mensagens
            .Where(m => ids.Contains(m.Id)
                     && m.ConversaId == conversaId
                     && m.TenantId == conversa.TenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.OcultaEm, request.Ocultar ? agora : null)
                .SetProperty(m => m.OcultaPorAtendenteId, request.Ocultar ? ctx.AtendenteId : null)
                .SetProperty(m => m.AtualizadoEm, agora), ct);

        log.LogInformation(
            "{Total} mensagem(ns) {Acao} na conversa {ConversaId} por {AtendenteId}",
            afetadas, request.Ocultar ? "ocultada(s)" : "reexibida(s)", conversaId, ctx.AtendenteId);

        var resumo = await conversas.ObterResumoAsync(conversaId, ct);
        await notificador.ConversaAtualizadaAsync(conversa.TenantId, resumo, ct);

        return afetadas;
    }

    public async Task<int> EncaminharAsync(
        Guid conversaId,
        EncaminharMensagensRequest request,
        CancellationToken ct = default)
    {
        var origem = await CarregarParaAcaoAsync(conversaId, ct);

        if (request.MensagemIds.Count == 0)
        {
            return 0;
        }

        if (request.ConversaDestinoId == conversaId)
        {
            throw AtendimentoServiceException.EncaminhamentoParaMesmaConversa();
        }

        var destino = await db.Conversas
            .FirstOrDefaultAsync(c => c.Id == request.ConversaDestinoId
                                   && c.TenantId == origem.TenantId, ct)
            ?? throw AtendimentoServiceException.ConversaNaoEncontrada();

        if (destino.Status != StatusConversa.Ativa)
        {
            throw AtendimentoServiceException.ConversaEncerrada();
        }

        // Mesma regra de quem pode escrever no destino que vale para uma mensagem
        // digitada ali: conversa na fila e assumida, conversa de terceiro e
        // recusada. Encaminhar nao pode ser porta dos fundos para escrever numa
        // conversa que o atendente nao poderia responder.
        if (destino.AtendenteId is null)
        {
            await conversas.AssumirAsync(destino.Id, ct);
        }
        else if (destino.AtendenteId != ctx.AtendenteId && !ctx.Pode(PermissaoDoSistema.AtendimentoMexerNoDeTerceiro))
        {
            var responsavel = await db.Atendentes.AsNoTracking()
                .Where(a => a.Id == destino.AtendenteId)
                .Select(a => a.Nome)
                .FirstOrDefaultAsync(ct) ?? "outro atendente";

            throw AtendimentoServiceException.NaoEhOResponsavel(responsavel);
        }

        var ids = request.MensagemIds;

        var originais = await db.Mensagens.AsNoTracking()
            .Where(m => ids.Contains(m.Id)
                     && m.ConversaId == conversaId
                     && m.TenantId == origem.TenantId)
            .OrderBy(m => m.EnviadaEm)
            .ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.Conteudo })
            .ToListAsync(ct);

        if (originais.Count == 0)
        {
            throw AtendimentoServiceException.MensagemNaoEncontrada();
        }

        var agora = DateTime.UtcNow;

        foreach (var original in originais)
        {
            db.Mensagens.Add(new Mensagem
            {
                TenantId = origem.TenantId,
                ConversaId = destino.Id,

                // Quem encaminha ASSINA. O texto e de outra conversa, mas quem
                // decidiu trazê-lo para cá foi este atendente, e o RF-007 vale
                // igual: a mensagem carrega quem a colocou no fio.
                AutorTipo = AutorTipo.Atendente,
                AtendenteId = ctx.AtendenteId,

                Conteudo = original.Conteudo,
                Tipo = TipoMensagem.Texto,
                EncaminhadaDeMensagemId = original.Id,
                StatusEntrega = StatusEntrega.Enviada,
                EnviadaEm = agora
            });
        }

        await db.SaveChangesAsync(ct);

        // Encaminhamento e mensagem de gente: reordena a inbox, ao contrario do
        // aviso automatico — e, pelo mesmo motivo, cancela a pergunta de
        // encerramento pendente na conversa de destino.
        await db.Conversas
            .Where(c => c.Id == destino.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.UltimaMensagemEm, agora)
                .SetProperty(c => c.PerguntaDeEncerramentoEnviadaEm, (DateTime?)null)
                .SetProperty(c => c.AtualizadoEm, agora), ct);

        log.LogInformation(
            "{Total} mensagem(ns) encaminhada(s) de {Origem} para {Destino} por {AtendenteId}",
            originais.Count, conversaId, destino.Id, ctx.AtendenteId);

        var resumo = await conversas.ObterResumoAsync(destino.Id, ct);
        await notificador.ConversaAtualizadaAsync(origem.TenantId, resumo, ct);

        return originais.Count;
    }

    public async Task<IReadOnlyList<DestinoDeEncaminhamentoResponse>> DestinosAsync(
        Guid conversaOrigemId,
        string? busca,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.AtendimentoVer);

        // Acesso tecnico escopado a UMA conversa nao tem "destino" valido —
        // qualquer resultado aqui seria, por definicao, outra conversa fora
        // do escopo aprovado. Nega de cara, nao filtra para lista vazia (que
        // pareceria bug, nao regra).
        if (ctx.EscopoDeConversaId is not null)
        {
            throw AtendimentoServiceException.SemPermissao();
        }

        var tenantId = ctx.TenantId;

        var consulta = db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                     && c.Status == StatusConversa.Ativa
                     && c.Id != conversaOrigemId);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();

            consulta = consulta.Where(c =>
                c.Protocolo.Contains(termo) ||
                db.Clientes.Any(cl => cl.Id == c.ClienteId && cl.Nome.Contains(termo)));
        }

        return await consulta
            .OrderByDescending(c => c.UltimaMensagemEm)
            .Take(30)
            .Select(c => new DestinoDeEncaminhamentoResponse(
                c.Id,
                c.Protocolo,
                db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.Nome).FirstOrDefault() ?? string.Empty,
                db.Setores.Where(s => s.Id == c.SetorId).Select(s => s.Nome).FirstOrDefault(),
                db.Atendentes.Where(a => a.Id == c.AtendenteId).Select(a => a.Nome).FirstOrDefault()))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Carrega a conversa e confere que quem pediu pode mexer nela.
    /// </summary>
    /// <remarks>
    /// Fixar e ocultar sao acoes sobre o fio, nao envio: nao assumem conversa. Quem
    /// nao e o responsavel — nem tem perfil de supervisao — nao reorganiza o
    /// atendimento alheio.
    /// </remarks>
    private async Task<Conversa> CarregarParaAcaoAsync(Guid conversaId, CancellationToken ct)
    {
        var conversa = await db.Conversas
            .FirstOrDefaultAsync(c => c.Id == conversaId && c.TenantId == ctx.TenantId, ct)
            ?? throw AtendimentoServiceException.ConversaNaoEncontrada();

        if (conversa.AtendenteId is not null
            && conversa.AtendenteId != ctx.AtendenteId
            && !ctx.Pode(PermissaoDoSistema.AtendimentoMexerNoDeTerceiro))
        {
            var responsavel = await db.Atendentes.AsNoTracking()
                .Where(a => a.Id == conversa.AtendenteId)
                .Select(a => a.Nome)
                .FirstOrDefaultAsync(ct) ?? "outro atendente";

            throw AtendimentoServiceException.NaoEhOResponsavel(responsavel);
        }

        return conversa;
    }

    /// <summary>
    /// Le a mensagem de volta pela projecao, em vez de montar o response a mao.
    /// Assim a citacao ja vem resolvida e o objeto devolvido no envio e igual ao
    /// que a listagem devolve — duas formas do mesmo dado divergiriam.
    /// </summary>
    private async Task<MensagemResponse> ObterUmaAsync(
        Guid mensagemId,
        Guid conversaId,
        CancellationToken ct)
    {
        var clienteNome = await db.Conversas.AsNoTracking()
            .Where(c => c.Id == conversaId)
            .Select(c => db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.Nome).FirstOrDefault())
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return await ProjecaoDeMensagem
            .ObterUmaAsync(db, db.Mensagens.AsNoTracking().Where(m => m.Id == mensagemId), clienteNome, ct);
    }

    private void GarantirPermissao(PermissaoDoSistema permissao)
    {
        if (!ctx.Pode(permissao))
        {
            throw AtendimentoServiceException.SemPermissao();
        }
    }

    /// <summary>Mesma trava de <see cref="ConversaService.GarantirEscopoDeConversa"/> — ver lá o porquê.</summary>
    private void GarantirEscopoDeConversa(Guid conversaId)
    {
        if (ctx.EscopoDeConversaId is { } escopo && escopo != conversaId)
        {
            throw AtendimentoServiceException.SemPermissao();
        }
    }
}
