using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Usuarios.Infrastructure.Persistence.Configurations;

public class DispositivoConfiavelConfiguration : IEntityTypeConfiguration<DispositivoConfiavel>
{
    public void Configure(EntityTypeBuilder<DispositivoConfiavel> builder)
    {
        builder.ToTable("DispositivosConfiaveis");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.HashDoToken).HasMaxLength(64).IsFixedLength().IsRequired().IsUnicode(false);
        builder.Property(d => d.ExpiraEm).HasColumnType("datetime2(3)");
        builder.Property(d => d.RevogadoEm).HasColumnType("datetime2(3)");
        builder.Property(d => d.UltimoUsoEm).HasColumnType("datetime2(3)");
        builder.Property(d => d.IpDeOrigem).HasMaxLength(45).IsUnicode(false);
        builder.Property(d => d.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(d => d.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Atendente>().WithMany().HasForeignKey(d => d.AtendenteId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.HashDoToken).IsUnique().HasDatabaseName("UQ_DispositivosConfiaveis_Hash");

        // Contagem/revogacao em lote por conta: so os ainda ativos interessam.
        builder.HasIndex(d => d.AtendenteId)
            .HasFilter("[RevogadoEm] IS NULL")
            .HasDatabaseName("IX_DispositivosConfiaveis_Atendente_Ativos");
    }
}
