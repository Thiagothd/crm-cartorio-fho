using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Modules.Tenants.Domain.Enums;

namespace CrmCartorio.Api.Modules.Tenants.Domain.Entities;

/// <summary>
/// Cartorio cliente. Na Fase 1 existe um unico registro (o cartorio do cartório parceiro),
/// mas a estrutura ja e multi-tenant.
/// </summary>
public class Tenant : EntidadeBase
{
    public required string RazaoSocial { get; set; }

    public string? NomeFantasia { get; set; }

    /// <summary>Somente digitos.</summary>
    public required string Cnpj { get; set; }

    /// <summary>WABA ID da Meta. Preenchido apenas na Fase 2.</summary>
    public string? WhatsappBusinessAccountId { get; set; }

    /// <summary>Phone Number ID da Cloud API. Preenchido apenas na Fase 2.</summary>
    public string? WhatsappPhoneNumberId { get; set; }

    /// <summary>Numero em formato E.164, apenas para exibicao.</summary>
    public string? WhatsappNumeroExibicao { get; set; }

    /// <summary>
    /// Textura de fundo do fio de mensagens.
    /// </summary>
    /// <remarks>
    /// Por TENANT, e nao por atendente: e a cara do cartorio, e a mesma tela vista
    /// por duas pessoas da mesma serventia nao deveria parecer dois produtos.
    /// </remarks>
    public AparenciaDaConversa AparenciaDaConversa { get; set; } = AparenciaDaConversa.PapelLiso;

    public bool Ativo { get; set; } = true;
}
