namespace CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;

/// <summary>
/// Por que uma tentativa de provedor não resultou em resposta. Persistido em
/// <see cref="Entities.ChamadaDeIa"/> como <c>tinyint</c> — a ordem destes valores
/// nunca muda depois de gravado no banco.
/// </summary>
/// <remarks>
/// <see cref="Nenhuma"/> é o valor de uma tentativa bem-sucedida — inclui tanto
/// <c>podeResponder=true</c> quanto <c>podeResponder=false</c>, porque as duas são
/// respostas válidas do modelo, não falha de transporte. Só o que impede a IA de
/// responder de verdade (erro de rede, de configuração, de cota) ganha um valor
/// próprio aqui.
/// </remarks>
public enum TipoDeFalhaDeIa : byte
{
    Nenhuma = 0,
    RateLimit = 1,
    QuotaEsgotada = 2,
    Indisponibilidade = 3,
    Timeout = 4,
    Rede = 5,
    Configuracao = 6,
    RespostaInvalida = 7,
}
