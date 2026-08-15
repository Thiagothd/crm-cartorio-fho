using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Usuarios.Infrastructure.Persistence.Configurations;

public class AtendenteConfiguration : IEntityTypeConfiguration<Atendente>
{
    public void Configure(EntityTypeBuilder<Atendente> builder)
    {
        builder.ToTable("Atendentes");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Nome).HasMaxLength(150).IsRequired();
        builder.Property(a => a.Email).HasMaxLength(160).IsRequired();
        builder.Property(a => a.SenhaHash).HasMaxLength(500).IsRequired().IsUnicode(false);
        builder.Property(a => a.Perfil).HasConversion<byte>();
        builder.Property(a => a.Cargo).HasConversion<byte>();
        builder.Property(a => a.UltimoAcessoEm).HasColumnType("datetime2(3)");
        builder.Property(a => a.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(a => a.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.Ignore(a => a.PodeGerirCadastros);
        builder.Ignore(a => a.PodeGerirAtendentes);
        builder.Ignore(a => a.PodeOperarConversaDeTerceiro);

        // Relacionamentos declarados SEM propriedade de navegacao: o EF precisa
        // conhecer a dependencia para ordenar os INSERTs e nao violar as FKs do
        // schema, mas navegar de Atendente para Setor acoplaria os dois modulos.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Setor>()
            .WithMany()
            .HasForeignKey(a => a.SetorPadraoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.TenantId, a.Email })
            .IsUnique()
            .HasDatabaseName("UQ_Atendentes_Tenant_Email");
    }
}
