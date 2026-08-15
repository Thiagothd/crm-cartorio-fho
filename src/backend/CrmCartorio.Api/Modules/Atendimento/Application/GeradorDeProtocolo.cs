using CrmCartorio.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Atendimento.Application;

public interface IGeradorDeProtocolo
{
    Task<string> GerarAsync(Guid tenantId, DateTime referencia, CancellationToken ct = default);
}

/// <summary>
/// Protocolo no formato AAAAMM-NNNN, sequencial reiniciado a cada mes e unico por tenant.
/// </summary>
/// <remarks>
/// A leitura do ultimo numero usa UPDLOCK/HOLDLOCK para serializar duas conversas
/// criadas no mesmo instante. O indice unico (TenantId, Protocolo) e a rede de
/// seguranca: se ainda assim colidir, a insercao falha em vez de gerar duplicata.
/// O formato pode mudar se o cartorio tiver numeracao oficial de livro a respeitar
/// (REQUISITOS.md, pendencia 6) — e o unico ponto da lista que exige codigo.
/// </remarks>
public class GeradorDeProtocolo(CrmDbContext db) : IGeradorDeProtocolo
{
    public async Task<string> GerarAsync(Guid tenantId, DateTime referencia, CancellationToken ct = default)
    {
        var competencia = referencia.ToString("yyyyMM");
        var prefixo = $"{competencia}-";

        var ultimo = await db.Database
            .SqlQuery<string?>($"""
                SELECT TOP 1 Protocolo AS Value
                  FROM Conversas WITH (UPDLOCK, HOLDLOCK)
                 WHERE TenantId = {tenantId}
                   AND Protocolo LIKE {prefixo + "%"}
                 ORDER BY Protocolo DESC
                """)
            .FirstOrDefaultAsync(ct);

        var proximo = 1;

        if (!string.IsNullOrEmpty(ultimo))
        {
            var sufixo = ultimo[prefixo.Length..];
            if (int.TryParse(sufixo, out var atual))
            {
                proximo = atual + 1;
            }
        }

        return $"{prefixo}{proximo:D4}";
    }
}
