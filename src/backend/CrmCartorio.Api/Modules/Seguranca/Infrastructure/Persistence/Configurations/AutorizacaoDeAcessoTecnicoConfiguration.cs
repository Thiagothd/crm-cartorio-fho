using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Seguranca.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Seguranca.Infrastructure.Persistence.Configurations;

public class AutorizacaoDeAcessoTecnicoConfiguration : IEntityTypeConfiguration<AutorizacaoDeAcessoTecnico>
{
    public void Configure(EntityTypeBuilder<AutorizacaoDeAcessoTecnico> builder)
    {
        builder.ToTable("AutorizacoesDeAcessoTecnico");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Motivo).HasMaxLength(400);
        builder.Property(a => a.Escopo).HasConversion<byte>();
        builder.Property(a => a.IpDeSolicitacao).HasMaxLength(45).IsUnicode(false);
        builder.Property(a => a.SolicitadaEm).HasColumnType("datetime2(3)");
        builder.Property(a => a.AprovadaEm).HasColumnType("datetime2(3)");
        builder.Property(a => a.ValidaAte).HasColumnType("datetime2(3)");
        builder.Property(a => a.NegadaEm).HasColumnType("datetime2(3)");
        builder.Property(a => a.RevogadaEm).HasColumnType("datetime2(3)");
        builder.Property(a => a.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(a => a.AtualizadoEm).HasColumnType("datetime2(3)");

        // Sem navegacao de proposito, mesmo padrao das demais configurations
        // deste modulo: a FK e so para ordenar INSERTs, nao para acoplar.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Atendente>().WithMany().HasForeignKey(a => a.ContaTecnicaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Conversa>().WithMany().HasForeignKey(a => a.ConversaId).OnDelete(DeleteBehavior.Restrict);

        // AprovadaPorAtendenteId/NegadaPorAtendenteId/RevogadaPorAtendenteId
        // NAO tem FK: sao tres caminhos para a MESMA tabela Atendentes, e o EF
        // exigiria um DeleteBehavior por caminho so para navegacao que este
        // modulo nunca usa (mesma decisao de EventoDeSeguranca.AtorAtendenteId).

        builder.HasIndex(a => new { a.TenantId, a.ContaTecnicaId, a.ValidaAte })
            .HasDatabaseName("IX_AutorizacoesDeAcessoTecnico_Tenant_Conta_ValidaAte");

        builder.HasIndex(a => new { a.TenantId, a.SolicitadaEm })
            .HasDatabaseName("IX_AutorizacoesDeAcessoTecnico_Tenant_Solicitada");
    }
}
