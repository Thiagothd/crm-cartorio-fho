using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// Operações de emergência que exigem autorização de uma segunda pessoa. O
/// nome canônico entra no desafio assinado — é ele que impede que uma
/// autorização de reset sirva para uma reemissão (e vice-versa).
/// </summary>
internal enum OperacaoDeEmergencia
{
    ResetDeEmergencia,
    ReemitirTicketDeEmergencia
}

/// <summary>
/// SEC-03 (bloqueadores B22 e B23, oitava revisão independente) —
/// autorização ASSIMÉTRICA da segunda pessoa, compartilhada por
/// <see cref="FerramentaDeResetDeEmergencia"/> e
/// <see cref="FerramentaDeReemissaoDeTicketDeEmergencia"/>: o mesmo código,
/// nunca uma cópia por ferramenta.
/// </summary>
/// <remarks>
/// <b>O que estava errado antes (ControleDeDuasPessoas, removido):</b>
/// <list type="number">
/// <item><b>B22 — replay.</b> A verificação era
/// <c>ValidadorDeTotp.Validar(segredo, codigo, out _)</c>, descartando o
/// passo TOTP que o próprio validador devolve justamente para permitir
/// anti-replay (como <c>Atendente.MfaUltimoPassoUsado</c> faz no login). O
/// MESMO código de 6 dígitos autorizava mais de uma operação enquanto
/// estivesse dentro da janela de ±1 passo (até 90s) — o operador podia
/// rodar reset E reemissão, ou duas vezes a mesma operação, com uma única
/// autorização.</item>
/// <item><b>B23 — o servidor conhecia o segredo.</b>
/// <c>Emergencia:SegredoTotpDeDuasPessoas</c> vivia na configuração
/// carregada pelo próprio CRM. Quem tem SSH lê o arquivo e passa a gerar
/// sozinho os códigos atribuídos ao "segundo custodiante" — nunca houve
/// separação criptográfica real entre as duas pessoas, só a aparência
/// dela.</item>
/// </list>
///
/// <b>A correção é uma coisa só, não dois remendos:</b> segredo compartilhado
/// (simétrico) trocado por ASSINATURA DIGITAL (assimétrica).
/// <list type="bullet">
/// <item>O servidor guarda <b>somente a chave pública</b>
/// (<c>Emergencia:ChavePublicaDoSegundoCustodiante</c>, PEM SPKI). Chave
/// pública não produz autorização nenhuma — quem controla o servidor não
/// consegue fabricar a assinatura da segunda pessoa. Isto fecha B23.</item>
/// <item>Cada execução monta um desafio NOVO, com nonce de 256 bits vindo de
/// <see cref="RandomNumberGenerator"/>, e a assinatura cobre esse nonce.
/// Uma assinatura capturada de uma execução anterior cobre um nonce
/// anterior: contra o desafio novo ela simplesmente não verifica. Isto
/// fecha B22 <b>sem precisar persistir nonce</b> (ver "Modelo de ameaça do
/// anti-replay", abaixo).</item>
/// <item>O desafio amarra também OPERAÇÃO, ALVO, OPERADOR e o SHA-256 do
/// MOTIVO — uma autorização de reset não vale para reemissão, a de uma
/// conta não vale para outra, e trocar motivo ou operador depois de
/// assinado invalida a assinatura.</item>
/// </list>
///
/// <b>Modelo de ameaça do anti-replay (por que não há migration nova).</b>
/// A propriedade que precisamos é "uma assinatura antiga é inútil numa
/// execução nova". O nonce tem 256 bits de <see cref="RandomNumberGenerator"/>
/// por execução: a chance de duas execuções sortearem o mesmo nonce é
/// desprezível (2^-256), então nenhuma assinatura antiga volta a casar com
/// um desafio futuro. Persistir nonces usados só acrescentaria proteção se
/// o nonce pudesse REPETIR — que é exatamente o que a fonte criptográfica
/// exclui. Dentro de UMA execução a assinatura autoriza uma única operação
/// (o processo verifica, executa a transação e termina; não há laço que
/// reaproveite a autorização). Por isso: nenhuma tabela nova, nenhuma
/// migration nova.
///
/// <b>Formato.</b> ECDSA sobre P-256 (secp256r1) com SHA-256, assinatura em
/// DER (<see cref="DSASignatureFormat.Rfc3279DerSequence"/>) transportada em
/// Base64 — exatamente o que <c>openssl dgst -sha256 -sign</c> produz, para
/// o custodiante usar ferramenta padrão, sem nada específico deste projeto.
/// Só primitivas do runtime (<c>System.Security.Cryptography</c>): nenhuma
/// biblioteca nova, nenhum esquema caseiro.
///
/// <b>A assinatura não é segredo.</b> Ela é lida com eco normal e pode ser
/// pública depois de usada — a segurança vem de ela estar amarrada a um
/// desafio irrepetível, nunca de mantê-la escondida. O que é segredo (a
/// chave privada) nunca entra neste processo, em lugar nenhum.
/// </remarks>
internal static class AutorizacaoDeSegundaPessoa
{
    /// <summary>Primeira linha do desafio — muda se o formato canônico mudar.</summary>
    internal const string VersaoDoProtocolo = "CRM-SEC03-AUTORIZACAO-V1";

