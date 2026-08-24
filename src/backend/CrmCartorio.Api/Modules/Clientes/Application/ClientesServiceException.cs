using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Clientes.Application;

/// <summary>
/// Recusas do cadastro de cliente.
/// </summary>
/// <remarks>
/// O texto destas mensagens vai PARA DEBAIXO DO CAMPO na tela, e nao mais para
/// um toast que passa: o dialogo usa o `codigo` para achar o campo culpado e
/// mostra a mensagem ali, fixa, ate o atendente corrigir. Por isso levam acento
/// (CLAUDE.md 7.2 permite em texto exibido) e falam do que fazer, nao do que
/// esta errado por dentro.
/// </remarks>
public class ClientesServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static ClientesServiceException NaoEncontrado() =>
        new("cliente_nao_encontrado", "Cliente não encontrado.", 404);

    // Nao diz mais "apenas numeros": o campo tem mascara e mostra "(38) 99999-0001".
    // Mandar digitar so numero contradizia o que estava na tela.
    public static ClientesServiceException TelefoneInvalido() =>
        new("telefone_invalido",
            "Informe o telefone com DDD (ex.: (38) 99999-0001).", 400);

    public static ClientesServiceException NomeObrigatorio() =>
        new("nome_obrigatorio", "Informe o nome do cliente.", 400);

    public static ClientesServiceException TelefoneEmUso(string nome) =>
        new("telefone_em_uso",
            $"Este WhatsApp já está cadastrado para {nome}.", 409);

    public static ClientesServiceException CpfInvalido() =>
        new("cpf_invalido", "CPF deve ter 11 dígitos.", 400);

    public static ClientesServiceException CnpjInvalido() =>
        new("cnpj_invalido", "CNPJ deve ter 14 dígitos.", 400);

    public static ClientesServiceException CepInvalido() =>
        new("cep_invalido", "CEP deve ter 8 dígitos.", 400);

    public static ClientesServiceException UfInvalida() =>
        new("uf_invalida", "UF deve ter 2 letras (ex.: MG).", 400);

    public static ClientesServiceException NascimentoNoFuturo() =>
        new("nascimento_no_futuro", "A data de nascimento não pode ser futura.", 400);

    public static ClientesServiceException NascimentoImplausivel() =>
        new("nascimento_implausivel", "Confira a data de nascimento.", 400);

    public static ClientesServiceException SemPermissao() =>
        new("sem_permissao", "Seu perfil não permite alterar cadastros.", 403);

    public static ClientesServiceException TextoDeImportacaoObrigatorio() =>
        new("texto_de_importacao_obrigatorio", "Cole a lista de contatos ou escolha um arquivo.", 400);

    /// <summary>
    /// A instância não devolveu contatos — pode ser porque de fato não há
    /// nenhum, ou porque a Evolution não está persistindo contatos nesta
    /// instância. A tela precisa dizer isto e não "falhou", ver
    /// docs/DEPLOY-WHATSAPP.md.
    /// </summary>
    public static ClientesServiceException GatewayIndisponivelParaImportacao() =>
        new("gateway_indisponivel_para_importacao",
            "Não foi possível consultar os contatos do WhatsApp agora. Tente de novo em instantes.", 503);

    public static ClientesServiceException ImportacaoMuitoGrande(int total) =>
        new("importacao_muito_grande",
            $"A lista tem {total} linhas. Separe em arquivos de até 5.000 linhas.", 400);
}
