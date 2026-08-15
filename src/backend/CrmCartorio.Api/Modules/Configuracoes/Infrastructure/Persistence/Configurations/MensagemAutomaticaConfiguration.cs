using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Configuracoes.Infrastructure.Persistence.Configurations;

public class MensagemAutomaticaConfiguration : IEntityTypeConfiguration<MensagemAutomatica>
{
    public void Configure(EntityTypeBuilder<MensagemAutomatica> builder)
    {
        builder.ToTable("MensagensAutomaticas", t =>
        {
            // Gatilho de etapa SEM etapa alvo dispararia em toda movimentacao do
            // funil; etapa alvo em gatilho que nao e de etapa e dado morto que
            // engana quem le a tabela. As duas metades sao validadas no servico —
            // aqui e a rede de seguranca contra qualquer caminho de escrita futuro.
            t.HasCheckConstraint(
                "CK_MensagensAutomaticas_EtapaAlvo",
                "([Gatilho] = 2 AND [EtapaAlvoId] IS NOT NULL) OR ([Gatilho] <> 2 AND [EtapaAlvoId] IS NULL)");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Gatilho).HasConversion<byte>();

        // 1000, e nao os 4000 de Mensagem: o texto GRAVADO aqui e o modelo, e os
        // marcadores crescem na substituicao. Sobra folga para {MenuSetores} sem
        // risco de a mensagem renderizada estourar a coluna de destino.
        builder.Property(m => m.Conteudo).HasMaxLength(1000).IsRequired();

        builder.Property(m => m.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EtapaFunil>()
            .WithMany()
            .HasForeignKey(m => m.EtapaAlvoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Consulta do disparador: as regras ativas de um gatilho, num tenant.
        builder.HasIndex(m => new { m.TenantId, m.Gatilho, m.Ativo })
            .HasDatabaseName("IX_MensagensAutomaticas_Tenant_Gatilho");
    }
}
