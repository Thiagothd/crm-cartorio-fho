using System.Globalization;
using System.Text;

namespace CrmCartorio.Api.Modules.Clientes.Domain;

/// <summary>
/// Decide se um nome de contato serve para falar com o cliente, e le o nome que
/// ele digita quando o sistema pergunta.
/// </summary>
/// <remarks>
/// Logica pura, sem banco e sem relogio — mesma forma de
/// <c>Atendimento/Domain/TriagemAutomatica.cs</c>, e pelo mesmo motivo: e a peca
/// que o verificador consegue cobrir caso a caso sem encostar em infraestrutura.
///
/// Mora em <c>Clientes/Domain</c> porque a regra e sobre o nome do CLIENTE. O
/// modulo Atendimento a consome direto — permitido, e o mesmo tipo de dependencia
/// que ele ja tem sobre <c>Clientes.Domain.Entities</c>; o que a fronteira do
/// CLAUDE.md 7.1 proibe e chamar o <c>Application</c> de outro slice.
///
/// <b>As duas leituras tem vies OPOSTO, de proposito.</b> O custo de errar nao e
/// simetrico:
/// <list type="bullet">
/// <item><see cref="EhUtil"/> erra para NAO perguntar — aceitar um perfil ruim
/// custa uma mensagem a mais; recusar um nome bom incomoda quem ja estava
/// certo.</item>
/// <item><see cref="LerRespostaDePergunta"/> erra para NAO gravar — nao capturar
/// um nome de verdade custa zero (o atendente corrige a ficha, que e o
/// funcionamento normal do sistema), enquanto gravar "quero uma certidao de
/// nascimento" como nome e PERMANENTE e sai em toda mensagem automatica dali em
/// diante.</item>
/// </list>
/// </remarks>
public static class NomeDeContato
{
    /// <summary>Espelha <c>ClienteConfiguration</c>: <c>HasMaxLength(200)</c>.</summary>
    public const int TamanhoMaximo = 200;

    /// <summary>Minimo de letras para um nome ser considerado utilizavel.</summary>
    private const int LetrasMinimas = 2;

    /// <summary>Teto do que o cliente digita como resposta a pergunta de nome.</summary>
    /// <remarks>
    /// Bem abaixo de <see cref="TamanhoMaximo"/>: aqui nao e o limite da coluna, e
    /// o limite do que ainda parece um nome. "Maria Auxiliadora de Fatima Santos"
    /// cabe folgado; uma frase de pedido, nao.
    /// </remarks>
    private const int TamanhoMaximoDaResposta = 60;

    /// <summary>Teto de palavras da resposta, pelo mesmo motivo do tamanho.</summary>
    private const int PalavrasMaximasDaResposta = 5;

    /// <summary>
    /// Prefixos conversacionais descartados antes de ler o nome, comparados
    /// PALAVRA A PALAVRA sobre o texto normalizado. Do mais longo para o mais
    /// curto — senao "sou" comeria o inicio de "sou o Antonio".
    /// </summary>
    private static readonly string[][] PrefixosConversacionais =
    [
        ["pode", "me", "chamar", "de"],
        ["meu", "nome", "e"],
        ["me", "chamo"],
        ["meu", "nome"],
        ["aqui", "e"],
        ["sou", "o"],
        ["sou", "a"],
        ["sou"]
    ];

    /// <summary>
    /// Palavras que denunciam que a resposta nao e um nome. Comparadas por
    /// PALAVRA INTEIRA, nunca substring.
    /// </summary>
    /// <remarks>
    /// Substring aqui seria armadilha pela mesma razao que
    /// <c>TriagemAutomatica.LerRespostaDeEncerramento</c> documenta: "Doria"
    /// contem "oria", "Duarte" contem "dua". Palavra inteira e o unico corte
    /// seguro.
    ///
    /// A lista e curta e trata do caso comum — o cliente responde a pergunta com
    /// o pedido dele em vez do nome. O teto de palavras e o de caracteres pegam
    /// o resto; esta lista existe para as frases CURTAS ("oi", "quero certidao")
    /// que passariam por eles.
    /// </remarks>
    private static readonly HashSet<string> PalavrasQueNaoSaoNome = new(StringComparer.Ordinal)
    {
        "oi", "ola", "bom", "boa", "dia", "tarde", "noite", "obrigado", "obrigada",
        "quero", "queria", "preciso", "precisava", "gostaria", "pode", "poderia",
        "certidao", "escritura", "procuracao", "documento", "documentos", "firma",
        "reconhecimento", "autenticacao", "informacao", "informacoes", "duvida",
        "duvidas", "atendente", "ajuda", "menu", "sim", "nao"
    };

