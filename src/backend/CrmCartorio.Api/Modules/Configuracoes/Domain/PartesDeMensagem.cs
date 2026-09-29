using System.Text.RegularExpressions;

namespace CrmCartorio.Api.Modules.Configuracoes.Domain;

/// <summary>
/// Divide um texto cadastrado em mais de uma mensagem de WhatsApp: uma linha só com
/// <c>---</c> separa uma mensagem da outra.
/// </summary>
/// <remarks>
/// POR QUE EXISTE
///     O WhatsApp não aceita mais de 4096 caracteres numa mensagem, e a coluna
///     <c>Mensagem.Conteudo</c> guarda até 4000. O texto do cartório parceiro para habilitação de
///     casamento (2026-09-29) tem 4217. Cortar o texto de um cartório não é opção;
///     dividir, sim — e quem escreve decide onde, pela tela.
///
/// Lógica pura, sem banco: o cadastro de serviços valida com ela e o disparador de
/// mensagens automáticas gera uma mensagem por parte, na ordem.
/// </remarks>
public static partial class PartesDeMensagem
{
    /// <summary>Teto de cada mensagem que sai, igual ao de <c>Mensagem.Conteudo</c>.</summary>
    public const int TamanhoMaximoDaParte = 4000;

    /// <summary>Teto do texto cadastrado, somadas as partes.</summary>
    public const int TamanhoMaximoDoTexto = 12000;

    [GeneratedRegex(@"^[ \t]*---[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Separador();

    /// <summary>
    /// As partes do texto, na ordem, sem as linhas em branco das pontas. Texto sem
    /// separador devolve ele mesmo, numa parte só.
    /// </summary>
    public static IReadOnlyList<string> Dividir(string texto) =>
    [
        .. Separador().Split(texto.Replace("\r\n", "\n"))
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
    ];
}
