using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Usuarios.Infrastructure.Persistence.Configurations;

public class DesafioDeMfaConfiguration : IEntityTypeConfiguration<DesafioDeMfa>
{
    public void Configure(EntityTypeBuilder<DesafioDeMfa> builder)
    {
        builder.ToTable("DesafiosDeMfa");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.HashDoDesafio).HasMaxLength(64).IsFixedLength().IsRequired().IsUnicode(false);
        builder.Property(d => d.Tipo).HasConversion<byte>();
        builder.Property(d => d.ExpiraEm).HasColumnType("datetime2(3)");
        builder.Property(d => d.ConsumidoEm).HasColumnType("datetime2(3)");
        builder.Property(d => d.IpDeOrigem).HasMaxLength(45).IsUnicode(false);
        builder.Property(d => d.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(d => d.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Atendente>().WithMany().HasForeignKey(d => d.AtendenteId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.HashDoDesafio).IsUnique().HasDatabaseName("UQ_DesafiosDeMfa_Hash");

        // Varredura de expirados: so os ainda em aberto interessam.
        builder.HasIndex(d => d.ExpiraEm)
            .HasFilter("[ConsumidoEm] IS NULL")
            .HasDatabaseName("IX_DesafiosDeMfa_ExpiraEm_Abertos");
    }
}
