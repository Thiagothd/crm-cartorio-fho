using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;

namespace CrmCartorio.Api.Modules.Seguranca.Domain.Entities;

/// <summary>
/// Registro append-only de quem leu o quê. É a resposta, visível na tela ao
/// cartório, para "o programador consegue ver as conversas do meu cliente
/// sem eu saber?" — ver docs/PROTECAO-DE-DADOS.md.
/// </summary>
/// <remarks>
/// Mesmo espírito de <c>EventoDeSeguranca</c> (fato, nunca conteúdo), mas
/// volume e semântica diferentes: leitura é rotina do dia a dia (cada
/// abertura de conversa), não evento raro — por isso vive em tabela própria,
/// com deduplicação na gravação (<c>IRegistradorDeAcesso</c>), em vez de
/// entrar em <c>EventosDeSeguranca</c>.
///
/// <b>Nunca grava conteúdo</b> — só o ponteiro (<see cref="ConversaId"/>,
/// <see cref="ClienteId"/>, <see cref="MensagemId"/>) e o fato de que alguém
/// abriu aquilo.
///
/// Somente-inserção reforçado por trigger no banco
/// (<c>TR_RegistrosDeAcesso_SomenteInsercao</c>, sql/045) — ver o comentário
/// do trigger para o limite honesto disso.
/// </remarks>
public class RegistroDeAcesso : EntidadeBase
{
    public required Guid TenantId { get; set; }

    /// <summary>Quem leu, quando foi alguém de dentro do cartório. Nulo quando foi um ator externo.</summary>
    public Guid? AtendenteId { get; set; }

    /// <summary>
    /// Identidade de quem leu FORA do modelo Atendente/Tenant comum — mesmo
    /// papel que em <c>EventoDeSeguranca.AtorExterno</c>. Hoje não preenchido
    /// (nenhum leitor externo existe ainda); reservado para a conta técnica
    /// da equipe do projeto, se um dia ela precisar ser distinta de um Atendente.
    /// </summary>
    public string? AtorExterno { get; set; }

    public required TipoDeAcesso Tipo { get; set; }

    public Guid? ConversaId { get; set; }

    public Guid? ClienteId { get; set; }

    public Guid? MensagemId { get; set; }

    /// <summary>
    /// Preenchida quando a leitura aconteceu sob um acesso técnico
    /// autorizado — é o que faz a trilha mostrar "via acesso técnico #…" em
    /// vez de deixar a leitura de uma conta técnica parecer leitura comum.
    /// </summary>
    public Guid? AutorizacaoDeAcessoTecnicoId { get; set; }

    public string? IpDeOrigem { get; set; }

    public required DateTime OcorridoEm { get; set; }
}
