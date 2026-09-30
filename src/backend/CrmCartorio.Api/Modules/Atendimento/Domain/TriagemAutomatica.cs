using System.Globalization;
using System.Text;

namespace CrmCartorio.Api.Modules.Atendimento.Domain;

/// <summary>
/// Menu numerado de escolha (setor, e depois servico) e leitura da resposta do
/// cliente.
/// </summary>
/// <remarks>
/// Logica pura, sem banco e sem relogio, no mesmo lugar que
/// <see cref="AvaliadorDeSituacaoPrazo"/> — e regra de dominio, nao consulta.
///
/// O menu e a interpretacao da resposta moram na MESMA classe de proposito: os dois
/// dependem da mesma lista de opcoes na mesma ordem. Separados, uma alteracao de
/// ordenacao em um lado faria "1" levar a opcao errada no outro, e o sintoma
/// apareceria semanas depois, na conversa de um cliente real.
///
/// Serve os DOIS niveis da triagem automatica — setor (<c>{MenuSetores}</c>) e,
/// dentro do setor escolhido, servico (<c>{MenuServicos}</c>) — com o mesmo
/// algoritmo de numero/nome-exato/nome-parcial. Duplicar isso por nivel e o tipo
/// de risco que o paragrafo acima ja avisa.
/// </remarks>
public static class TriagemAutomatica
{
    /// <summary>Opcao oferecida no menu. A ordem da lista E a numeracao mostrada.</summary>
    public sealed record OpcaoDeMenu(Guid Id, string Nome);

    /// <summary>
    /// Minimo de caracteres para aceitar correspondencia parcial de nome.
    /// </summary>
    /// <remarks>
    /// Sem piso, um "a" solto casaria com "Tabelionato de Notas". Tres e o menor
    /// valor que ainda aceita as abreviacoes que uma pessoa realmente digita
    /// ("notas", "civil") sem transformar ruido em escolha.
    /// </remarks>
    private const int MinimoParaParcial = 3;

    /// <summary>
    /// Lista numerada das opcoes, para os marcadores {MenuSetores}/{MenuServicos}.
    /// </summary>
    public static string MontarMenu(IReadOnlyList<OpcaoDeMenu> opcoes) =>
        string.Join(
            Environment.NewLine,
            opcoes.Select((o, i) => $"{i + 1} - {o.Nome}"));

    /// <summary>
    /// Opcao escolhida pelo cliente, ou nulo quando a resposta nao corresponde a
    /// nenhuma opcao — ou corresponde a mais de uma.
    /// </summary>
    /// <remarks>
    /// Ambiguidade devolve nulo de proposito. Chutar entre duas opcoes manda o
    /// cliente para a fila errada e o erro so aparece quando alguem le a conversa;
    /// cair para atendimento humano, no mesmo caminho de quem digitou qualquer
    /// coisa, custa uma mensagem e nao erra.
    /// </remarks>
    public static Guid? InterpretarResposta(string? resposta, IReadOnlyList<OpcaoDeMenu> opcoes)
    {
        if (opcoes.Count == 0)
        {
            return null;
        }

        var texto = Normalizar(resposta);
        if (texto.Length == 0)
        {
            return null;
        }

        // 1. Numero da opcao. E o caminho que o menu pede, entao vem primeiro.
        if (int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var escolha)
            && escolha >= 1
            && escolha <= opcoes.Count)
        {
            return opcoes[escolha - 1].Id;
        }

        var normalizados = opcoes
            .Select(o => (o.Id, Nome: Normalizar(o.Nome)))
            .ToList();

        // 2. Nome exato.
        var exatos = normalizados.Where(s => s.Nome == texto).ToList();
        if (exatos.Count == 1)
        {
            return exatos[0].Id;
        }

