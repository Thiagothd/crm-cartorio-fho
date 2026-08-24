namespace CrmCartorio.Api.Modules.Clientes.Application;

/// <summary>
/// Importação de contatos em lote — texto colado, CSV, ou (quando a sonda do
/// gateway confirmar o formato) a agenda do WhatsApp pareado. Nunca envia
/// mensagem: só cadastra. Ver <c>docs/DEPLOY-WHATSAPP.md</c> e o CLAUDE.md,
/// seção 2, sobre por que disparo em massa foi cancelado deste ajuste.
/// </summary>
public interface IImportacaoDeContatosService
{
    /// <summary>Classifica cada linha sem gravar nada — a prévia que o operador revisa.</summary>
    Task<AnalisarImportacaoResponse> AnalisarAsync(AnalisarImportacaoRequest request, CancellationToken ct = default);

    /// <summary>Grava só as linhas que o operador aprovou na prévia.</summary>
    Task<ConfirmarImportacaoResponse> ConfirmarAsync(ConfirmarImportacaoRequest request, CancellationToken ct = default);
}
