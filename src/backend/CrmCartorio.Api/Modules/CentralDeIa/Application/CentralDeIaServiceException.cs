using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.CentralDeIa.Application;

/// <remarks>
/// Só para mau uso da própria porta (segredo errado ou ausente) — mesmo espírito
/// de <c>RespostaInteligenteServiceException</c>: a Central não tem outro tipo de
/// falha operacional para modelar, porque não chama provedor nenhum, só lê
/// <c>ChamadasDeIa</c> já gravado por quem chamou.
/// </remarks>
public class CentralDeIaServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static CentralDeIaServiceException SegredoInvalido() =>
        new("segredo_invalido", "Segredo inválido.", 401);
}
