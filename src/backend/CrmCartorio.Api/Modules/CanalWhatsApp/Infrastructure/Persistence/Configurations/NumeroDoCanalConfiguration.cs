using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Infrastructure.Persistence.Configurations;

public class NumeroDoCanalConfiguration : IEntityTypeConfiguration<NumeroDoCanal>
{
    public void Configure(EntityTypeBuilder<NumeroDoCanal> builder)
    {
        builder.ToTable("CanalWhatsappNumeros");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Telefone).HasMaxLength(20).IsRequired().IsUnicode(false);
        builder.Property(n => n.Rotulo).HasMaxLength(80);
        builder.Property(n => n.Tipo).HasConversion<byte>();
        builder.Property(n => n.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(n => n.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(n => n.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(n => n.CriadoPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        // O mesmo numero nao pode estar liberado e bloqueado ao mesmo tempo —
        // essa contradicao e recusada aqui, nao so validada no service.
        builder.HasIndex(n => new { n.TenantId, n.Telefone })
            .IsUnique()
            .HasDatabaseName("UQ_CanalWhatsappNumeros_Tenant_Telefone");
    }
}
