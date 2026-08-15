using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.Servicos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

public class ConversaService(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IGeradorDeProtocolo protocolos,
    INotificadorDeAtendimento notificador,
    IDisparadorDeMensagemAutomatica automaticas,
    ILogger<ConversaService> log) : IConversaService
{
    // ---------------------------------------------------------------- leitura

    public async Task<ResultadoPaginado<ConversaResumoResponse>> ListarAsync(
        FiltroDeConversas filtro,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;

        var consulta = db.Conversas.AsNoTracking().Where(c => c.TenantId == tenantId);

        if (filtro.Status is { } status)
        {
            consulta = consulta.Where(c => c.Status == status);
        }

        if (filtro.SetorId is { } setorId)
        {
            consulta = consulta.Where(c => c.SetorId == setorId);
        }

        if (filtro.EtapaId is { } etapaId)
        {
            consulta = consulta.Where(c => c.EtapaId == etapaId);
        }

        if (filtro.TipoServicoId is { } tipoServicoId)
        {
            consulta = consulta.Where(c => c.TipoServicoId == tipoServicoId);
        }

        if (filtro.ApenasMinhas)
        {
            var eu = ctx.AtendenteId;
            consulta = consulta.Where(c => c.AtendenteId == eu);
        }
        else if (filtro.AtendenteId is { } atendenteId)
        {
            consulta = consulta.Where(c => c.AtendenteId == atendenteId);
        }

        if (filtro.ApenasNaFila)
        {
            consulta = consulta.Where(c => c.AtendenteId == null);
        }

        if (filtro.ApenasEncerradasHoje)
        {
            // Mesmo limite de dia que ResumoDoPainelAsync usa para contar, de modo
            // que o indicador "encerradas hoje" e a lista nunca discordem.
            var inicioDoDia = InicioDoDiaLocal(agora);

            consulta = consulta.Where(c => c.Status == StatusConversa.Encerrada
                                        && c.EncerradaEm >= inicioDoDia);
        }

        // Situacao de prazo vira comparacao de datas — nao subconsulta de percentual.
        // E por isso que PrazoAlertaEm e materializado na conversa.
        consulta = filtro.SituacaoPrazo switch
        {
            Domain.Enums.SituacaoPrazo.SemPrazo =>
                consulta.Where(c => c.PrazoLimite == null),

            Domain.Enums.SituacaoPrazo.Atrasada =>
                consulta.Where(c => c.PrazoLimite != null && agora > c.PrazoLimite),

            Domain.Enums.SituacaoPrazo.EmAlerta =>
                consulta.Where(c => c.PrazoLimite != null
                                 && agora <= c.PrazoLimite
                                 && c.PrazoAlertaEm != null
                                 && agora >= c.PrazoAlertaEm),

            Domain.Enums.SituacaoPrazo.EmDia =>
                consulta.Where(c => c.PrazoLimite != null
                                 && agora <= c.PrazoLimite
                                 && (c.PrazoAlertaEm == null || agora < c.PrazoAlertaEm)),

            _ => consulta
        };

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var busca = filtro.Busca.Trim();

            consulta = consulta.Where(c =>
                c.Protocolo.Contains(busca) ||
                db.Clientes.Any(cl => cl.Id == c.ClienteId &&
                    (cl.Nome.Contains(busca) || cl.TelefoneWhatsapp.Contains(busca))));
        }

        var total = await consulta.CountAsync(ct);

        var pagina = filtro.Pagina < 1 ? 1 : filtro.Pagina;
        var tamanho = Math.Clamp(filtro.Tamanho, 1, 200);

        var itens = await ProjecaoDeConversa
            .De(db, consulta.OrderByDescending(c => c.UltimaMensagemEm))
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync(ct);

        return new ResultadoPaginado<ConversaResumoResponse>(
            [.. itens.Select(p => p.ParaResposta(agora))],
            total,
            pagina,
            tamanho);
    }

    public async Task<ConversaDetalheResponse> ObterAsync(Guid id, CancellationToken ct = default)
    {
        var conversa = await ObterResumoAsync(id, ct);

        var mensagens = await ProjecaoDeMensagem
            .De(
                db,
                db.Mensagens.AsNoTracking()
                    .Where(m => m.ConversaId == id && m.TenantId == ctx.TenantId)
                    .OrderBy(m => m.EnviadaEm)
                    .ThenBy(m => m.Id),
                conversa.ClienteNome)
            .ToListAsync(ct);

        var historico = await db.HistoricoEtapas.AsNoTracking()
            .Where(h => h.ConversaId == id && h.TenantId == ctx.TenantId)
            .OrderBy(h => h.OcorridoEm)
            .Select(h => new HistoricoEtapaResponse(
                h.Id,
                h.TipoMudanca,
                db.EtapasFunil.Where(e => e.Id == h.EtapaAnteriorId).Select(e => e.Nome).FirstOrDefault(),
                db.EtapasFunil.Where(e => e.Id == h.EtapaNovaId).Select(e => e.Nome).FirstOrDefault(),
                db.Setores.Where(s => s.Id == h.SetorAnteriorId).Select(s => s.Nome).FirstOrDefault(),
                db.Setores.Where(s => s.Id == h.SetorNovoId).Select(s => s.Nome).FirstOrDefault(),
                db.Atendentes.Where(a => a.Id == h.AtendenteId).Select(a => a.Nome).FirstOrDefault(),
                h.OcorridoEm,
                h.Observacao))
            .ToListAsync(ct);

        return new ConversaDetalheResponse(conversa, mensagens, historico);
    }

    public async Task<PainelResumoResponse> ResumoDoPainelAsync(CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;
        var eu = ctx.AtendenteId;
        var agora = DateTime.UtcNow;

        var inicioDoDiaLocal = InicioDoDiaLocal(agora);

        var ativas = db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa);

        var totalAtivas = await ativas.CountAsync(ct);
        var naFila = await ativas.CountAsync(c => c.AtendenteId == null, ct);
        var minhas = await ativas.CountAsync(c => c.AtendenteId == eu, ct);

        var encerradasHoje = await db.Conversas.AsNoTracking()
            .CountAsync(c => c.TenantId == tenantId
                          && c.Status == StatusConversa.Encerrada
                          && c.EncerradaEm >= inicioDoDiaLocal, ct);

        var atrasadas = await ativas.CountAsync(
            c => c.PrazoLimite != null && agora > c.PrazoLimite, ct);

        var emAlerta = await ativas.CountAsync(
            c => c.PrazoLimite != null
              && agora <= c.PrazoLimite
              && c.PrazoAlertaEm != null
              && agora >= c.PrazoAlertaEm, ct);

        var contagemPorEtapa = await ativas
            .GroupBy(c => c.EtapaId)
            .Select(g => new { EtapaId = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        var contagemPorSetor = await ativas
            .GroupBy(c => c.SetorId)
            .Select(g => new { SetorId = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        var etapas = await db.EtapasFunil.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Ativo)
            .OrderBy(e => e.Ordem)
            .Select(e => new { e.Id, e.Nome, e.Cor })
            .ToListAsync(ct);

        var setores = await db.Setores.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Ativo)
            .OrderBy(s => s.Ordem)
            .Select(s => new { s.Id, s.Nome })
            .ToListAsync(ct);

        // Etapa/setor sem conversa alguma continua na lista com zero: o painel
        // precisa mostrar o funil inteiro, nao so as colunas que tem movimento.
        var porEtapa = etapas
            .Select(e => new ContagemPorChaveResponse(
                e.Id,
                e.Nome,
                e.Cor,
                contagemPorEtapa.FirstOrDefault(x => x.EtapaId == e.Id)?.Total ?? 0))
            .ToList();

        var semEtapa = contagemPorEtapa.FirstOrDefault(x => x.EtapaId == null)?.Total ?? 0;
        if (semEtapa > 0)
        {
            porEtapa.Add(new ContagemPorChaveResponse(null, "Sem etapa", "#94A3B8", semEtapa));
        }

        var porSetor = setores
            .Select(s => new ContagemPorChaveResponse(
                s.Id,
                s.Nome,
                null,
                contagemPorSetor.FirstOrDefault(x => x.SetorId == s.Id)?.Total ?? 0))
            .ToList();

        var semSetor = contagemPorSetor.FirstOrDefault(x => x.SetorId == null)?.Total ?? 0;
        if (semSetor > 0)
        {
            porSetor.Add(new ContagemPorChaveResponse(null, "Sem setor", null, semSetor));
        }

        return new PainelResumoResponse(
            totalAtivas, naFila, minhas, encerradasHoje, emAlerta, atrasadas, porEtapa, porSetor);
    }

    // ----------------------------------------------------------------- escrita

    public async Task<ConversaResumoResponse> CriarAsync(
        CriarConversaRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;

        var cliente = await db.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ClienteId && c.TenantId == tenantId, ct)
            ?? throw AtendimentoServiceException.ClienteNaoEncontrado();

        var conversaAberta = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                     && c.ClienteId == cliente.Id
                     && c.Status == StatusConversa.Ativa)
            .Select(c => c.Protocolo)
            .FirstOrDefaultAsync(ct);

        if (conversaAberta is not null)
        {
            throw AtendimentoServiceException.ClienteJaTemConversaAtiva(conversaAberta);
        }

        if (request.SetorId is { } setorId && !await SetorExisteAsync(setorId, ct))
        {
            throw AtendimentoServiceException.SetorNaoEncontrado();
        }

        // Sem etapa informada, a conversa entra na primeira do funil configurado.
        var etapaId = request.EtapaId;
        if (etapaId is { } informada)
        {
            if (!await EtapaExisteAsync(informada, ct))
            {
                throw AtendimentoServiceException.EtapaNaoEncontrada();
            }
        }
        else
        {
            etapaId = await db.EtapasFunil.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.Ativo)
                .OrderBy(e => e.Ordem)
                .Select(e => (Guid?)e.Id)
                .FirstOrDefaultAsync(ct);
        }

        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        var protocolo = await protocolos.GerarAsync(tenantId, agora, ct);

        var conversa = new Conversa
        {
            TenantId = tenantId,
            ClienteId = cliente.Id,
            Protocolo = protocolo,
            SetorId = request.SetorId,
            EtapaId = etapaId,
            Status = StatusConversa.Ativa,
            Canal = CanalOrigem.Simulado,
            IniciadaEm = agora,
            UltimaMensagemEm = agora
        };

        db.Conversas.Add(conversa);

        db.HistoricoEtapas.Add(new HistoricoEtapa
        {
            TenantId = tenantId,
            ConversaId = conversa.Id,
            TipoMudanca = TipoMudancaHistorico.Abertura,
            EtapaNovaId = etapaId,
            SetorNovoId = request.SetorId,
            AtendenteId = ctx.AtendenteId,
            OcorridoEm = agora,
            Observacao = $"Atendimento aberto com o protocolo {protocolo}."
        });

        if (!string.IsNullOrWhiteSpace(request.PrimeiraMensagem))
        {
            db.Mensagens.Add(new Mensagem
            {
                TenantId = tenantId,
                ConversaId = conversa.Id,
                AutorTipo = AutorTipo.Cliente,
                Conteudo = request.PrimeiraMensagem.Trim(),
                StatusEntrega = StatusEntrega.Entregue,
                EnviadaEm = agora
            });

            conversa.MensagensNaoLidas = 1;
        }

        // Boas-vindas e triagem so fazem sentido quando NAO se sabe o setor. Se o
        // atendente ja escolheu para onde vai o atendimento, mandar o menu
        // "1 - Notas / 2 - Registro Civil" seria perguntar ao cliente algo que a
        // serventia acabou de decidir.
        if (request.SetorId is null)
        {
            var boasVindas = await automaticas.EnfileirarAsync(
                conversa, GatilhoDeMensagem.NovoAtendimento, ct: ct);

            // A flag depende de o menu ter REALMENTE saido. Sem regra cadastrada nao
            // ha menu, e esperar resposta de uma pergunta que ninguem fez faria a
            // proxima mensagem do cliente receber um fallback vindo do nada.
            conversa.AguardandoTriagem = boasVindas.Count > 0;
        }

        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        var resumo = await ObterResumoAsync(conversa.Id, ct);
        await notificador.ConversaAtualizadaAsync(tenantId, resumo, ct);

        return resumo;
    }

    /// <summary>
    /// Trava de conversa. A garantia real nao esta em ler-e-depois-gravar: esta no
    /// UPDATE condicional abaixo, que so afeta linha ainda sem responsavel. Se o
    /// UPDATE afetar zero linhas, outro atendente ganhou a corrida.
    /// </summary>
    public async Task<ConversaResumoResponse> AssumirAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;
        var atendenteId = ctx.AtendenteId;
        var agora = DateTime.UtcNow;

        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        var afetadas = await db.Conversas
            .Where(c => c.Id == id
                     && c.TenantId == tenantId
                     && c.AtendenteId == null
                     && c.Status == StatusConversa.Ativa)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.AtendenteId, atendenteId)
                .SetProperty(c => c.AtualizadoEm, agora), ct);

        if (afetadas == 0)
        {
            await transacao.RollbackAsync(ct);
            return await ResolverFalhaAoAssumirAsync(id, ct);
        }

        db.Mensagens.Add(MensagemDeSistema(id, tenantId, $"Conversa assumida por {ctx.Nome}.", agora));

        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        // ExecuteUpdate escreve direto no banco, sem passar pelo change tracker. Se
        // esta conversa ja estiver rastreada neste escopo, ela continua em memoria
        // com AtendenteId nulo — e a proxima operacao no MESMO DbContext a trataria
        // como se ainda estivesse na fila ("assuma a conversa antes de executar esta
        // acao", logo depois de assumir).
        //
        // Na API cada requisicao tem escopo proprio e isso nao aparece. Recarregar
        // aqui e o que torna o servico seguro para encadear — que e como ele e usado
        // fora do pipeline HTTP, nos verificadores e em qualquer rotina futura.
        var rastreada = db.ChangeTracker.Entries<Conversa>()
            .FirstOrDefault(e => e.Entity.Id == id);

        if (rastreada is not null)
        {
            await rastreada.ReloadAsync(ct);
        }

        log.LogInformation(
            "Conversa {ConversaId} assumida por {AtendenteId} ({Nome})", id, atendenteId, ctx.Nome);

        var resumo = await ObterResumoAsync(id, ct);
        await notificador.ConversaAtribuidaAsync(tenantId, resumo, ct);

        return resumo;
    }

    public async Task<ConversaResumoResponse> LiberarAsync(Guid id, CancellationToken ct = default)
    {
        var conversa = await CarregarAsync(id, ct);
        GarantirAtiva(conversa);
        await GarantirQuePodeOperarAsync(conversa, ct);

        var agora = DateTime.UtcNow;

        conversa.AtendenteId = null;

        db.Mensagens.Add(MensagemDeSistema(
            id, conversa.TenantId, $"Conversa devolvida para a fila por {ctx.Nome}.", agora));

        await db.SaveChangesAsync(ct);

        var resumo = await ObterResumoAsync(id, ct);
        await notificador.ConversaLiberadaAsync(conversa.TenantId, resumo, ct);

        return resumo;
    }

    public async Task<ConversaResumoResponse> TransferirAsync(
        Guid id,
        TransferirConversaRequest request,
        CancellationToken ct = default)
    {
        var conversa = await CarregarAsync(id, ct);
        GarantirAtiva(conversa);
        await GarantirQuePodeOperarAsync(conversa, ct);

        var destino = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AtendenteDestinoId
                                   && a.TenantId == conversa.TenantId
                                   && a.Ativo, ct)
            ?? throw AtendimentoServiceException.AtendenteNaoEncontrado();

        var agora = DateTime.UtcNow;

        conversa.AtendenteId = destino.Id;

        var texto = string.IsNullOrWhiteSpace(request.Observacao)
            ? $"Conversa transferida por {ctx.Nome} para {destino.Nome}."
            : $"Conversa transferida por {ctx.Nome} para {destino.Nome}. {request.Observacao.Trim()}";

        db.Mensagens.Add(MensagemDeSistema(id, conversa.TenantId, texto, agora));

        await db.SaveChangesAsync(ct);

        var resumo = await ObterResumoAsync(id, ct);
        await notificador.ConversaTransferidaAsync(conversa.TenantId, resumo, ct);

        return resumo;
    }

    public async Task<ConversaResumoResponse> MoverAsync(
        Guid id,
        MoverConversaRequest request,
        CancellationToken ct = default)
    {
        if (request.EtapaId is null && request.SetorId is null)
        {
            throw AtendimentoServiceException.NadaParaMover();
        }

        var conversa = await CarregarAsync(id, ct);
        GarantirAtiva(conversa);
        await GarantirQuePodeOperarAsync(conversa, ct);

        var agora = DateTime.UtcNow;
        var etapaAnterior = conversa.EtapaId;
        var setorAnterior = conversa.SetorId;

        string? nomeEtapa = null;
        string? nomeSetor = null;

        if (request.EtapaId is { } etapaId && etapaId != conversa.EtapaId)
        {
            nomeEtapa = await db.EtapasFunil.AsNoTracking()
                .Where(e => e.Id == etapaId && e.TenantId == conversa.TenantId)
                .Select(e => e.Nome)
                .FirstOrDefaultAsync(ct)
                ?? throw AtendimentoServiceException.EtapaNaoEncontrada();

            conversa.EtapaId = etapaId;
        }

        if (request.SetorId is { } setorId && setorId != conversa.SetorId)
        {
            nomeSetor = await db.Setores.AsNoTracking()
                .Where(s => s.Id == setorId && s.TenantId == conversa.TenantId)
                .Select(s => s.Nome)
                .FirstOrDefaultAsync(ct)
                ?? throw AtendimentoServiceException.SetorNaoEncontrado();

            conversa.SetorId = setorId;
        }

        var mudouEtapa = conversa.EtapaId != etapaAnterior;
        var mudouSetor = conversa.SetorId != setorAnterior;

        if (!mudouEtapa && !mudouSetor)
        {
            // Reenvio do mesmo destino: nao suja o historico com registro vazio.
            return await ObterResumoAsync(id, ct);
        }

        var tipo = (mudouEtapa, mudouSetor) switch
        {
            (true, true) => TipoMudancaHistorico.EtapaESetor,
            (true, false) => TipoMudancaHistorico.Etapa,
            _ => TipoMudancaHistorico.Setor
        };

        db.HistoricoEtapas.Add(new HistoricoEtapa
        {
            TenantId = conversa.TenantId,
            ConversaId = conversa.Id,
            TipoMudanca = tipo,
            EtapaAnteriorId = mudouEtapa ? etapaAnterior : null,
            EtapaNovaId = mudouEtapa ? conversa.EtapaId : null,
            SetorAnteriorId = mudouSetor ? setorAnterior : null,
            SetorNovoId = mudouSetor ? conversa.SetorId : null,
            AtendenteId = ctx.AtendenteId,
            OcorridoEm = agora,
            Observacao = request.Observacao?.Trim()
        });

        var partes = new List<string>(2);
        if (mudouEtapa)
        {
            partes.Add($"etapa alterada para {nomeEtapa}");
        }

        if (mudouSetor)
        {
            partes.Add($"encaminhada ao setor {nomeSetor}");
        }

        db.Mensagens.Add(MensagemDeSistema(
            id,
            conversa.TenantId,
            $"Conversa {string.Join(" e ", partes)} por {ctx.Nome}.",
            agora));

        // Aviso ao cliente quando a etapa muda — tipicamente o "seu ato esta pronto"
        // da etapa final. Quais etapas avisam e QUE texto sai e cadastro: nao ha
        // nome de etapa escrito em codigo aqui, so o Id da etapa que recebeu a
        // conversa. Etapa sem regra cadastrada nao dispara nada.
        if (mudouEtapa)
        {
            await automaticas.EnfileirarAsync(
                conversa, GatilhoDeMensagem.EtapaAlterada, conversa.EtapaId, ct);
        }

        await db.SaveChangesAsync(ct);

        var resumo = await ObterResumoAsync(id, ct);
        await notificador.ConversaAtualizadaAsync(conversa.TenantId, resumo, ct);

        return resumo;
    }

    public async Task<ConversaResumoResponse> VincularServicoAsync(
        Guid id,
        VincularServicoRequest request,
        CancellationToken ct = default)
    {
        var conversa = await CarregarAsync(id, ct);
        GarantirAtiva(conversa);
        await GarantirQuePodeOperarAsync(conversa, ct);

        var servico = await db.TiposServico.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TipoServicoId
                                   && t.TenantId == conversa.TenantId
                                   && t.Ativo, ct)
            ?? throw AtendimentoServiceException.TipoServicoNaoEncontrado();

        var prazo = await db.PrazosServico.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TipoServicoId == servico.Id && p.Ativo, ct);

        var agora = DateTime.UtcNow;

        conversa.TipoServicoId = servico.Id;

        if (prazo is not null)
        {
            // O contador comeca agora, no momento da captacao — nao na abertura da
            // conversa. A triagem nao deve consumir o SLA do ato.
            conversa.PrazoIniciadoEm = agora;
            conversa.PrazoLimite = CalculadoraDePrazo.CalcularLimite(agora, prazo);

            var duracao = conversa.PrazoLimite.Value - agora;
            conversa.PrazoAlertaEm = agora.AddMinutes(duracao.TotalMinutes * prazo.PercentualAlerta / 100d);
        }
        else
        {
            conversa.PrazoIniciadoEm = null;
            conversa.PrazoLimite = null;
            conversa.PrazoAlertaEm = null;
        }

        var texto = prazo is null
            ? $"Servico definido como {servico.Nome} por {ctx.Nome}. Sem prazo configurado."
            : $"Servico definido como {servico.Nome} por {ctx.Nome}. Prazo ate {Formatar(conversa.PrazoLimite!.Value)}.";

        db.Mensagens.Add(MensagemDeSistema(id, conversa.TenantId, texto, agora));

        await db.SaveChangesAsync(ct);

        var resumo = await ObterResumoAsync(id, ct);
        await notificador.ConversaAtualizadaAsync(conversa.TenantId, resumo, ct);

        return resumo;
    }

    public async Task<ConversaResumoResponse> EncerrarAsync(
        Guid id,
        EncerrarConversaRequest request,
        CancellationToken ct = default)
    {
        var conversa = await CarregarAsync(id, ct);
        GarantirAtiva(conversa);
        await GarantirQuePodeOperarAsync(conversa, ct);

        var agora = DateTime.UtcNow;

        // Encerrar arquiva. Nao existe DELETE em nenhum caminho: exigencia do
        // cliente (RF-015) e postura correta em cartorio.
        conversa.Status = StatusConversa.Encerrada;
        conversa.EncerradaEm = agora;
        conversa.EncerradaPorAtendenteId = ctx.AtendenteId;
        conversa.MotivoEncerramento = string.IsNullOrWhiteSpace(request.Motivo)
            ? null
            : request.Motivo.Trim();
        conversa.MensagensNaoLidas = 0;

        var texto = conversa.MotivoEncerramento is null
            ? $"Atendimento encerrado por {ctx.Nome}."
            : $"Atendimento encerrado por {ctx.Nome}. Motivo: {conversa.MotivoEncerramento}";

        db.Mensagens.Add(MensagemDeSistema(id, conversa.TenantId, texto, agora));

        await db.SaveChangesAsync(ct);

        log.LogInformation("Conversa {ConversaId} encerrada por {AtendenteId}", id, ctx.AtendenteId);

        var resumo = await ObterResumoAsync(id, ct);
        await notificador.ConversaEncerradaAsync(conversa.TenantId, resumo, ct);

        return resumo;
    }

    public async Task<ConversaResumoResponse> ReabrirAsync(Guid id, CancellationToken ct = default)
    {
        var conversa = await CarregarAsync(id, ct);

        if (conversa.Status == StatusConversa.Ativa)
        {
            throw AtendimentoServiceException.ConversaJaAtiva();
        }

        var agora = DateTime.UtcNow;

        conversa.Status = StatusConversa.Ativa;
        conversa.ReabertaEm = agora;
        conversa.ReabertaPorAtendenteId = ctx.AtendenteId;
        conversa.EncerradaEm = null;
        conversa.EncerradaPorAtendenteId = null;
        conversa.MotivoEncerramento = null;

        // Reabertura devolve a conversa a quem reabriu: deixar sem dono faria a
        // conversa reaparecer na fila sem ninguem responsavel por ela.
        conversa.AtendenteId = ctx.AtendenteId;

        db.Mensagens.Add(MensagemDeSistema(
            id, conversa.TenantId, $"Atendimento reaberto por {ctx.Nome}.", agora));

        await db.SaveChangesAsync(ct);

        var resumo = await ObterResumoAsync(id, ct);
        await notificador.ConversaReabertaAsync(conversa.TenantId, resumo, ct);

        return resumo;
    }

    public async Task<ConversaResumoResponse> MarcarComoLidaAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;

        var existe = await db.Conversas.AsNoTracking()
            .AnyAsync(c => c.Id == id && c.TenantId == tenantId, ct);

        if (!existe)
        {
            throw AtendimentoServiceException.ConversaNaoEncontrada();
        }

        await db.Mensagens
            .Where(m => m.ConversaId == id
                     && m.TenantId == tenantId
                     && m.AutorTipo == AutorTipo.Cliente
                     && m.LidaEm == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.LidaEm, agora)
                .SetProperty(m => m.AtualizadoEm, agora), ct);

        await db.Conversas
            .Where(c => c.Id == id && c.TenantId == tenantId && c.MensagensNaoLidas != 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.MensagensNaoLidas, 0)
                .SetProperty(c => c.AtualizadoEm, agora), ct);

        return await ObterResumoAsync(id, ct);
    }

    // -------------------------------------------------------------- auxiliares

    public async Task<ConversaResumoResponse> ObterResumoAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        var projecao = await ProjecaoDeConversa
            .De(db, db.Conversas.AsNoTracking().Where(c => c.Id == id && c.TenantId == tenantId))
            .FirstOrDefaultAsync(ct)
            ?? throw AtendimentoServiceException.ConversaNaoEncontrada();

        return projecao.ParaResposta(DateTime.UtcNow);
    }

    private async Task<Conversa> CarregarAsync(Guid id, CancellationToken ct) =>
        await db.Conversas.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == ctx.TenantId, ct)
        ?? throw AtendimentoServiceException.ConversaNaoEncontrada();

    private static void GarantirAtiva(Conversa conversa)
    {
        if (conversa.Status != StatusConversa.Ativa)
        {
            throw AtendimentoServiceException.ConversaEncerrada();
        }
    }

    /// <summary>
    /// Só opera a conversa quem e o responsavel — ou quem tem perfil de supervisao.
    /// E o que impede o segundo atendente de responder por cima do primeiro.
    /// </summary>
    private async Task GarantirQuePodeOperarAsync(Conversa conversa, CancellationToken ct)
    {
        if (conversa.AtendenteId is null)
        {
            throw AtendimentoServiceException.ConversaSemResponsavel();
        }

        if (conversa.AtendenteId == ctx.AtendenteId || ctx.PodeOperarConversaDeTerceiro)
        {
            return;
        }

        var responsavel = await db.Atendentes.AsNoTracking()
            .Where(a => a.Id == conversa.AtendenteId)
            .Select(a => a.Nome)
            .FirstOrDefaultAsync(ct) ?? "outro atendente";

        throw AtendimentoServiceException.NaoEhOResponsavel(responsavel);
    }

    private async Task<ConversaResumoResponse> ResolverFalhaAoAssumirAsync(Guid id, CancellationToken ct)
    {
        var atual = await db.Conversas.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == ctx.TenantId, ct)
            ?? throw AtendimentoServiceException.ConversaNaoEncontrada();

        if (atual.Status != StatusConversa.Ativa)
        {
            throw AtendimentoServiceException.ConversaEncerrada();
        }

        // Ja e minha: dois cliques no mesmo botao nao viram erro na cara do usuario.
        if (atual.AtendenteId == ctx.AtendenteId)
        {
            return await ObterResumoAsync(id, ct);
        }

        var nome = await db.Atendentes.AsNoTracking()
            .Where(a => a.Id == atual.AtendenteId)
            .Select(a => a.Nome)
            .FirstOrDefaultAsync(ct) ?? "outro atendente";

        log.LogInformation(
            "Conflito ao assumir a conversa {ConversaId}: {Solicitante} perdeu para {Responsavel}",
            id, ctx.Nome, nome);

        throw new ConflitoDeAtribuicaoException(id, atual.AtendenteId!.Value, nome);
    }

    private static Mensagem MensagemDeSistema(Guid conversaId, Guid tenantId, string texto, DateTime agora) =>
        new()
        {
            TenantId = tenantId,
            ConversaId = conversaId,
            AutorTipo = AutorTipo.Sistema,
            Conteudo = texto,
            Tipo = TipoMensagem.Texto,
            StatusEntrega = StatusEntrega.Entregue,
            EnviadaEm = agora,
            LidaEm = agora
        };

    private Task<bool> SetorExisteAsync(Guid id, CancellationToken ct) =>
        db.Setores.AsNoTracking().AnyAsync(s => s.Id == id && s.TenantId == ctx.TenantId, ct);

    private Task<bool> EtapaExisteAsync(Guid id, CancellationToken ct) =>
        db.EtapasFunil.AsNoTracking().AnyAsync(e => e.Id == id && e.TenantId == ctx.TenantId, ct);

    private static string Formatar(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local).ToString("dd/MM/yyyy HH:mm");

    /// <summary>
    /// Meia-noite de hoje no fuso da serventia, devolvida em UTC.
    /// </summary>
    /// <remarks>
    /// "Hoje" e o dia do cartorio, nao o dia UTC — as duas coisas divergem entre
    /// 21h e meia-noite no horario de Brasilia. Fica num metodo so porque o
    /// contador do painel e o filtro da lista PRECISAM usar o mesmo limite; se
    /// cada um calculasse o seu, o numero no indicador nao bateria com a lista
    /// que ele abre.
    /// </remarks>
    private static DateTime InicioDoDiaLocal(DateTime agoraUtc) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(agoraUtc, TimeZoneInfo.Local).Date,
                DateTimeKind.Unspecified),
            TimeZoneInfo.Local);
}