    /// <summary>
    /// O nome serve para se dirigir ao cliente?
    /// </summary>
    /// <remarks>
    /// <b>Uma regra so: tem ao menos duas letras?</b> Ela cobre a lista inteira de
    /// casos ruins sem precisar enumera-los — <c>""</c>, <c>"."</c>, <c>"..."</c>,
    /// <c>"🔥"</c> e o telefone E.164 (<c>"5519990001488"</c>, que e so digito) tem
    /// ZERO letras; <c>"A"</c> tem uma. E aceita tudo que e nome de gente:
    /// <c>"Ze"</c>, <c>"Ana 💅"</c>, <c>"JOAO"</c>, <c>"Tia Neuza"</c>, alfabetos
    /// nao latinos.
    ///
    /// Conta por <see cref="Rune"/>, nao por <c>char</c>: emoji fora do BMP e par
    /// substituto, e <c>char.IsLetter</c> sobre metade de um par mente.
    ///
    /// <b>Nao existe lista negra</b> de "Contato"/"WhatsApp"/"Cliente". Seria
    /// ilimitada, mudaria por serventia, e o primeiro falso positivo transformaria
    /// um sobrenome legitimo em interrogatorio — exatamente o que o vies desta
    /// funcao existe para evitar.
    ///
    /// Nao recebe o telefone: pela regra das duas letras, telefone nenhum passa —
    /// em qualquer formato, cru ou pontuado.
    /// </remarks>
    public static bool EhUtil(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            return false;
        }

        var letras = 0;

