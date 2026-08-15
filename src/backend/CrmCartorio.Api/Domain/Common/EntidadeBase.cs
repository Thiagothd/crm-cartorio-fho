namespace CrmCartorio.Api.Domain.Common;

/// <summary>
/// Base de toda entidade persistida. Id e Guid v7 (sequencial) gerado na aplicacao,
/// o que evita round-trip ao banco e mantem o indice clusterizado sem fragmentacao.
/// </summary>
public abstract class EntidadeBase
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
