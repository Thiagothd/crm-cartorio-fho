namespace CrmCartorio.Api.Modules.Seguranca.Application;

/// <summary>
/// Leitura da trilha "Acesso aos dados" — quem abriu o quê, os eventos de
/// segurança do SEC-03 e o resumo de proteção ativa. Tudo aqui é
/// somente-leitura; a gravação da trilha é <c>IRegistradorDeAcesso</c>
/// (Infrastructure/Auditoria), chamado de dentro dos módulos que de fato leem
/// dado de cliente.
/// </summary>
public interface ISegurancaService
{
    Task<PaginaDeSeguranca<RegistroDeAcessoResponse>> ListarAcessosAsync(
        FiltroDeAcessos filtro, CancellationToken ct = default);

    Task<PaginaDeSeguranca<EventoDeSegurancaResponse>> ListarEventosAsync(
        FiltroDeEventos filtro, CancellationToken ct = default);

    Task<ProtecaoAtivaResponse> ObterProtecaoAtivaAsync(CancellationToken ct = default);
}
