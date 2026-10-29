using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.Clientes.Domain;
using CrmCartorio.Api.Modules.Devedores.Domain.Enums;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

/// <summary>
/// Forma achatada da conversa com os nomes de cliente, atendente, setor, etapa e
/// servico ja resolvidos.
/// </summary>
/// <remarks>
/// Os nomes vem por subconsulta e nao por propriedade de navegacao: adicionar
/// navegacao de <see cref="Conversa"/> para as entidades de Clientes, Usuarios,
/// Configuracoes e Servicos acoplaria o dominio deste slice ao dominio dos outros
/// quatro. Na escala de um cartorio, o custo dessas subconsultas e irrelevante
/// perto do custo de embaralhar as fronteiras dos modulos.
/// </remarks>
public sealed record ProjecaoDeConversa(
    Guid Id,
    string Protocolo,
    Guid ClienteId,
    string ClienteNome,
    string ClienteTelefone,
    Guid? AtendenteId,
    string? AtendenteNome,
    Guid? SetorId,
    string? SetorNome,
    Guid? EtapaId,
    string? EtapaNome,
    string? EtapaCor,
    Guid? TipoServicoId,
    string? TipoServicoNome,
    StatusConversa Status,
    DateTime IniciadaEm,
    DateTime UltimaMensagemEm,
    int MensagensNaoLidas,
    bool AguardandoTriagem,
    DateTime? PrazoIniciadoEm,
    DateTime? PrazoLimite,
    DateTime? PrazoAlertaEm,
    string? PreviaUltimaMensagem,
    DateTime? EncerradaEm,
    string? EncerradaPorNome,
    string? MotivoEncerramento,
    DateTime? ReabertaEm,
    string? ClienteFotoPerfilUrl,
    bool AtendidaPeloAparelho,
    bool EmAtendimentoHumano,
    DateTime? RoboPausadoEm,
    string? RoboPausadoPorNome,
    bool ClienteSemRobo,
    int ClientePendenciasAbertas,
    decimal ClienteSaldoDevedor)
{
    /// <summary>
    /// Projeta conversas com os nomes resolvidos.
    /// </summary>
    /// <remarks>
    /// ORDENE A <paramref name="origem"/>, NAO O RESULTADO. Aplicar OrderBy sobre o
    /// IQueryable devolvido faz o EF tentar ordenar pelo objeto projetado inteiro —
    /// as subconsultas entram na clausula ORDER BY, nada disso traduz e a consulta
    /// estoura em tempo de execucao. Skip/Take depois da projecao sao seguros.
    /// </remarks>
    public static IQueryable<ProjecaoDeConversa> De(CrmDbContext db, IQueryable<Conversa> origem) =>
        origem.Select(c => new ProjecaoDeConversa(
            c.Id,
            c.Protocolo,
            c.ClienteId,
            db.Clientes.Where(x => x.Id == c.ClienteId).Select(x => x.Nome).FirstOrDefault() ?? string.Empty,
            db.Clientes.Where(x => x.Id == c.ClienteId).Select(x => x.TelefoneWhatsapp).FirstOrDefault() ?? string.Empty,
            c.AtendenteId,
            db.Atendentes.Where(x => x.Id == c.AtendenteId).Select(x => x.Nome).FirstOrDefault(),
            c.SetorId,
            db.Setores.Where(x => x.Id == c.SetorId).Select(x => x.Nome).FirstOrDefault(),
            c.EtapaId,
            db.EtapasFunil.Where(x => x.Id == c.EtapaId).Select(x => x.Nome).FirstOrDefault(),
            db.EtapasFunil.Where(x => x.Id == c.EtapaId).Select(x => x.Cor).FirstOrDefault(),
            c.TipoServicoId,
            db.TiposServico.Where(x => x.Id == c.TipoServicoId).Select(x => x.Nome).FirstOrDefault(),
            c.Status,
            c.IniciadaEm,
            c.UltimaMensagemEm,
            c.MensagensNaoLidas,
            c.AguardandoTriagem,
            c.PrazoIniciadoEm,
            c.PrazoLimite,
            c.PrazoAlertaEm,
            db.Mensagens
                .Where(m => m.ConversaId == c.Id)
                .OrderByDescending(m => m.EnviadaEm)
                .Select(m => m.Conteudo)
                .FirstOrDefault(),
            c.EncerradaEm,
            db.Atendentes.Where(x => x.Id == c.EncerradaPorAtendenteId).Select(x => x.Nome).FirstOrDefault(),
            c.MotivoEncerramento,
            c.ReabertaEm,
            db.Clientes.Where(x => x.Id == c.ClienteId).Select(x => x.FotoPerfilUrl).FirstOrDefault(),

            // O selo so vale sem dono no painel. AtendidaPeloAparelhoEm ja e
            // apagado quando alguem assume, mas a leitura nao depende disso para
            // nunca mostrar "No celular" numa conversa que tem responsavel.
            c.AtendidaPeloAparelhoEm != null && c.AtendenteId == null,
            c.EmAtendimentoHumano,
            c.RoboPausadoEm,
            db.Atendentes.Where(x => x.Id == c.RoboPausadoPorAtendenteId).Select(x => x.Nome).FirstOrDefault(),
            db.Clientes.Where(x => x.Id == c.ClienteId).Select(x => x.AtendimentoSemRobo).FirstOrDefault(),
            db.PendenciasFinanceiras.Count(p =>
                p.TenantId == c.TenantId && p.ClienteId == c.ClienteId && p.Status == StatusPendencia.Aberta),
            db.PendenciasFinanceiras
                .Where(p => p.TenantId == c.TenantId
                         && p.ClienteId == c.ClienteId
                         && p.Status == StatusPendencia.Aberta
                         && p.ValorOriginal != null
                         && p.ValorOriginal > p.ValorPago)
                .Select(p => (decimal?)(p.ValorOriginal!.Value - p.ValorPago))
                .Sum() ?? 0m));

    /// <summary>
    /// A situacao de prazo depende do relogio, entao e resolvida aqui, na memoria,
    /// e nunca no banco. Quando <paramref name="podeVerDevedores"/> e <c>false</c>,
    /// os contadores financeiros sao mascarados com zero.
    /// </summary>
    public ConversaResumoResponse ParaResposta(DateTime agora, bool podeVerDevedores = true) =>
        new(Id,
            Protocolo,
            ClienteId,
            ClienteNome,
            ClienteTelefone,
            AtendenteId,
            AtendenteNome,
            SetorId,
            SetorNome,
            EtapaId,
            EtapaNome,
            EtapaCor,
            TipoServicoId,
            TipoServicoNome,
            Status,
            IniciadaEm,
            UltimaMensagemEm,
            MensagensNaoLidas,
            AguardandoTriagem,
            PrazoIniciadoEm,
            PrazoLimite,
            AvaliadorDeSituacaoPrazo.Avaliar(Status, PrazoLimite, PrazoAlertaEm, EncerradaEm, agora),
            PreviaUltimaMensagem,
            EncerradaEm,
            EncerradaPorNome,
            MotivoEncerramento,
            ReabertaEm,
            $"/api/clientes/{ClienteId}/foto-perfil",
            // Calculado na leitura, nunca persistido — Cliente.Nome guarda o que
            // veio do WhatsApp e o painel decide como apresentar, mesmo principio
            // de PercentualDeQualificacao.
            !NomeDeContato.EhUtil(ClienteNome),
            AtendidaPeloAparelho,
            EmAtendimentoHumano,
            RoboPausadoEm,
            RoboPausadoPorNome,
            ClienteSemRobo,
            podeVerDevedores ? ClientePendenciasAbertas : 0,
            podeVerDevedores ? ClienteSaldoDevedor : 0m);
}

