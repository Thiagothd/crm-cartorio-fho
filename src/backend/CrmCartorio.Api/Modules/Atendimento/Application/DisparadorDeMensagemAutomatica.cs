using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
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
    private readonly Dictionary<Guid, IReadOnlyList<TriagemAutomatica.OpcaoDeSetor>> _setores = [];

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

    public async Task<IReadOnlyList<TriagemAutomatica.OpcaoDeSetor>> SetoresDoMenuAsync(
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
            .Select(s => new TriagemAutomatica.OpcaoDeSetor(s.Id, s.Nome))
            .ToListAsync(ct);

        _setores[tenantId] = setores;
        return setores;
    }

    private async Task<DadosParaMensagem> MontarDadosAsync(Conversa conversa, CancellationToken ct)
    {
        var clienteNome = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == conversa.ClienteId)
            .Select(c => c.Nome)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var servico = conversa.TipoServicoId is { } servicoId
            ? await db.TiposServico.AsNoTracking()
                .Where(t => t.Id == servicoId)
                .Select(t => t.Nome)
                .FirstOrDefaultAsync(ct)
            : null;

        var setores = await SetoresDoMenuAsync(conversa.TenantId, ct);

        return new DadosParaMensagem(
            clienteNome,
            await NomeDoCartorioAsync(conversa.TenantId, ct),
            servico,
            conversa.Protocolo,
            TriagemAutomatica.MontarMenu(setores));
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
