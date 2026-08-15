using CrmCartorio.Api.Modules.Servicos.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Servicos.Infrastructure.Persistence.Configurations;

public class PrazoServicoConfiguration : IEntityTypeConfiguration<PrazoServico>
{
    public void Configure(EntityTypeBuilder<PrazoServico> builder)
    {
        builder.ToTable("PrazosServico", t =>
        {
            t.HasCheckConstraint("CK_PrazosServico_PrazoHoras", "[PrazoHoras] > 0");
            t.HasCheckConstraint(
                "CK_PrazosServico_PercentualAlerta",
                "[PercentualAlerta] BETWEEN 1 AND 99");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(p => p.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Garante o 1:1 com TipoServico.
        builder.HasIndex(p => p.TipoServicoId)
            .IsUnique()
            .HasDatabaseName("UQ_PrazosServico_TipoServico");
    }
}
