using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Usuarios.Infrastructure.Persistence.Configurations;

public class EventoDeSegurancaConfiguration : IEntityTypeConfiguration<EventoDeSeguranca>
{
    public void Configure(EntityTypeBuilder<EventoDeSeguranca> builder)
    {
        builder.ToTable("EventosDeSeguranca");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Tipo).HasConversion<byte>();
        builder.Property(e => e.AtorExterno).HasMaxLength(120);
        builder.Property(e => e.IpDeOrigem).HasMaxLength(45).IsUnicode(false);
        builder.Property(e => e.Motivo).HasMaxLength(400);
        builder.Property(e => e.OcorridoEm).HasColumnType("datetime2(3)");
        builder.Property(e => e.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(e => e.AtualizadoEm).HasColumnType("datetime2(3)");

        // Sem navegacao de proposito, mesmo padrao de AtendenteConfiguration: o EF
        // so precisa da FK para ordenar INSERTs, nao para acoplar os modulos.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Atendente>().WithMany().HasForeignKey(e => e.AtendenteId).OnDelete(DeleteBehavior.Restrict);

        // AtorAtendenteId NAO tem FK: e so um registro para leitura em tela/relatorio
        // (append-only, nunca navegado por join critico), e uma segunda FK para a
        // mesma tabela Atendentes exigiria um segundo caminho de cascata para o EF
        // resolver sem necessidade real.
        builder.HasIndex(e => new { e.TenantId, e.OcorridoEm }).HasDatabaseName("IX_EventosDeSeguranca_Tenant_Ocorrido");
        builder.HasIndex(e => e.AtendenteId).HasDatabaseName("IX_EventosDeSeguranca_Atendente");
    }
}
