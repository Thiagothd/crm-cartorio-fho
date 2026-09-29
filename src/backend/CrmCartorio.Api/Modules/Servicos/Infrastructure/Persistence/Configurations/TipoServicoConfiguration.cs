using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Servicos.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Servicos.Infrastructure.Persistence.Configurations;

public class TipoServicoConfiguration : IEntityTypeConfiguration<TipoServico>
{
    public void Configure(EntityTypeBuilder<TipoServico> builder)
    {
        builder.ToTable("TiposServico");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Nome).HasMaxLength(160).IsRequired();
        builder.Property(t => t.Codigo).HasMaxLength(30).IsUnicode(false);
        builder.Property(t => t.Descricao).HasMaxLength(500);
        builder.Property(t => t.DocumentosNecessarios).HasMaxLength(800);
        // Sem teto na coluna (NVARCHAR(MAX)): o texto pode vir em várias mensagens,
        // separadas por uma linha "---", cada uma até 4000. O teto do cadastro
        // (12000) e o de cada parte moram em PartesDeMensagem e são validados no
        // serviço — ver sql/053.
        builder.Property(t => t.MensagemAoEscolher);
        builder.Property(t => t.MensagemAposDados);
        builder.Property(t => t.MensagemAposComprovante);

        builder.HasOne<TipoServico>()
            .WithMany()
            .HasForeignKey(t => t.ServicoPaiId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(t => t.Categoria).HasConversion<byte>();
        builder.Property(t => t.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(t => t.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(t => t.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nula de proposito para servico aguardando confirmacao de atribuicao — ver
        // TipoServico.SetorId. Restrict: apagar um Setor com servico vinculado e erro
        // de cadastro, nao operacao silenciosa.
        builder.HasOne<Setor>()
            .WithMany()
            .HasForeignKey(t => t.SetorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Prazo)
            .WithOne(p => p.TipoServico!)
            .HasForeignKey<PrazoServico>(p => p.TipoServicoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.TenantId, t.Nome })
            .IsUnique()
            .HasDatabaseName("UQ_TiposServico_Tenant_Nome");

        builder.HasIndex(t => new { t.TenantId, t.Categoria })
            .HasDatabaseName("IX_TiposServico_Tenant_Categoria");

        // Suporta o menu de 2o nivel: "servicos ativos deste setor".
        builder.HasIndex(t => new { t.TenantId, t.SetorId, t.Ativo })
            .HasDatabaseName("IX_TiposServico_Tenant_Setor_Ativo");
    }
}
