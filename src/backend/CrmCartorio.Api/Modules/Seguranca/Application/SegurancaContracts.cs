using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

namespace CrmCartorio.Api.Modules.Seguranca.Application;

/// <summary>Uma linha da trilha "Acesso aos dados" — nomes já resolvidos, nunca conteúdo.</summary>
public record RegistroDeAcessoResponse(
    Guid Id,
    DateTime OcorridoEm,
    TipoDeAcesso Tipo,
    string? AtendenteNome,
    string? AtorExterno,
    string? ConversaProtocolo,
    string? ClienteNome,
    bool TemAnexo,
    Guid? AutorizacaoDeAcessoTecnicoId,
    string? IpDeOrigem);

public record FiltroDeAcessos(
    DateTime? De,
    DateTime? Ate,
    Guid? AtendenteId,
    Guid? ConversaId,
    int Pagina = 1,
    int Tamanho = 50);

/// <summary>Um fato de <c>EventosDeSeguranca</c> — mesma tabela do SEC-03, agora com tela própria.</summary>
public record EventoDeSegurancaResponse(
    Guid Id,
    DateTime OcorridoEm,
    TipoDeEventoDeSeguranca Tipo,
    string? AtendenteNome,
    string? AtorAtendenteNome,
    string? AtorExterno,
    string? IpDeOrigem,
    string? Motivo);

public record FiltroDeEventos(
    DateTime? De,
    DateTime? Ate,
    int Pagina = 1,
    int Tamanho = 50);

/// <summary>
/// O bloco "Proteção ativa" da tela — resumo do que já está em vigor, para o
/// cartório conferir sem precisar ler código ou documentação.
/// </summary>
public record ProtecaoAtivaResponse(
    bool CifraAtiva,
    string? VersaoDaChave,
    DateTime? CifraDesde,
    long TotalDeRegistrosDeAcesso);

/// <summary>
/// Paginação local ao módulo (CLAUDE.md 7.1 — cada módulo tem o próprio
/// contrato de página, nunca importa o de outro).
/// </summary>
public record PaginaDeSeguranca<T>(IReadOnlyList<T> Itens, int Total, int Pagina, int Tamanho);