        // 3. Parcial, nas duas direcoes: "quero registro civil" contem o nome, e
        //    "notas" esta contido nele. Uma correspondencia so ou nenhuma.
        if (texto.Length >= MinimoParaParcial)
        {
            var parciais = normalizados
                .Where(s => s.Nome.Contains(texto, StringComparison.Ordinal)
                         || texto.Contains(s.Nome, StringComparison.Ordinal))
                .ToList();

            if (parciais.Count == 1)
            {
                return parciais[0].Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Palavras que pedem atendimento humano explicitamente — a IA nem chega a
    /// ser chamada quando o cliente ja disse isso.
    /// </summary>
    private static readonly string[] PalavrasDePedidoDeHumano =
        ["atendente", "humano", "pessoa de verdade", "falar com alguem"];

    /// <summary>
    /// Palavras que pedem para reabrir o atendimento do zero, a qualquer momento
    /// da conversa — nao so quando uma flag especifica esta esperando resposta.
    /// </summary>
    private static readonly string[] PalavrasDeReabrirMenu =
        ["menu", "voltar", "inicio", "reiniciar", "recomecar"];

    /// <summary>O cliente pediu, em texto livre, para falar com uma pessoa.</summary>
    /// <remarks>
    /// Normalizado pelo mesmo <see cref="Normalizar"/> do resto da classe — "Falar
    /// com Alguém!", "ALGUEM" e "alguem" caem no mesmo texto comparavel, sem
    /// precisar listar cada variante de acentuacao a mao.
    /// </remarks>
    public static bool PediuHumano(string? conteudo)
    {
        var normalizado = Normalizar(conteudo);
        return PalavrasDePedidoDeHumano.Any(palavra => normalizado.Contains(palavra, StringComparison.Ordinal));
    }

    /// <summary>O cliente pediu, em texto livre, para voltar ao menu inicial.</summary>
    public static bool PediuMenu(string? conteudo)
    {
        var normalizado = Normalizar(conteudo);
        return PalavrasDeReabrirMenu.Any(palavra => normalizado.Contains(palavra, StringComparison.Ordinal));
    }

    /// <summary>
    /// O que o cliente respondeu a pergunta de encerramento por inatividade
    /// (<see cref="Configuracoes.Domain.Enums.GatilhoDeMensagem.ConfirmacaoDeEncerramento"/>).
    /// </summary>
    public enum RespostaDeEncerramento
    {
        /// <summary>Pode encerrar.</summary>
        Confirmou,

        /// <summary>Nao encerre, ainda preciso.</summary>
        Negou,

        /// <summary>
        /// Nem uma coisa nem outra — o cliente voltou falando de outro assunto.
        /// </summary>
        NaoEntendeu
    }

    /// <summary>
    /// Palavras que autorizam o encerramento. Comparadas por PALAVRA INTEIRA.
    /// </summary>
    private static readonly HashSet<string> PalavrasDeConfirmacao = new(StringComparer.Ordinal)
    {
        "sim", "s", "isso", "claro", "pode", "encerrar", "encerra", "encerrado",
        "finalizar", "finaliza", "concluir", "resolvido", "resolvida"
    };

    /// <summary>
    /// Palavras que recusam o encerramento. Comparadas por PALAVRA INTEIRA.
    /// </summary>
    private static readonly HashSet<string> PalavrasDeNegativa = new(StringComparer.Ordinal)
    {
        "nao", "n", "negativo", "continuar", "continua", "ainda", "espera",
        "espere", "aguarda", "aguarde", "preciso"
    };

    /// <summary>
    /// Le a resposta do cliente a pergunta de encerramento.
    /// </summary>
    /// <remarks>
    /// <b>Palavra inteira, nunca substring.</b> <see cref="PediuHumano"/> e
    /// <see cref="PediuMenu"/> podem usar <c>Contains</c> porque errar la custa uma
    /// mensagem a mais; errar aqui FECHA o atendimento de alguem. E o erro estaria
    /// esperando: "assim" contem "sim", entao "assim que eu tiver os documentos eu
    /// volto" encerraria a conversa de quem acabou de dizer que vai voltar.
    ///
    /// <b>Tambem nao usa <see cref="InterpretarResposta"/></b>, apesar de a pergunta
    /// ser um menu de duas opcoes: aquele metodo aceita correspondencia PARCIAL nas
    /// duas direcoes (linha 91), e a frase "nao quero encerrar agora" contem
    /// "encerrar" — casaria com a opcao de encerrar e fecharia a conversa de quem
    /// disse exatamente o contrario.
    ///
    /// <b>A negativa e avaliada ANTES da confirmacao</b>, e a assimetria e
    /// intencional: "nao, pode encerrar nao" tem palavra das duas familias. Errar
    /// para "continua aberta" custa uma conversa a mais no painel; errar para
    /// "encerra" fecha o atendimento de quem ainda precisa.
    ///
    /// "obrigado" e "ok" ficam de fora das duas listas de proposito — sao ambiguos
    /// ("obrigado, mas ainda preciso de...") e caem em
    /// <see cref="RespostaDeEncerramento.NaoEntendeu"/>, que e o resultado barato:
    /// a conversa segue viva e, se o silencio voltar, a pergunta tambem volta.
    /// </remarks>
    public static RespostaDeEncerramento LerRespostaDeEncerramento(string? conteudo)
    {
        var texto = Normalizar(conteudo);

        if (texto.Length == 0)
        {
            return RespostaDeEncerramento.NaoEntendeu;
        }

        // O numero que a pergunta oferece vem primeiro — e o caminho que o menu
        // pede, igual ao da triagem.
        if (texto == "1")
        {
            return RespostaDeEncerramento.Confirmou;
        }

        if (texto == "2")
        {
            return RespostaDeEncerramento.Negou;
        }

        var palavras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (palavras.Any(PalavrasDeNegativa.Contains))
        {
            return RespostaDeEncerramento.Negou;
        }

        return palavras.Any(PalavrasDeConfirmacao.Contains)
            ? RespostaDeEncerramento.Confirmou
            : RespostaDeEncerramento.NaoEntendeu;
    }

    /// <summary>O que o cliente respondeu à pergunta "ficou alguma dúvida?".</summary>
    public enum RespostaDeDuvida
    {
        /// <summary>Está tudo certo: pode encerrar.</summary>
        SemDuvida,

        /// <summary>Quer falar com uma pessoa.</summary>
        Atendente,

        /// <summary>Qualquer outra coisa — é a dúvida, e a IA responde.</summary>
        Duvida
    }

    /// <summary>
    /// Respostas que valem "não tenho dúvida". Comparadas com a MENSAGEM INTEIRA,
    /// já normalizada.
    /// </summary>
    private static readonly HashSet<string> RespostasSemDuvida = new(StringComparer.Ordinal)
    {
        "1", "nao", "n", "nao obrigado", "nao obrigada", "nao obrigado a", "obrigado", "obrigada",
        "obrigado a", "muito obrigado", "muito obrigada", "valeu", "vlw", "entendi", "entendido",
        "tudo certo", "ta certo", "certo", "ok", "okay", "ok obrigado", "ok obrigada", "so isso",
        "somente", "sem duvida", "sem duvidas", "nenhuma", "nenhuma duvida", "pode encerrar",
        "encerrar", "encerra", "nao tenho duvida", "nao tenho duvidas", "nao tenho", "tudo bem",
        "beleza", "blz", "perfeito", "otimo", "show", "nao tenho duvidas pode encerrar"
    };

    /// <summary>
    /// Lê a resposta à pergunta de dúvida depois da orientação de um serviço.
    /// </summary>
    /// <remarks>
    /// <b>"Sem dúvida" só pela mensagem inteira</b>, nunca por palavra solta: "não
    /// entendi a parte da DNV" começa com "não" e é uma dúvida. Errar para
    /// <see cref="RespostaDeDuvida.Duvida"/> custa uma resposta a mais da IA; errar
    /// para <see cref="RespostaDeDuvida.SemDuvida"/> encerra o atendimento de quem
    /// ainda estava perguntando.
    ///
    /// Pedido de atendente usa <see cref="PediuHumano"/>, o mesmo do resto do fluxo.
    /// </remarks>
    public static RespostaDeDuvida LerRespostaDeDuvida(string? conteudo)
    {
        var texto = Normalizar(conteudo);

        if (texto == "2" || PediuHumano(conteudo))
        {
            return RespostaDeDuvida.Atendente;
        }

        // Pergunta e "não entendi" são dúvida, digam o que disserem as outras palavras.
        if ((conteudo ?? string.Empty).Contains('?')
            || texto.Contains("nao entendi", StringComparison.Ordinal)
            || texto.Contains("nao sei", StringComparison.Ordinal))
        {
            return RespostaDeDuvida.Duvida;
        }

        if (RespostasSemDuvida.Contains(texto))
        {
            return RespostaDeDuvida.SemDuvida;
        }

        // Frase curta feita só de agradecimento e confirmação ("entendi, obrigada",
        // "ok, tudo certo, valeu"), com ou sem um "não" na frente ("não, obrigado").
        var palavras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var resto = palavras.Length > 0 && palavras[0] == "nao" ? palavras[1..] : palavras;

        return palavras.Length <= 6 && resto.Length > 0 && resto.All(PalavrasDeFechamento.Contains)
            ? RespostaDeDuvida.SemDuvida
            : RespostaDeDuvida.Duvida;
    }

    /// <summary>Palavras de agradecimento e confirmação, para frases curtas de fechamento.</summary>
    private static readonly HashSet<string> PalavrasDeFechamento = new(StringComparer.Ordinal)
    {
        "ok", "okay", "certo", "entendi", "entendido", "obrigado", "obrigada", "a", "muito", "valeu",
        "vlw", "beleza", "blz", "perfeito", "otimo", "show", "tudo", "bem", "ta", "so", "isso", "somente"
    };

    /// <summary>
    /// Minusculas, sem acento, sem pontuacao e com espaco colapsado.
    /// </summary>
    /// <remarks>
    /// O cliente digita no WhatsApp, no celular, com pressa: "2." , "Registro Civil!",
    /// "REGISTRO CIVIL". Comparar o texto cru recusaria os tres.
    /// </remarks>
    private static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        var decomposto = valor.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var limpo = new StringBuilder(decomposto.Length);
        var espacoPendente = false;

        foreach (var caractere in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(caractere))
            {
                if (espacoPendente && limpo.Length > 0)
                {
                    limpo.Append(' ');
                }

                espacoPendente = false;
                limpo.Append(caractere);
            }
            else
            {
                // Pontuacao e espaco viram um separador so, adicionado apenas se
                // vier outra letra depois — assim "2." nao fica "2 ".
                espacoPendente = true;
            }
        }

        return limpo.ToString().Normalize(NormalizationForm.FormC);
    }
}
