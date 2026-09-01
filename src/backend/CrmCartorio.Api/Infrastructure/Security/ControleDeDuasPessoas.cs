using System.Text;
using Microsoft.Extensions.Configuration;
using OtpNet;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// SEC-03 (bloqueador B21, sétima revisão independente) — controle técnico
/// de DUAS PESSOAS, compartilhado por <see cref="FerramentaDeResetDeEmergencia"/>
/// e <see cref="FerramentaDeReemissaoDeTicketDeEmergencia"/>: o mesmo código,
/// não uma cópia, para as duas ferramentas nunca divergirem sobre o que
/// "duas pessoas presentes" significa.
/// </summary>
/// <remarks>
/// CORRECAO (bloqueador B21) — a versão anterior (<c>PinDeDuasPessoasConfere</c>,
/// removida) lia um PIN com <see cref="Console.ReadLine()"/> (ecoado no
/// terminal — o operador SSH via o proprio PIN na tela) e comparava
/// SHA-256 direto contra um hash fixo em configuração. Dois problemas
/// reais: (1) depois de UMA emergência legítima, o operador SSH já
/// conhecia o PIN e podia repetir o procedimento sozinho no futuro — nada
/// tecnicamente impedia; (2) SHA-256 de um PIN humano de baixa entropia é
/// atacável por força bruta offline se o hash vazar.
///
/// A correção troca o PIN estático por um SEGREDO TOTP (RFC 6238, o MESMO
/// mecanismo já usado para o segundo fator de login — <see cref="ValidadorDeTotp"/>,
/// nunca uma implementação nova) custodiado pelo segundo custodiante:
/// <list type="bullet">
/// <item><b>Sem eco</b> — o código é lido tecla a tecla
/// (<see cref="Console.ReadKey(bool)"/>, nunca <see cref="Console.ReadLine()"/>),
/// nunca aparece no terminal.</item>
/// <item><b>≥128 bits de entropia</b> — o segredo TOTP configurado precisa
/// decodificar para pelo menos 16 bytes Base32; <see cref="ValidadorDeTotp.GerarSegredoBase32"/>
/// já produz 20 bytes (160 bits) por padrão. Configuração ausente, mal
/// formada ou curta demais falha FECHADA (<see cref="SegredoConfiguradoValido"/>).</item>
/// <item><b>Nunca plaintext do que é DIGITADO</b> — o código de 6 dígitos
/// nunca é persistido, nunca vai a log. (O segredo TOTP em si fica em
/// configuração, mesmo nível de confiança que a chave AES do MFA e a chave
/// do JWT — arquivo fora do controle de versão, custódia de servidor,
/// nunca "o que o operador digita".)</item>
/// <item><b>Comparação segura</b> — delegada a <see cref="ValidadorDeTotp.Validar"/>,
/// que já é o código de produção usado para todo segundo fator do sistema.</item>
/// <item><b>Rotação por CONSTRUÇÃO, não por reescrita de configuração</b> —
/// um código TOTP muda a cada 30s (janela de validação de ±1 passo, 90s de
/// tolerância). Um operador que observou o código USADO numa emergência não
/// consegue reproduzi-lo semanas depois: reproduzir exigiria o SEGREDO em
/// si, não o código observado — o mesmo motivo por que ver o código MFA de
/// alguém uma vez não permite logar como essa pessoa depois. Isto substitui
/// "rotacionar o segredo compartilhado após cada uso" (que exigiria
/// reescrever `appsettings` em produção, fora do mesmo rigor de revisão do
/// resto da configuração) por um mecanismo equivalente que já é código
/// maduro deste mesmo projeto.</item>
/// </list>
/// A IDENTIDADE do segundo custodiante (nome/identificador, nunca o
/// segredo) é capturada separadamente e cabe ao CHAMADOR compor em
/// <c>EventosDeSeguranca.AtorExterno</c> junto da identidade de quem tem
/// acesso SSH — nenhum segredo entra nesse campo, só identidade.
/// </remarks>
internal static class ControleDeDuasPessoas
{
    private const string ChaveDeConfiguracao = "Emergencia:SegredoTotpDeDuasPessoas";

    /// <summary>Mínimo de 128 bits — RFC 4226 recomenda 160 (20 bytes, o que <see cref="ValidadorDeTotp.GerarSegredoBase32"/> já produz).</summary>
    private const int BytesMinimosDoSegredo = 16;