    internal const string ChaveDeConfiguracaoDaChavePublica = "Emergencia:ChavePublicaDoSegundoCustodiante";
    internal const string ChaveDeConfiguracaoDoIdentificador = "Emergencia:IdentificadorDoSegundoCustodiante";

    /// <summary>256 bits — o exigido pela revisão, e o que torna repetição de nonce desprezível.</summary>
    internal const int BytesDoNonce = 32;

    /// <summary>secp256r1 / prime256v1 / P-256 — a única curva aceita.</summary>
    private const string OidDaCurvaP256 = "1.2.840.10045.3.1.7";

    /// <summary>Máximo de <c>EventosDeSeguranca.AtorExterno</c> (NVARCHAR(120)).</summary>
    private const int TamanhoMaximoDoAtorExterno = 120;

    /// <summary>
    /// Nome CANÔNICO da operação dentro do desafio assinado. Trocar estes
    /// literais quebra assinaturas antigas de propósito — eles são parte do
    /// contrato criptográfico, não texto de tela.
    /// </summary>
    internal static string NomeCanonicoDaOperacao(OperacaoDeEmergencia operacao) => operacao switch
    {
        OperacaoDeEmergencia.ResetDeEmergencia => "RESET_DE_EMERGENCIA",
        OperacaoDeEmergencia.ReemitirTicketDeEmergencia => "REEMITIR_TICKET_DE_EMERGENCIA",
        _ => throw new ArgumentOutOfRangeException(nameof(operacao))
    };

    /// <summary>Nonce fresco de 256 bits, Base64Url (sem padding, seguro para uma linha de terminal).</summary>
    internal static string GerarNonce() =>
        Base64UrlSemPadding(RandomNumberGenerator.GetBytes(BytesDoNonce));

