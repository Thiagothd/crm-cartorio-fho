using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CrmCartorio.Api.Infrastructure.Persistence;

/// <summary>
/// Marca todo <see cref="DateTime"/> lido do banco como <see cref="DateTimeKind.Utc"/>,
/// sem alterar o valor gravado.
/// </summary>
/// <remarks>
/// SQL Server <c>datetime2</c> nao guarda fuso — todo valor volta do banco com
/// <c>Kind = Unspecified</c>, mesmo tendo sido gravado com <c>DateTime.UtcNow</c>
/// (MODELO-DADOS.md, secao 3: "Datas UTC em datetime2(3)"). Sem o Kind explicito,
/// o <c>System.Text.Json</c> serializa a data sem o sufixo <c>"Z"</c>, e o
/// navegador — sem marcador de fuso na string — interpreta a hora como se ja
/// fosse local, em vez de converter. Resultado observado ao vivo em
/// 2026-08-23: mensagem enviada as 18:53 (hora do Brasil) aparecia como 21:53
/// no painel — exatamente o deslocamento do fuso (UTC-3), aplicado ao contrario.
///
/// Registrado como conversao global em
/// <see cref="CrmDbContext.ConfigureConventions"/> — corrigir propriedade por
/// propriedade deixaria a proxima entidade nova sujeita ao mesmo bug.
/// </remarks>
public sealed class ConversorDeDataUtc : ValueConverter<DateTime, DateTime>
{
    public ConversorDeDataUtc() : base(
        paraOBanco => paraOBanco,
        doBanco => DateTime.SpecifyKind(doBanco, DateTimeKind.Utc))
    {
    }
}

/// <summary>Mesma correcao de <see cref="ConversorDeDataUtc"/>, para colunas anulaveis.</summary>
public sealed class ConversorDeDataUtcNulavel : ValueConverter<DateTime?, DateTime?>
{
    public ConversorDeDataUtcNulavel() : base(
        paraOBanco => paraOBanco,
        doBanco => doBanco.HasValue ? DateTime.SpecifyKind(doBanco.Value, DateTimeKind.Utc) : doBanco)
    {
    }
}
