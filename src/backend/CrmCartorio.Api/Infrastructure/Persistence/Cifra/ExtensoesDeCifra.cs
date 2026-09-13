using CrmCartorio.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Infrastructure.Persistence.Cifra;

/// <summary>
/// Marca uma propriedade como cifrada em repouso (PR3), sem precisar
/// injetar <see cref="ICifraDeDados"/> dentro de cada
/// <c>IEntityTypeConfiguration&lt;T&gt;</c> — essas classes sao instanciadas
/// pelo <c>ApplyConfigurationsFromAssembly</c> sem passar por DI.
/// <c>.Cifrada(...)</c> so grava metadado (annotation); quem de fato troca o
/// <see cref="Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter"/>
/// e <see cref="AplicarConversoresDeCifra"/>, chamado por
/// <c>CrmDbContext.OnModelCreating</c> DEPOIS de
/// <c>ApplyConfigurationsFromAssembly</c>, ja com a instancia real de
/// <see cref="ICifraDeDados"/> do construtor do contexto.
/// </summary>
public static class ExtensoesDeCifra
{
    private const string AnotacaoCifrada = "CrmCartorio:Cifrada";
    private const string AnotacaoTamanho = "CrmCartorio:TamanhoDoClaro";

    /// <summary>
    /// Coluna de texto, obrigatoria ou opcional. Generico em
    /// <typeparamref name="TPropriedade"/> (em vez de fixar <c>string</c> ou
    /// <c>string?</c>) de proposito — <c>PropertyBuilder&lt;T&gt;</c> e
    /// invariante, entao qualquer escolha fixa gera aviso CS8620 no outro
    /// caso (propriedade obrigatoria x opcional); genérico aceita as duas sem
    /// aviso, ja que aqui dentro so se manipula metadado, nunca o valor.
    /// </summary>
    public static PropertyBuilder<TPropriedade> Cifrada<TPropriedade>(
        this PropertyBuilder<TPropriedade> builder, int tamanhoMaximoDoClaro)
    {
        builder.HasAnnotation(AnotacaoCifrada, true);
        builder.HasAnnotation(AnotacaoTamanho, tamanhoMaximoDoClaro);
        builder.HasColumnType("nvarchar(max)");
        builder.Metadata.SetMaxLength(null);
        return builder;
    }

    /// <summary>
    /// Data cifrada como texto ISO (<c>yyyy-MM-dd</c>). Aponta para uma coluna
    /// NOVA (<paramref name="novaColuna"/>), nunca reaproveita a <c>date</c>
    /// existente — mudar o TIPO de uma coluna que o binario anterior ainda lê
    /// como <c>date</c> quebraria o deploy expand-only (N-1 rodando durante a
    /// migracao veria uma coluna que não é mais data). A coluna antiga fica
    /// orfã no banco até uma migração de limpeza futura, depois de confirmado
    /// que ninguém mais depende dela.
    /// </summary>
    public static PropertyBuilder<DateOnly?> CifradaComoData(this PropertyBuilder<DateOnly?> builder, string novaColuna)
    {
        builder.HasAnnotation(AnotacaoCifrada, true);
        builder.HasColumnType("nvarchar(max)");
        builder.HasColumnName(novaColuna);
        return builder;
    }

    public static void AplicarConversoresDeCifra(this ModelBuilder modelBuilder, ICifraDeDados cifra)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var tabela = entityType.GetTableName() ?? entityType.ClrType.Name;

            foreach (var property in entityType.GetProperties())
            {
                if (property[AnotacaoCifrada] is not true)
                {
                    continue;
                }

                var rotulo = $"{tabela}.{property.Name}";

                if (property.ClrType == typeof(DateOnly) || property.ClrType == typeof(DateOnly?))
                {
                    property.SetValueConverter(new ConversorDeDataCifrada(cifra, rotulo));
                }
                else
                {
                    var tamanho = (int)property[AnotacaoTamanho]!;
                    property.SetValueConverter(new ConversorCifrado(cifra, rotulo, tamanho));
                }
            }
        }
    }
}
