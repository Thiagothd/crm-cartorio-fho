using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Servicos.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmCartorio.Api.Modules.Atendimento.Infrastructure.Persistence.Configurations;

public class ConversaConfiguration : IEntityTypeConfiguration<Conversa>
{
    public void Configure(EntityTypeBuilder<Conversa> builder)
    {
        builder.ToTable("Conversas");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Protocolo).HasMaxLength(20).IsRequired().IsUnicode(false);
        builder.Property(c => c.Status).HasConversion<byte>();
        builder.Property(c => c.Canal).HasConversion<byte>();
        builder.Property(c => c.MotivoEncerramento).HasMaxLength(500);
        builder.Property(c => c.IniciadaEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.UltimaMensagemEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.PrazoIniciadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.PrazoLimite).HasColumnType("datetime2(3)");
        builder.Property(c => c.PrazoAlertaEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.AvisoDeAlertaEnviadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.AvisoDeVencimentoEnviadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.PerguntaDeEncerramentoEnviadaEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.EncerradaEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.ReabertaEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.AtualizadoEm).HasColumnType("datetime2(3)");

        // Token de concorrencia otimista. Cobre toda escrita sobre a conversa —
        // etapa, setor, encerramento, transferencia — nao so a atribuicao.
        builder.Property(c => c.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.Ignore(c => c.EstaAtiva);
        builder.Ignore(c => c.EstaNaFila);

        // Relacionamentos com os demais modulos, declarados SEM navegacao: o EF
        // precisa da dependencia para ordenar INSERT e respeitar as FKs do schema,
        // mas navegar de Conversa para Cliente/Setor/Etapa acoplaria os dominios.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(c => c.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(c => c.AtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(c => c.EncerradaPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(c => c.ReabertaPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Setor>()
            .WithMany()
            .HasForeignKey(c => c.SetorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EtapaFunil>()
            .WithMany()
            .HasForeignKey(c => c.EtapaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TipoServico>()
            .WithMany()
            .HasForeignKey(c => c.TipoServicoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Mensagens)
            .WithOne(m => m.Conversa!)
            .HasForeignKey(m => m.ConversaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Historico)
            .WithOne(h => h.Conversa!)
            .HasForeignKey(h => h.ConversaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.TenantId, c.Protocolo })
            .IsUnique()
            .HasDatabaseName("UQ_Conversas_Tenant_Protocolo");

        // Consulta padrao da inbox: ativas do tenant, mais recentes primeiro.
        builder.HasIndex(c => new { c.TenantId, c.Status, c.UltimaMensagemEm })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_Conversas_Inbox");

        builder.HasIndex(c => new { c.TenantId, c.AtendenteId, c.Status })
            .HasDatabaseName("IX_Conversas_Atendente");

        builder.HasIndex(c => new { c.TenantId, c.EtapaId, c.Status })
            .HasDatabaseName("IX_Conversas_Etapa");

        builder.HasIndex(c => new { c.TenantId, c.SetorId, c.Status })
            .HasDatabaseName("IX_Conversas_Setor");

        builder.HasIndex(c => new { c.TenantId, c.ClienteId })
            .HasDatabaseName("IX_Conversas_Cliente");

        // Sustenta o filtro e os contadores de situacao de prazo do painel.
        builder.HasIndex(c => new { c.TenantId, c.Status, c.PrazoLimite })
            .HasDatabaseName("IX_Conversas_Prazo");

        // Varredura de prazo: so interessa conversa ativa que tem prazo e ainda nao
        // recebeu os dois avisos. O indice e filtrado porque, em regime, quase toda
        // conversa antiga ja avisou — indexar essas seria carregar peso morto.
        builder.HasIndex(c => new { c.TenantId, c.PrazoAlertaEm })
            .HasFilter("[AvisoDeAlertaEnviadoEm] IS NULL AND [PrazoLimite] IS NOT NULL")
            .HasDatabaseName("IX_Conversas_AvisoAlertaPendente");

        builder.HasIndex(c => new { c.TenantId, c.PrazoLimite })
            .HasFilter("[AvisoDeVencimentoEnviadoEm] IS NULL AND [PrazoLimite] IS NOT NULL")
            .HasDatabaseName("IX_Conversas_AvisoVencimentoPendente");

        // Segunda fase da varredura de inatividade: so interessa conversa que ja
        // recebeu a pergunta de encerramento e ainda nao respondeu. Filtrado porque
        // em regime esse conjunto e um punhado de linhas, contra uma tabela que
        // cresce para sempre.
        //
        // A PRIMEIRA fase nao ganha indice de proposito: ela e servida por
        // IX_Conversas_Inbox (TenantId, Status, UltimaMensagemEm), e um indice
        // filtrado por "PerguntaDeEncerramentoEnviadaEm IS NULL" cobriria quase toda
        // conversa ativa — seria a inbox indexada duas vezes.
        builder.HasIndex(c => new { c.TenantId, c.PerguntaDeEncerramentoEnviadaEm })
            .HasFilter("[PerguntaDeEncerramentoEnviadaEm] IS NOT NULL")
            .HasDatabaseName("IX_Conversas_EncerramentoPendente");
    }
}
