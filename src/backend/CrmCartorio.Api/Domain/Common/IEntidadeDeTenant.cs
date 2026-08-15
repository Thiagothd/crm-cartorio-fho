namespace CrmCartorio.Api.Domain.Common;

/// <summary>
/// Marca a entidade como pertencente a um cartorio (tenant).
/// Presente desde a Fase 1 mesmo com um unico tenant: adicionar isolamento depois
/// obrigaria a revisar toda consulta ja escrita, com risco de vazamento entre clientes.
/// </summary>
public interface IEntidadeDeTenant
{
    Guid TenantId { get; set; }
}
