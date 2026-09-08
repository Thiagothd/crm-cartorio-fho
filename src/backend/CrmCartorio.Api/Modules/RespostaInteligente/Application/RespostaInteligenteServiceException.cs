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

    public static RespostaInteligenteServiceException SemPermissao() =>
        new("sem_permissao", "Seu perfil não permite ver a situação da IA.", 403);

    // Conhecimento de IA -------------------------------------------------------

    public static RespostaInteligenteServiceException SemPermissaoParaGerirConhecimento() =>
        new("sem_permissao", "Seu perfil não permite alterar o conhecimento da IA.", 403);

    public static RespostaInteligenteServiceException ConhecimentoNaoEncontrado() =>
        new("conhecimento_nao_encontrado", "Conhecimento não encontrado.", 404);

    public static RespostaInteligenteServiceException TituloObrigatorio() =>
        new("titulo_obrigatorio", "Informe o título.", 400);

    public static RespostaInteligenteServiceException ConteudoObrigatorio() =>
        new("conteudo_obrigatorio", "Escreva o conteúdo do conhecimento.", 400);

    public static RespostaInteligenteServiceException TemaObrigatorio() =>
        new("tema_obrigatorio", "Informe o tema.", 400);

    public static RespostaInteligenteServiceException TipoServicoNaoEncontrado() =>
        new("tipo_servico_nao_encontrado", "Serviço não encontrado.", 404);

    public static RespostaInteligenteServiceException VigenciaInvalida() =>
        new("vigencia_invalida", "A data de início da vigência não pode ser depois da data de fim.", 400);
}
