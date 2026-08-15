using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Atendimento.Infrastructure.Persistence.Configurations;

public class MensagemConfiguration : IEntityTypeConfiguration<Mensagem>
{
    public void Configure(EntityTypeBuilder<Mensagem> builder)
    {
        builder.ToTable("Mensagens", t =>
        {
            // RF-007 reforcado no banco: mensagem de atendente sem AtendenteId nao entra.
            // A validacao tambem existe no servico; aqui e a rede de seguranca contra
            // qualquer caminho de escrita futuro (importacao, webhook da Fase 2).
            t.HasCheckConstraint(
                "CK_Mensagens_AtendenteObrigatorio",
                "([AutorTipo] <> 2 AND [AtendenteId] IS NULL) OR ([AutorTipo] = 2 AND [AtendenteId] IS NOT NULL)");

            // Mensagem automatica e sempre do sistema. Marcar como automatica uma
            // mensagem de gente inverteria a autoria na tela: o atendente veria o
            // rotulo "Automatica" em algo que ele mesmo escreveu, ou o contrario.
            t.HasCheckConstraint(
                "CK_Mensagens_AutomaticaEhDoSistema",
                "([EhAutomatica] = 0) OR ([EhAutomatica] = 1 AND [AutorTipo] = 3)");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Conteudo).HasMaxLength(4000).IsRequired();
        builder.Property(m => m.AutorTipo).HasConversion<byte>();
        builder.Property(m => m.Tipo).HasConversion<byte>();
        builder.Property(m => m.StatusEntrega).HasConversion<byte>();
        builder.Property(m => m.IdExternoWhatsapp).HasMaxLength(80).IsUnicode(false);
        builder.Property(m => m.EnviadaEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.LidaEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(m => m.AtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        // Auto-relacionamento da citacao. Restrict porque mensagem nunca e apagada;
        // se algum dia fosse, apagar a citada nao pode levar a resposta com ela.
        builder.HasOne(m => m.MensagemRespondida)
            .WithMany()
            .HasForeignKey(m => m.MensagemRespondidaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.ConversaId, m.EnviadaEm })
            .HasDatabaseName("IX_Mensagens_Conversa_Enviada");

        builder.HasIndex(m => new { m.TenantId, m.AtendenteId })
            .HasDatabaseName("IX_Mensagens_Tenant_Atendente");
    }
}
