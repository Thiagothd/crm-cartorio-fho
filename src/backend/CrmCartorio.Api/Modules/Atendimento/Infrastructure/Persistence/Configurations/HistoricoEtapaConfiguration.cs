using CrmCartorio.Api.Infrastructure.Persistence.Cifra;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Atendimento.Infrastructure.Persistence.Configurations;

public class HistoricoEtapaConfiguration : IEntityTypeConfiguration<HistoricoEtapa>
{
    public void Configure(EntityTypeBuilder<HistoricoEtapa> builder)
    {
        builder.ToTable("HistoricoEtapas");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.TipoMudanca).HasConversion<byte>();
        // Cifrada em repouso (PR3): observacao livre de transferencia/mudanca de etapa.
        builder.Property(h => h.Observacao).Cifrada(500);
        builder.Property(h => h.OcorridoEm).HasColumnType("datetime2(3)");
        builder.Property(h => h.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(h => h.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(h => h.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EtapaFunil>()
            .WithMany()
            .HasForeignKey(h => h.EtapaAnteriorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EtapaFunil>()
            .WithMany()
            .HasForeignKey(h => h.EtapaNovaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Setor>()
            .WithMany()
            .HasForeignKey(h => h.SetorAnteriorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Setor>()
            .WithMany()
            .HasForeignKey(h => h.SetorNovoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(h => h.AtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => new { h.ConversaId, h.OcorridoEm })
            .HasDatabaseName("IX_HistoricoEtapas_Conversa_Ocorrido");
    }
}
