using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Domain.Entities;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Usuarios.Application;

public class AtendenteService(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IHashDeSenha hash) : IAtendenteService
{
    public async Task<IReadOnlyList<AtendenteResponse>> ListarAsync(
        bool incluirInativos,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        var consulta = db.Atendentes.AsNoTracking().Where(a => a.TenantId == tenantId);

        if (!incluirInativos)
        {
            consulta = consulta.Where(a => a.Ativo);
        }

        var itens = await consulta
            .OrderBy(a => a.Nome)
            .Select(a => new
            {
                a.Id,
                a.Nome,
                a.Email,
                a.Perfil,
                a.Cargo,
                a.SetorPadraoId,
                SetorPadraoNome = db.Setores
                    .Where(s => s.Id == a.SetorPadraoId)
                    .Select(s => s.Nome)
                    .FirstOrDefault(),
                a.Ativo,
                a.UltimoAcessoEm
            })
            .ToListAsync(ct);

        // O rotulo do cargo e resolvido na memoria: switch de enum nao traduz para
        // SQL, e mapear isso em CASE WHEN espalharia vocabulario de interface dentro
        // da consulta.
        return [.. itens.Select(a => new AtendenteResponse(
            a.Id,
            a.Nome,
            a.Email,
            a.Perfil,
            a.Cargo,
            DescreverCargo(a.Cargo),
            a.SetorPadraoId,
            a.SetorPadraoNome,
            a.Ativo,
            a.UltimoAcessoEm))];
    }

    /// <summary>
    /// Rotulo do cargo no vocabulario da serventia.
    /// </summary>
    /// <remarks>
    /// Publico porque a resposta do login tambem precisa dele, e duplicar o de-para
    /// em dois lugares faria os dois divergirem no primeiro ajuste de palavra.
    ///
    /// O de-para em si migrou para <see cref="DescricaoDeCargo"/>, no
    /// <c>Domain</c>, quando o modulo de Indicadores passou a precisar do mesmo
    /// rotulo: modulo nao referencia o <c>Application</c> de outro (CLAUDE.md
    /// 7.1). Este metodo fica como atalho para os chamadores que ja existiam.
    /// </remarks>
    public static string DescreverCargo(CargoNaServentia cargo) => DescricaoDeCargo.De(cargo);

    public async Task<AtendenteResponse> ObterAsync(Guid id, CancellationToken ct = default)
    {
        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == ctx.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        return await MapearAsync(atendente, ct);
    }

    public async Task<AtendenteResponse> CriarAsync(
        CriarAtendenteRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var email = NormalizarEmail(request.Email);

        if (string.IsNullOrWhiteSpace(request.Senha) || request.Senha.Length < 8)
        {
            throw UsuariosServiceException.SenhaFraca();
        }

        if (await db.Atendentes.AnyAsync(a => a.TenantId == ctx.TenantId && a.Email == email, ct))
        {
            throw UsuariosServiceException.EmailEmUso(email);
        }

        var atendente = new Atendente
        {
            TenantId = ctx.TenantId,
            Nome = request.Nome.Trim(),
            Email = email,
            SenhaHash = hash.Gerar(request.Senha),
            Perfil = request.Perfil,
            Cargo = request.Cargo,
            SetorPadraoId = request.SetorPadraoId,
            Ativo = true
        };

        db.Atendentes.Add(atendente);
        await db.SaveChangesAsync(ct);

        return await MapearAsync(atendente, ct);
    }

