using CrmCartorio.Api.Modules.CanalWhatsApp.Domain.Enums;

namespace CrmCartorio.Api.Modules.CanalWhatsApp.Application;

/// <summary>
/// De onde vem a regra que está valendo agora.
/// </summary>
/// <remarks>
/// Existe para a tela ser honesta: enquanto ninguém salvou um modo pelo painel,
/// quem manda é <c>CanalWhatsApp:NumerosDeTeste</c> do <c>appsettings</c> do
/// servidor — não a lista que a tela mostra vazia. Ver
/// <see cref="Tenants.Domain.Entities.Tenant.WhatsappModoDeRecebimento"/>.
/// </remarks>
public enum OrigemDaRegra
{
    Banco,
    Appsettings
}

public record NumeroDoCanalResponse(
    Guid Id,
    string Telefone,
    string? Rotulo,
    TipoDeNumero Tipo,
    DateTime CriadoEm,
    string? CriadoPorNome);

public record RegrasDoCanalResponse(
    ModoDeRecebimento Modo,
    OrigemDaRegra Origem,
    IReadOnlyList<NumeroDoCanalResponse> Liberados,
    IReadOnlyList<NumeroDoCanalResponse> Bloqueados);

public record DefinirModoRequest(ModoDeRecebimento Modo);

public record AdicionarNumeroRequest(string Telefone, string? Rotulo, TipoDeNumero Tipo);

/// <summary>Se um telefone pode falar com o canal agora, e por que não, quando não pode.</summary>
public record VeredictoDoNumero(bool Permitido, MotivoDoDescarte? Motivo);

public record DescarteResponse(string Telefone, MotivoDoDescarte Motivo, DateTime CriadoEm);

/// <param name="TotalHoje">Descartes nas últimas 24h — não desde meia-noite, e não os 7 dias que a poda mantém.</param>
public record DescartesResponse(int TotalHoje, IReadOnlyList<DescarteResponse> Recentes);
