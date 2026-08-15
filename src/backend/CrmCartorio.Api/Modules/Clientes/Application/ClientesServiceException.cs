using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Clientes.Application;

public class ClientesServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static ClientesServiceException NaoEncontrado() =>
        new("cliente_nao_encontrado", "Cliente nao encontrado.", 404);

    public static ClientesServiceException TelefoneInvalido() =>
        new("telefone_invalido",
            "Informe o WhatsApp com DDD, apenas numeros (ex.: 38999990001).", 400);

    public static ClientesServiceException NomeObrigatorio() =>
        new("nome_obrigatorio", "Informe o nome do cliente.", 400);

    public static ClientesServiceException TelefoneEmUso(string nome) =>
        new("telefone_em_uso",
            $"Este WhatsApp ja esta cadastrado para {nome}.", 409);

    public static ClientesServiceException CpfInvalido() =>
        new("cpf_invalido", "CPF deve ter 11 digitos.", 400);

    public static ClientesServiceException CnpjInvalido() =>
        new("cnpj_invalido", "CNPJ deve ter 14 digitos.", 400);

    public static ClientesServiceException CepInvalido() =>
        new("cep_invalido", "CEP deve ter 8 digitos.", 400);

    public static ClientesServiceException UfInvalida() =>
        new("uf_invalida", "UF deve ter 2 letras (ex.: MG).", 400);

    public static ClientesServiceException NascimentoNoFuturo() =>
        new("nascimento_no_futuro", "A data de nascimento nao pode ser futura.", 400);

    public static ClientesServiceException NascimentoImplausivel() =>
        new("nascimento_implausivel", "Confira a data de nascimento.", 400);
}
