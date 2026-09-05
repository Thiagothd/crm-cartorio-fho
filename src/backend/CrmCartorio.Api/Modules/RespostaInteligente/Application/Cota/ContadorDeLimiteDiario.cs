using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.RespostaInteligente.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.RespostaInteligente.Application.Cota;

public enum CategoriaDeLimiteDiario
{
    /// <summary>Respostas ENTREGUES ao cliente, qualquer provedor — <see cref="OpcoesDaIA.LimiteDiario"/>.</summary>
    Geral,

    /// <summary>Respostas produzidas PELO ANTHROPIC — <see cref="OpcoesDoAnthropic.LimiteDiarioHaiku"/>.</summary>
    Haiku,
}

/// <summary>
/// Reserva atômica dos dois tetos diários por tenant. Corrige a não-atomicidade do
/// <c>LimiteDiario</c> original (COUNT seguido de decisão, sem lock — duas
/// mensagens simultâneas perto do teto podiam ambas ler "abaixo do limite"): aqui
/// a reserva acontece ANTES da chamada, sob um semáforo único, e só é confirmada
/// se algum provedor tiver sucesso — <see cref="LiberarAsync"/> devolve a vaga
/// quando isso não acontece.
/// </summary>
/// <remarks>
/// Singleton, com hidratação preguiçosa por (tenant, categoria) a partir de
/// <see cref="Domain.Entities.ChamadaDeIa"/> — sobrevive a reciclagem do App Pool
/// sem reabrir o teto do zero no meio do dia. Um único semáforo serializa a
/// decisão em si (rápida, só memória), não a chamada HTTP ao provedor — por isso
/// não vira gargalo de latência mesmo com várias conversas simultâneas.
/// </remarks>
public class ContadorDeLimiteDiario(IServiceScopeFactory scopeFactory)
{
    private readonly SemaphoreSlim _semaforo = new(1, 1);
    private readonly Dictionary<(Guid TenantId, CategoriaDeLimiteDiario Categoria), Estado> _porChave = new();

    private sealed class Estado
    {
        public DateOnly Dia;
        public int Usado;
    }

    public async Task<bool> TentarReservarAsync(
        Guid tenantId, CategoriaDeLimiteDiario categoria, int limite, CancellationToken ct)
    {
        if (limite <= 0)
        {
            return false;
        }

        await _semaforo.WaitAsync(ct);

        try
        {
            var estado = await ObterOuHidratarAsync(tenantId, categoria, ct);
            RenovarDiaSeNecessario(estado);

            if (estado.Usado >= limite)
            {
                return false;
            }

            estado.Usado++;
            return true;
        }
        finally
        {
            _semaforo.Release();
        }
    }

    /// <summary>Devolve uma reserva feita por <see cref="TentarReservarAsync"/> quando ela não foi usada (ex.: os dois provedores falharam).</summary>
    public async Task LiberarAsync(Guid tenantId, CategoriaDeLimiteDiario categoria, CancellationToken ct)
    {
        await _semaforo.WaitAsync(ct);

        try
        {
            if (_porChave.TryGetValue((tenantId, categoria), out var estado) && estado.Usado > 0)
            {
                estado.Usado--;
            }
        }
        finally
        {
            _semaforo.Release();
        }
    }

    public async Task<int> ConsultarUsadoAsync(Guid tenantId, CategoriaDeLimiteDiario categoria, CancellationToken ct)
    {
        await _semaforo.WaitAsync(ct);

        try
        {
            var estado = await ObterOuHidratarAsync(tenantId, categoria, ct);
            RenovarDiaSeNecessario(estado);
            return estado.Usado;
        }
        finally
        {
            _semaforo.Release();
        }
    }

    private async Task<Estado> ObterOuHidratarAsync(Guid tenantId, CategoriaDeLimiteDiario categoria, CancellationToken ct)
    {
        var chave = (tenantId, categoria);

        if (_porChave.TryGetValue(chave, out var existente))
        {
            return existente;
        }

        var estado = new Estado { Dia = DateOnly.FromDateTime(DateTime.UtcNow) };
        _porChave[chave] = estado;

        try
        {
            using var escopo = scopeFactory.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<CrmDbContext>();
            var inicioDoDia = DateTime.UtcNow.Date;

            estado.Usado = categoria == CategoriaDeLimiteDiario.Geral
                ? await db.ChamadasDeIa.AsNoTracking()
                    .CountAsync(c => c.TenantId == tenantId && c.Sucesso && c.IniciadaEm >= inicioDoDia, ct)
                : await db.ChamadasDeIa.AsNoTracking()
                    .CountAsync(c => c.TenantId == tenantId && c.Sucesso
                                   && c.Provedor == ProvedorDeIa.Anthropic && c.IniciadaEm >= inicioDoDia, ct);
        }
        catch (Exception)
        {
            // Comeca do zero na pior das hipoteses — o teto so fica mais
            // permissivo por um instante, nunca deixa o cliente sem resposta.
        }

        return estado;
    }

    private static void RenovarDiaSeNecessario(Estado estado)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        if (estado.Dia != hoje)
        {
            estado.Dia = hoje;
            estado.Usado = 0;
        }
    }
}