    /// <summary>
    /// Decisão PURA: o segredo TOTP configurado é utilizável? Falha fechada
    /// em qualquer formato inválido ou entropia insuficiente — nunca lança.
    /// </summary>
    internal static bool SegredoConfiguradoValido(string? segredoBase32Configurado)
    {
        if (string.IsNullOrWhiteSpace(segredoBase32Configurado))
        {
            return false;
        }

        try
        {
            return Base32Encoding.ToBytes(segredoBase32Configurado.Trim()).Length >= BytesMinimosDoSegredo;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Decisão PURA: o código digitado confere com o segredo configurado?
    /// Nunca lança — configuração inválida simplesmente nunca confere.
    /// </summary>
    internal static bool CodigoConfere(string? segredoBase32Configurado, string? codigoDigitado)
    {
        if (!SegredoConfiguradoValido(segredoBase32Configurado))
        {
            return false;
        }

        return ValidadorDeTotp.Validar(segredoBase32Configurado!, codigoDigitado ?? string.Empty, out _);
    }

    /// <summary>
    /// Fluxo interativo completo: confere se o segredo está configurado,
    /// pede a IDENTIDADE do segundo custodiante (texto livre, nunca o
    /// segredo) e o CÓDIGO TOTP (sem eco). Devolve a identidade só quando
    /// tudo confere — o chamador usa isso para compor `AtorExterno`, nunca
    /// para decidir sozinho se o controle passou.
    /// </summary>
    internal static (bool Ok, string? IdentidadeDoCustodiante) ConferirInterativo(IConfiguration configuracao)
    {
        var segredoConfigurado = configuracao[ChaveDeConfiguracao];

        if (!SegredoConfiguradoValido(segredoConfigurado))
        {
            Console.WriteLine(
                "RECUSADO: controle de duas pessoas não configurado ou fraco demais " +
                $"({ChaveDeConfiguracao} ausente, mal formado, ou com menos de {BytesMinimosDoSegredo * 8} bits). " +
                "Este comando não roda sem ele.");
            return (false, null);
        }

        Console.WriteLine();
        Console.WriteLine("Este procedimento exige DUAS PESSOAS presentes: quem executa este");
        Console.WriteLine("comando (acesso SSH) e um segundo custodiante, dono de um autenticador");
        Console.WriteLine("TOTP próprio para emergência — NUNCA o mesmo autenticador MFA de");
        Console.WriteLine("nenhuma conta individual, e nunca a mesma pessoa que roda o comando.");

        Console.Write("Identidade do segundo custodiante (nome — NUNCA o segredo dele): ");
        var identidade = (Console.ReadLine() ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(identidade))
        {
            Console.WriteLine("RECUSADO: identidade do segundo custodiante em branco.");
            return (false, null);
        }

        Console.Write("Código TOTP do segundo custodiante (6 dígitos, entrada SEM ECO): ");
        var codigo = LerSemEco();
        Console.WriteLine();

        if (!CodigoConfere(segredoConfigurado, codigo))
        {
            Console.WriteLine("RECUSADO: código do segundo custodiante incorreto, ausente ou expirado.");
            return (false, null);
        }

        return (true, identidade);
    }

    /// <summary>Máximo de <c>EventosDeSeguranca.AtorExterno</c> (NVARCHAR(120)).</summary>
    private const int TamanhoMaximoDoAtorExterno = 120;

    /// <summary>
    /// Compõe a identidade das DUAS pessoas para <c>EventosDeSeguranca.AtorExterno</c>
    /// — nunca o segredo de nenhuma delas, só identidade. Compartilhado
    /// entre <see cref="FerramentaDeResetDeEmergencia"/> e
    /// <see cref="FerramentaDeReemissaoDeTicketDeEmergencia"/> para o
    /// formato nunca divergir entre as duas.
    /// </summary>
    internal static string FormatarAtorDeDuasPessoas(string operadorSsh, string identidadeDoCustodiante)
    {
        var texto = $"{operadorSsh} + segundo custodiante: {identidadeDoCustodiante}";
        return texto.Length <= TamanhoMaximoDoAtorExterno ? texto : texto[..TamanhoMaximoDoAtorExterno];
    }

    /// <summary>
    /// Lê uma linha SEM ecoar os caracteres digitados — exige console
    /// interativo de verdade (chamado só depois do guard de ambiente não
    /// interativo em quem chama esta classe).
    /// </summary>
    private static string LerSemEco()
    {
        var construtor = new StringBuilder();
        ConsoleKeyInfo tecla;

        do
        {
            tecla = Console.ReadKey(intercept: true);

            if (tecla.Key == ConsoleKey.Backspace)
            {
                if (construtor.Length > 0)
                {
                    construtor.Length--;
                }
                continue;
            }

            if (!char.IsControl(tecla.KeyChar))
            {
                construtor.Append(tecla.KeyChar);
            }
        } while (tecla.Key != ConsoleKey.Enter);

        return construtor.ToString();
    }
}
