using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Modules.Seguranca.Domain.Enums;
using CrmCartorio.Api.Modules.Usuarios.Domain;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Infrastructure.Security;

/// <summary>
/// Sessão vigente de um atendente, já com as permissões resolvidas — ou
/// <c>null</c> quando o token não corresponde a uma sessão ativa.
/// </summary>
/// <param name="AutorizacaoDeAcessoTecnicoId">
/// Blindagem LGPD: preenchido só quando o atendente é conta técnica
/// (<c>EhContaTecnica</c>) e tem uma autorização ATIVA neste instante — e
/// só então <paramref name="Permissoes"/> já vem com <c>AtendimentoVer</c>/
/// <c>ClientesVer</c> acrescentados. Sem autorização ativa, conta técnica
/// segue só com o preset fixo dela (hoje: <c>suporte.solicitar_acesso</c>).
/// </param>
/// <param name="EscopoDeConversaId">Preenchido só quando a autorização acima tem escopo de uma conversa específica.</param>
public sealed record SessaoDoAtendente(
    IReadOnlySet<PermissaoDoSistema> Permissoes,
    Guid? AutorizacaoDeAcessoTecnicoId = null,
    Guid? EscopoDeConversaId = null);

/// <summary>
/// SEC-01: confere se a sessao carregada nas claims de um JWT ja validado
/// (assinatura, issuer/audience, expiracao) ainda corresponde ao estado atual
/// do atendente no banco. E o mecanismo de revogacao — sem ele, um token so
/// deixa de funcionar quando expira, mesmo depois de troca de senha,
/// desativacao ou mudanca de Perfil.
/// </summary>
/// <remarks>
/// Usado nos dois pontos de entrada autenticados do sistema: a autorizacao de
/// controller (<see cref="SessaoAtivaHandler"/>, via a policy padrao) e o
/// handshake do SignalR (<see cref="Realtime.AtendimentoHub.OnConnectedAsync"/>,
/// em defesa de profundidade). Uma conexao SignalR JA aberta antes de uma
/// revogacao nao e reavaliada por este componente — ver SEC-01B no README do
/// modulo Usuarios.
///
/// Desde as permissões granulares, esta é também a ÚNICA consulta ao banco
/// que resolve o que o atendente pode fazer — o mesmo round-trip que já
/// existia para SEC-01 devolve o conjunto de permissões, sem custo extra por
/// requisição. Ver Modules/Usuarios/README.md, "Permissões granulares".
/// </remarks>
public interface IValidadorDeSessao
{
    /// <summary>Nulo quando a sessão não é válida (inativo, ou TokenVersion não bate).</summary>
    Task<SessaoDoAtendente?> CarregarSessaoAsync(
        Guid atendenteId,
        Guid tenantId,
        int tokenVersionDaClaim,
        CancellationToken ct = default);
}

public class ValidadorDeSessao(CrmDbContext db) : IValidadorDeSessao
{
    public async Task<SessaoDoAtendente?> CarregarSessaoAsync(
        Guid atendenteId,
        Guid tenantId,
        int tokenVersionDaClaim,
        CancellationToken ct = default)
    {
        if (atendenteId == Guid.Empty || tenantId == Guid.Empty)
        {
            return null;
        }

        // Consulta minima, por PK + TenantId, sem rastreamento: e o unico
        // custo extra que este mecanismo adiciona a cada request autenticado
        // — Permissoes/Perfil viajam de graca no mesmo round-trip do SEC-01.
        var estado = await db.Atendentes.AsNoTracking()
            .Where(a => a.Id == atendenteId && a.TenantId == tenantId)
            .Select(a => new { a.Ativo, a.TokenVersion, a.Perfil, a.Permissoes, a.EhContaTecnica })
            .FirstOrDefaultAsync(ct);

        if (estado is null || !estado.Ativo || estado.TokenVersion != tokenVersionDaClaim)
        {
            return null;
        }

        var conjunto = estado.Permissoes is null
            ? CatalogoDePermissoes.ConjuntoDaClasse(estado.Perfil)
            : CatalogoDePermissoes.Desserializar(estado.Permissoes);

        if (!estado.EhContaTecnica)
        {
            return new SessaoDoAtendente(conjunto);
        }

        // Blindagem LGPD: conta tecnica so ganha leitura enquanto houver uma
        // autorizacao ATIVA neste instante — resolvida a cada requisicao,
        // nunca guardada na claim do JWT (a autorizacao pode ser aprovada ou
        // revogada a qualquer momento, e o token da conta tecnica nao seria
        // reemitido so por isso).
        var agora = DateTime.UtcNow;
        var autorizacaoAtiva = await db.AutorizacoesDeAcessoTecnico.AsNoTracking()
            .Where(a => a.ContaTecnicaId == atendenteId
                     && a.TenantId == tenantId
                     && a.AprovadaEm != null
                     && a.RevogadaEm == null
                     && a.ValidaAte != null && a.ValidaAte > agora)
            .OrderByDescending(a => a.AprovadaEm)
            .Select(a => new { a.Id, a.Escopo, a.ConversaId })
            .FirstOrDefaultAsync(ct);

        if (autorizacaoAtiva is null)
        {
            return new SessaoDoAtendente(conjunto);
        }

        var escopoDeConversaId = autorizacaoAtiva.Escopo == EscopoDeAcesso.Conversa
            ? autorizacaoAtiva.ConversaId
            : null;

        // Escopo Conversa ganha SÓ AtendimentoVer — é uma sessão de suporte
        // sobre UM atendimento, não motivo para abrir a ficha do cliente
        // (CPF, RG, filiação). Escopo Geral ganha os dois: é a autorização
        // ampla, pensada para investigação que pode precisar da ficha.
        var conjuntoAmpliado = escopoDeConversaId is not null
            ? conjunto.Append(PermissaoDoSistema.AtendimentoVer).ToHashSet()
            : conjunto.Append(PermissaoDoSistema.AtendimentoVer).Append(PermissaoDoSistema.ClientesVer).ToHashSet();

        return new SessaoDoAtendente(conjuntoAmpliado, autorizacaoAtiva.Id, escopoDeConversaId);
    }
}
