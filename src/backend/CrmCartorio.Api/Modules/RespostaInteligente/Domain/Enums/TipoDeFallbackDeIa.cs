namespace CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;

/// <summary>
/// Distingue as duas formas de a tentativa ao Anthropic existir. Persistido em
/// <see cref="Entities.ChamadaDeIa"/> como <c>tinyint</c> — a ordem destes
/// valores nunca muda depois de gravado no banco.
/// </summary>
/// <remarks>
/// <see cref="Executado"/> é o Gemini que foi discado e falhou — existem DUAS
/// linhas para a mesma <see cref="Entities.ChamadaDeIa.RodadaId"/>, a que falhou
/// (com <see cref="Entities.ChamadaDeIa.FoiFallback"/> = false e
/// <see cref="NaoEhFallback"/>) e a que resolveu.
///
/// <see cref="PreventivoDesligado"/>, <see cref="PreventivoDisjuntor"/> e
/// <see cref="PreventivoMargemDeCota"/> são os três motivos de o Gemini NUNCA
/// ter sido discado — existe UMA linha só, do Anthropic. Sem esta distinção, uma
/// tela que conte "fallback" como "falha do Gemini" erra sempre que o Gemini foi
/// pulado por configuração, disjuntor ou cota: o Haiku responde sozinho e nenhuma
/// falha do Gemini chega a ser gravada.
///
/// <see cref="Indeterminado"/> é só para linha gravada antes desta coluna
/// existir — nunca escrito por código novo.
/// </remarks>
public enum TipoDeFallbackDeIa : byte
{
    NaoEhFallback = 0,
    Executado = 1,
    PreventivoDesligado = 2,
    PreventivoDisjuntor = 3,
    PreventivoMargemDeCota = 4,
    Indeterminado = 5,
}
