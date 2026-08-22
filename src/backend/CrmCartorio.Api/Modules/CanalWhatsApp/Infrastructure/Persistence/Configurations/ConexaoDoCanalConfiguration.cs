using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Infrastructure.Persistence.Configurations;

public class ConexaoDoCanalConfiguration : IEntityTypeConfiguration<ConexaoDoCanal>
{
    public void Configure(EntityTypeBuilder<ConexaoDoCanal> builder)
    {
        builder.ToTable("CanalWhatsappConexoes");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Numero).HasMaxLength(20).IsUnicode(false);
        builder.Property(c => c.PerfilNome).HasMaxLength(120);
        builder.Property(c => c.Motivo).HasConversion<byte>();
        builder.Property(c => c.ConectadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.DesconectadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(c => c.AtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.TenantId, c.ConectadoEm })
            .HasDatabaseName("IX_CanalWhatsappConexoes_Tenant_ConectadoEm");
    }
}
