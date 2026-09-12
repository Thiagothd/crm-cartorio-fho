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
        builder.Property(a => a.TokenVersion).HasDefaultValue(1);

        // SEC-02 — defaults no BANCO, nao so no objeto: uma linha inserida por
        // script SQL (seed, correcao manual) precisa nascer destravada do mesmo
        // jeito que uma criada pelo AtendenteService.
        builder.Property(a => a.FalhasDeLoginConsecutivas).HasDefaultValue(0);
        builder.Property(a => a.NivelDeBloqueio).HasDefaultValue((byte)0);
        builder.Property(a => a.BloqueadoAte).HasColumnType("datetime2(3)");

        // SEC-03 — mesma disciplina de defaults no banco.
        builder.Property(a => a.MfaAtivo).HasDefaultValue(false);
        builder.Property(a => a.MfaSegredoCifrado).HasMaxLength(256);
        builder.Property(a => a.MfaSegredoPendenteCifrado).HasMaxLength(256);
        builder.Property(a => a.MfaPendenteExpiraEm).HasColumnType("datetime2(3)");
        builder.Property(a => a.MfaConfirmadoEm).HasColumnType("datetime2(3)");
        builder.Property(a => a.MfaFalhasConsecutivas).HasDefaultValue(0);
        builder.Property(a => a.MfaNivelDeBloqueio).HasDefaultValue((byte)0);
        builder.Property(a => a.MfaBloqueadoAte).HasColumnType("datetime2(3)");
        builder.Property(a => a.RematriculaObrigatoriaAposEmergencia).HasDefaultValue(false);

        // Permissoes granulares — NULL e o valor de "segue a Classe da
        // conta" (ver o XML doc da entidade). Sem HasDefaultValue: o default
        // de uma coluna NULLABLE sem DEFAULT explicito ja e NULL, e e assim
        // que uma linha inserida por um binario anterior a esta coluna
        // continua se comportando identico a hoje.
        builder.Property(a => a.Permissoes).HasMaxLength(4000);
        builder.Property(a => a.ModeloDePermissao).HasConversion<byte?>();

        // Blindagem LGPD — mesma disciplina de defaults no banco.
        builder.Property(a => a.EhContaTecnica).HasDefaultValue(false);

        builder.Property(a => a.UltimoAcessoEm).HasColumnType("datetime2(3)");
        builder.Property(a => a.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(a => a.AtualizadoEm).HasColumnType("datetime2(3)");

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