        foreach (var rune in nome.EnumerateRunes())
        {
            if (Rune.IsLetter(rune) && ++letras >= LetrasMinimas)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// O que gravar em <c>Cliente.Nome</c> a partir do perfil do WhatsApp.
    /// </summary>
    /// <remarks>
    /// Mantem o comportamento historico byte a byte — perfil vazio vira o telefone
    /// E.164 — e acrescenta as duas defesas que faltavam:
    ///
    /// <b>Trim</b>, para <c>"  Joao  "</c> nao entrar no cadastro com os espacos.
    ///
    /// <b>Corte em <see cref="TamanhoMaximo"/></b>, que conserta um defeito real:
    /// a coluna e <c>NVARCHAR(200) NOT NULL</c> e o webhook nao truncava, entao um
    /// <c>pushName</c> de 201 caracteres derrubava o <c>SaveChanges</c> DENTRO da
    /// transacao do recebimento. O gateway reentregava, a mesma excecao acontecia
    /// de novo, e a mensagem daquele cliente se perdia em definitivo — nao era
    /// adiada. Cortar por elemento de texto, e nao por <c>nome[..200]</c>, porque
    /// partir um par substituto no meio produz string invalida.
    ///
    /// Para qualquer perfil real o valor devolvido e identico ao de antes: o corte
    /// so age acima de 200 caracteres, e o WhatsApp limita o campo a ~25.
    /// </remarks>
    public static string DoPerfil(string? nomeDoPerfil, string telefoneE164)
    {
        var limpo = nomeDoPerfil?.Trim();

        return string.IsNullOrEmpty(limpo)
            ? telefoneE164
            : Truncar(limpo, TamanhoMaximo);
    }

    /// <summary>
    /// Le o nome na resposta do cliente a pergunta "como posso te chamar?".
    /// Devolve <c>null</c> quando o texto nao parece um nome.
    /// </summary>
    /// <remarks>
    /// <b>Na duvida, devolve null.</b> Ver o vies documentado na classe: nao
    /// gravar custa zero, gravar errado e permanente e visivel ao cliente.
    ///
    /// A ordem importa — o prefixo sai antes da limpeza, para "meu nome e Joao"
    /// nao ser recusado pela palavra "nome"; e os tetos vem antes da lista de
    /// palavras, porque sao mais baratos e pegam mais.
    /// </remarks>
    public static string? LerRespostaDePergunta(string? conteudo)
    {
        if (string.IsNullOrWhiteSpace(conteudo))
        {
            return null;
        }

        var palavras = conteudo.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        palavras = RemoverPrefixoConversacional(palavras);

        if (palavras.Length == 0 || palavras.Length > PalavrasMaximasDaResposta)
        {
            return null;
        }

        // Descarta o que nao compoe nome: emoji, digito e pontuacao caem aqui de
        // uma vez so. Apostrofo, hifen e ponto ficam por causa de "d'Avila",
        // "Ana-Maria" e "Joao P. Silva".
        var limpas = palavras
            .Select(LimparPalavra)
            .Where(p => p.Length > 0)
            .ToArray();

        if (limpas.Length == 0)
        {
            return null;
        }

        var nome = string.Join(' ', limpas);

        if (nome.Length > TamanhoMaximoDaResposta)
        {
            return null;
        }

        if (limpas.Select(Normalizar).Any(PalavrasQueNaoSaoNome.Contains))
        {
            return null;
        }

        // Ultima porta: o que sobrou ainda precisa passar pelo mesmo criterio que
        // reprovou o nome do perfil. Sem isto, "..." e "2" voltariam pela janela.
        return EhUtil(nome) ? nome : null;
    }

    /// <summary>
    /// Corta o "meu nome e" / "me chamo" do inicio, comparando sobre a forma
    /// normalizada mas devolvendo as PALAVRAS ORIGINAIS — acento e caixa do nome
    /// precisam sobreviver.
    /// </summary>
    private static string[] RemoverPrefixoConversacional(string[] palavras)
    {
        var normalizadas = palavras.Select(Normalizar).ToArray();

        foreach (var prefixo in PrefixosConversacionais)
        {
            if (normalizadas.Length <= prefixo.Length)
            {
                continue;
            }

            if (normalizadas.Take(prefixo.Length).SequenceEqual(prefixo, StringComparer.Ordinal))
            {
                return palavras[prefixo.Length..];
            }
        }

        return palavras;
    }

    /// <summary>Mantem so o que pode compor um nome; o resto e descartado.</summary>
    private static string LimparPalavra(string palavra)
    {
        var limpa = new StringBuilder(palavra.Length);

        foreach (var rune in palavra.EnumerateRunes())
        {
            if (Rune.IsLetter(rune) || rune.Value is '\'' or '-' or '.')
            {
                limpa.Append(rune.ToString());
            }
        }

        // Pontuacao solta ("...", "-") vira palavra sem letra nenhuma: descartada
        // pelo filtro de quem chama, nao aqui.
        return limpa.ToString().Trim('\'', '-', '.');
    }

    /// <summary>Minuscula e sem acento, so para comparar.</summary>
    /// <remarks>
    /// Mesmo algoritmo de <c>TriagemAutomatica.Normalizar</c>. Duplicado, e nao
    /// compartilhado, porque aquele metodo e privado de outro modulo e extrai-lo
    /// para um utilitario comum acoplaria dois slices por causa de dez linhas.
    /// </remarks>
    private static string Normalizar(string valor)
    {
        var decomposto = valor.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var limpo = new StringBuilder(decomposto.Length);

        foreach (var caractere in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(caractere))
            {
                limpo.Append(caractere);
            }
        }

        return limpo.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Corta em fronteira de grafema, nunca no meio de um par substituto.</summary>
    private static string Truncar(string valor, int maximo)
    {
        if (valor.Length <= maximo)
        {
            return valor;
        }

        var enumerador = StringInfo.GetTextElementEnumerator(valor);
        var fim = 0;

        while (enumerador.MoveNext())
        {
            var proximo = enumerador.ElementIndex + ((string)enumerador.Current).Length;

            if (proximo > maximo)
            {
                break;
            }

            fim = proximo;
        }

        return valor[..fim].TrimEnd();
    }
}
