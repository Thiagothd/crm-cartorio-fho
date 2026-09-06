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

            // Só a faixa — sem correlacionar com FoiFallback. Um binário anterior
            // a esta migração continua gravando ChamadasDeIa durante a janela do
            // deploy (schema muda antes do binário) e nunca preenche esta coluna
            // fora do default 0; uma constraint cruzada rejeitaria toda gravação
            // de fallback feita por ele. A coerência (FoiFallback=1 <=>
            // TipoDeFallback entre 1 e 5) é responsabilidade do orquestrador, não
            // do banco — ver deploy/migration-compatibility.json.
            t.HasCheckConstraint(
                "CK_ChamadasDeIa_TipoDeFallback",
                "[TipoDeFallback] BETWEEN 0 AND 5");

            // Mesmo raciocínio: sem correlacionar com Sucesso/PodeResponder. O
            // binário anterior nunca preenche esta coluna (fica NULL) independente
            // do valor de PodeResponder.
            t.HasCheckConstraint(
                "CK_ChamadasDeIa_MotivoDaNaoResposta",
                "[MotivoDaNaoResposta] IS NULL OR [MotivoDaNaoResposta] BETWEEN 0 AND 8");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ChaveDeProjeto).HasMaxLength(16).IsUnicode(false);
        builder.Property(c => c.Provedor).HasConversion<byte>();
        builder.Property(c => c.Modelo).HasMaxLength(60).IsUnicode(false).IsRequired();
        builder.Property(c => c.TipoDeFalha).HasConversion<byte>();
        builder.Property(c => c.MotivoDoFallback).HasMaxLength(200);
        builder.Property(c => c.TipoDeFallback).HasConversion<byte>();
        builder.Property(c => c.MotivoDaNaoResposta).HasConversion<byte?>();
        builder.Property(c => c.NivelDeRaciocinio).HasMaxLength(10).IsUnicode(false);
        builder.Property(c => c.CustoEstimadoUsd).HasColumnType("decimal(10,6)");
        builder.Property(c => c.IniciadaEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.CriadoEm).HasColumnType("datetime2(3)");
        builder.Property(c => c.AtualizadoEm).HasColumnType("datetime2(3)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Sem FK para Conversa, TipoServico, Setor ou Mensagem: uma sonda do
        // disjuntor pode não ter conversa nenhuma, e a linha de log não pode
        // impedir que um registro antigo circule livremente por rotinas de
        // manutenção nem falhar em gravar por causa de uma FK.
        //
        // INCLUDE torna esta consulta — a mais usada da tela do cartório —
        // covering: nenhum key lookup para pegar provedor, sucesso, tokens ou custo.
        builder.HasIndex(c => new { c.TenantId, c.IniciadaEm })
            .IncludeProperties(c => new
            {
                c.Provedor, c.Sucesso, c.PodeResponder, c.TipoDeFalha, c.TipoDeFallback,
                c.RodadaId, c.TipoServicoId, c.SetorId, c.MotivoDaNaoResposta,
                c.LatenciaMs, c.CustoEstimadoUsd, c.TokensEntrada, c.TokensSaida, c.TokensPensamento,
            })
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

        // A Central de IA filtra só por período, cross-tenant — nenhum dos
        // índices acima tem IniciadaEm à esquerda sem TenantId/ChaveDeProjeto
        // na frente, então aquela consulta só resolvia por scan.
        builder.HasIndex(c => c.IniciadaEm)
            .IncludeProperties(c => new
            {
                c.TenantId, c.Provedor, c.Sucesso, c.PodeResponder, c.TipoDeFalha,
                c.TipoDeFallback, c.RodadaId, c.LatenciaMs, c.CustoEstimadoUsd,
            })
            .HasDatabaseName("IX_ChamadasDeIa_Iniciada");

        // Drill-down "todas as chamadas de IA desta conversa". Filtrado porque a
        // sonda do disjuntor grava ConversaId nulo.
        builder.HasIndex(c => c.ConversaId)
            .IncludeProperties(c => new { c.IniciadaEm, c.Sucesso, c.PodeResponder, c.TipoDeFallback })
            .HasFilter("[ConversaId] IS NOT NULL")
            .HasDatabaseName("IX_ChamadasDeIa_Conversa");
    }
}
