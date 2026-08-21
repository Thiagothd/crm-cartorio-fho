using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Indicadores.Application;

public class IndicadoresServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static IndicadoresServiceException SemPermissao() =>
        new("sem_permissao", "Seu perfil não permite ver relatórios da operação.", 403);
}
