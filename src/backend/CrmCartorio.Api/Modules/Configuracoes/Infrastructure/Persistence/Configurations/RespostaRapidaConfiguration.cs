using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Configuracoes.Infrastructure.Persistence.Configurations;

public class RespostaRapidaConfiguration : IEntityTypeConfiguration<RespostaRapida>
{
    public void Configure(EntityTypeBuilder<RespostaRapida> builder)
    {
        builder.ToTable("RespostasRapidas");

        builder.HasKey(r => r.Id);

        // Atalho e ASCII sem espaco: o atendente digita "/prazo" no meio do
        // atendimento e nao deveria precisar de acento para acertar.
        builder.Property(r => r.Atalho).HasMaxLength(30).IsUnicode(false).IsRequired();

        builder.Property(r => r.Titulo).HasMaxLength(120).IsRequired();
        builder.Property(r => r.Conteudo).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(r => r.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Dois atalhos iguais deixariam o seletor ambiguo: o atendente digita
        // "/prazo", ve duas linhas identicas e nao tem como escolher a certa.
        builder.HasIndex(r => new { r.TenantId, r.Atalho })
            .IsUnique()
            .HasDatabaseName("UQ_RespostasRapidas_Tenant_Atalho");
    }
}
