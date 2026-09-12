using CrmCartorio.Api.Infrastructure.Persistence.Cifra;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Infrastructure.Persistence;

/// <summary>
/// Ponto unico de configuracao do provider. A API e os verificadores em
/// src/backend/*.cs usam esta mesma funcao.
/// </summary>
/// <remarks>
/// Existe porque ja custou caro: os verificadores montavam o DbContext com opcoes
/// proprias e por isso nao reproduziam o comportamento da API — um erro que so
/// aparecia em execucao ficou invisivel para eles. Configuracao duplicada e
/// configuracao que diverge.
/// </remarks>
public static class ConfiguracaoDoBanco
{
    public static void Configurar(DbContextOptionsBuilder opcoes, string? connectionString)
    {
        // Ver ChaveDeCacheDeModeloDeCrm: sem isto, o model cacheado por tipo
        // de DbContext ignoraria qual ICifraDeDados construiu esta instancia
        // (PR3, cifra em repouso).
        opcoes.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, ChaveDeCacheDeModeloDeCrm>();

        opcoes.UseSqlServer(connectionString, sql =>
        {
            sql.CommandTimeout(30);

            // NAO ligar EnableRetryOnFailure aqui.
            //
            // A estrategia de retry do SQL Server recusa transacao iniciada pela
            // aplicacao (BeginTransactionAsync) — e o sistema depende de transacao
            // explicita em dois pontos: a geracao de protocolo, que le o ultimo
            // numero do mes com UPDLOCK/HOLDLOCK, e a atribuicao de conversa.
            //
            // Ligar as duas coisas juntas exige envolver cada operacao em
            // CreateExecutionStrategy() com um DbContext novo por tentativa; sem
            // isso, uma retentativa reaproveita entidades ja rastreadas e insere
            // duplicado. Para IIS e SQL Server na mesma rede, a falha transitoria
            // que o retry cobre e rara e o custo dela e o atendente clicar de novo.
        });
    }
}
