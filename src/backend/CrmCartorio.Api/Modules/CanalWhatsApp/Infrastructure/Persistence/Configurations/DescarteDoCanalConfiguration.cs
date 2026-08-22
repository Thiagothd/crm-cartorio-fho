using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Infrastructure.Persistence.Configurations;

public class DescarteDoCanalConfiguration : IEntityTypeConfiguration<DescarteDoCanal>
{
    public void Configure(EntityTypeBuilder<DescarteDoCanal> builder)
    {
        builder.ToTable("CanalWhatsappDescartes");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Telefone).HasMaxLength(20).IsRequired().IsUnicode(false);
        builder.Property(d => d.Motivo).HasConversion<byte>();
        builder.Property(d => d.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(d => d.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(d => d.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => new { d.TenantId, d.CriadoEm })
            .HasDatabaseName("IX_CanalWhatsappDescartes_Tenant_CriadoEm");
    }
}
