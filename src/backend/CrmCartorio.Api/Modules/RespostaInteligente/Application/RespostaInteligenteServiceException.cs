using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application;

/// <remarks>
/// Raramente lançada: a falha de chamar a IA em si (chave ausente, timeout, gateway
/// fora do ar, resposta mal formada) é tratada dentro do serviço e vira
/// <c>PodeResponder = false</c>, nunca uma exceção — ver
/// <see cref="IRespostaInteligenteService"/>. Esta classe existe para o mau uso da
/// própria porta, não para falha da chamada externa.
/// </remarks>
public class RespostaInteligenteServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static RespostaInteligenteServiceException TenantObrigatorio() =>
        new("tenant_obrigatorio", "TenantId é obrigatório para montar o contexto da IA.", 400);
}
