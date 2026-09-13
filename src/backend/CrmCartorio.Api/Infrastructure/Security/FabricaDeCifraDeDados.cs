using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// Monta <see cref="ICifraDeDados"/> fora do container de DI normal — as
/// ferramentas de linha de comando (<c>FerramentaDeResetDeEmergencia</c> e
/// afins) e o <c>CrmDbContext</c> manual dos verificadores precisam de uma
/// instancia sem passar pelo <c>WebApplication</c>.
/// </summary>
public static class FabricaDeCifraDeDados
{
    /// <summary>Le <see cref="OpcoesDeProtecaoDeDados"/> de uma configuracao ja carregada (appsettings real). Fica dormente se a secao nao existir — mesma regra do boot normal.</summary>
    public static ICifraDeDados CriarDeConfiguracao(IConfiguration configuracao)
    {
        var opcoes = configuracao.GetSection(OpcoesDeProtecaoDeDados.Secao).Get<OpcoesDeProtecaoDeDados>()
            ?? new OpcoesDeProtecaoDeDados();
        return Criar(opcoes).Cifra;
    }

    /// <summary>Sem nenhuma chave — identidade em todas as operacoes. Para ferramenta/verificador que nao precisa provar cifra de verdade.</summary>
    public static ICifraDeDados CriarDormente() => Criar(new OpcoesDeProtecaoDeDados()).Cifra;

    /// <summary>
    /// Mesmo par (provedor, cifra) dormente de <see cref="CriarDormente"/> —
    /// para o verificador que tambem precisa do <see cref="IProvedorDeChaveDeDados"/>
    /// direto (ex.: montar <c>ClienteService</c>, que agora depende dele para
    /// a busca por documento — ver Modules/Clientes/Application/ClienteService.cs).
    /// </summary>
    public static (IProvedorDeChaveDeDados Provedor, ICifraDeDados Cifra) CriarParDormente() =>
        Criar(new OpcoesDeProtecaoDeDados());

    private const string VersaoDeDesenvolvimento = "dev";
    // 32 bytes fixos, so para banco de dev/verificador — nunca usar em ambiente real.
    private const string ChaveDeDesenvolvimento = "chave-de-desenvolvimento-32-byte";
    private const string ChaveDeIndiceDeDesenvolvimento = "indice-de-desenvolvimento-32-byt";

    /// <summary>Chave fixa de desenvolvimento — para verificador provar round-trip de cifra sem depender de appsettings.</summary>
    public static ICifraDeDados CriarComChaveDeDesenvolvimento() => CriarParComChaveDeDesenvolvimento().Cifra;

    /// <summary>
    /// Mesmo par de <see cref="CriarComChaveDeDesenvolvimento"/>, com o
    /// provedor junto. <paramref name="exigirCifrado"/> exposto para o
    /// verificador provar <see cref="DadoNaoCifradoException"/> sem duplicar
    /// as chaves de desenvolvimento noutro lugar.
    /// </summary>
    public static (IProvedorDeChaveDeDados Provedor, ICifraDeDados Cifra) CriarParComChaveDeDesenvolvimento(
        bool exigirCifrado = false) =>
        Criar(OpcoesDeDesenvolvimento(exigirCifrado));

    /// <summary>
    /// <see cref="IConfiguration"/> equivalente as chaves de desenvolvimento
    /// acima, com a connection string informada — para o verificador invocar
    /// <c>FerramentaDeCifraDeDadosLegados.ExecutarAsync</c> de verdade (ela le
    /// de <see cref="IConfiguration"/>, nao aceita as chaves diretamente).
    /// </summary>
    public static IConfiguration CriarConfiguracaoDeDesenvolvimento(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = connectionString,
                [$"{OpcoesDeProtecaoDeDados.Secao}:VersaoCorrente"] = VersaoDeDesenvolvimento,
                [$"{OpcoesDeProtecaoDeDados.Secao}:Chaves:{VersaoDeDesenvolvimento}"] =
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ChaveDeDesenvolvimento)),
                [$"{OpcoesDeProtecaoDeDados.Secao}:ChaveDeIndice"] =
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ChaveDeIndiceDeDesenvolvimento))
            })
            .Build();

    private static OpcoesDeProtecaoDeDados OpcoesDeDesenvolvimento(bool exigirCifrado) => new()
    {
        VersaoCorrente = VersaoDeDesenvolvimento,
        Chaves = new Dictionary<string, string>
        {
            [VersaoDeDesenvolvimento] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ChaveDeDesenvolvimento))
        },
        ChaveDeIndice = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ChaveDeIndiceDeDesenvolvimento)),
        ExigirCifrado = exigirCifrado
    };

    private static (IProvedorDeChaveDeDados Provedor, ICifraDeDados Cifra) Criar(OpcoesDeProtecaoDeDados opcoes)
    {
        var provedor = new ProvedorDeChaveEmConfiguracao(Options.Create(opcoes));
        return (provedor, new CifraDeDados(provedor, Options.Create(opcoes)));
    }
}
