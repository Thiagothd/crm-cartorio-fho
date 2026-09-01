using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Tenants.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.AparenciaDaConversa).HasConversion<byte>();
        builder.Property(t => t.MfaObrigatorioParaAdministradores).HasDefaultValue(false);

        builder.Property(t => t.RazaoSocial).HasMaxLength(200).IsRequired();
        builder.Property(t => t.NomeFantasia).HasMaxLength(200);
        builder.Property(t => t.Cnpj).HasMaxLength(14).IsRequired().IsUnicode(false);
        builder.Property(t => t.WhatsappBusinessAccountId).HasMaxLength(64).IsUnicode(false);
        builder.Property(t => t.WhatsappPhoneNumberId).HasMaxLength(64).IsUnicode(false);
        builder.Property(t => t.WhatsappNumeroExibicao).HasMaxLength(20).IsUnicode(false);
        builder.Property(t => t.WhatsappInstancia).HasMaxLength(120).IsUnicode(false);
        builder.Property(t => t.WhatsappModoDeRecebimento).HasConversion<byte?>();
        builder.Property(t => t.WhatsappPerfilNome).HasMaxLength(120);
        builder.Property(t => t.WhatsappPerfilFotoUrl).HasMaxLength(500);
        builder.Property(t => t.WhatsappConectadoEm).HasColumnType("datetime2(3)");
        builder.Property(t => t.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(t => t.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasIndex(t => t.Cnpj).IsUnique().HasDatabaseName("UQ_Tenants_Cnpj");

        // Duas serventias apontando para a mesma instancia fariam a mensagem cair
        // no atendimento da serventia errada. Filtrado para permitir varios nulos
        // — quem ainda nao conectou o canal.
        builder.HasIndex(t => t.WhatsappInstancia)
            .IsUnique()
            .HasFilter("[WhatsappInstancia] IS NOT NULL")
            .HasDatabaseName("UQ_Tenants_WhatsappInstancia");
    }
}
