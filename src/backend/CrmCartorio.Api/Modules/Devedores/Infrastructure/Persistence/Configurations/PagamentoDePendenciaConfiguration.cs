using CrmCartorio.Api.Infrastructure.Persistence.Cifra;
using CrmCartorio.Api.Modules.Devedores.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Devedores.Infrastructure.Persistence.Configurations;

public class PagamentoDePendenciaConfiguration : IEntityTypeConfiguration<PagamentoDePendencia>
{
    public void Configure(EntityTypeBuilder<PagamentoDePendencia> builder)
    {
        builder.ToTable("PagamentosDePendencia");

        builder.HasKey(pg => pg.Id);

        builder.Property(pg => pg.Valor).HasPrecision(18, 2);
        builder.Property(pg => pg.MeioPagamento).HasConversion<byte>();
        builder.Property(pg => pg.Observacao).Cifrada(500);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(pg => pg.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(pg => pg.RegistradoPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(pg => new { pg.PendenciaFinanceiraId, pg.PagoEm })
            .HasDatabaseName("IX_PagamentosDePendencia_Pendencia_PagoEm");
    }
}
