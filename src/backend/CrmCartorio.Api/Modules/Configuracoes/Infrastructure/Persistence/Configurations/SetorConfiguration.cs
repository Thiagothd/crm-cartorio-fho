using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Configuracoes.Infrastructure.Persistence.Configurations;

public class SetorConfiguration : IEntityTypeConfiguration<Setor>
{
    public void Configure(EntityTypeBuilder<Setor> builder)
    {
        builder.ToTable("Setores");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Nome).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Descricao).HasMaxLength(300);
        builder.Property(s => s.ModoDoRobo).HasConversion<byte>();
        builder.Property(s => s.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(s => s.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.TenantId, s.Nome })
            .IsUnique()
            .HasDatabaseName("UQ_Setores_Tenant_Nome");

        builder.HasIndex(s => new { s.TenantId, s.Ordem })
            .HasDatabaseName("IX_Setores_Tenant_Ordem");
    }
}
