using CrmCartorio.Api.Modules.Tenants.Domain.Enums;

namespace CrmCartorio.Api.Modules.Tenants.Application;

public record SalvarAparenciaRequest(AparenciaDaConversa AparenciaDaConversa);

public record AparenciaResponse(AparenciaDaConversa AparenciaDaConversa);
