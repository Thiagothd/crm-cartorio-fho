using CrmCartorio.Api.Infrastructure.Persistence.Cifra;
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

            // Mensagem gerada por IA e sempre automatica. GeradaPorIa=1 com
            // EhAutomatica=0 inverteria a autoria na tela do mesmo jeito que o
            // constraint acima evita.
            t.HasCheckConstraint(
                "CK_Mensagens_IaEhAutomatica",
                "([GeradaPorIa] = 0) OR ([GeradaPorIa] = 1 AND [EhAutomatica] = 1)");

            // Estado de processamento existe apenas para entrada real de cliente.
            // Mensagem manual/simulada e toda saida permanecem com estado nulo.
            t.HasCheckConstraint(
                "CK_Mensagens_ProcessamentoEntrada",
                "[EstadoProcessamentoEntrada] IS NULL OR " +
                "([AutorTipo] = 1 AND [IdExternoWhatsapp] IS NOT NULL AND [EstadoProcessamentoEntrada] IN (1, 2, 3, 4))");

            // Correcao e apagamento sao acoes de ATENDENTE sobre o que o proprio
            // painel mandou (AutorTipo = 2). O cliente nao tem como pedir conserto
            // por aqui, e mensagem automatica ou da IA se conserta no cadastro que
            // a gerou — nao no fio, uma por uma.
            //
            // As tres colunas da correcao andam juntas: texto original sem autor,
            // ou autor sem instante, seria um registro que nao responde "quem
            // mudou o que, e quando" — exatamente a pergunta que ele existe para
            // responder.
            t.HasCheckConstraint(
                "CK_Mensagens_Correcao",
                """
                ([CorrigidaEm] IS NULL AND [CorrigidaPorAtendenteId] IS NULL AND [ConteudoOriginal] IS NULL)
                OR
                ([AutorTipo] = 2 AND [CorrigidaEm] IS NOT NULL AND [CorrigidaPorAtendenteId] IS NOT NULL AND [ConteudoOriginal] IS NOT NULL)
                """);

            t.HasCheckConstraint(
                "CK_Mensagens_ApagadaParaTodos",
                """
                ([ApagadaParaTodosEm] IS NULL AND [ApagadaParaTodosPorAtendenteId] IS NULL)
                OR
                ([AutorTipo] = 2 AND [ApagadaParaTodosEm] IS NOT NULL AND [ApagadaParaTodosPorAtendenteId] IS NOT NULL)
                """);

            // Ou o anexo esta inteiro, ou nao ha anexo. Meia linha gravada —
            // caminho sem tipo, tipo sem caminho — daria um fio que promete
            // arquivo e um download que devolve 404.
            t.HasCheckConstraint(
                "CK_Mensagens_Arquivo",
                """
                ([ArquivoCaminho] IS NULL AND [ArquivoNome] IS NULL AND [ArquivoTipoMime] IS NULL AND [ArquivoTamanhoBytes] IS NULL)
                OR
                ([ArquivoCaminho] IS NOT NULL AND [ArquivoNome] IS NOT NULL AND [ArquivoTipoMime] IS NOT NULL AND [ArquivoTamanhoBytes] IS NOT NULL)
                """);
        });

        builder.HasKey(m => m.Id);

        // Cifrada em repouso (PR3, blindagem LGPD): conteudo da conversa e o
        // dado mais sensivel do sistema. .Cifrada() troca a coluna para
        // nvarchar(max) — o tamanho maximo do TEXTO EM CLARO continua
        // validado no converter (ver ExtensoesDeCifra), so que na aplicacao,
        // nao mais no banco (nvarchar(max) nao tem limite fixo pra
        // ciphertext+overhead cifrado).
        builder.Property(m => m.Conteudo).IsRequired().Cifrada(4000);

        // Mesma cifra do Conteudo, pelo mesmo motivo: e o MESMO dado — o texto que
        // o cliente recebeu antes da correcao. Guardar o original em claro ao lado
        // de um conteudo cifrado entregaria de graca o que a cifra protege.
        builder.Property(m => m.ConteudoOriginal).Cifrada(4000);
        builder.Property(m => m.AutorTipo).HasConversion<byte>();
        builder.Property(m => m.Tipo).HasConversion<byte>();
        builder.Property(m => m.StatusEntrega).HasConversion<byte>();
        builder.Property(m => m.EstadoProcessamentoEntrada).HasConversion<byte?>();
        builder.Property(m => m.IdExternoWhatsapp).HasMaxLength(80).IsUnicode(false);
        builder.Property(m => m.ErroProcessamentoEntrada).HasMaxLength(1000);

        builder.Property(m => m.ArquivoNome).Cifrada(260);
        builder.Property(m => m.ArquivoTipoMime).HasMaxLength(150).IsUnicode(false);
        builder.Property(m => m.ArquivoCaminho).HasMaxLength(400);

        // Propriedade so de leitura sobre ArquivoCaminho — nao vira coluna.
        builder.Ignore(m => m.TemArquivo);
        builder.Property(m => m.EnviadaEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.EnvioIniciadoEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.EnvioDisparadoEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.ProcessamentoEntradaIniciadoEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.ProcessamentoEntradaConcluidoEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.ProcessamentoEntradaDisponivelEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.LidaEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.FixadaEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.OcultaEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.CorrigidaEm).HasColumnType("datetime2(3)");
        builder.Property(m => m.ApagadaParaTodosEm).HasColumnType("datetime2(3)");
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

        // Quem fixou e quem ocultou: Restrict, porque atendente nunca e apagado —
        // desligamento vira Ativo = false e o historico permanece atribuivel.
        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(m => m.FixadaPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(m => m.OcultaPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        // Quem corrigiu e quem apagou no aparelho do cliente. Mesmo Restrict de
        // fixar e ocultar: a assinatura da correcao precisa continuar atribuivel
        // depois de o atendente sair da serventia.
        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(m => m.CorrigidaPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Atendente>()
            .WithMany()
            .HasForeignKey(m => m.ApagadaParaTodosPorAtendenteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.EncaminhadaDeMensagem)
            .WithMany()
            .HasForeignKey(m => m.EncaminhadaDeMensagemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.ConversaId, m.EnviadaEm })
            .HasDatabaseName("IX_Mensagens_Conversa_Enviada");

        // Filtrado: a mensagem fixada de uma conversa e uma entre dezenas, e a
        // consulta do topo do fio nao deve varrer o resto.
        builder.HasIndex(m => m.ConversaId)
            .HasFilter("[FixadaEm] IS NOT NULL")
            .HasDatabaseName("IX_Mensagens_Fixada");

        builder.HasIndex(m => new { m.TenantId, m.AtendenteId })
            .HasDatabaseName("IX_Mensagens_Tenant_Atendente");

        // Idempotencia do webhook: o mesmo evento recebido nao pode abrir dois
        // ciclos de automacao. Restrito ao autor Cliente porque esta coluna tambem
        // guarda o id devolvido pelo gateway para mensagens de saida, cujo namespace
        // nao precisa coincidir com o das mensagens recebidas.
        builder.HasIndex(m => new { m.TenantId, m.IdExternoWhatsapp })
            .IsUnique()
            .HasFilter("[IdExternoWhatsapp] IS NOT NULL AND [AutorTipo] = 1")
            .HasDatabaseName("UQ_Mensagens_Tenant_IdExterno_Cliente");

        // Fila duravel de entrada. O filtro mantem mensagens de saida e historico
        // processado fora do indice quente varrido pelos workers.
        builder.HasIndex(m => new
            {
                m.EstadoProcessamentoEntrada,
                m.ProcessamentoEntradaDisponivelEm,
                m.ProcessamentoEntradaIniciadoEm,
                m.ConversaId,
                m.EnviadaEm
            })
            .HasFilter("[EstadoProcessamentoEntrada] IN (1, 2, 4)")
            .HasDatabaseName("IX_Mensagens_ProcessamentoEntrada");
    }
}
