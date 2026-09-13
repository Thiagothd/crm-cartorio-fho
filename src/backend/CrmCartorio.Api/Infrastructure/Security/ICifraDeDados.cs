namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// Lancada ao ler uma coluna cifrada que ainda esta em texto puro (sem o
/// prefixo <c>$c1$</c>) enquanto <see cref="OpcoesDeProtecaoDeDados.ExigirCifrado"/>
/// esta ligado — sinal de linha legada que <c>--cifrar-dados-legados</c> nao
/// alcancou, ou de caminho de escrita que ainda grava em claro.
/// </summary>
public class DadoNaoCifradoException(string rotulo)
    : InvalidOperationException(
        $"Coluna '{rotulo}' tem dado em texto puro com ProtecaoDeDados:ExigirCifrado ligado. " +
        "Rode --cifrar-dados-legados antes de exigir cifra, ou desligue ExigirCifrado ate rodar.");

/// <summary>
/// Cifra/decifra dado em repouso (PR3 da blindagem LGPD). Duas formas: texto
/// (colunas NVARCHAR via <see cref="Infrastructure.Persistence.Cifra.ConversorCifrado"/>)
/// e bytes (anexo em disco, <see cref="Anexos.ArmazenamentoDeAnexosEmDisco"/>).
/// </summary>
/// <remarks>
/// Enquanto <see cref="IProvedorDeChaveDeDados.CifraAtiva"/> for falso, toda
/// operacao aqui e identidade — devolve o mesmo texto/bytes recebidos, sem
/// prefixo, sem tocar em nada. Isso e o que torna o deploy deste modulo
/// seguro num ambiente sem chave configurada ainda (ver
/// <see cref="OpcoesDeProtecaoDeDados"/>): a coluna continua exatamente como
/// era antes desta entrega ate alguem provisionar a chave.
/// </remarks>
public interface ICifraDeDados
{
    /// <summary>Cifra <paramref name="claro"/> para gravar na coluna identificada por <paramref name="rotulo"/> (ex.: <c>"Mensagens.Conteudo"</c>, usado como AAD). Nulo entra e sai nulo.</summary>
    string? Cifrar(string? claro, string rotulo);

    /// <summary>Decifra o valor lido da coluna. Sem o prefixo <c>$c1$</c>, devolve como esta (dado legado ou cifra dormente) — a menos que <see cref="OpcoesDeProtecaoDeDados.ExigirCifrado"/> esteja ligado, caso em que lanca <see cref="DadoNaoCifradoException"/>.</summary>
    string? Decifrar(string? armazenado, string rotulo);

    /// <summary>Cifra bytes de anexo. <paramref name="aad"/> amarra o ciphertext ao dono (tenant+mensagem) — mover o arquivo para outro registro invalida a tag.</summary>
    byte[] CifrarBytes(byte[] claro, byte[] aad);

    /// <summary>Decifra bytes de anexo cifrados com <see cref="CifrarBytes"/> e o mesmo <paramref name="aad"/>.</summary>
    byte[] DecifrarBytes(byte[] cifrado, byte[] aad);
}