    /// <summary>
    /// O desafio canônico — determinístico para os mesmos campos, sem
    /// ambiguidade, e legível para que o segundo custodiante ENTENDA o que
    /// está autorizando antes de assinar.
    /// </summary>
    /// <remarks>
    /// Formato (linhas separadas por <c>\n</c>, ordem fixa, sem espaços
    /// extras):
    /// <code>
    /// CRM-SEC03-AUTORIZACAO-V1
    /// OPERACAO=&lt;RESET_DE_EMERGENCIA|REEMITIR_TICKET_DE_EMERGENCIA&gt;
    /// ALVO=&lt;e-mail do alvo, normalizado minúsculo&gt;
    /// OPERADOR=&lt;usuario@maquina de quem tem SSH&gt;
    /// CUSTODIANTE=&lt;identificador da chave pública configurada&gt;
    /// MOTIVO_SHA256=&lt;hex minúsculo do SHA-256 do motivo em UTF-8&gt;
    /// NONCE=&lt;Base64Url de 32 bytes aleatórios&gt;
    /// </code>
    /// O motivo entra por HASH, não em claro: motivo é texto livre e pode ter
    /// quebra de linha, o que criaria ambiguidade estrutural no payload (duas
    /// combinações diferentes de campos poderiam produzir os mesmos bytes). O
    /// motivo em claro é mostrado ao operador e ao custodiante ao lado do
    /// desafio — o hash é o que amarra criptograficamente. Os demais campos
    /// são rejeitados se contiverem caractere de controle, pelo mesmo motivo.
    /// </remarks>
    internal static string MontarDesafioCanonico(
        OperacaoDeEmergencia operacao,
        string alvo,
        string operador,
        string identificadorDoCustodiante,
        string motivo,
        string nonce)
    {
        var alvoNormalizado = (alvo ?? string.Empty).Trim().ToLowerInvariant();
        var operadorNormalizado = (operador ?? string.Empty).Trim();
        var custodianteNormalizado = (identificadorDoCustodiante ?? string.Empty).Trim();
        var nonceNormalizado = (nonce ?? string.Empty).Trim();

        GarantirCampoSemAmbiguidade(alvoNormalizado, nameof(alvo));
        GarantirCampoSemAmbiguidade(operadorNormalizado, nameof(operador));
        GarantirCampoSemAmbiguidade(custodianteNormalizado, nameof(identificadorDoCustodiante));
        GarantirCampoSemAmbiguidade(nonceNormalizado, nameof(nonce));

        var motivoSha256 = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes((motivo ?? string.Empty).Trim())));

        // string.Join com \n (nunca Environment.NewLine): o desafio precisa
        // produzir os MESMOS bytes no Windows do servidor e no Linux/macOS do
        // custodiante — senao a assinatura dele nunca verificaria aqui.
        return string.Join('\n',
        [
            VersaoDoProtocolo,
            $"OPERACAO={NomeCanonicoDaOperacao(operacao)}",
            $"ALVO={alvoNormalizado}",
            $"OPERADOR={operadorNormalizado}",
            $"CUSTODIANTE={custodianteNormalizado}",
            $"MOTIVO_SHA256={motivoSha256}",
            $"NONCE={nonceNormalizado}"
        ]);
    }

    /// <summary>
    /// Decisão PURA: a assinatura confere com a chave PÚBLICA configurada,
    /// para EXATAMENTE este desafio? Falha FECHADA em tudo — chave ausente,
    /// PEM malformado, PEM de chave PRIVADA (bloqueador B24), curva/algoritmo
    /// inesperados, assinatura malformada, assinatura de outra chave, desafio
    /// diferente. Nunca lança.
    /// </summary>
    internal static bool AssinaturaConfere(string? chavePublicaPem, string desafioCanonico, string? assinaturaBase64)
    {
        if (string.IsNullOrWhiteSpace(assinaturaBase64))
        {
            return false;
        }

        byte[] assinatura;
        try
        {
            assinatura = Convert.FromBase64String(assinaturaBase64.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (assinatura.Length == 0)
        {
            return false;
        }

        if (!TentarImportarChavePublicaP256(chavePublicaPem, out var verificador))
        {
            return false;
        }

        using (verificador)
        {
            try
            {
                return verificador!.VerifyData(
                    Encoding.UTF8.GetBytes(desafioCanonico),
                    assinatura,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence);
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                // Assinatura sintaticamente Base64 mas nao um DER valido para
                // este algoritmo — falha FECHADA, nunca cai para outro mecanismo.
                return false;
            }
        }
    }

    /// <summary>
    /// A chave pública configurada é utilizável (existe, é um ÚNICO bloco PEM
    /// rotulado exatamente "PUBLIC KEY", é ECDSA P-256)? Decisão PURA, nunca
    /// lança — usada para recusar cedo, com mensagem útil, antes de montar
    /// desafio nenhum.
    /// </summary>
    internal static bool ChavePublicaConfiguradaValida(string? chavePublicaPem)
    {
        var valida = TentarImportarChavePublicaP256(chavePublicaPem, out var chave);
        chave?.Dispose();
        return valida;
    }

    /// <summary>
    /// Fluxo interativo completo da autorização da segunda pessoa. Recebe os
    /// campos que JÁ foram coletados pela ferramenta (alvo, operador, motivo)
    /// e devolve, só quando a assinatura confere, o <c>AtorExterno</c> já
    /// composto (operador + custodiante + fingerprint da chave). O chamador
    /// nunca decide isso sozinho, nunca compõe a identidade por conta própria
    /// (não tem como esquecer o fingerprint), e nunca pode trocar os campos
    /// depois: o que foi assinado é o que vai ser executado.
    /// </summary>
    internal static (bool Ok, string? AtorExterno) AutorizarInterativo(
        IConfiguration configuracao,
        OperacaoDeEmergencia operacao,
        string alvo,
        string operador,
        string motivo)
    {
        var chavePublicaPem = configuracao[ChaveDeConfiguracaoDaChavePublica];
        var identificador = (configuracao[ChaveDeConfiguracaoDoIdentificador] ?? string.Empty).Trim();

        if (!ChavePublicaConfiguradaValida(chavePublicaPem))
        {
            Console.WriteLine(
                $"RECUSADO: {ChaveDeConfiguracaoDaChavePublica} ausente, malformada, ou nao e uma chave " +
                "publica ECDSA P-256 em PEM. Este comando nao roda sem a chave publica do segundo " +
                "custodiante — e nao existe caminho alternativo (nao ha fallback para PIN/TOTP).");
            return (false, null);
        }

        if (string.IsNullOrWhiteSpace(identificador) || ContemCaractereDeControle(identificador))
        {
            Console.WriteLine(
                $"RECUSADO: {ChaveDeConfiguracaoDoIdentificador} ausente ou invalida. O identificador da " +
                "chave (quem e o segundo custodiante) e obrigatorio: e ele que vai para a auditoria.");
            return (false, null);
        }

        if (ContemCaractereDeControle(alvo) || ContemCaractereDeControle(operador))
        {
            Console.WriteLine("RECUSADO: alvo ou identidade do operador com caractere invalido.");
            return (false, null);
        }

        var nonce = GerarNonce();
        var desafio = MontarDesafioCanonico(operacao, alvo, operador, identificador, motivo, nonce);

        Console.WriteLine();
        Console.WriteLine("=== AUTORIZACAO DA SEGUNDA PESSOA (assinatura ECDSA P-256) ===");
        Console.WriteLine();
        Console.WriteLine("Este procedimento exige DUAS PESSOAS: quem executa este comando (acesso");
        Console.WriteLine("SSH, abaixo como OPERADOR) e o segundo custodiante, dono da chave PRIVADA");
        Console.WriteLine("que NUNCA fica neste servidor. O servidor so tem a chave publica: com ela");
        Console.WriteLine("nao da para fabricar a autorizacao abaixo.");
        Console.WriteLine();
        Console.WriteLine("1. Envie o DESAFIO abaixo (as 7 linhas, sem alterar nada) ao custodiante.");
        Console.WriteLine("2. Ele confere ALVO/OPERACAO/OPERADOR, assina no equipamento DELE e");
        Console.WriteLine("   devolve SO a assinatura em Base64 (ver docs/INSTALACAO.md).");
        Console.WriteLine();
        Console.WriteLine($"Motivo (em claro, conferido pelo hash do desafio): {motivo.Trim()}");
        Console.WriteLine();
        Console.WriteLine("----- INICIO DO DESAFIO -----");
        Console.WriteLine(desafio);
        Console.WriteLine("----- FIM DO DESAFIO -----");
        Console.WriteLine();
        Console.Write("Assinatura Base64 do segundo custodiante: ");

        // Eco NORMAL de proposito: a assinatura nao e segredo. Ela so vale
        // para ESTE desafio (nonce fresco de 256 bits) e pode ser publica
        // depois de usada.
        var assinatura = (Console.ReadLine() ?? string.Empty).Trim();

        if (!AssinaturaConfere(chavePublicaPem, desafio, assinatura))
        {
            Console.WriteLine();
            Console.WriteLine(
                "RECUSADO: assinatura invalida para este desafio. Causas possiveis: assinatura de " +
                "outra chave, de outro desafio (nonce ja usado/execucao anterior), de outra operacao " +
                "ou de outro alvo, Base64 malformado, ou motivo/operador alterados depois de assinar.");
            return (false, null);
        }

        var fingerprint = FingerprintDaChavePublica(chavePublicaPem!);

        Console.WriteLine();
        Console.WriteLine(
            $"Autorizacao da segunda pessoa CONFERIDA (custodiante '{identificador}', chave {fingerprint}).");

        return (true, FormatarAtorDeDuasPessoas(operador, identificador, fingerprint));
    }

    /// <summary>
    /// Impressão digital curta da chave pública que verificou a autorização —
    /// SHA-256 do SPKI, primeiros 8 bytes em hex. Não é segredo (deriva de
    /// material público) e nunca permite recriar a chave.
    /// </summary>
    /// <remarks>
    /// Existe para amarrar o evento de auditoria à CHAVE que de fato
    /// autorizou, não só ao nome declarado em configuração. Sem isto, alguém
    /// com acesso de escrita ao <c>appsettings</c> poderia trocar a chave
    /// pública pela própria e continuar gravando o identificador do
    /// custodiante legítimo — a auditoria mostraria uma operação de duas
    /// pessoas indistinguível de uma genuína. Com o fingerprint, a troca do
    /// trust anchor aparece: o evento registra uma chave diferente da que o
    /// custodiante reconhece como sua (ele confere a dele com
    /// <c>openssl pkey -pubin -outform DER | openssl dgst -sha256</c>).
    /// </remarks>
    internal static string FingerprintDaChavePublica(string chavePublicaPem)
    {
        if (!TentarImportarChavePublicaP256(chavePublicaPem, out var chave))
        {
            // So alcancavel se o chamador nao checou ChavePublicaConfiguradaValida
            // antes — nos dois pontos de chamada de producao (AutorizarInterativo)
            // essa checagem ja aconteceu. Falha explicita em vez de mascarar.
            throw new ArgumentException("Chave publica invalida para gerar fingerprint.", nameof(chavePublicaPem));
        }

        using (chave)
        {
            return Convert.ToHexStringLower(
                SHA256.HashData(chave!.ExportSubjectPublicKeyInfo()).AsSpan(0, 8));
        }
    }

    /// <summary>
    /// Compõe a identidade das DUAS pessoas para
    /// <c>EventosDeSeguranca.AtorExterno</c> — operador SSH, identificador do
    /// custodiante e o fingerprint da chave que verificou. Nunca a
    /// assinatura, nunca chave privada, nunca segredo. O identificador vem da
    /// CONFIGURAÇÃO, não de texto livre digitado pelo operador; o fingerprint
    /// vem da chave que de fato validou a assinatura.
    /// </summary>
    /// <remarks>
    /// CORREÇÃO (bloqueador B25, nona revisão independente) — a versão
    /// anterior reservava espaço para o fingerprint só quando ele e o
    /// identificador juntos não cabiam; quando o OPERADOR sozinho já
    /// estourava a coluna (<c>espacoParaIdentificador &lt;= 0</c>), o
    /// fallback fazia <c>texto[..120]</c> sobre a string inteira — cortando
    /// pelo INÍCIO, do lado do operador, e descartando o sufixo com o
    /// fingerprint junto. A garantia documentada ("o fingerprint nunca é
    /// cortado") não valia nesse caminho. Agora o orçamento é reservado do
    /// fim para o início: sufixo (fingerprint) primeiro — nunca cede;
    /// identificador cede em seguida; operador cede por último, só se
    /// identificador já cedeu por completo e ainda faltar espaço.
    /// </remarks>
    internal static string FormatarAtorDeDuasPessoas(
        string operadorSsh, string identificadorDoCustodiante, string fingerprintDaChave)
    {
        const string separador = " + custodiante: ";
        var sufixo = $" (chave {fingerprintDaChave})";

        static string Cortar(string valor, int maximo) =>
            maximo <= 0 ? string.Empty : valor.Length <= maximo ? valor : valor[..maximo];

        // O FINGERPRINT nunca cede — reserva o espaco dele primeiro.
        var orcamentoAposSufixo = TamanhoMaximoDoAtorExterno - sufixo.Length;
        if (orcamentoAposSufixo <= 0)
        {
            // So alcancavel se o fingerprint sozinho (tamanho fixo, 16 hex
            // chars na pratica) ja estourasse a coluna — nao acontece com o
            // gerador real; falha de forma segura mesmo assim.
            return Cortar(sufixo, TamanhoMaximoDoAtorExterno);
        }

        var espacoParaIdentificador = orcamentoAposSufixo - operadorSsh.Length - separador.Length;
        if (espacoParaIdentificador >= 0)
        {
            // Operador e separador cabem inteiros — so o identificador cede,
            // se precisar ceder.
            var identificadorCabendo = Cortar(identificadorDoCustodiante, espacoParaIdentificador);
            return $"{operadorSsh}{separador}{identificadorCabendo}{sufixo}";
        }

        // Identificador ja cedeu por completo (comprimento 0) e ainda falta
        // espaco: o OPERADOR cede tambem, por ultimo. O separador so
        // sobrevive se ainda couber depois do operador truncado.
        var operadorCabendo = Cortar(operadorSsh, orcamentoAposSufixo);
        var separadorCabendo = Cortar(separador, orcamentoAposSufixo - operadorCabendo.Length);

        var resultado = $"{operadorCabendo}{separadorCabendo}{sufixo}";

        // Garantia final, independente da aritmetica acima: nunca estoura o
        // limite da coluna.
        return resultado.Length <= TamanhoMaximoDoAtorExterno ? resultado : Cortar(resultado, TamanhoMaximoDoAtorExterno);
    }

    /// <summary>
    /// ÚNICO ponto de importação de chave pública do sistema — usado por
    /// <see cref="ChavePublicaConfiguradaValida"/>, <see cref="AssinaturaConfere"/>
    /// e <see cref="FingerprintDaChavePublica"/>, nunca uma cópia por método.
    /// </summary>
    /// <remarks>
    /// CORREÇÃO (bloqueador B24, nona revisão independente) —
    /// <see cref="ECDsa.ImportFromPem(ReadOnlySpan{char})"/> aceita, além de
    /// <c>"PUBLIC KEY"</c> (SPKI), também <c>"PRIVATE KEY"</c> (PKCS8) e
    /// <c>"EC PRIVATE KEY"</c> (SEC1) — confirmado empiricamente antes desta
    /// correção. Um PEM com a chave PRIVADA P-256 colada em
    /// <c>Emergencia:ChavePublicaDoSegundoCustodiante</c> por engano (ou um
    /// texto com os dois blocos colados) seria aceito e usado normalmente,
    /// reabrindo B23 na prática: material privado voltaria a ficar acessível
    /// a partir do servidor.
    ///
    /// Esta rotina aceita EXCLUSIVAMENTE um texto com UM ÚNICO bloco PEM cujo
    /// rótulo seja, byte a byte (<see cref="StringComparison.Ordinal"/>),
    /// <c>"PUBLIC KEY"</c> — nunca <c>"PRIVATE KEY"</c>, nunca
    /// <c>"EC PRIVATE KEY"</c>, nunca qualquer outro rótulo. Um texto com
    /// mais de um bloco (pública + privada coladas, ou dois blocos
    /// quaisquer) é recusado pela mesma checagem de contagem, mesmo que o
    /// primeiro bloco sozinho fosse uma chave pública válida — não há
    /// ambiguidade aceitável sobre qual conteúdo usar. Só depois disso importa
    /// o SubjectPublicKeyInfo (<see cref="ECDsa.ImportSubjectPublicKeyInfo(ReadOnlySpan{byte}, out int)"/>,
    /// nunca <c>ImportFromPem</c>) e exige que o DER seja consumido por
    /// inteiro — sobra de bytes depois do SPKI é tratada como malformado.
    /// </remarks>
    private static bool TentarImportarChavePublicaP256(string? pem, out ECDsa? chave)
    {
        chave = null;

        if (string.IsNullOrWhiteSpace(pem))
        {
            return false;
        }

        List<(string Label, string Base64)> blocos;
        try
        {
            blocos = EncontrarTodosOsBlocosPem(pem);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return false;
        }

        if (blocos.Count != 1 || !string.Equals(blocos[0].Label, "PUBLIC KEY", StringComparison.Ordinal))
        {
            return false;
        }

        byte[] der;
        try
        {
            der = Convert.FromBase64String(blocos[0].Base64);
        }
        catch (FormatException)
        {
            return false;
        }

        ECDsa? candidata = null;
        try
        {
            candidata = ECDsa.Create();
            candidata.ImportSubjectPublicKeyInfo(der, out var bytesLidos);

            if (bytesLidos != der.Length || !EhP256(candidata))
            {
                candidata.Dispose();
                return false;
            }

            chave = candidata;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException or NotSupportedException)
        {
            candidata?.Dispose();
            return false;
        }
    }

    /// <summary>
    /// Enumera TODOS os blocos PEM (rótulo + payload Base64) presentes no
    /// texto — não só o primeiro. É essa enumeração completa que detecta
    /// chave pública e privada coladas no mesmo valor de configuração; olhar
    /// só o primeiro bloco (o que <c>PemEncoding.Find</c> sozinho devolveria)
    /// deixaria passar exatamente essa mistura.
    /// </summary>
    private static List<(string Label, string Base64)> EncontrarTodosOsBlocosPem(string texto)
    {
        var blocos = new List<(string, string)>();
        ReadOnlySpan<char> restante = texto;

        while (true)
        {
            PemFields campos;
            try
            {
                campos = PemEncoding.Find(restante);
            }
            catch (ArgumentException)
            {
                break; // nenhum bloco PEM valido no restante — para de procurar.
            }

            blocos.Add((new string(restante[campos.Label]), new string(restante[campos.Base64Data])));

            var fim = campos.Location.End.GetOffset(restante.Length);
            if (fim <= 0 || fim > restante.Length)
            {
                break; // defensivo: nao deveria acontecer com um Location valido.
            }

            restante = restante[fim..];
        }

        return blocos;
    }

    private static bool EhP256(ECDsa chave)
    {
        if (chave.KeySize != 256)
        {
            return false;
        }

        // KeySize sozinho nao basta: secp256k1 tambem tem 256 bits. Confere a
        // curva pelo OID (ou pelo nome amigavel, quando o runtime so
        // preenche esse).
        var oid = chave.ExportParameters(false).Curve.Oid;

        return string.Equals(oid?.Value, OidDaCurvaP256, StringComparison.Ordinal)
            || string.Equals(oid?.FriendlyName, "nistP256", StringComparison.OrdinalIgnoreCase)
            || string.Equals(oid?.FriendlyName, "ECDSA_P256", StringComparison.OrdinalIgnoreCase);
    }

    private static void GarantirCampoSemAmbiguidade(string valor, string nome)
    {
        if (string.IsNullOrWhiteSpace(valor) || ContemCaractereDeControle(valor))
        {
            throw new ArgumentException(
                $"Campo '{nome}' do desafio nao pode ser vazio nem conter caractere de controle.", nome);
        }
    }

    private static bool ContemCaractereDeControle(string? valor) =>
        valor is not null && valor.Any(char.IsControl);

    private static string Base64UrlSemPadding(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
