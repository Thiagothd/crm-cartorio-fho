using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;

namespace CrmCartorio.Api.Modules.Seguranca.Application;

/// <summary>
/// Estado da autorização, sempre DERIVADO na leitura — nunca uma coluna
/// própria (ver <c>AutorizacaoDeAcessoTecnico</c>).
/// </summary>
public enum EstadoDeAutorizacao
{
    Pendente,
    Ativa,
    Expirada,
    Negada,
    Revogada
}

/// <param name="ConversaProtocolo">
/// Só quando <paramref name="Escopo"/> é <see cref="EscopoDeAcesso.Conversa"/>.
/// Protocolo, não o id interno — a conta técnica não tem `atendimento.ver`
/// antes de aprovada, então nunca teria como saber o Guid; o protocolo é o
/// que o cartório já comunica por fora (chat, ticket).
/// </param>
public record SolicitarAcessoTecnicoRequest(string Motivo, EscopoDeAcesso Escopo, string? ConversaProtocolo, int DuracaoMinutos);

public record AutorizacaoDeAcessoTecnicoResponse(
    Guid Id,
    Guid ContaTecnicaId,
    string ContaTecnicaNome,
    string Motivo,
    EscopoDeAcesso Escopo,
    Guid? ConversaId,
    string? ConversaProtocolo,
    int DuracaoSolicitadaMinutos,
    DateTime SolicitadaEm,
    EstadoDeAutorizacao Estado,
    string? AprovadaPorNome,
    DateTime? ValidaAte,
    string? NegadaPorNome,
    string? RevogadaPorNome);

public record FiltroDeAcessoTecnico(DateTime? De, DateTime? Ate, int Pagina = 1, int Tamanho = 50);

/// <summary>O que a faixa global mostra a QUALQUER usuário do tenant — nunca detalhe sensível.</summary>
public record AcessoTecnicoAtivoResponse(DateTime ValidaAte, string Motivo);
