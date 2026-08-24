using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Clientes.Infrastructure.Persistence.Configurations;

public class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.ToTable("Clientes");

        builder.HasKey(c => c.Id);

        // --- identificacao ---
        builder.Property(c => c.Nome).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Tipo).HasConversion<byte>();
        builder.Property(c => c.TelefoneWhatsapp).HasMaxLength(20).IsRequired().IsUnicode(false);
        builder.Property(c => c.TelefoneAlternativo).HasMaxLength(20).IsUnicode(false);
        builder.Property(c => c.Email).HasMaxLength(160);
        builder.Property(c => c.Documento).HasMaxLength(14).IsUnicode(false);

        // --- pessoa fisica ---
        builder.Property(c => c.Rg).HasMaxLength(20).IsUnicode(false);
        builder.Property(c => c.OrgaoEmissor).HasMaxLength(20);
        builder.Property(c => c.UfOrgaoEmissor).HasMaxLength(2).IsUnicode(false);
        builder.Property(c => c.DataNascimento).HasColumnType("date");
        builder.Property(c => c.EstadoCivil).HasConversion<byte>();
        builder.Property(c => c.RegimeDeBens).HasConversion<byte>();
        builder.Property(c => c.Profissao).HasMaxLength(100);
        builder.Property(c => c.Nacionalidade).HasMaxLength(60);
        builder.Property(c => c.Naturalidade).HasMaxLength(120);
        builder.Property(c => c.NomeDoPai).HasMaxLength(200);
        builder.Property(c => c.NomeDaMae).HasMaxLength(200);

        // --- pessoa juridica ---
        builder.Property(c => c.RazaoSocial).HasMaxLength(200);
        builder.Property(c => c.RepresentanteLegal).HasMaxLength(200);

        // --- endereco ---
        builder.Property(c => c.Cep).HasMaxLength(8).IsUnicode(false);
        builder.Property(c => c.Logradouro).HasMaxLength(200);
        builder.Property(c => c.Numero).HasMaxLength(20);
        builder.Property(c => c.Complemento).HasMaxLength(100);
        builder.Property(c => c.Bairro).HasMaxLength(100);
        builder.Property(c => c.Cidade).HasMaxLength(120);
        builder.Property(c => c.Uf).HasMaxLength(2).IsUnicode(false);

        // --- controle ---
        builder.Property(c => c.Observacoes).HasMaxLength(1000);
        builder.Property(c => c.FotoPerfilUrl).HasMaxLength(500);
        builder.Property(c => c.FotoPerfilAtualizadaEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.NomePerguntadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.PrimeiroContatoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.AtualizadoEm).HasColumnType("datetime2(3)");

        // PercentualDeQualificacao() e metodo, nao propriedade. O EF ignora
        // metodos naturalmente; chamar Ignore aqui e invalido porque a API
        // aceita apenas acesso simples a propriedade ou campo.

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // O telefone e a identidade real do contato: e por ele que a mensagem chega.
        builder.HasIndex(c => new { c.TenantId, c.TelefoneWhatsapp })
            .IsUnique()
            .HasDatabaseName("UQ_Clientes_Tenant_Telefone");

        builder.HasIndex(c => new { c.TenantId, c.Nome })
            .HasDatabaseName("IX_Clientes_Tenant_Nome");

        // Sustenta a busca por documento, que o balcao usa para achar ficha.
        builder.HasIndex(c => new { c.TenantId, c.Documento })
            .HasDatabaseName("IX_Clientes_Tenant_Documento");

        // Sustenta o filtro por cidade na listagem.
        builder.HasIndex(c => new { c.TenantId, c.Cidade })
            .HasDatabaseName("IX_Clientes_Tenant_Cidade");
    }
}
