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
/// prompt — só metadado técnico da chamada. <see cref="MensagemId"/> é a única
/// exceção parcial a isso: não é conteúdo, mas é um ponteiro PARA conteúdo do
/// cliente. Por isso a Central de IA (que lê esta tabela sem filtro de tenant)
/// nunca projeta <see cref="MensagemId"/>, <see cref="ConversaId"/> nem
/// <see cref="MotivoDoFallback"/> — só os campos estruturados.
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

    /// <summary>
    /// Distingue fallback executado (Gemini discou e falhou) de preventivo (Gemini
    /// nem foi discado). Ver <see cref="Enums.TipoDeFallbackDeIa"/> — é o campo que
    /// corrige a leitura de <see cref="FoiFallback"/> sozinho não dar conta.
    /// </summary>
    public required TipoDeFallbackDeIa TipoDeFallback { get; set; }

    /// <summary>
    /// Mesmo valor nas duas tentativas de uma mesma pergunta do cliente (a que
    /// falhou e a que resolveu) — permite contar "perguntas", não só "tentativas",
    /// e medir a latência total que o cliente esperou de verdade. Nula em linha
    /// gravada antes desta coluna existir.
    /// </summary>
    public Guid? RodadaId { get; set; }

    /// <summary>
    /// Por que a IA não soube responder — categoria fechada devolvida pelo
    /// próprio modelo. Só preenchida quando <see cref="Sucesso"/> é true e
    /// <see cref="PodeResponder"/> é false. Nunca o texto livre do modelo — ver
    /// <see cref="Enums.MotivoDaNaoRespostaDeIa"/> para o porquê.
    /// </summary>
    public MotivoDaNaoRespostaDeIa? MotivoDaNaoResposta { get; set; }

    /// <summary>
    /// Snapshot de <c>Conversa.TipoServicoId</c> no INSTANTE desta chamada — não
    /// o estado final da conversa, que pode mudar depois. Sem FK, mesmo motivo de
    /// <see cref="ConversaId"/>. Nula quando a conversa ainda não tinha serviço
    /// definido, ou em linha anterior a esta coluna.
    /// </summary>
    public Guid? TipoServicoId { get; set; }

    /// <summary>Snapshot de <c>Conversa.SetorId</c> no instante desta chamada. Sem FK, mesmo motivo.</summary>
    public Guid? SetorId { get; set; }

    /// <summary>
    /// A mensagem do cliente que originou esta chamada. Sem FK. Ver a nota de
    /// privacidade da classe — é o único campo que aproxima esta linha de
    /// conteúdo do cliente, e por isso nunca é projetado pela Central de IA.
    /// </summary>
    public Guid? MensagemId { get; set; }
}
