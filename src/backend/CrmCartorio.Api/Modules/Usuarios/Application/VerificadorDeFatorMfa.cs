using System.Security.Cryptography;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

/// <summary>
/// SEC-03: interpreta e verifica um codigo de segundo fator (TOTP ou codigo de
/// recuperacao) apresentado por quem chama — compartilhado entre
/// <see cref="AutenticacaoService"/> (etapa 2 do login) e <see cref="MfaService"/>
/// (step-up de operacoes sensiveis), para as duas nunca divergirem sobre o que
/// conta como codigo valido.
/// </summary>
/// <remarks>
/// <b>Deliberadamente sem efeito colateral.</b> Este verificador nunca escreve no
/// banco — nem consome o codigo de recuperacao, nem grava o CAS de
/// <c>MfaUltimoPassoUsado</c>. Cada chamador faz a escrita atomica dentro da sua
/// PROPRIA transacao de porteiro unico (login: <c>ConcluirLoginMfaAsync</c>;
/// gestao: o step-up de cada operacao), porque so o chamador sabe compor o
/// <c>WHERE</c> com o resto do estado que precisa ser reconfirmado no mesmo golpe
/// (TokenVersion do desafio, Ativo, MfaAtivo, etc.). Um verificador que decidisse
/// e escrevesse sozinho reabriria exatamente a corrida que a revisao de SEC-03
/// fechou no consumo do desafio.
/// </remarks>
public static class VerificadorDeFatorMfa
{
    public static bool PareceCodigoTotp(string codigo) =>
        codigo.Length == 6 && codigo.All(char.IsAsciiDigit);

    /// <summary>
    /// Decifra o segredo e valida o TOTP (janela +-1). Devolve o PASSO aceito —
    /// quem chama ainda precisa confirmar, na propria escrita atomica, que esse
    /// passo e maior que <c>MfaUltimoPassoUsado</c> (o CAS de verdade). Esta
    /// checagem aqui e so uma pre-filtragem que evita abrir transacao para um
    /// codigo obviamente repetido.
    /// </summary>
    public static bool TentarValidarTotp(
        ICifraDeSegredoMfa cifra, Atendente atendente, string codigo, out long passoValidado)
    {
        passoValidado = 0;

        if (atendente.MfaSegredoCifrado is null)
        {
            return false;
        }

        string segredo;
        try
        {
            segredo = cifra.Decifrar(atendente.MfaSegredoCifrado, atendente.TenantId, atendente.Id);
        }
        catch (CryptographicException)
        {
            // Chave errada, tag inconsistente ou dado corrompido — fail closed,
            // sem detalhe do motivo. Nunca deveria acontecer em operacao normal.
            return false;
        }

        if (!ValidadorDeTotp.Validar(segredo, codigo, out passoValidado))
        {
            return false;
        }

        return atendente.MfaUltimoPassoUsado is not { } ultimo || passoValidado > ultimo;
    }

    /// <summary>SHA-256 do codigo normalizado — a chave de busca em <c>CodigosDeRecuperacaoMfa</c>.</summary>
    public static string HashDoCodigoDeRecuperacao(string codigo) =>
        SegredosAleatoriosMfa.Sha256Hex(SegredosAleatoriosMfa.NormalizarCodigoDeRecuperacao(codigo));
}
