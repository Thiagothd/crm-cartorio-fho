using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Usuarios.Infrastructure.Persistence.Configurations;

public class CodigoDeRecuperacaoMfaConfiguration : IEntityTypeConfiguration<CodigoDeRecuperacaoMfa>
{
    public void Configure(EntityTypeBuilder<CodigoDeRecuperacaoMfa> builder)
    {
        builder.ToTable("CodigosDeRecuperacaoMfa");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.HashDoCodigo).HasMaxLength(64).IsFixedLength().IsRequired().IsUnicode(false);
        builder.Property(c => c.UsadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Atendente>().WithMany().HasForeignKey(c => c.AtendenteId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.AtendenteId, c.HashDoCodigo })
            .IsUnique()
            .HasDatabaseName("UQ_CodigosDeRecuperacaoMfa_Atendente_Hash");
    }
}
