using CrmCartorio.Api.Infrastructure.Persistence.Cifra;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Devedores.Domain.Entities;
using CrmCartorio.Api.Modules.Servicos.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Devedores.Infrastructure.Persistence.Configurations;

public class PendenciaFinanceiraConfiguration : IEntityTypeConfiguration<PendenciaFinanceira>
{
    public void Configure(EntityTypeBuilder<PendenciaFinanceira> builder)
    {
        builder.ToTable("PendenciasFinanceiras");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Titulo).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Observacoes).Cifrada(1000);
        builder.Property(p => p.ValorOriginal).HasPrecision(18, 2);
        builder.Property(p => p.ValorPago).HasPrecision(18, 2);
        builder.Property(p => p.Status).HasConversion<byte>();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(p => p.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Conversa>()
            .WithMany()
            .HasForeignKey(p => p.ConversaOrigemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TipoServico>()
            .WithMany()
            .HasForeignKey(p => p.TipoServicoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(p => p.RegistradoPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(p => p.QuitadoPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Pagamentos)
            .WithOne()
            .HasForeignKey(pg => pg.PendenciaFinanceiraId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.TenantId, p.Status, p.DataPromessaPagamento })
            .HasDatabaseName("IX_PendenciasFinanceiras_Tenant_Status_Promessa");

        builder.HasIndex(p => new { p.TenantId, p.ClienteId, p.Status })
            .HasDatabaseName("IX_PendenciasFinanceiras_Tenant_Cliente_Status");
    }
}
