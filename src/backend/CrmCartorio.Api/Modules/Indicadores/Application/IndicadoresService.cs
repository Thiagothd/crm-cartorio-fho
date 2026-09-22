using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using CrmCartorio.Api.Modules.Clientes.Domain.Enums;
using CrmCartorio.Api.Modules.RespostaInteligente.Application;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CrmCartorio.Api.Modules.Indicadores.Application;

/// <summary>
/// Só consulta — nenhuma escrita, nenhum schema novo. Lê pelo <see cref="CrmDbContext"/>
/// diretamente em vez de chamar o service de outro módulo (regra de arquitetura:
/// ler entidade de outro slice pelo DbContext é permitido; chamar o Service dele não).
/// </summary>
/// <remarks>
/// Cada <c>await</c> contra o banco é uma ida e volta em série, e a Visão Geral
/// monta dez peças de uma vez — por isso as consultas aqui são deliberadamente
/// agrupadas: quando duas peças respondem sobre o mesmo conjunto de linhas, elas
/// dividem a mesma leitura em vez de varrer a tabela duas vezes.
/// </remarks>
public class IndicadoresService(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IPareamentoDoCanal pareamento,
    ISituacaoDaIa situacaoDaIa,
    IMemoryCache cache) : IIndicadoresService
{
    /// <summary>Quanto a Visão Geral e o sino esperam pelo gateway antes de desistir.</summary>
    /// <remarks>
    /// O timeout de 20s do módulo do canal é o certo para a tela de Integrações, que
    /// não tem outra informação para mostrar. Aqui é o contrário: o selo de saúde é
    /// uma peça entre dez. Com o gateway fora do ar a Visão Geral levava mais de 4s
    /// para entregar dado que o banco tinha respondido em 20ms — a página parecia
    /// lenta por causa de um selo.
    /// </remarks>
    private static readonly TimeSpan EsperaMaximaPeloGateway = TimeSpan.FromSeconds(1.5);

    /// <summary>Por quanto tempo a situação do canal é reaproveitada entre requisições.</summary>
    /// <remarks>
    /// Mesma janela do <c>staleTime</c> padrão do frontend (15s). A Visão Geral e o
    /// sino consultam o gateway a cada carga de página, e sem isto cada aba aberta
    /// paga a consulta — ou o timeout — outra vez. A tela de Integrações NÃO passa
    /// por aqui: ela continua lendo o gateway ao vivo, que é o que o pareamento e o
    /// QR exigem.
    /// </remarks>
    private static readonly TimeSpan ValidadeDaSituacaoDoCanal = TimeSpan.FromSeconds(15);

    // ------------------------------------------------------------- Visão Geral

    public async Task<VisaoGeralResponse> ObterVisaoGeralAsync(
        FiltroDeIndicadores filtro,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;
        var eu = ctx.AtendenteId;
        var agora = DateTime.UtcNow;
        var apenasMinhas = string.Equals(filtro.Escopo, "minhas", StringComparison.OrdinalIgnoreCase);
        var escopoAtendenteId = apenasMinhas ? eu : (Guid?)null;

        var (de, ate, granularidade) = PeriodoDeAnalise.Resolver(filtro.De, filtro.Ate, agora);

        var agoraResponse = await MontarAgoraAsync(tenantId, escopoAtendenteId, agora, ct);

        // Duas leituras do período alimentam quatro peças: total de abertos e
        // encerrados, tempo de primeira resposta, o gráfico de fluxo e o ranking de
        // serviços. Antes cada peça ia ao banco por conta própria e varria a mesma
        // faixa de datas — sete idas para responder sobre as mesmas linhas.
        var aberturas = await LerAberturasDoPeriodoAsync(tenantId, de, ate, escopoAtendenteId, ct);
        var encerramentos = await LerEncerramentosDoPeriodoAsync(tenantId, de, ate, escopoAtendenteId, ct);
        var emAbertoNoInicio = await ContarEmAbertoAntesAsync(tenantId, de, escopoAtendenteId, ct);

        var noPeriodoResponse = MontarNoPeriodo(aberturas, encerramentos);
        var fluxo = MontarFluxo(aberturas, encerramentos, emAbertoNoInicio, de, ate, granularidade);
        var requerAtencao = await MontarRequerAtencaoAsync(tenantId, escopoAtendenteId, agora, agoraResponse, ct);
        var servicos = await MontarServicosMaisProcuradosAsync(aberturas, ct);
        var (porEtapa, minhaDistribuicao) = await MontarDistribuicoesPorEtapaAsync(tenantId, eu, ct);
        var saude = await MontarSaudeAsync(tenantId, ct);

        // Só para gestão, e por isso a consulta também só acontece para gestão:
        // atendente comum nem paga a ida ao banco de um painel que não vai ver.
        var carga = ctx.Pode(PermissaoDoSistema.RelatoriosEquipe)
            ? await MontarCargaDaEquipeAsync(tenantId, ct)
            : null;

        return new VisaoGeralResponse(
            new PeriodoResponse(de, ate, granularidade),
            agoraResponse,
            noPeriodoResponse,
            fluxo,
            requerAtencao,
            carga,
            servicos,
            porEtapa,
            saude,
            minhaDistribuicao);
    }

    /// <summary>
    /// Conversa aberta dentro do período, com as três colunas que a Visão Geral
    /// consome — projeção explícita para não trazer a entidade inteira do banco.
    /// </summary>
    private sealed record AberturaNoPeriodo(
        DateTime IniciadaEm,
        Guid? TipoServicoId,
        DateTime? PrimeiraRespostaEm);

    private async Task<IndicadoresAgoraResponse> MontarAgoraAsync(
        Guid tenantId, Guid? apenasAtendenteId, DateTime agora, CancellationToken ct)
    {
        var ativas = db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa);

        // Uma varredura das conversas ativas responde as cinco contagens. O escopo
        // "minhas" entra como predicado de cada contagem, e nao como filtro da
        // consulta, porque a fila e' sempre global: nao existe "minha fila" — fila
        // e', por definicao, o que ainda nao tem responsavel.
        //
        // A bandeira de "aguardando resposta" sai na PROJECAO, antes do agrupamento,
        // e nao dentro de `g.Count(...)`: subconsulta correlacionada no argumento de
        // um agregado e' o padrao que o tradutor do EF mais recusa. Aqui ela vira um
        // OUTER APPLY na tabela derivada e o GroupBy agrega uma coluna comum.
        //
        // O corpo da bandeira e' o mesmo de AguardandoRespostaDoCartorio.Expressao —
        // a unica copia dele no sistema, e ela existe porque `g.Count()` recebe
        // Func<>, nao Expression<>, e nao ha como injetar a expressao compartilhada
        // dentro de uma arvore sem LINQKit. Mexeu la, mexe aqui: o verificador de
        // traducao checa as duas formas lado a lado.
        var contagens = await ativas
            .Select(c => new
            {
                c.AtendenteId,
                c.PrazoLimite,
                c.PrazoAlertaEm,
                AguardandoResposta = db.Mensagens
                    .Where(m => m.ConversaId == c.Id
                             && (m.AutorTipo != AutorTipo.Sistema || m.EhAutomatica))
                    .OrderByDescending(m => m.EnviadaEm)
                    .ThenByDescending(m => m.Id)
                    .Select(m => m.AutorTipo)
                    .FirstOrDefault() == AutorTipo.Cliente
            })
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Ativas = g.Count(c => apenasAtendenteId == null || c.AtendenteId == apenasAtendenteId),

                NaFila = g.Count(c => c.AtendenteId == null),

                AguardandoResposta = g.Count(c =>
                    (apenasAtendenteId == null || c.AtendenteId == apenasAtendenteId)
                    && c.AguardandoResposta),

                EmAlerta = g.Count(c =>
                    (apenasAtendenteId == null || c.AtendenteId == apenasAtendenteId)
                    && c.PrazoLimite != null
                    && agora <= c.PrazoLimite
                    && c.PrazoAlertaEm != null
                    && agora >= c.PrazoAlertaEm),

                Atrasadas = g.Count(c =>
                    (apenasAtendenteId == null || c.AtendenteId == apenasAtendenteId)
                    && c.PrazoLimite != null
                    && agora > c.PrazoLimite)
            })
            .FirstOrDefaultAsync(ct);

        // Serventia sem nenhuma conversa ativa nao devolve linha de agrupamento —
        // e' zero legitimo, nao ausencia de dado.
        return contagens is null
            ? new IndicadoresAgoraResponse(0, 0, 0, 0, 0)
            : new IndicadoresAgoraResponse(
                contagens.Ativas, contagens.NaFila, contagens.EmAlerta,
                contagens.Atrasadas, contagens.AguardandoResposta);
    }

    /// <summary>
    /// As conversas abertas no período, uma vez só. A subconsulta da primeira
    /// resposta viaja junto porque incide exatamente sobre este mesmo conjunto —
    /// separá-la significaria filtrar as mesmas linhas pela segunda vez.
    /// </summary>
    /// <remarks>
    /// Primeira resposta HUMANA — nunca automática, nunca IA. As três CHECK
    /// constraints de Mensagens garantem que <c>AutorTipo = Atendente</c> exige
    /// <c>AtendenteId</c> preenchido e nunca coincide com <c>EhAutomatica</c>, então
    /// esta consulta não corre risco de contar resposta de regra ou de IA como
    /// resposta de gente.
    /// </remarks>
    private async Task<List<AberturaNoPeriodo>> LerAberturasDoPeriodoAsync(
        Guid tenantId, DateTime de, DateTime ate, Guid? apenasAtendenteId, CancellationToken ct)
    {
        var abertos = db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate);

        if (apenasAtendenteId is { } atendenteId)
        {
            abertos = abertos.Where(c => c.AtendenteId == atendenteId);
        }

        // Subconsulta correlacionada, nao navegacao — mesma disciplina de
        // ProjecaoDeConversa. Traduz para APPLY no SQL Server.
        return await abertos
            .Select(c => new AberturaNoPeriodo(
                c.IniciadaEm,
                c.TipoServicoId,
                db.Mensagens
                    .Where(m => m.ConversaId == c.Id && m.AutorTipo == AutorTipo.Atendente)
                    .OrderBy(m => m.EnviadaEm)
                    .Select(m => (DateTime?)m.EnviadaEm)
                    .FirstOrDefault()))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Os instantes de encerramento do período. Só a coluna da data sai do banco —
    /// é tudo que o total e o gráfico de fluxo precisam.
    /// </summary>
    private async Task<List<DateTime>> LerEncerramentosDoPeriodoAsync(
        Guid tenantId, DateTime de, DateTime ate, Guid? apenasAtendenteId, CancellationToken ct)
    {
        var encerrados = db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                     && c.Status == StatusConversa.Encerrada
                     && c.EncerradaEm != null
                     && c.EncerradaEm >= de
                     && c.EncerradaEm < ate);

        if (apenasAtendenteId is { } atendenteId)
        {
            encerrados = encerrados.Where(c => c.AtendenteId == atendenteId);
        }

        return await encerrados.Select(c => c.EncerradaEm!.Value).ToListAsync(ct);
    }

    private static IndicadoresDoPeriodoResponse MontarNoPeriodo(
        IReadOnlyList<AberturaNoPeriodo> aberturas,
        IReadOnlyList<DateTime> encerramentos)
    {
        var minutos = aberturas
            .Where(a => a.PrimeiraRespostaEm != null)
            .Select(a => (a.PrimeiraRespostaEm!.Value - a.IniciadaEm).TotalMinutes)
            .Where(m => m >= 0)
            .OrderBy(m => m)
            .ToList();

        return minutos.Count == 0
            ? new IndicadoresDoPeriodoResponse(aberturas.Count, encerramentos.Count, null, null, 0)
            : new IndicadoresDoPeriodoResponse(
                aberturas.Count,
                encerramentos.Count,
                Math.Round(Mediana(minutos), 1),
                Math.Round(minutos.Average(), 1),
                minutos.Count);
    }

    /// <summary>
    /// A série do gráfico principal: volume que entrou, volume que foi concluído
    /// e o tempo até a primeira resposta humana — os três no mesmo eixo de tempo.
    /// </summary>
    /// <remarks>
    /// A mediana de primeira resposta sai DE GRAÇA aqui: as aberturas já vêm com
    /// <c>PrimeiraRespostaEm</c> de <see cref="LerAberturasDoPeriodoAsync"/>, então
    /// é só agrupar o que está na mão. Zero consulta nova.
    ///
    /// Ela é atribuída ao balde em que a conversa ABRIU, não ao balde em que a
    /// resposta aconteceu — a pergunta é "o que entrou naquele dia esperou
    /// quanto?", e datar pela resposta jogaria a demora de uma conversa de sexta
    /// para a segunda, exatamente escondendo o atraso que se quer ver.
    ///
    /// Balde sem nenhuma conversa respondida devolve <c>null</c>, nunca zero:
    /// zero significaria "respondemos instantaneamente", que é o oposto de "não
    /// há o que medir". Quem desenha a linha trata o nulo como interrupção.
    /// </remarks>
    /// <summary>
    /// Quantas conversas já estavam abertas quando o período começou — o ponto de
    /// partida do estoque, sem o qual a curva de "em aberto" começaria do zero e
    /// mentiria sobre o tamanho da fila.
    /// </summary>
    /// <remarks>
    /// Uma contagem escalar, não uma série: com este número, o estoque de cada
    /// balde sai por acumulação do fluxo que já está em memória
    /// (<c>anterior + abertos − encerrados</c>). A alternativa seria contar
    /// conversas abertas em cada fronteira de dia, ou seja uma consulta por dia
    /// do período.
    ///
    /// A identidade fecha: <c>inicial + Σabertos − Σencerrados</c> no último
    /// balde é exatamente o total de ativas de hoje, porque quem abriu antes de
    /// <paramref name="de"/> e encerrou dentro da janela entra no inicial e sai
    /// nos encerramentos.
    ///
    /// LIMITE CONHECIDO: reabrir uma conversa zera <c>EncerradaEm</c>
    /// (<c>ConversaService.ReabrirAsync</c>), então uma conversa encerrada no dia
    /// 10 e reaberta no dia 15 conta como nunca encerrada em toda a curva. A
    /// curva reflete o estado ATUAL de cada registro, não uma trilha de
    /// auditoria — o schema não guarda o histórico de encerramentos, e inventar
    /// um aqui seria pior que a imprecisão.
    /// </remarks>
    private async Task<int> ContarEmAbertoAntesAsync(
        Guid tenantId, DateTime de, Guid? apenasAtendenteId, CancellationToken ct)
    {
        var query = db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                     && c.IniciadaEm < de
                     && (c.EncerradaEm == null || c.EncerradaEm >= de));

        if (apenasAtendenteId is { } atendenteId)
        {
            query = query.Where(c => c.AtendenteId == atendenteId);
        }

        return await query.CountAsync(ct);
    }

    private static IReadOnlyList<PontoDoFluxoResponse> MontarFluxo(
        IReadOnlyList<AberturaNoPeriodo> aberturas,
        IReadOnlyList<DateTime> encerramentos,
        int emAbertoNoInicio,
        DateTime de,
        DateTime ate,
        string granularidade)
    {
        // Bucket em memoria, no fuso local — agrupar por data em SQL daria o
        // balde em UTC, e o "dia do cartorio" ficaria errado entre 21h e meia
        // noite no horario de Brasilia. Volume de cartorio torna isso barato.
        DateTime Chave(DateTime instanteUtc)
        {
            var local = DiaLocal.ParaLocal(instanteUtc);
            return granularidade == "hora"
                ? new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified)
                : local.Date;
        }

        var baldes = new Dictionary<DateTime, (int Abertos, int Encerrados)>();
        var esperasPorBalde = new Dictionary<DateTime, List<double>>();

        void Incrementar(DateTime instanteUtc, bool abertura)
        {
            var chave = Chave(instanteUtc);
            baldes.TryGetValue(chave, out var atual);
            baldes[chave] = abertura
                ? (atual.Abertos + 1, atual.Encerrados)
                : (atual.Abertos, atual.Encerrados + 1);
        }

        foreach (var abertura in aberturas)
        {
            Incrementar(abertura.IniciadaEm, abertura: true);

            if (abertura.PrimeiraRespostaEm is not { } respondidaEm)
            {
                continue;
            }

            var espera = (respondidaEm - abertura.IniciadaEm).TotalMinutes;

            // Negativo nao existe no dominio (resposta antes da abertura) e so
            // apareceria por relogio torto — descartar e' mais honesto que
            // deixar puxar a mediana para baixo.
            if (espera < 0)
            {
                continue;
            }

            var chave = Chave(abertura.IniciadaEm);

            if (!esperasPorBalde.TryGetValue(chave, out var lista))
            {
                lista = [];
                esperasPorBalde[chave] = lista;
            }

            lista.Add(espera);
        }

        foreach (var instante in encerramentos)
        {
            Incrementar(instante, abertura: false);
        }

        // Eixo inteiro preenchido, mesmo balde vazio — senao o grafico "salta"
        // sobre o dia/hora sem nenhum atendimento em vez de mostrar zero.
        var passo = granularidade == "hora" ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        var inicioLocal = Chave(de);
        var fimLocal = Chave(ate);

        var pontos = new List<PontoDoFluxoResponse>();
        var emAberto = emAbertoNoInicio;

        for (var atual = inicioLocal; atual <= fimLocal; atual = atual.Add(passo))
        {
            baldes.TryGetValue(atual, out var valores);

            // Estoque acumulado: o que sobrou de ontem, mais o que entrou hoje,
            // menos o que saiu hoje. `Max(0, ...)` e' cinto de seguranca contra
            // dado torto (encerramento sem abertura correspondente na janela),
            // que renderizaria uma curva abaixo do eixo.
            emAberto = Math.Max(0, emAberto + valores.Abertos - valores.Encerrados);

            double? mediana = null;

            if (esperasPorBalde.TryGetValue(atual, out var esperas) && esperas.Count > 0)
            {
                esperas.Sort();
                mediana = Math.Round(Mediana(esperas), 1);
            }

            pontos.Add(new PontoDoFluxoResponse(atual, valores.Abertos, valores.Encerrados, emAberto, mediana));
        }

        return pontos;
    }

    /// <summary>
    /// Os grupos de "Requer atenção", reaproveitando as contagens que
    /// <see cref="MontarAgoraAsync"/> já trouxe.
    /// </summary>
    /// <remarks>
    /// Os totais de atrasadas, em alerta e na fila são EXATAMENTE os mesmos três
    /// números do bloco AGORA, sobre o mesmo conjunto de conversas ativas e com o
    /// mesmo escopo — inclusive a fila, que ignora "minhas" nos dois lugares.
    /// Contá-los de novo aqui eram três idas ao banco para reobter valor que já
    /// estava na mão. Só a projeção dos itens de cada grupo (que precisa das
    /// linhas em si, não da contagem) continua indo ao banco, e apenas para grupo
    /// que existe: serventia sem atraso nenhum não paga consulta de atraso.
    /// </remarks>
    private async Task<IReadOnlyList<GrupoRequerAtencaoResponse>> MontarRequerAtencaoAsync(
        Guid tenantId, Guid? apenasAtendenteId, DateTime agora, IndicadoresAgoraResponse contagens, CancellationToken ct)
    {
        const int limitePorGrupo = 6;

        var ativas = db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa);

        var ativasNoEscopo = apenasAtendenteId is { } atendenteId
            ? ativas.Where(c => c.AtendenteId == atendenteId)
            : ativas;

        var atrasadasQuery = ativasNoEscopo.Where(c => c.PrazoLimite != null && agora > c.PrazoLimite);
        var totalAtrasadas = contagens.Atrasadas;

        var alertaQuery = ativasNoEscopo.Where(
            c => c.PrazoLimite != null
              && agora <= c.PrazoLimite
              && c.PrazoAlertaEm != null
              && agora >= c.PrazoAlertaEm);
        var totalAlerta = contagens.EmAlerta;

        // Fila sem responsavel ignora o escopo "minhas" — nao ha "minha fila".
        var filaQuery = ativas.Where(c => c.AtendenteId == null);
        var totalFila = contagens.NaFila;

        var grupos = new List<GrupoRequerAtencaoResponse>();

        if (totalAtrasadas > 0)
        {
            var itens = await ProjetarItensAsync(atrasadasQuery.OrderBy(c => c.PrazoLimite), limitePorGrupo, agora, ct);
            grupos.Add(new GrupoRequerAtencaoResponse("atrasadas", "Atrasados", totalAtrasadas, itens));
        }

        if (totalAlerta > 0)
        {
            var itens = await ProjetarItensAsync(alertaQuery.OrderBy(c => c.PrazoLimite), limitePorGrupo, agora, ct);
            grupos.Add(new GrupoRequerAtencaoResponse("alerta", "Próximos do prazo", totalAlerta, itens));
        }

        if (totalFila > 0)
        {
            var itens = await ProjetarItensAsync(filaQuery.OrderBy(c => c.UltimaMensagemEm), limitePorGrupo, agora, ct);
            grupos.Add(new GrupoRequerAtencaoResponse("fila", "Na fila sem responsável", totalFila, itens));
        }

        return grupos;
    }

    private async Task<IReadOnlyList<ItemRequerAtencaoResponse>> ProjetarItensAsync(
        IOrderedQueryable<Conversa> consultaOrdenada, int limite, DateTime agora, CancellationToken ct)
    {
        var brutos = await consultaOrdenada
            .Take(limite)
            .Select(c => new
            {
                c.Id,
                c.Protocolo,
                ClienteNome = db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.Nome).FirstOrDefault(),
                ClienteTelefone = db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.TelefoneWhatsapp).FirstOrDefault(),
                ClienteFotoPerfilUrl = db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.FotoPerfilUrl).FirstOrDefault(),
                EtapaNome = db.EtapasFunil.Where(e => e.Id == c.EtapaId).Select(e => e.Nome).FirstOrDefault(),
                EtapaCor = db.EtapasFunil.Where(e => e.Id == c.EtapaId).Select(e => e.Cor).FirstOrDefault(),
                AtendenteNome = db.Atendentes.Where(a => a.Id == c.AtendenteId).Select(a => a.Nome).FirstOrDefault(),
                c.PrazoLimite,
                c.PrazoAlertaEm,
                c.UltimaMensagemEm
            })
            .ToListAsync(ct);

        return brutos
            .Select(c => new ItemRequerAtencaoResponse(
                c.Id,
                c.Protocolo,
                c.ClienteNome ?? "",
                c.ClienteTelefone ?? "",
                c.ClienteFotoPerfilUrl,
                c.EtapaNome,
                c.EtapaCor,
                AvaliadorDeSituacaoPrazo.Avaliar(StatusConversa.Ativa, c.PrazoLimite, c.PrazoAlertaEm, null, agora),
                c.PrazoLimite,
                c.UltimaMensagemEm,
                c.AtendenteNome))
            .ToList();
    }

    /// <summary>
    /// Ranking de serviços do período, agrupado sobre as aberturas que já estão em
    /// memória — não é uma segunda varredura da mesma faixa de datas.
    /// </summary>
    /// <remarks>
    /// <see cref="LerAberturasDoPeriodoAsync"/> já trouxe <c>TipoServicoId</c> de toda
    /// conversa aberta no período, com o escopo aplicado. Agrupar de novo no banco
    /// significaria filtrar exatamente as mesmas linhas pela segunda vez. Sobra uma
    /// única ida ao banco — a dos nomes dos serviços, que as aberturas não carregam.
    ///
    /// O percentual é sobre o total dos 8 primeiros, não sobre todas as aberturas do
    /// período: é a mesma base que a versão anterior usava, e é o que faz a soma dos
    /// percentuais exibidos fechar em 100%.
    /// </remarks>
    private async Task<IReadOnlyList<ServicoMaisProcuradoResponse>> MontarServicosMaisProcuradosAsync(
        IReadOnlyList<AberturaNoPeriodo> aberturas, CancellationToken ct)
    {
        var contagem = aberturas
            .Where(a => a.TipoServicoId != null)
            .GroupBy(a => a.TipoServicoId!.Value)
            .Select(g => new { TipoServicoId = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .Take(8)
            .ToList();

        var totalGeral = contagem.Sum(c => c.Total);

        if (totalGeral == 0)
        {
            return [];
        }

        var idsServicos = contagem.Select(c => c.TipoServicoId).ToList();

        var nomes = await db.TiposServico.AsNoTracking()
            .Where(t => idsServicos.Contains(t.Id))
            .Select(t => new { t.Id, t.Nome })
            .ToDictionaryAsync(t => t.Id, t => t.Nome, ct);

        return contagem
            .Select(c => new ServicoMaisProcuradoResponse(
                c.TipoServicoId,
                nomes.TryGetValue(c.TipoServicoId, out var nome) ? nome : "Serviço removido",
                c.Total,
                Math.Round(100.0 * c.Total / totalGeral, 1)))
            .ToList();
    }

    /// <summary>
    /// A distribuição por etapa da operação inteira E a do próprio atendente, nas
    /// mesmas duas idas ao banco que uma delas custava.
    /// </summary>
    /// <remarks>
    /// A Visão Geral precisa das duas: a global alimenta o cartão de etapas, e a
    /// "minha" é o que ocupa o lugar de Saúde da operação para quem não gerencia
    /// (ver <c>saude-da-operacao.tsx</c>). Como as duas leem o mesmo conjunto —
    /// conversas ativas do tenant — e diferem só no predicado da contagem, saem de
    /// um <c>GroupBy</c> só com duas agregações, no mesmo espírito de
    /// <see cref="MontarAgoraAsync"/>. Chamar o método duas vezes custava quatro
    /// idas: dois agrupamentos e a tabela de etapas lida em duplicidade.
    /// </remarks>
    private async Task<(IReadOnlyList<ContagemPorChaveResponse> Global, IReadOnlyList<ContagemPorChaveResponse> Minha)>
        MontarDistribuicoesPorEtapaAsync(Guid tenantId, Guid? euId, CancellationToken ct)
    {
        // `Guid.Empty` como sentinela em vez de comparar com `Guid?` nulo: a
        // comparacao com parametro nulo viraria `AtendenteId = NULL` no SQL, que e'
        // UNKNOWN e nunca conta — funciona por acidente. Empty nunca e' id de
        // atendente real, entao o zero e' deliberado e legivel no SQL gerado.
        var eu = euId ?? Guid.Empty;

        var contagemPorEtapa = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa)
            .GroupBy(c => c.EtapaId)
            .Select(g => new
            {
                EtapaId = g.Key,
                Total = g.Count(),
                Meu = g.Count(c => c.AtendenteId == eu)
            })
            .ToListAsync(ct);

        var etapas = await db.EtapasFunil.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Ativo)
            .OrderBy(e => e.Ordem)
            .Select(e => new { e.Id, e.Nome, e.Cor })
            .ToListAsync(ct);

        var global = new List<ContagemPorChaveResponse>();
        var minha = new List<ContagemPorChaveResponse>();

        foreach (var etapa in etapas)
        {
            var linha = contagemPorEtapa.FirstOrDefault(x => x.EtapaId == etapa.Id);
            global.Add(new ContagemPorChaveResponse(etapa.Id, etapa.Nome, etapa.Cor, linha?.Total ?? 0));
            minha.Add(new ContagemPorChaveResponse(etapa.Id, etapa.Nome, etapa.Cor, linha?.Meu ?? 0));
        }

        var semEtapa = contagemPorEtapa.FirstOrDefault(x => x.EtapaId == null);

        if (semEtapa is { Total: > 0 })
        {
            global.Add(new ContagemPorChaveResponse(null, "Sem etapa", "#94A3B8", semEtapa.Total));
        }

        if (semEtapa is { Meu: > 0 })
        {
            minha.Add(new ContagemPorChaveResponse(null, "Sem etapa", "#94A3B8", semEtapa.Meu));
        }

        return (global, minha);
    }

    private async Task<SaudeDaOperacaoResponse> MontarSaudeAsync(Guid tenantId, CancellationToken ct)
    {
        SaudeDoWhatsAppResponse? whatsapp = null;
        SaudeDaIaResponse? ia = null;
        SaudeDaAutomacaoResponse? automacao = null;

        // Ninguem tem a permissao ampliada aqui — os mesmos gates do resto do
        // sistema. Quem nao pode ver, recebe o campo nulo, e o frontend nao
        // renderiza a peca (nunca um aviso de "sem permissao" no lugar do card).
        if (ctx.Pode(PermissaoDoSistema.RelatoriosSaudeWhatsapp))
        {
            var situacao = await ConsultarWhatsAppComSegurancaAsync(tenantId, ct);
            whatsapp = new SaudeDoWhatsAppResponse(situacao.Habilitado, situacao.Conectado, situacao.Estado);
        }

        if (ctx.Pode(PermissaoDoSistema.RelatoriosSaudeIa))
        {
            // Mesmo calculo que SituacaoDaIa usa para o painel /automacao/ia —
            // antes duplicado em quatro lugares, agora so aqui e la.
            var resumo = await situacaoDaIa.ObterResumoAsync(ct);
            ia = new SaudeDaIaResponse(resumo.Habilitada, resumo.EstaConfigurada, resumo.Modelo, resumo.LimiteDiario, resumo.UsadoHoje);

            var ativas = await db.MensagensAutomaticas.AsNoTracking()
                .CountAsync(m => m.TenantId == tenantId && m.Ativo, ct);

            var inativas = await db.MensagensAutomaticas.AsNoTracking()
                .CountAsync(m => m.TenantId == tenantId && !m.Ativo, ct);

            var habilitadas = await db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => (bool?)t.MensagensAutomaticasHabilitadas)
                .FirstOrDefaultAsync(ct) ?? true;

            automacao = new SaudeDaAutomacaoResponse(ativas, inativas, habilitadas);
        }

        return new SaudeDaOperacaoResponse(whatsapp, ia, automacao);
    }

    // ---------------------------------------------------------------- Relatório

    public async Task<RelatorioResponse> ObterRelatorioAsync(
        FiltroDeIndicadores filtro,
        CancellationToken ct = default)
    {
        GarantirGestao();

        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;
        var (de, ate, granularidade) = PeriodoDeAnalise.Resolver(filtro.De, filtro.Ate, agora);

        var evolucao = await MontarEvolucaoDaPrimeiraRespostaAsync(tenantId, de, ate, ct);
        var porSetor = await MontarDistribuicaoPorSetorNoPeriodoAsync(tenantId, de, ate, ct);
        var porEtapa = await MontarDistribuicaoPorEtapaNoPeriodoAsync(tenantId, de, ate, ct);
        var horarios = await MontarHorariosDeMaiorDemandaAsync(tenantId, de, ate, ct);
        var carga = await MontarCargaDaEquipeAsync(tenantId, ct);
        var encerramentos = await MontarEncerramentosAsync(tenantId, de, ate, ct);
        var absorcao = await MontarAbsorcaoDaAutomacaoAsync(tenantId, de, ate, ct);

        return new RelatorioResponse(
            new PeriodoResponse(de, ate, granularidade),
            evolucao, porSetor, porEtapa, horarios, carga, encerramentos, absorcao);
    }

    private void GarantirGestao()
    {
        if (!ctx.Pode(PermissaoDoSistema.RelatoriosVer))
        {
            throw IndicadoresServiceException.SemPermissao();
        }
    }

    private async Task<IReadOnlyList<EvolucaoDaPrimeiraRespostaResponse>> MontarEvolucaoDaPrimeiraRespostaAsync(
        Guid tenantId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var pares = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
            .Select(c => new
            {
                c.IniciadaEm,
                PrimeiraRespostaEm = db.Mensagens
                    .Where(m => m.ConversaId == c.Id && m.AutorTipo == AutorTipo.Atendente)
                    .OrderBy(m => m.EnviadaEm)
                    .Select(m => (DateTime?)m.EnviadaEm)
                    .FirstOrDefault()
            })
            .Where(x => x.PrimeiraRespostaEm != null)
            .ToListAsync(ct);

        return pares
            .Select(x => new
            {
                Dia = DiaLocal.ParaLocal(x.IniciadaEm).Date,
                Minutos = (x.PrimeiraRespostaEm!.Value - x.IniciadaEm).TotalMinutes
            })
            .Where(x => x.Minutos >= 0)
            .GroupBy(x => x.Dia)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var minutos = g.Select(x => x.Minutos).OrderBy(m => m).ToList();
                return new EvolucaoDaPrimeiraRespostaResponse(g.Key, Math.Round(Mediana(minutos), 1), minutos.Count);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<ContagemPorChaveResponse>> MontarDistribuicaoPorSetorNoPeriodoAsync(
        Guid tenantId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var contagem = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
            .GroupBy(c => c.SetorId)
            .Select(g => new { SetorId = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        var setores = await db.Setores.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Ativo)
            .OrderBy(s => s.Ordem)
            .Select(s => new { s.Id, s.Nome })
            .ToListAsync(ct);

        var resultado = setores
            .Select(s => new ContagemPorChaveResponse(
                s.Id, s.Nome, null, contagem.FirstOrDefault(x => x.SetorId == s.Id)?.Total ?? 0))
            .ToList();

        var semSetor = contagem.FirstOrDefault(x => x.SetorId == null)?.Total ?? 0;

        if (semSetor > 0)
        {
            resultado.Add(new ContagemPorChaveResponse(null, "Sem setor", null, semSetor));
        }

        return resultado;
    }

    private async Task<IReadOnlyList<ContagemPorChaveResponse>> MontarDistribuicaoPorEtapaNoPeriodoAsync(
        Guid tenantId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var contagem = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
            .GroupBy(c => c.EtapaId)
            .Select(g => new { EtapaId = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        var etapas = await db.EtapasFunil.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Ativo)
            .OrderBy(e => e.Ordem)
            .Select(e => new { e.Id, e.Nome, e.Cor })
            .ToListAsync(ct);

        var resultado = etapas
            .Select(e => new ContagemPorChaveResponse(
                e.Id, e.Nome, e.Cor, contagem.FirstOrDefault(x => x.EtapaId == e.Id)?.Total ?? 0))
            .ToList();

        var semEtapa = contagem.FirstOrDefault(x => x.EtapaId == null)?.Total ?? 0;

        if (semEtapa > 0)
        {
            resultado.Add(new ContagemPorChaveResponse(null, "Sem etapa", "#94A3B8", semEtapa));
        }

        return resultado;
    }

    private async Task<IReadOnlyList<int>> MontarHorariosDeMaiorDemandaAsync(
        Guid tenantId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var instantes = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
            .Select(c => c.IniciadaEm)
            .ToListAsync(ct);

        var horas = new int[24];

        foreach (var instante in instantes)
        {
            horas[DiaLocal.ParaLocal(instante).Hour]++;
        }

        return horas;
    }

    private async Task<IReadOnlyList<CargaDoAtendenteResponse>> MontarCargaDaEquipeAsync(
        Guid tenantId, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        var contagem = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa && c.AtendenteId != null)
            .GroupBy(c => c.AtendenteId)
            .Select(g => new
            {
                AtendenteId = g.Key!.Value,
                Ativas = g.Count(),
                EmAlerta = g.Count(c =>
                    c.PrazoLimite != null && agora <= c.PrazoLimite && c.PrazoAlertaEm != null && agora >= c.PrazoAlertaEm),
                Atrasadas = g.Count(c => c.PrazoLimite != null && agora > c.PrazoLimite)
            })
            .ToListAsync(ct);

        var atendentes = await db.Atendentes.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Ativo)
            .Select(a => new { a.Id, a.Nome })
            .ToListAsync(ct);

        // Alfabetica, nao por volume: e' distribuicao de trabalho, nao ranking
        // de desempenho — AGENTS.md pede que desempenho individual so apareca
        // com proposito operacional, nunca como carnaval punitivo.
        return atendentes
            .Select(a =>
            {
                var c = contagem.FirstOrDefault(x => x.AtendenteId == a.Id);
                return new CargaDoAtendenteResponse(a.Id, a.Nome, c?.Ativas ?? 0, c?.EmAlerta ?? 0, c?.Atrasadas ?? 0);
            })
            .OrderBy(a => a.AtendenteNome, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<EncerramentosResponse> MontarEncerramentosAsync(
        Guid tenantId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var encerradas = db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                     && c.Status == StatusConversa.Encerrada
                     && c.EncerradaEm != null
                     && c.EncerradaEm >= de
                     && c.EncerradaEm < ate);

        var porAtendente = await encerradas.CountAsync(c => c.EncerradaPorAtendenteId != null, ct);
        var peloCliente = await encerradas.CountAsync(c => c.EncerradaPorAtendenteId == null, ct);

        return new EncerramentosResponse(porAtendente, peloCliente);
    }

    private async Task<AbsorcaoDaAutomacaoResponse> MontarAbsorcaoDaAutomacaoAsync(
        Guid tenantId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var mensagens = db.Mensagens.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.EnviadaEm >= de && m.EnviadaEm < ate);

        var humanas = await mensagens.CountAsync(m => m.AutorTipo == AutorTipo.Atendente, ct);
        var automaticas = await mensagens.CountAsync(m => m.EhAutomatica && !m.GeradaPorIa, ct);
        var ia = await mensagens.CountAsync(m => m.GeradaPorIa, ct);

        var total = humanas + automaticas + ia;
        var percentual = total == 0 ? 0 : Math.Round(100.0 * (automaticas + ia) / total, 1);

        return new AbsorcaoDaAutomacaoResponse(humanas, automaticas, ia, percentual);
    }

    // -------------------------------------------------------------------- Sino

    public async Task<IReadOnlyList<AlertaOperacionalResponse>> ObterAlertasAsync(CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;
        var alertas = new List<AlertaOperacionalResponse>();

        var ativas = db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa);

        var atrasadas = await ativas
            .Where(c => c.PrazoLimite != null && agora > c.PrazoLimite)
            .OrderBy(c => c.PrazoLimite)
            .Take(30)
            .Select(c => new
            {
                c.Id,
                c.Protocolo,
                c.PrazoLimite,
                ClienteNome = db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.Nome).FirstOrDefault()
            })
            .ToListAsync(ct);

        foreach (var c in atrasadas)
        {
            alertas.Add(new AlertaOperacionalResponse(
                $"atrasada:{c.Id}", "atrasada", SeveridadeDeAlerta.Critica,
                $"{c.Protocolo} está atrasado", c.ClienteNome, c.PrazoLimite!.Value, $"/atendimentos/{c.Id}"));
        }

        var emAlerta = await ativas
            .Where(c => c.PrazoLimite != null
                     && agora <= c.PrazoLimite
                     && c.PrazoAlertaEm != null
                     && agora >= c.PrazoAlertaEm)
            .OrderBy(c => c.PrazoLimite)
            .Take(30)
            .Select(c => new
            {
                c.Id,
                c.Protocolo,
                c.PrazoAlertaEm,
                ClienteNome = db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.Nome).FirstOrDefault()
            })
            .ToListAsync(ct);

        foreach (var c in emAlerta)
        {
            alertas.Add(new AlertaOperacionalResponse(
                $"alerta:{c.Id}", "prazo_proximo", SeveridadeDeAlerta.Alerta,
                $"{c.Protocolo} está perto do prazo", c.ClienteNome, c.PrazoAlertaEm!.Value, $"/atendimentos/{c.Id}"));
        }

        var naFila = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa && c.AtendenteId == null)
            .OrderBy(c => c.UltimaMensagemEm)
            .Take(30)
            .Select(c => new
            {
                c.Id,
                c.Protocolo,
                c.UltimaMensagemEm,
                ClienteNome = db.Clientes.Where(cl => cl.Id == c.ClienteId).Select(cl => cl.Nome).FirstOrDefault()
            })
            .ToListAsync(ct);

        foreach (var c in naFila)
        {
            alertas.Add(new AlertaOperacionalResponse(
                $"fila:{c.Id}", "fila_sem_responsavel", SeveridadeDeAlerta.Neutra,
                $"{c.Protocolo} está na fila sem responsável", c.ClienteNome, c.UltimaMensagemEm, $"/atendimentos/{c.Id}"));
        }

        if (ctx.Pode(PermissaoDoSistema.RelatoriosSaudeWhatsapp))
        {
            var situacao = await ConsultarWhatsAppComSegurancaAsync(tenantId, ct);

            if (situacao.Habilitado && !situacao.Conectado)
            {
                alertas.Add(new AlertaOperacionalResponse(
                    "whatsapp:desconectado", "whatsapp_desconectado", SeveridadeDeAlerta.Critica,
                    "WhatsApp desconectado", "Nenhuma mensagem nova entra até reconectar.",
                    agora, "/integracoes/whatsapp"));
            }
        }

        if (ctx.Pode(PermissaoDoSistema.RelatoriosSaudeIa))
        {
            var resumo = await situacaoDaIa.ObterResumoAsync(ct);

            if (resumo.EstaConfigurada && resumo.UsadoHoje >= resumo.LimiteDiario)
            {
                alertas.Add(new AlertaOperacionalResponse(
                    "ia:limite", "ia_limite_atingido", SeveridadeDeAlerta.Alerta,
                    "Teto diário da IA atingido",
                    $"{resumo.UsadoHoje} de {resumo.LimiteDiario} respostas hoje. Atendimento humano assume o resto do dia.",
                    agora, "/automacao/ia"));
            }
        }

        return alertas.OrderByDescending(a => a.Quando).ToList();
    }

    /// <summary>
    /// A situação do canal para a Visão Geral e o sino: nunca derruba a resposta,
    /// nunca espera mais que <see cref="EsperaMaximaPeloGateway"/>, e é reaproveitada
    /// por <see cref="ValidadeDaSituacaoDoCanal"/> entre requisições.
    /// </summary>
    /// <remarks>
    /// Três proteções, cada uma para um modo de falha distinto:
    ///
    /// 1. <b>Exceção</b> — `PareamentoDoCanal.ConsultarAsync` lança
    ///    <see cref="CanalWhatsAppServiceException"/> de propósito quando o gateway
    ///    não responde, e isso é correto na tela de Integrações, onde "gateway fora
    ///    do ar" é a única informação da página. Aqui não: a Visão Geral agrega dez
    ///    fontes independentes, e uma dependência externa caída não pode derrubar
    ///    filas, prazos e atividade recente junto.
    ///
    /// 2. <b>Timeout curto</b> — o gateway fora do ar não falha rápido, ele PENDURA
    ///    até o timeout de 20s do módulo do canal. A Visão Geral inteira ficava
    ///    esperando por causa de um selo de saúde. 1,5s é generoso para um serviço
    ///    na mesma máquina e é o teto do que essa peça pode custar.
    ///
    /// 3. <b>Cache</b> — sem ele, cada carga de página e cada refetch do sino paga a
    ///    consulta (ou o timeout) outra vez, e é o mesmo dado para todo mundo. A
    ///    tela de Integrações NÃO passa por aqui: ela continua lendo ao vivo, que é
    ///    o que pareamento e QR exigem.
    /// </remarks>
    private async Task<SituacaoDoCanal> ConsultarWhatsAppComSegurancaAsync(Guid tenantId, CancellationToken ct)
    {
        var chave = $"indicadores:situacao-canal:{tenantId}";

        if (cache.TryGetValue(chave, out SituacaoDoCanal? emCache) && emCache is not null)
        {
            return emCache;
        }

        var situacao = await ConsultarGatewayAsync(tenantId, ct);

        // Indisponibilidade tambem entra no cache, e de proposito: e' justamente o
        // caso que custa caro (o timeout inteiro), e repetir a espera a cada carga de
        // pagina enquanto o gateway esta fora e' o pior dos mundos. 15s de atraso
        // para perceber que voltou e' aceitavel num selo de saude.
        cache.Set(chave, situacao, ValidadeDaSituacaoDoCanal);

        return situacao;
    }

    private async Task<SituacaoDoCanal> ConsultarGatewayAsync(Guid tenantId, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(EsperaMaximaPeloGateway);

        try
        {
            return await pareamento.ConsultarAsync(tenantId, limite.Token);
        }
        catch (CanalWhatsAppServiceException)
        {
            return new SituacaoDoCanal(true, false, "indisponivel", null, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Estourou o teto local, nao foi o cliente que desistiu — degrada. A
            // guarda em `ct` e' o que distingue os dois: sem ela, o cliente fechando
            // a aba viraria "gateway indisponivel" no cache.
            return new SituacaoDoCanal(true, false, "indisponivel", null, null);
        }
    }

    private static double Mediana(IReadOnlyList<double> valoresOrdenados)
    {
        var n = valoresOrdenados.Count;

        return n % 2 == 1
            ? valoresOrdenados[n / 2]
            : (valoresOrdenados[n / 2 - 1] + valoresOrdenados[n / 2]) / 2;
    }

    // ========================================= Abas de Indicadores (gestão)

    /// <summary>
    /// Primeiro instante (UTC) do mês local que contém <paramref name="referencia"/>,
    /// deslocado por <paramref name="mesesAtras"/>.
    /// </summary>
    /// <remarks>
    /// A fronteira é do mês LOCAL, não do mês UTC: "agosto" para o cartório
    /// começa à meia-noite de Brasília, e agrupar por mês UTC jogaria as
    /// conversas das últimas horas do dia 31 para o mês seguinte.
    /// </remarks>
    private static DateTime InicioDoMesLocalEmUtc(DateTime referencia, int mesesAtras)
    {
        var local = DiaLocal.ParaLocal(referencia);
        var primeiroDoMes = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified)
            .AddMonths(-mesesAtras);

        return DateTime.SpecifyKind(primeiroDoMes, DateTimeKind.Local).ToUniversalTime();
    }

    /// <summary>
    /// Doze meses de fluxo, terminando no mês corrente. Duas leituras e uma
    /// contagem — não doze consultas.
    /// </summary>
    private async Task<IReadOnlyList<MesDoFluxoResponse>> MontarDozeMesesAsync(
        Guid tenantId, DateTime agora, CancellationToken ct)
    {
        // 11 meses atrás + o corrente = 12 baldes.
        var de = InicioDoMesLocalEmUtc(agora, 11);
        var ate = InicioDoMesLocalEmUtc(agora, -1);

        var aberturas = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
            .Select(c => c.IniciadaEm)
            .ToListAsync(ct);

        var encerramentos = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                     && c.EncerradaEm != null
                     && c.EncerradaEm >= de
                     && c.EncerradaEm < ate)
            .Select(c => c.EncerradaEm!.Value)
            .ToListAsync(ct);

        var emAberto = await ContarEmAbertoAntesAsync(tenantId, de, null, ct);

        DateTime ChaveDoMes(DateTime instanteUtc)
        {
            var local = DiaLocal.ParaLocal(instanteUtc);
            return new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        }

        var abertosPorMes = aberturas.GroupBy(ChaveDoMes).ToDictionary(g => g.Key, g => g.Count());
        var encerradosPorMes = encerramentos.GroupBy(ChaveDoMes).ToDictionary(g => g.Key, g => g.Count());

        var pontos = new List<MesDoFluxoResponse>(12);
        var primeiroBalde = ChaveDoMes(de);

        for (var i = 0; i < 12; i++)
        {
            var mes = primeiroBalde.AddMonths(i);
            abertosPorMes.TryGetValue(mes, out var abertos);
            encerradosPorMes.TryGetValue(mes, out var encerrados);

            // Mesma acumulacao de estoque do grafico diario — ver MontarFluxo.
            emAberto = Math.Max(0, emAberto + abertos - encerrados);

            pontos.Add(new MesDoFluxoResponse(mes, abertos, encerrados, emAberto));
        }

        return pontos;
    }

    /// <summary>
    /// Resumo de uma janela fixa. Uma leitura de aberturas (que já traz a
    /// primeira resposta) e uma contagem de encerramentos.
    /// </summary>
    private async Task<ResumoDeJanelaResponse> MontarJanelaAsync(
        Guid tenantId, string chave, string rotulo, DateTime de, DateTime ate, CancellationToken ct)
    {
        var aberturas = await LerAberturasDoPeriodoAsync(tenantId, de, ate, null, ct);

        var encerrados = await db.Conversas.AsNoTracking()
            .CountAsync(
                c => c.TenantId == tenantId
                  && c.EncerradaEm != null
                  && c.EncerradaEm >= de
                  && c.EncerradaEm < ate,
                ct);

        var esperas = aberturas
            .Where(a => a.PrimeiraRespostaEm != null)
            .Select(a => (a.PrimeiraRespostaEm!.Value - a.IniciadaEm).TotalMinutes)
            .Where(m => m >= 0)
            .OrderBy(m => m)
            .ToList();

        return new ResumoDeJanelaResponse(
            chave,
            rotulo,
            de,
            ate,
            aberturas.Count,
            encerrados,
            esperas.Count == 0 ? null : Math.Round(Mediana(esperas), 1),
            esperas.Count);
    }

    public async Task<PainelResponse> ObterPainelAsync(CancellationToken ct = default)
    {
        GarantirGestao();

        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;

        var dozeMeses = await MontarDozeMesesAsync(tenantId, agora, ct);

        // As tres janelas nao sao configuraveis de proposito: elas existem para
        // dar a MESMA leitura em tres escalas de tempo, e um filtro por cima
        // desmontaria a comparacao.
        var inicioDeHoje = DiaLocal.InicioDoDia(agora);
        var fimDeHoje = inicioDeHoje.AddDays(1);

        var janelas = new List<ResumoDeJanelaResponse>
        {
            await MontarJanelaAsync(tenantId, "hoje", "Hoje", inicioDeHoje, fimDeHoje, ct),
            await MontarJanelaAsync(tenantId, "7dias", "Últimos 7 dias", inicioDeHoje.AddDays(-6), fimDeHoje, ct),
            await MontarJanelaAsync(tenantId, "mes", "Este mês", InicioDoMesLocalEmUtc(agora, 0), fimDeHoje, ct),
        };

        return new PainelResponse(dozeMeses, janelas);
    }

    /// <summary>
    /// Clientes ordenados por volume de atendimento no período, com o total de
    /// clientes distintos atendidos.
    /// </summary>
    /// <remarks>
    /// O percentual é sobre o total de atendimentos do período, não sobre o topo
    /// da lista: aqui a pergunta é "quanto do movimento este cliente
    /// representa?", e normalizar pelo topo daria percentuais somando 100% num
    /// recorte de 10 clientes, o que seria falso.
    /// </remarks>
    private async Task<(IReadOnlyList<ClienteNoRankingResponse> Lista, int Distintos)> MontarClientesMaisAtendidosAsync(
        Guid tenantId, DateTime de, DateTime ate, int limite, CancellationToken ct)
    {
        var porCliente = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
            .GroupBy(c => c.ClienteId)
            .Select(g => new
            {
                ClienteId = g.Key,
                Total = g.Count(),
                UltimoEm = g.Max(c => c.IniciadaEm)
            })
            .ToListAsync(ct);

        var totalDeAtendimentos = porCliente.Sum(x => x.Total);

        if (totalDeAtendimentos == 0)
        {
            return ([], 0);
        }

        var topo = porCliente.OrderByDescending(x => x.Total).ThenByDescending(x => x.UltimoEm).Take(limite).ToList();
        var ids = topo.Select(x => x.ClienteId).ToList();

        var clientes = await db.Clientes.AsNoTracking()
            .Where(cl => ids.Contains(cl.Id))
            .Select(cl => new { cl.Id, cl.Nome, cl.FotoPerfilUrl, cl.TelefoneWhatsapp })
            .ToListAsync(ct);

        var lista = topo
            .Select(x =>
            {
                var cliente = clientes.FirstOrDefault(cl => cl.Id == x.ClienteId);

                return new ClienteNoRankingResponse(
                    x.ClienteId,
                    cliente?.Nome ?? "Cliente removido",
                    cliente?.FotoPerfilUrl,
                    cliente?.TelefoneWhatsapp ?? "",
                    x.Total,
                    Math.Round(100.0 * x.Total / totalDeAtendimentos, 1),
                    x.UltimoEm);
            })
            .ToList();

        return (lista, porCliente.Count);
    }

    public async Task<RelatorioDeAtendimentosResponse> ObterRelatorioDeAtendimentosAsync(
        FiltroDeIndicadores filtro,
        CancellationToken ct = default)
    {
        GarantirGestao();

        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;
        var (de, ate, granularidade) = PeriodoDeAnalise.Resolver(filtro.De, filtro.Ate, agora);

        var aberturas = await LerAberturasDoPeriodoAsync(tenantId, de, ate, null, ct);
        var encerramentos = await LerEncerramentosDoPeriodoAsync(tenantId, de, ate, null, ct);
        var noPeriodo = MontarNoPeriodo(aberturas, encerramentos);

        var emAbertoAgora = await db.Conversas.AsNoTracking()
            .CountAsync(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa, ct);

        var totalMensal = await MontarDozeMesesAsync(tenantId, agora, ct);
        var (clientes, distintos) = await MontarClientesMaisAtendidosAsync(tenantId, de, ate, 10, ct);
        var porSetor = await MontarDistribuicaoPorSetorNoPeriodoAsync(tenantId, de, ate, ct);
        var porEtapa = await MontarDistribuicaoPorEtapaNoPeriodoAsync(tenantId, de, ate, ct);
        var horarios = await MontarHorariosDeMaiorDemandaAsync(tenantId, de, ate, ct);
        var absorcao = await MontarAbsorcaoDaAutomacaoAsync(tenantId, de, ate, ct);

        return new RelatorioDeAtendimentosResponse(
            new PeriodoResponse(de, ate, granularidade),
            noPeriodo.Abertos,
            noPeriodo.Encerrados,
            emAbertoAgora,
            noPeriodo.PrimeiraRespostaMedianaMinutos,
            noPeriodo.AmostraPrimeiraResposta,
            totalMensal,
            clientes,
            distintos,
            porSetor,
            porEtapa,
            horarios,
            absorcao);
    }

    public async Task<RelatorioDeAtendentesResponse> ObterRelatorioDeAtendentesAsync(
        FiltroDeIndicadores filtro,
        CancellationToken ct = default)
    {
        GarantirGestao();

        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;
        var (de, ate, granularidade) = PeriodoDeAnalise.Resolver(filtro.De, filtro.Ate, agora);

        var atendentes = await db.Atendentes.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Ativo)
            .OrderBy(a => a.Nome)
            .Select(a => new { a.Id, a.Nome, a.Cargo, a.Perfil, a.UltimoAcessoEm })
            .ToListAsync(ct);

        // Carga de AGORA, por atendente — uma varredura das ativas.
        var cargaAgora = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa && c.AtendenteId != null)
            .GroupBy(c => c.AtendenteId)
            .Select(g => new
            {
                AtendenteId = g.Key!.Value,
                Ativas = g.Count(),
                EmAlerta = g.Count(c =>
                    c.PrazoLimite != null && agora <= c.PrazoLimite && c.PrazoAlertaEm != null && agora >= c.PrazoAlertaEm),
                Atrasadas = g.Count(c => c.PrazoLimite != null && agora > c.PrazoLimite)
            })
            .ToListAsync(ct);

        // Encerramentos do PERÍODO, por quem encerrou. `EncerradaPorAtendenteId`
        // nulo e' encerramento pelo proprio cliente — nao entra na conta de
        // ninguem, e por isso o filtro de nao-nulo esta aqui.
        var encerradosPorAtendente = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                     && c.EncerradaEm != null
                     && c.EncerradaEm >= de
                     && c.EncerradaEm < ate
                     && c.EncerradaPorAtendenteId != null)
            .GroupBy(c => c.EncerradaPorAtendenteId)
            .Select(g => new { AtendenteId = g.Key!.Value, Total = g.Count() })
            .ToListAsync(ct);

        // Primeiras respostas humanas do periodo, com quem respondeu e quanto
        // tempo levou. A subconsulta pega a PRIMEIRA mensagem de atendente de
        // cada conversa aberta no periodo — mesma disciplina de
        // LerAberturasDoPeriodoAsync, mas trazendo tambem o autor.
        var primeirasRespostas = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IniciadaEm >= de && c.IniciadaEm < ate)
            .Select(c => new
            {
                c.IniciadaEm,
                Primeira = db.Mensagens
                    .Where(m => m.ConversaId == c.Id && m.AutorTipo == AutorTipo.Atendente)
                    .OrderBy(m => m.EnviadaEm)
                    .Select(m => new { m.EnviadaEm, m.AtendenteId })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var esperasPorAtendente = new Dictionary<Guid, List<double>>();

        foreach (var linha in primeirasRespostas)
        {
            if (linha.Primeira is null || linha.Primeira.AtendenteId is not { } autorId)
            {
                continue;
            }

            var espera = (linha.Primeira.EnviadaEm - linha.IniciadaEm).TotalMinutes;

            if (espera < 0)
            {
                continue;
            }

            if (!esperasPorAtendente.TryGetValue(autorId, out var lista))
            {
                lista = [];
                esperasPorAtendente[autorId] = lista;
            }

            lista.Add(espera);
        }

        var naFila = await db.Conversas.AsNoTracking()
            .CountAsync(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa && c.AtendenteId == null, ct);

        var encerramentos = await MontarEncerramentosAsync(tenantId, de, ate, ct);

        var linhas = atendentes
            .Select(a =>
            {
                var carga = cargaAgora.FirstOrDefault(x => x.AtendenteId == a.Id);
                var encerrados = encerradosPorAtendente.FirstOrDefault(x => x.AtendenteId == a.Id)?.Total ?? 0;

                double? mediana = null;
                var amostra = 0;

                if (esperasPorAtendente.TryGetValue(a.Id, out var esperas) && esperas.Count > 0)
                {
                    esperas.Sort();
                    mediana = Math.Round(Mediana(esperas), 1);
                    amostra = esperas.Count;
                }

                return new AtendenteNoRelatorioResponse(
                    a.Id,
                    a.Nome,
                    DescricaoDeCargo.De(a.Cargo),
                    a.Perfil.ToString(),
                    carga?.Ativas ?? 0,
                    carga?.EmAlerta ?? 0,
                    carga?.Atrasadas ?? 0,
                    encerrados,
                    amostra,
                    mediana,
                    a.UltimoAcessoEm);
            })
            .ToList();

        return new RelatorioDeAtendentesResponse(
            new PeriodoResponse(de, ate, granularidade),
            linhas.Count,
            linhas.Count(l => l.Ativas > 0),
            naFila,
            encerramentos,
            linhas);
    }

    public async Task<RelatorioDeClientesResponse> ObterRelatorioDeClientesAsync(
        FiltroDeIndicadores filtro,
        CancellationToken ct = default)
    {
        GarantirGestao();

        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;
        var (de, ate, granularidade) = PeriodoDeAnalise.Resolver(filtro.De, filtro.Ate, agora);

        var clientes = db.Clientes.AsNoTracking().Where(cl => cl.TenantId == tenantId);

        // Uma varredura da tabela de clientes responde as cinco contagens.
        var contagens = await clientes
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Novos = g.Count(cl => cl.CriadoEm >= de && cl.CriadoEm < ate),
                Fisica = g.Count(cl => cl.Tipo == TipoDePessoa.Fisica),
                Juridica = g.Count(cl => cl.Tipo == TipoDePessoa.Juridica),
                SemDocumento = g.Count(cl => cl.Documento == null || cl.Documento == "")
            })
            .FirstOrDefaultAsync(ct);

        var comAtendimentoAtivo = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == StatusConversa.Ativa)
            .Select(c => c.ClienteId)
            .Distinct()
            .CountAsync(ct);

        // Clientes novos por mes, nos mesmos 12 meses do painel — a leitura de
        // crescimento da base, que nao existe em nenhuma outra tela.
        var deDozeMeses = InicioDoMesLocalEmUtc(agora, 11);
        var ateDozeMeses = InicioDoMesLocalEmUtc(agora, -1);

        var criacoes = await clientes
            .Where(cl => cl.CriadoEm >= deDozeMeses && cl.CriadoEm < ateDozeMeses)
            .Select(cl => cl.CriadoEm)
            .ToListAsync(ct);

        DateTime ChaveDoMes(DateTime instanteUtc)
        {
            var local = DiaLocal.ParaLocal(instanteUtc);
            return new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        }

        var novosPorMesBruto = criacoes.GroupBy(ChaveDoMes).ToDictionary(g => g.Key, g => g.Count());
        var primeiroBalde = ChaveDoMes(deDozeMeses);

        var novosPorMes = Enumerable.Range(0, 12)
            .Select(i =>
            {
                var mes = primeiroBalde.AddMonths(i);
                novosPorMesBruto.TryGetValue(mes, out var total);
                return new PontoMensalResponse(mes, total);
            })
            .ToList();

        var (maisAtendidos, _) = await MontarClientesMaisAtendidosAsync(tenantId, de, ate, 10, ct);

        return new RelatorioDeClientesResponse(
            new PeriodoResponse(de, ate, granularidade),
            contagens?.Total ?? 0,
            contagens?.Novos ?? 0,
            comAtendimentoAtivo,
            contagens?.Fisica ?? 0,
            contagens?.Juridica ?? 0,
            contagens?.SemDocumento ?? 0,
            novosPorMes,
            maisAtendidos);
    }
}
