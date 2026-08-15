using System.Text.RegularExpressions;

namespace CrmCartorio.Api.Modules.Configuracoes.Domain;

/// <summary>
/// Valores que preenchem os marcadores de um modelo de mensagem.
/// </summary>
/// <param name="Servico">
/// Nome do tipo de servico da conversa. Nulo enquanto o servico nao foi captado —
/// nesse caso o marcador resolve para <see cref="PlaceholdersDeMensagem.ServicoNaoInformado"/>.
/// </param>
/// <param name="MenuSetores">
/// Lista numerada dos setores ativos, montada por quem dispara. Vem de fora porque
/// a numeracao tem de ser a MESMA que a triagem usa para interpretar a resposta do
/// cliente — se as duas listas divergirem, "1" leva ao setor errado.
/// </param>
public sealed record DadosParaMensagem(
    string NomeCliente,
    string NomeCartorio,
    string? Servico,
    string Protocolo,
    string MenuSetores);

/// <summary>
/// Marcadores aceitos no texto de <see cref="Entities.MensagemAutomatica"/> e de
/// <see cref="Entities.RespostaRapida"/>, e a substituicao deles.
/// </summary>
/// <remarks>
/// Substituicao literal, sem expressao nem condicional. Um modelo de mensagem nao e
/// linguagem de programacao: se o cartório parceiro precisar de texto diferente por situacao, a
/// resposta e cadastrar duas mensagens com gatilhos diferentes, nao inventar um
/// "se" dentro do campo.
///
/// A lista de tokens e espelhada no frontend em
/// <c>src/features/atendimento/placeholders.ts</c>, que faz a mesma substituicao ao
/// inserir uma resposta rapida no campo de digitacao. Mexer aqui exige mexer la.
/// </remarks>
public static partial class PlaceholdersDeMensagem
{
    /// <summary>
    /// Texto que entra em {Servico} quando a conversa ainda nao tem servico captado.
    /// Escolhido para nao criar concordancia de genero na frase — "Servico: nao
    /// informado" funciona, "certidao esta pronto" nao.
    /// </summary>
    public const string ServicoNaoInformado = "não informado";

    public sealed record Marcador(string Token, string Descricao);

    public static readonly IReadOnlyList<Marcador> Catalogo =
    [
        new("{NomeCliente}", "Nome do cliente como esta no cadastro."),
        new("{NomeCartorio}", "Nome fantasia da serventia, ou a razao social quando nao houver."),
        new("{Servico}", "Tipo de servico vinculado ao atendimento."),
        new("{Protocolo}", "Protocolo do atendimento, no formato AAAAMM-NNNN."),
        new("{MenuSetores}", "Lista numerada dos setores ativos. Usada no menu de triagem.")
    ];

    public static string Aplicar(string modelo, DadosParaMensagem dados) =>
        modelo
            .Replace("{NomeCliente}", dados.NomeCliente, StringComparison.Ordinal)
            .Replace("{NomeCartorio}", dados.NomeCartorio, StringComparison.Ordinal)
            .Replace(
                "{Servico}",
                string.IsNullOrWhiteSpace(dados.Servico) ? ServicoNaoInformado : dados.Servico,
                StringComparison.Ordinal)
            .Replace("{Protocolo}", dados.Protocolo, StringComparison.Ordinal)
            .Replace("{MenuSetores}", dados.MenuSetores, StringComparison.Ordinal);

    /// <summary>
    /// Marcadores presentes no modelo que nao existem no <see cref="Catalogo"/>.
    /// </summary>
    /// <remarks>
    /// Existe para o cadastro RECUSAR o modelo, nao para consertar. Um
    /// "{NomeClinte}" digitado errado nao daria erro nenhum — sairia literal na
    /// mensagem que o cliente do cartorio le. Barrar no salvamento e a unica forma
    /// de esse erro nunca chegar la.
    /// </remarks>
    public static IReadOnlyList<string> MarcadoresDesconhecidos(string modelo)
    {
        var conhecidos = Catalogo.Select(m => m.Token).ToHashSet(StringComparer.Ordinal);

        return [.. RegexDeMarcador()
            .Matches(modelo)
            .Select(m => m.Value)
            .Where(token => !conhecidos.Contains(token))
            .Distinct(StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"\{[A-Za-z][A-Za-z0-9]*\}")]
    private static partial Regex RegexDeMarcador();
}
