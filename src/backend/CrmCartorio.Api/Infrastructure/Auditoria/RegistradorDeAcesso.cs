using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Seguranca.Domain.Entities;
using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace CrmCartorio.Api.Infrastructure.Auditoria;

/// <inheritdoc cref="IRegistradorDeAcesso"/>
/// <remarks>
/// Usa o MESMO <see cref="CrmDbContext"/> escopado do serviço que chamou —
/// mesmo padrão de <c>AutenticacaoService.RegistrarEventoAsync</c> — e faz
/// seu próprio <c>SaveChangesAsync</c>: os métodos que chamam isto (leituras)
/// não têm outra alteração pendente na mesma unidade de trabalho.
/// </remarks>
public class RegistradorDeAcesso(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IHttpContextAccessor acessor,
    IMemoryCache cache) : IRegistradorDeAcesso
{
    /// <summary>
    /// Abrir a mesma conversa várias vezes em poucos minutos é o uso normal
    /// do painel (recarregar a tela, alternar de aba) — sem isto, a trilha
    /// vira ruído e o cartório para de ler. <see cref="TipoDeAcesso.DownloadDeAnexo"/>
    /// e o ciclo de vida do acesso técnico (10-13) nunca deduplicam: cada um
    /// é uma ação discreta e distinta (baixar UM arquivo, pedir/aprovar/
    /// negar/revogar UMA autorização) — nunca "a mesma tela ainda aberta".
    /// </summary>
    private static readonly TimeSpan JanelaDeDeduplicacao = TimeSpan.FromMinutes(15);

    private static bool Deduplica(TipoDeAcesso tipo) =>
        tipo is TipoDeAcesso.LeituraDeConversa or TipoDeAcesso.LeituraDeFichaDeCliente;

    public async Task RegistrarAsync(
        TipoDeAcesso tipo,
        Guid? conversaId = null,
        Guid? clienteId = null,
        Guid? mensagemId = null,
        Guid? autorizacaoDeAcessoTecnicoId = null,
        CancellationToken ct = default)
    {
        var alvo = mensagemId ?? conversaId ?? clienteId ?? autorizacaoDeAcessoTecnicoId ?? Guid.Empty;

        if (Deduplica(tipo))
        {
            var chave = $"acesso:{ctx.TenantId:N}:{ctx.AtendenteId:N}:{tipo}:{alvo:N}";

            if (cache.TryGetValue(chave, out _))
            {
                return;
            }

            cache.Set(chave, true, JanelaDeDeduplicacao);
        }

        db.RegistrosDeAcesso.Add(new RegistroDeAcesso
        {
            TenantId = ctx.TenantId,
            AtendenteId = ctx.AtendenteId,
            Tipo = tipo,
            ConversaId = conversaId,
            ClienteId = clienteId,
            MensagemId = mensagemId,
            AutorizacaoDeAcessoTecnicoId = autorizacaoDeAcessoTecnicoId ?? ctx.AutorizacaoDeAcessoTecnicoId,
            IpDeOrigem = acessor.HttpContext is { } http ? ResolvedorDeIpDoCliente.Resolver(http) : null,
            OcorridoEm = DateTime.UtcNow
        });

        // Sem try/catch de propósito: se isto falhar, a leitura que chamou
        // falha junto — fail-closed, nunca ler sem conseguir provar que leu.
        await db.SaveChangesAsync(ct);
    }
}
