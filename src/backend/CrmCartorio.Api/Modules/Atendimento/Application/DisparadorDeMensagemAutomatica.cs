using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain;
using CrmCartorio.Api.Modules.Configuracoes.Domain;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <inheritdoc cref="IDisparadorDeMensagemAutomatica"/>
/// <remarks>
/// Le <c>MensagensAutomaticas</c> pelo DbContext, sem passar pelo Application de
/// Configuracoes: e um banco so, e essa e a fronteira que o projeto adotou
/// (CLAUDE.md, 7.1). O cadastro pertence a Configuracoes; transformar cadastro em
/// mensagem no fio pertence aqui.
/// </remarks>
public class DisparadorDeMensagemAutomatica(
    CrmDbContext db,
    ILogger<DisparadorDeMensagemAutomatica> log) : IDisparadorDeMensagemAutomatica
{
    /// <summary>Limite da coluna Mensagem.Conteudo.</summary>
    private const int TamanhoMaximoDaMensagem = 4000;

    // Memoria de escopo. O varredor de prazo processa varias conversas do mesmo
    // tenant numa passada; sem isto, cada uma releria o nome do cartorio e a lista
    // de setores. Escopo curto (uma requisicao ou uma varredura), entao nao ha
    // risco de servir cadastro velho depois de uma edicao.
    private readonly Dictionary<Guid, string> _nomeDoCartorio = [];
    private readonly Dictionary<Guid, IReadOnlyList<TriagemAutomatica.OpcaoDeMenu>> _setores = [];

    public async Task<IReadOnlyList<Mensagem>> EnfileirarAsync(
        Conversa conversa,
        GatilhoDeMensagem gatilho,
        Guid? etapaAlvoId = null,
        CancellationToken ct = default)
    {
        var consulta = db.MensagensAutomaticas.AsNoTracking()
            .Where(m => m.TenantId == conversa.TenantId && m.Ativo && m.Gatilho == gatilho);

        consulta = gatilho == GatilhoDeMensagem.EtapaAlterada
            ? consulta.Where(m => m.EtapaAlvoId == etapaAlvoId)
            : consulta;

        var modelos = await consulta
            .OrderBy(m => m.CriadoEm)
            .Select(m => m.Conteudo)
            .ToListAsync(ct);

        if (modelos.Count == 0)
        {
            // Nenhuma regra cadastrada para o evento e situacao normal, nao falha:
            // o cartório parceiro decide quais avisos quer automaticos desativando linhas na
            // tela. Silencio aqui e a configuracao dele sendo respeitada.
            return [];
        }

        var dados = await MontarDadosAsync(conversa, ct);
        var agora = DateTime.UtcNow;
        var geradas = new List<Mensagem>(modelos.Count);

        foreach (var modelo in modelos)
        {
            var texto = PlaceholdersDeMensagem.Aplicar(modelo, dados);

            if (texto.Length > TamanhoMaximoDaMensagem)
            {
                // O cadastro limita o MODELO a 1000 caracteres, mas {MenuSetores}
                // cresce com o numero de setores. Cortar e melhor que estourar a
                // coluna e derrubar a operacao que disparou o gatilho.
                log.LogWarning(
                    "Mensagem automatica de {Gatilho} truncada na conversa {ConversaId}: " +
                    "o texto resolvido passou de {Limite} caracteres",
                    gatilho, conversa.Id, TamanhoMaximoDaMensagem);

                texto = texto[..TamanhoMaximoDaMensagem];
            }

            var mensagem = new Mensagem
            {
                TenantId = conversa.TenantId,
                ConversaId = conversa.Id,

                // Sistema + EhAutomatica: mensagem endereçada ao cliente, nao
                // anotacao interna. Ver Mensagem.EhAutomatica.
                AutorTipo = AutorTipo.Sistema,
                AtendenteId = null,
                EhAutomatica = true,

                Conteudo = texto,
                Tipo = TipoMensagem.Texto,

                // FASE 1: gerada, nao entregue. Pendente e o unico estado honesto —
                // nao ha WhatsApp conectado para entregar nada. Na Fase 2 quem
                // promove para Enviada e o retorno da Graph API, e para
                // Entregue/Lida o webhook de status; nenhuma linha daqui muda.
                StatusEntrega = StatusEntrega.Pendente,
                EnviadaEm = agora
            };

            db.Mensagens.Add(mensagem);
            geradas.Add(mensagem);
        }

        // UltimaMensagemEm NAO e tocado. A regra do modulo e que so mensagem de
        // gente reordena a inbox (ver README); um aviso de prazo disparado de
        // madrugada jogaria dez conversas para o topo da fila sem que nada tivesse
        // acontecido com o cliente.
        log.LogInformation(
            "{Total} mensagem(ns) automatica(s) de {Gatilho} geradas na conversa {ConversaId}",
            geradas.Count, gatilho, conversa.Id);

        return geradas;
    }

    public async Task<IReadOnlyList<TriagemAutomatica.OpcaoDeMenu>> SetoresDoMenuAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        if (_setores.TryGetValue(tenantId, out var memorizados))
        {
            return memorizados;
        }

        // Mesma ordenacao de SetorService.ListarAsync — o menu tem de sair na ordem
        // que o cartorio configurou na tela de setores.
        var setores = await db.Setores.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Ativo)
            .OrderBy(s => s.Ordem)
            .ThenBy(s => s.Nome)
            .Select(s => new TriagemAutomatica.OpcaoDeMenu(s.Id, s.Nome))
            .ToListAsync(ct);

        _setores[tenantId] = setores;
        return setores;
    }

    public async Task<IReadOnlyList<TriagemAutomatica.OpcaoDeMenu>> ServicosDoMenuAsync(
        Guid tenantId,
        Guid setorId,
        CancellationToken ct = default)
    {
        // Sem memoria de escopo, ao contrario de SetoresDoMenuAsync: o disparador
        // atende varios setores por passada (varredor de prazo), e o custo de
        // memorizar por (tenant, setor) nao compensa para uma consulta indexada
        // (IX_TiposServico_Tenant_Setor_Ativo).
        return await db.TiposServico.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.SetorId == setorId && t.Ativo)
            .OrderBy(t => t.Nome)
            .Select(t => new TriagemAutomatica.OpcaoDeMenu(t.Id, t.Nome))
            .ToListAsync(ct);
    }

    private async Task<DadosParaMensagem> MontarDadosAsync(Conversa conversa, CancellationToken ct)
    {
        var nomeGravado = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == conversa.ClienteId)
            .Select(c => c.Nome)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        // Nome que nao serve para chamar alguem nao entra na mensagem: vira vazio,
        // e PlaceholdersDeMensagem remove o vocativo junto para nao sobrar virgula
        // orfa. E o que impede "Ola, .!" e "Ola, 5519990001488!" de chegarem a um
        // cliente de verdade — inclusive para quem ja estava na base assim.
        var clienteNome = NomeDeContato.EhUtil(nomeGravado) ? nomeGravado : string.Empty;

        var dadosDoServico = conversa.TipoServicoId is { } servicoId
            ? await db.TiposServico.AsNoTracking()
                .Where(t => t.Id == servicoId)
                .Select(t => new
                {
                    t.Nome,
                    t.DocumentosNecessarios,
                    PrazoHoras = t.Prazo != null && t.Prazo.Ativo ? (int?)t.Prazo.PrazoHoras : null,
                    ApenasDiasUteis = t.Prazo != null && t.Prazo.Ativo && t.Prazo.ApenasDiasUteis
                })
                .FirstOrDefaultAsync(ct)
            : null;

        var setores = await SetoresDoMenuAsync(conversa.TenantId, ct);

        // So faz sentido com setor ja roteado — antes disso a conversa nem chegou
        // ao ponto de perguntar qual servico.
        var menuServicos = conversa.SetorId is { } setorId
            ? TriagemAutomatica.MontarMenu(await ServicosDoMenuAsync(conversa.TenantId, setorId, ct))
            : "";

        return new DadosParaMensagem(
            clienteNome,
            await NomeDoCartorioAsync(conversa.TenantId, ct),
            dadosDoServico?.Nome,
            conversa.Protocolo,
            TriagemAutomatica.MontarMenu(setores),
            menuServicos,
            dadosDoServico?.DocumentosNecessarios,
            dadosDoServico?.PrazoHoras is { } prazoHoras
                ? FormatarPrazo(prazoHoras, dadosDoServico.ApenasDiasUteis)
                : null);
    }

    /// <summary>
    /// Mesma logica de TipoServicoService.DescreverPrazo (Modules/Servicos), sem
    /// chamar aquele Service — modulo nao chama Application de outro modulo
    /// (CLAUDE.md, 7.1). Formatacao pura, pequena o bastante para nao valer um
    /// tipo compartilhado so para isto.
    /// </summary>
    private static string FormatarPrazo(int prazoHoras, bool apenasDiasUteis)
    {
        if (prazoHoras < 24)
        {
            return prazoHoras == 1 ? "até 1 hora" : $"até {prazoHoras} horas";
        }

        var dias = prazoHoras / 24;
        var sufixo = apenasDiasUteis ? " úteis" : " corridos";

        return dias == 1 ? $"até 1 dia{sufixo}" : $"até {dias} dias{sufixo}";
    }

    private async Task<string> NomeDoCartorioAsync(Guid tenantId, CancellationToken ct)
    {
        if (_nomeDoCartorio.TryGetValue(tenantId, out var memorizado))
        {
            return memorizado;
        }

        var nome = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.NomeFantasia ?? t.RazaoSocial)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        _nomeDoCartorio[tenantId] = nome;
        return nome;
    }
}
