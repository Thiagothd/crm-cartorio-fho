using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Servicos.Domain.Entities;
using CrmCartorio.Api.Modules.Tenants.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Infrastructure.Persistence;

public class CrmDbContext(DbContextOptions<CrmDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Atendente> Atendentes => Set<Atendente>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Setor> Setores => Set<Setor>();
    public DbSet<EtapaFunil> EtapasFunil => Set<EtapaFunil>();
    public DbSet<MensagemAutomatica> MensagensAutomaticas => Set<MensagemAutomatica>();
    public DbSet<RespostaRapida> RespostasRapidas => Set<RespostaRapida>();
    public DbSet<TipoServico> TiposServico => Set<TipoServico>();
    public DbSet<PrazoServico> PrazosServico => Set<PrazoServico>();
    public DbSet<Conversa> Conversas => Set<Conversa>();
    public DbSet<Mensagem> Mensagens => Set<Mensagem>();
    public DbSet<HistoricoEtapa> HistoricoEtapas => Set<HistoricoEtapa>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Cada modulo carrega a configuracao das proprias entidades. O assembly e
        // varrido inteiro para que adicionar um slice novo nao exija tocar aqui.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CrmDbContext).Assembly);
    }

    public override int SaveChanges()
    {
        CarimbarDatas();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        CarimbarDatas();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void CarimbarDatas()
    {
        var agora = DateTime.UtcNow;

        foreach (var entrada in ChangeTracker.Entries<EntidadeBase>())
        {
            switch (entrada.State)
            {
                case EntityState.Added:
                    entrada.Entity.CriadoEm = agora;
                    entrada.Entity.AtualizadoEm = agora;
                    break;

                case EntityState.Modified:
                    entrada.Entity.AtualizadoEm = agora;
                    // CriadoEm nao pode ser reescrito por engano em um update parcial.
                    entrada.Property(e => e.CriadoEm).IsModified = false;
                    break;
            }
        }
    }
}
