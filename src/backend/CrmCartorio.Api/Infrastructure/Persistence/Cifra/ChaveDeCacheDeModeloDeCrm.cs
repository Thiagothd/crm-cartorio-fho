using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CrmCartorio.Api.Infrastructure.Persistence.Cifra;

/// <summary>
/// Faz o cache de model do EF Core (por padrao, so por TIPO de DbContext)
/// tambem levar em conta QUAL instancia de <see cref="Security.ICifraDeDados"/>
/// construiu o <see cref="CrmDbContext"/> — sem isto, um segundo
/// <c>new CrmDbContext(mesmasOpcoes, outraCifra)</c> reaproveitaria (calado)
/// o model montado pelo primeiro, com os ValueConverter de cifra amarrados
/// para sempre na PRIMEIRA instancia de cifra que existiu no processo.
/// </summary>
/// <remarks>
/// Producao nunca sente esta diferenca: <c>ICifraDeDados</c> e singleton por
/// toda a vida do processo (Program.cs), entao so existe UMA instancia de
/// verdade e o cache nunca precisaria variar. O caso que expoe o problema e
/// ferramenta/verificador testando cifra DORMENTE e ATIVA no MESMO processo
/// — exatamente o padrao de <c>verificar-protecao-de-dados.cs</c>. Sem esta
/// chave, a cifra "ativa" nunca cifrava de verdade: o model ficava preso ao
/// primeiro <c>ICifraDeDados</c> construido (o dormente), e
/// <c>--cifrar-dados-legados</c> entrava em loop infinito (a linha nunca
/// saia da condicao de "ainda em claro" porque a escrita nunca cifrava).
/// </remarks>
public sealed class ChaveDeCacheDeModeloDeCrm : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is CrmDbContext crm
            ? (context.GetType(), designTime, crm.Cifra)
            : new ModelCacheKey(context, designTime);
}
