using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;
using CrmCartorio.Api.Modules.Servicos.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Infrastructure.Persistence.Configurations;

public class HistoricoDeConhecimentoDeIaConfiguration : IEntityTypeConfiguration<HistoricoDeConhecimentoDeIa>
{
    public void Configure(EntityTypeBuilder<HistoricoDeConhecimentoDeIa> builder)
    {
        builder.ToTable("HistoricoDeConhecimentosDeIa");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Titulo).HasMaxLength(160).IsRequired();
        builder.Property(h => h.Conteudo).HasMaxLength(2000).IsRequired();
        builder.Property(h => h.Tema).HasMaxLength(80).IsRequired();
        builder.Property(h => h.OcorridoEm).HasColumnType("datetime2(3)");
        builder.Property(h => h.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(h => h.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(h => h.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ConhecimentoDeIa>()
            .WithMany()
            .HasForeignKey(h => h.ConhecimentoDeIaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TipoServico>()
            .WithMany()
            .HasForeignKey(h => h.TipoServicoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(h => h.AlteradoPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reconstrói a linha do tempo de um item: todas as versões, mais recente primeiro.
        builder.HasIndex(h => new { h.ConhecimentoDeIaId, h.Versao })
            .HasDatabaseName("IX_HistoricoDeConhecimentosDeIa_Conhecimento_Versao");
    }
}
