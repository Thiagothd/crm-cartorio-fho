namespace CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;

/// <summary>
/// Por que a IA respondeu <c>podeResponder=false</c> — uma categoria fechada,
/// devolvida pelo próprio modelo. Persistido em <see cref="Entities.ChamadaDeIa"/>
/// como <c>tinyint</c>, só quando <see cref="Entities.ChamadaDeIa.Sucesso"/> é
/// true e <see cref="Entities.ChamadaDeIa.PodeResponder"/> é false — a ordem
/// destes valores nunca muda depois de gravado no banco.
/// </summary>
/// <remarks>
/// Deliberadamente uma categoria fechada, não o texto livre que o modelo também
/// devolve (<c>RespostaDaIA.Motivo</c>). Esse texto é gerado sobre a pergunta do
/// cliente e nada impede que cite o que foi perguntado — persistir o texto
/// quebraria a garantia que esta tabela sustenta desde a criação (nunca guarda
/// conteúdo de mensagem nem dado do cliente), e a Central de IA lê esta tabela
/// sem filtro de tenant. <see cref="NaoClassificado"/> é o piso seguro para
/// provedor que não devolver a categoria — a IA nunca inventa uma categoria só
/// para preencher.
/// </remarks>
public enum MotivoDaNaoRespostaDeIa : byte
{
    NaoClassificado = 0,
    PrecoNaoCadastrado = 1,
    PrazoNaoCadastrado = 2,
    DocumentoNaoCadastrado = 3,
    ServicoForaDoCatalogo = 4,
    OrientacaoJuridicaEspecifica = 5,
    ForaDoEscopoDaServentia = 6,
    PerguntaAmbigua = 7,
    Outro = 8,
}
