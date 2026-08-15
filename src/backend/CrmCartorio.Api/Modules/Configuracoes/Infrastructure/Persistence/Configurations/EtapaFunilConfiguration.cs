using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Configuracoes.Infrastructure.Persistence.Configurations;

public class EtapaFunilConfiguration : IEntityTypeConfiguration<EtapaFunil>
{
    public void Configure(EntityTypeBuilder<EtapaFunil> builder)
    {
        builder.ToTable("EtapasFunil");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Nome).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Descricao).HasMaxLength(300);
        builder.Property(e => e.Cor).HasMaxLength(9).IsRequired().IsUnicode(false);
        builder.Property(e => e.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(e => e.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Nome })
            .IsUnique()
            .HasDatabaseName("UQ_EtapasFunil_Tenant_Nome");

        builder.HasIndex(e => new { e.TenantId, e.Ordem })
            .HasDatabaseName("IX_EtapasFunil_Tenant_Ordem");
    }
}
