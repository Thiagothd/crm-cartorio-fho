using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;
using CrmCartorio.Api.Modules.Servicos.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Infrastructure.Persistence.Configurations;

public class ConhecimentoDeIaConfiguration : IEntityTypeConfiguration<ConhecimentoDeIa>
{
    public void Configure(EntityTypeBuilder<ConhecimentoDeIa> builder)
    {
        builder.ToTable("ConhecimentosDeIa");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Titulo).HasMaxLength(160).IsRequired();
        builder.Property(c => c.Conteudo).HasMaxLength(2000).IsRequired();
        builder.Property(c => c.Tema).HasMaxLength(80).IsRequired();
        builder.Property(c => c.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TipoServico>()
            .WithMany()
            .HasForeignKey(c => c.TipoServicoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(c => c.CriadoPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(c => c.AtualizadoPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        // A tela lista por tenant, filtrando so o que esta ativo — mesmo padrao
        // de indice usado em TiposServico (IX_TiposServico_Tenant_Setor_Ativo).
        builder.HasIndex(c => new { c.TenantId, c.Ativo })
            .HasDatabaseName("IX_ConhecimentosDeIa_Tenant_Ativo");
    }
}
