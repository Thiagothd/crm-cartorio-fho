using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;

/// <summary>
/// Uma tentativa de chamada a um provedor de IA — uma linha por tentativa, duas
/// quando há fallback (a que falhou e a que resolveu).
/// </summary>
/// <remarks>
/// Gravada por <see cref="Application.Cota.RegistradorDeChamadas"/> num escopo de
/// <c>DbContext</c> próprio, independente da transação de negócio que originou a
/// chamada — precisa sobreviver mesmo quando os dois provedores falham e nenhuma
/// <see cref="Atendimento.Domain.Entities.Mensagem"/> chega a ser criada, e mesmo
/// quando essa transação sofre rollback. É também a fonte de hidratação dos
/// contadores de cota (<see cref="Application.Cota.ContadorDeCotaDoGemini"/>,
/// <see cref="Application.Cota.DisjuntorDoGemini"/>) depois de uma reciclagem do
/// App Pool.
///
/// Nunca grava conteúdo de mensagem, dado do cliente, chave de API ou trecho de
/// prompt — só metadado técnico da chamada.
/// </remarks>
public class ChamadaDeIa : EntidadeBase, IEntidadeDeTenant
{
    public required Guid TenantId { get; set; }

    /// <summary>Nula quando a chamada não está associada a uma conversa específica (ex.: sonda do disjuntor).</summary>
    public Guid? ConversaId { get; set; }

    /// <summary>
    /// Fingerprint não secreto da chave do Gemini (16 caracteres hex — 8 bytes —
    /// do SHA-256) — nunca a chave em si. Particiona o contador global de
    /// RPM/TPM/RPD por projeto Google real, e evita que uma rotação de chave
    /// misture contagem antiga com a nova depois de reiniciar o processo. Nula
    /// para chamadas ao Anthropic, que não tem cota compartilhada entre tenants.
    /// </summary>
    public string? ChaveDeProjeto { get; set; }

    public required ProvedorDeIa Provedor { get; set; }

    public required string Modelo { get; set; }

    public required DateTime IniciadaEm { get; set; }

    public required int LatenciaMs { get; set; }

    /// <summary>A tentativa produziu uma resposta do modelo (com ou sem conteúdo útil) — não é falha de transporte.</summary>
    public required bool Sucesso { get; set; }

    /// <summary>
    /// Nulo quando <see cref="Sucesso"/> é falso. Quando verdadeiro, é o
    /// <c>podeResponder</c> devolvido pelo modelo — inclui o caso em que o modelo
    /// respondeu, mas disse que não sabe.
    /// </summary>
    public bool? PodeResponder { get; set; }

    public TipoDeFalhaDeIa TipoDeFalha { get; set; } = TipoDeFalhaDeIa.Nenhuma;

    public int? CodigoHttp { get; set; }

    /// <summary>
    /// Por que esta chamada foi feita como fallback (nula quando é a tentativa
    /// principal). Preenchida tanto quando o Gemini foi tentado e falhou (com o
    /// detalhe daquela falha) quanto quando o Gemini foi pulado preventivamente
    /// (margem de segurança, disjuntor aberto) — nesse segundo caso não existe uma
    /// linha própria do Gemini, porque nenhuma chamada HTTP chegou a sair.
    /// </summary>
    public string? MotivoDoFallback { get; set; }

    public required bool FoiFallback { get; set; }

    public int TokensEntrada { get; set; }

    public int TokensSaida { get; set; }

    public int TokensPensamento { get; set; }

    /// <summary>Estimativa de custo em dólar — sempre nula para o Gemini (gratuito na franquia).</summary>
    public decimal? CustoEstimadoUsd { get; set; }

    /// <summary><c>low</c>/<c>medium</c>/<c>high</c> quando o provedor é o Gemini; nulo para o Anthropic.</summary>
    public string? NivelDeRaciocinio { get; set; }
}
