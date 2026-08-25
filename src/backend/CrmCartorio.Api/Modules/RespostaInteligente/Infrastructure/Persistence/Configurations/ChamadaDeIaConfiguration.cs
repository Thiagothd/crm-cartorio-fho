using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Infrastructure.Persistence.Configurations;

public class ChamadaDeIaConfiguration : IEntityTypeConfiguration<ChamadaDeIa>
{
    public void Configure(EntityTypeBuilder<ChamadaDeIa> builder)
    {
        builder.ToTable("ChamadasDeIa", t =>
        {
            // Só existe leitura de PodeResponder quando a tentativa teve sucesso —
            // caso contrário o campo não tem de onde vir.
            t.HasCheckConstraint(
                "CK_ChamadasDeIa_PodeResponder",
                "([Sucesso] = 1) OR ([Sucesso] = 0 AND [PodeResponder] IS NULL)");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ChaveDeProjeto).HasMaxLength(16).IsUnicode(false);
        builder.Property(c => c.Provedor).HasConversion<byte>();
        builder.Property(c => c.Modelo).HasMaxLength(60).IsUnicode(false).IsRequired();
        builder.Property(c => c.TipoDeFalha).HasConversion<byte>();
        builder.Property(c => c.MotivoDoFallback).HasMaxLength(200);
        builder.Property(c => c.NivelDeRaciocinio).HasMaxLength(10).IsUnicode(false);
        builder.Property(c => c.CustoEstimadoUsd).HasColumnType("decimal(10,6)");
        builder.Property(c => c.IniciadaEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Sem FK para Conversa: uma sonda do disjuntor pode não ter conversa
        // nenhuma, e a linha de log não pode impedir que uma conversa antiga
        // circule livremente por rotinas de manutenção.
        builder.HasIndex(c => new { c.TenantId, c.IniciadaEm })
            .HasDatabaseName("IX_ChamadasDeIa_Tenant_Iniciada");

        // Hidratação global de RPM/TPM/RPD do Gemini após reciclagem do App Pool —
        // varre por projeto, não por tenant.
        builder.HasIndex(c => new { c.ChaveDeProjeto, c.IniciadaEm })
            .HasFilter("[ChaveDeProjeto] IS NOT NULL")
            .HasDatabaseName("IX_ChamadasDeIa_Projeto_Iniciada");

        // Contagem dos dois tetos diários (LimiteDiario, LimiteDiarioHaiku): só
        // tentativas bem-sucedidas contam para qualquer teto.
        builder.HasIndex(c => new { c.TenantId, c.Provedor, c.Sucesso, c.IniciadaEm })
            .HasDatabaseName("IX_ChamadasDeIa_Tenant_Provedor_Sucesso");
    }
}