    public async Task<AtendenteResponse> AtualizarAsync(
        Guid id,
        AtualizarAtendenteRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == ctx.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        var email = NormalizarEmail(request.Email);

        var emUso = await db.Atendentes.AnyAsync(
            a => a.TenantId == ctx.TenantId && a.Email == email && a.Id != id, ct);

        if (emUso)
        {
            throw UsuariosServiceException.EmailEmUso(email);
        }

        // Um administrador se trancando para fora deixaria o cartorio sem quem
        // gerencie acessos — e o desbloqueio so sairia por SQL manual.
        if (!request.Ativo && atendente.Id == ctx.AtendenteId)
        {
            throw UsuariosServiceException.NaoPodeDesativarASiMesmo();
        }

        // SEC-01/SEC-03: TokenVersion sobe dentro da MESMA instrucao atomica que
        // grava Perfil/Cargo/Ativo, comparando com o valor que esta no banco no
        // instante do UPDATE — nunca com o `atendente` lido acima, que pode
        // estar desatualizado sob concorrencia. Sobe quando:
        //   1. o Perfil muda (SEC-01, qualquer mudanca — inclusive entre dois
        //      perfis igualmente privilegiados ou nao-privilegiados: uma
        //      promocao Atendente -> Supervisor tambem precisa revogar
        //      sessoes antigas);
        //   2. Ativo vira false (reativar, false -> true, nao mexe na versao,
        //      so nao deixa reduzi-la nem zera-la);
        //   3. `Cargo == Titular` muda de valor (CORRECAO — revisao
        //      independente do PR #46, bloqueador 12).
        //
        // CORRECAO (bloqueador 12): a versao anterior comparava a expressao
        // COMPOSTA `Perfil == Administrador || Cargo == Titular` antes/depois
        // — e por isso nao detectava Administrador/Substituto ->
        // Administrador/Titular (antes=true, depois=true: a composta nao
        // muda, mesmo o Cargo tendo cruzado a linha). Isso importa porque
        // SEC-03 da ao Titular uma protecao PROPRIA, distinta de "privilegiado
        // em geral": um Administrador comum nao pode resetar/emitir ticket
        // para um Titular (GarantirTitularProtegido), so outro Administrador
        // que TAMBEM seja Titular pode. Um Administrador que vira Titular muda
        // de categoria de protecao mesmo sem o Perfil mudar — e por isso
        // precisa revogar sessoes/tickets/desafios antigos por si so. Agora a
        // comparacao e so sobre `Cargo == Titular`, isolado do Perfil —
        // simples e estritamente mais preciso que a composta: continua
        // cobrindo os casos originais (Substituto <-> Titular, com Perfil
        // fixo) e passa a cobrir tambem Administrador/Substituto <->
        // Administrador/Titular. Mudar entre dois cargos igualmente
        // NAO-Titular (ex.: Substituto -> Escrevente) continua sem subir —
        // nenhum ganho de seguranca em revogar por isso.
        await db.Atendentes
            .Where(a => a.Id == id && a.TenantId == ctx.TenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Nome, request.Nome.Trim())
                .SetProperty(a => a.Email, email)
                .SetProperty(a => a.Perfil, request.Perfil)
                .SetProperty(a => a.Cargo, request.Cargo)
                .SetProperty(a => a.SetorPadraoId, request.SetorPadraoId)
                .SetProperty(a => a.Ativo, request.Ativo)
                .SetProperty(a => a.TokenVersion, a =>
                    a.Perfil != request.Perfil
                    || (a.Ativo && !request.Ativo)
                    || (a.Cargo == CargoNaServentia.Titular) != (request.Cargo == CargoNaServentia.Titular)
                        ? a.TokenVersion + 1
                        : a.TokenVersion),
                ct);

        return await ObterAsync(id, ct);
    }

    public async Task RedefinirSenhaAsync(
        Guid id,
        RedefinirSenhaRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        if (string.IsNullOrWhiteSpace(request.NovaSenha) || request.NovaSenha.Length < 8)
        {
            throw UsuariosServiceException.SenhaFraca();
        }

        var novoHash = hash.Gerar(request.NovaSenha);

        // SEC-01: mesma logica atomica de AutenticacaoService.AlterarSenhaAsync
        // — grava o hash e sobe TokenVersion numa unica instrucao SQL.
        //
        // SEC-02: a mesma instrucao tambem DESBLOQUEIA. Redefinir a senha e a
        // saida administrativa para uma conta travada — sem ela, um atendente
        // bloqueado por engano (ou por ataque de terceiro contra o e-mail dele)
        // ficaria esperando ate 30 minutos mesmo com o administrador presente e
        // disposto a resolver. Zera incondicionalmente: quem pode redefinir a
        // senha de alguem ja tem autoridade suficiente para destravar a conta.
        var afetadas = await db.Atendentes
            .Where(a => a.Id == id && a.TenantId == ctx.TenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.SenhaHash, novoHash)
                .SetProperty(a => a.TokenVersion, a => a.TokenVersion + 1)
                .SetProperty(a => a.FalhasDeLoginConsecutivas, 0)
                .SetProperty(a => a.NivelDeBloqueio, (byte)0)
                .SetProperty(a => a.BloqueadoAte, (DateTime?)null), ct);

        if (afetadas == 0)
        {
            throw UsuariosServiceException.AtendenteNaoEncontrado();
        }
    }

    private void GarantirPermissao()
    {
        if (!ctx.PodeGerirAtendentes)
        {
            throw UsuariosServiceException.SemPermissao();
        }
    }

    private static string NormalizarEmail(string? email)
    {
        var normalizado = email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalizado) || !normalizado.Contains('@'))
        {
            throw new UsuariosServiceException("email_invalido", "Informe um e-mail valido.");
        }

        return normalizado;
    }

    private async Task<AtendenteResponse> MapearAsync(Atendente atendente, CancellationToken ct)
    {
        var setor = atendente.SetorPadraoId is null
            ? null
            : await db.Setores.AsNoTracking()
                .Where(s => s.Id == atendente.SetorPadraoId)
                .Select(s => s.Nome)
                .FirstOrDefaultAsync(ct);

        return new AtendenteResponse(
            atendente.Id,
            atendente.Nome,
            atendente.Email,
            atendente.Perfil,
            atendente.Cargo,
            DescreverCargo(atendente.Cargo),
            atendente.SetorPadraoId,
            setor,
            atendente.Ativo,
            atendente.UltimoAcessoEm);
    }
}
