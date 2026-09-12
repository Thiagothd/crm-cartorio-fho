using CrmCartorio.Api.Infrastructure.Persistence.Cifra;
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
        // Nome, TelefoneWhatsapp e RazaoSocial ficam em claro de proposito
        // (PR3, blindagem LGPD): sao a identidade real do contato pelo canal
        // e o campo de busca "contains" do balcao — cifrar exigiria abrir mao
        // da busca parcial nesses tres, o que travaria a rotina de balcao sem
        // ganho real (nao sao, sozinhos, o dado sensivel que o plano protege).
        // Ver docs/PROTECAO-DE-DADOS.md.
        builder.Property(c => c.Nome).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Tipo).HasConversion<byte>();
        builder.Property(c => c.TelefoneWhatsapp).HasMaxLength(20).IsRequired().IsUnicode(false);
        builder.Property(c => c.TelefoneAlternativo).Cifrada(20);
        builder.Property(c => c.Email).Cifrada(160);
        // Cifrado; busca exata por igualdade passa por DocumentoHash (indice
        // cego), nao mais por esta coluna — ver ClienteService.
        builder.Property(c => c.Documento).Cifrada(14);
        builder.Property(c => c.DocumentoHash).HasMaxLength(64).IsUnicode(false);

        // --- pessoa fisica ---
        builder.Property(c => c.Rg).Cifrada(20);
        builder.Property(c => c.OrgaoEmissor).Cifrada(20);
        builder.Property(c => c.UfOrgaoEmissor).Cifrada(2);
        builder.Property(c => c.DataNascimento).CifradaComoData("DataNascimentoCifrada");
        builder.Property(c => c.EstadoCivil).HasConversion<byte>();
        builder.Property(c => c.RegimeDeBens).HasConversion<byte>();
        builder.Property(c => c.Profissao).Cifrada(100);
        builder.Property(c => c.Nacionalidade).Cifrada(60);
        builder.Property(c => c.Naturalidade).Cifrada(120);
        builder.Property(c => c.NomeDoPai).Cifrada(200);
        builder.Property(c => c.NomeDaMae).Cifrada(200);

        // --- pessoa juridica ---
        builder.Property(c => c.RazaoSocial).HasMaxLength(200);
        builder.Property(c => c.RepresentanteLegal).Cifrada(200);

        // --- endereco ---
        // Cidade e Uf ficam em claro: sustentam o filtro de listagem
        // (geografia grosseira, nao o dado sensivel do endereco completo).
        builder.Property(c => c.Cep).Cifrada(8);
        builder.Property(c => c.Logradouro).Cifrada(200);
        builder.Property(c => c.Numero).Cifrada(20);
        builder.Property(c => c.Complemento).Cifrada(100);
        builder.Property(c => c.Bairro).Cifrada(100);
        builder.Property(c => c.Cidade).HasMaxLength(120);
        builder.Property(c => c.Uf).HasMaxLength(2).IsUnicode(false);

        // --- controle ---
        builder.Property(c => c.Observacoes).Cifrada(1000);
        builder.Property(c => c.FotoPerfilUrl).HasMaxLength(500);
        builder.Property(c => c.FotoPerfilAtualizadaEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.NomePerguntadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.OrigemDoCadastro).HasConversion<byte?>();
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

        // IX_Clientes_Tenant_Documento (indice antigo sobre Documento em claro)
        // foi removido em sql/047-cifra-em-repouso.sql: SQL Server nao aceita
        // NVARCHAR(MAX) como coluna-chave de indice, e Documento precisou
        // virar MAX para caber o ciphertext. Busca por documento em claro
        // (cifra dormente) continua funcionando via table scan — volume de
        // clientes de um cartorio nao justifica manter o indice as custas de
        // travar a largura da coluna.

        // Sustenta a busca por documento EXATO quando a cifra esta ativa
        // (indice cego — ver ClienteService e Infrastructure/Security/IndiceCego.cs).
        builder.HasIndex(c => new { c.TenantId, c.DocumentoHash })
            .HasDatabaseName("IX_Clientes_Tenant_DocumentoHash");

        // Sustenta o filtro por cidade na listagem.
        builder.HasIndex(c => new { c.TenantId, c.Cidade })
            .HasDatabaseName("IX_Clientes_Tenant_Cidade");
    }
}
