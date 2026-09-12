using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Seguranca.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Seguranca.Infrastructure.Persistence.Configurations;

public class RegistroDeAcessoConfiguration : IEntityTypeConfiguration<RegistroDeAcesso>
{
    public void Configure(EntityTypeBuilder<RegistroDeAcesso> builder)
    {
        builder.ToTable("RegistrosDeAcesso");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Tipo).HasConversion<byte>();
        builder.Property(r => r.AtorExterno).HasMaxLength(120);
        builder.Property(r => r.IpDeOrigem).HasMaxLength(45).IsUnicode(false);
        builder.Property(r => r.OcorridoEm).HasColumnType("datetime2(3)");
        builder.Property(r => r.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(r => r.AtualizadoEm).HasColumnType("datetime2(3)");

        // Sem navegacao de proposito, mesmo padrao de EventoDeSegurancaConfiguration:
        // o EF so precisa da FK para ordenar INSERTs, nao para acoplar modulos.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Atendente>().WithMany().HasForeignKey(r => r.AtendenteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Conversa>().WithMany().HasForeignKey(r => r.ConversaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Cliente>().WithMany().HasForeignKey(r => r.ClienteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Mensagem>().WithMany().HasForeignKey(r => r.MensagemId).OnDelete(DeleteBehavior.Restrict);

        // AutorizacaoDeAcessoTecnicoId nao tem FK ainda: a tabela que ela aponta
        // (AutorizacoesDeAcessoTecnico) so chega no PR de acesso tecnico
        // autorizado (sql/047) — a coluna ja nasce aqui para nao exigir outra
        // migration so para acrescenta-la.

        builder.HasIndex(r => new { r.TenantId, r.OcorridoEm })
            .HasDatabaseName("IX_RegistrosDeAcesso_Tenant_Ocorrido");

        builder.HasIndex(r => new { r.TenantId, r.ConversaId })
            .HasDatabaseName("IX_RegistrosDeAcesso_Tenant_Conversa");

        builder.HasIndex(r => new { r.TenantId, r.AtendenteId, r.OcorridoEm })
            .HasDatabaseName("IX_RegistrosDeAcesso_Tenant_Atendente_Ocorrido");
    }
}
