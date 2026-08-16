using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Consultas.Application;

public class ConsultasServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static ConsultasServiceException CepInvalido() =>
        new("cep_invalido", "CEP deve ter 8 dígitos.", 400);

    public static ConsultasServiceException CnpjInvalido() =>
        new("cnpj_invalido", "CNPJ deve ter 14 dígitos.", 400);

    public static ConsultasServiceException NaoEncontrado() =>
        new("consulta_nao_encontrada", "Não encontrado na base pública.", 404);

    public static ConsultasServiceException Indisponivel() =>
        new("consulta_indisponivel",
            "Consulta indisponível no momento. Preencha manualmente.", 503);
}
