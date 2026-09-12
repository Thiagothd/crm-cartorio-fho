using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <inheritdoc cref="IProvedorDeChaveDeDados"/>
/// <remarks>
/// Implementacao "nivel 1": le de <see cref="OpcoesDeProtecaoDeDados"/>
/// (appsettings). <see cref="CifraAtiva"/> so fica verdadeiro quando
/// <see cref="OpcoesDeProtecaoDeDados.VersaoCorrente"/>, a chave
/// correspondente em <see cref="OpcoesDeProtecaoDeDados.Chaves"/> e
/// <see cref="OpcoesDeProtecaoDeDados.ChaveDeIndice"/> estao TODOS presentes
/// e validos — falta qualquer um, e o provedor fica dormente (nunca "cifra
/// pela metade"). Uma chave presente porem malformada (base64 invalido,
/// tamanho errado) e erro de configuracao de verdade e lanca no boot —
/// ausencia total nao lanca nunca (ver <see cref="OpcoesDeProtecaoDeDados"/>).
/// </remarks>
public class ProvedorDeChaveEmConfiguracao : IProvedorDeChaveDeDados
{
    private readonly OpcoesDeProtecaoDeDados _opcoes;
    private readonly Dictionary<string, byte[]> _chaves;
    private readonly byte[]? _chaveDeIndice;

    public ProvedorDeChaveEmConfiguracao(IOptions<OpcoesDeProtecaoDeDados> opcoes)
    {
        _opcoes = opcoes.Value;
        _chaves = _opcoes.Chaves.ToDictionary(
            par => par.Key,
            par => DecodificarChave(par.Value, $"ProtecaoDeDados:Chaves:{par.Key}"));
        _chaveDeIndice = string.IsNullOrWhiteSpace(_opcoes.ChaveDeIndice)
            ? null
            : DecodificarChave(_opcoes.ChaveDeIndice, "ProtecaoDeDados:ChaveDeIndice");

        CifraAtiva =
            !string.IsNullOrWhiteSpace(_opcoes.VersaoCorrente) &&
            _chaves.ContainsKey(_opcoes.VersaoCorrente) &&
            _chaveDeIndice is not null;
    }

    public bool CifraAtiva { get; }

    public string VersaoCorrente =>
        CifraAtiva
            ? _opcoes.VersaoCorrente!
            : throw new InvalidOperationException(
                "Cifra em repouso nao esta ativa (ProtecaoDeDados sem chave configurada). " +
                "Conferir IProvedorDeChaveDeDados.CifraAtiva antes de pedir a versao corrente.");

    public byte[] ChaveDeIndice =>
        _chaveDeIndice ?? throw new InvalidOperationException(
            "Cifra em repouso nao esta ativa (ProtecaoDeDados:ChaveDeIndice ausente).");

    public byte[] ObterChave(string versao)
    {
        if (_chaves.TryGetValue(versao, out var chave))
        {
            return chave;
        }

        throw new InvalidOperationException(
            $"Nenhuma chave de dados configurada para a versao '{versao}' (ProtecaoDeDados:Chaves:{versao}). " +
            "Provavel rotacao de chave incompleta: a chave antiga precisa continuar no mapa " +
            "para decifrar dado gravado com ela.");
    }

    private static byte[] DecodificarChave(string base64, string nomeDoCampo)
    {
        byte[] chave;

        try
        {
            chave = Convert.FromBase64String(base64);
        }
        catch (FormatException erro)
        {
            throw new InvalidOperationException($"{nomeDoCampo} nao e base64 valido.", erro);
        }

        if (chave.Length != 32)
        {
            throw new InvalidOperationException($"{nomeDoCampo} precisa ter 32 bytes; tem {chave.Length}.");
        }

        return chave;
    }
}
