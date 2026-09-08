using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Usuarios.Domain;
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
        GarantirPermissao(PermissaoDoSistema.EquipeVer);

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
                a.UltimoAcessoEm,
                a.MfaAtivo,
                a.ModeloDePermissao,
                a.Permissoes
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
            a.UltimoAcessoEm,
            a.MfaAtivo,
            a.ModeloDePermissao,
            CatalogoDePermissoes.ChavesEfetivas(a.Permissoes, a.Perfil)))];
    }

    /// <summary>
    /// Projecao minima (id + nome) do que o balcao usa para "transferir
    /// para..." — deliberadamente SEM gate de <see cref="PermissaoDoSistema.EquipeVer"/>:
    /// transferir a propria conversa para um colega e uma acao basica de
    /// qualquer atendente, nao administracao de equipe. Nunca devolve e-mail,
    /// perfil ou ultimo acesso.
    /// </summary>
    public async Task<IReadOnlyList<AtendenteOpcaoResponse>> ListarOpcoesAsync(CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        return await db.Atendentes.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Ativo)
            .OrderBy(a => a.Nome)
            .Select(a => new AtendenteOpcaoResponse(a.Id, a.Nome))
            .ToListAsync(ct);
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
        GarantirPermissao(PermissaoDoSistema.EquipeVer);

        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == ctx.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        return await MapearAsync(atendente, ct);
    }

    public async Task<AtendenteResponse> CriarAsync(
        CriarAtendenteRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.EquipeCadastrar);

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
            // Permissoes/ModeloDePermissao ficam NULL — "segue a Classe da
            // conta" ate alguem personalizar em /equipe/:id/permissoes.
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
        GarantirPermissao(PermissaoDoSistema.EquipeEditar);

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

        // Desativar o ULTIMO outro administrador tambem tranca o cartorio pra
        // fora — mesma invariante de DefinirPermissoesAsync (c), so que aqui
        // olhando "vai desativar" em vez de "vai tirar a permissao".
        // "Administrador" e' quem tem EquipePermissoes de fato (personalizado
        // ou herdado da Classe) — mesmo calculo do pipe de cerca.
        if (!request.Ativo)
        {
            var chaveDeEquipePermissoes = CatalogoDePermissoes
                .Descrever(PermissaoDoSistema.EquipePermissoes).ChaveTexto;
            var marcador = $"|{chaveDeEquipePermissoes}|";
            var tenantId = ctx.TenantId;

            var souOUltimoAdministrador = (atendente.Permissoes != null
                    ? atendente.Permissoes.Contains(marcador)
                    : atendente.Perfil == PerfilDeAtendente.Administrador)
                && !await db.Atendentes.AnyAsync(x =>
                    x.TenantId == tenantId && x.Id != id && x.Ativo
                    && (x.Permissoes != null
                        ? x.Permissoes.Contains(marcador)
                        : x.Perfil == PerfilDeAtendente.Administrador), ct);

            if (souOUltimoAdministrador)
            {
                throw UsuariosServiceException.UltimoAdministrador();
            }
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
        //
        // Nota sobre permissoes granulares: este UPDATE NUNCA toca Permissoes
        // nem ModeloDePermissao — trocar a Classe da conta (Perfil) aqui NAO
        // reseta personalizacao ja feita em /equipe/:id/permissoes. As duas
        // acoes moram em endpoints separados de proposito (ver
        // DefinirPermissoesAsync).
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
        GarantirPermissao(PermissaoDoSistema.EquipeRedefinirSenha);

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

    /// <summary>
    /// Exclusao fisica — segundo caso no sistema, depois de
    /// ClienteService.ExcluirAsync (Modules/Clientes/README.md). Recusa quando
    /// ha qualquer rastro do atendente em Conversas, Mensagens,
    /// HistoricoEtapas, canal WhatsApp, conhecimento de IA ou como AUTOR de um
    /// evento de seguranca sobre outra conta (auditoria de terceiro nao pode
    /// sumir). Eventos e credenciais de MFA que pertencem so a ESTA conta sao
    /// limpos antes do Remove — nao contam como "historico" porque desaparecem
    /// naturalmente com a propria conta. Para quem tem historico, o caminho e
    /// desativar (<see cref="AtualizarAtendenteRequest.Ativo"/>), nao excluir.
    /// </summary>
    public async Task ExcluirAsync(Guid id, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.EquipeExcluir);

        if (id == ctx.AtendenteId)
        {
            throw UsuariosServiceException.NaoPodeExcluirASiMesmo();
        }

        var tenantId = ctx.TenantId;

        var atendente = await db.Atendentes
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        // Mesma invariante de AtualizarAsync (desativar o ultimo administrador)
        // — excluir e pelo menos tao disruptivo quanto desativar.
        var chaveDeEquipePermissoes = CatalogoDePermissoes
            .Descrever(PermissaoDoSistema.EquipePermissoes).ChaveTexto;
        var marcador = $"|{chaveDeEquipePermissoes}|";

        var souOUltimoAdministrador = (atendente.Permissoes != null
                ? atendente.Permissoes.Contains(marcador)
                : atendente.Perfil == PerfilDeAtendente.Administrador)
            && !await db.Atendentes.AnyAsync(x =>
                x.TenantId == tenantId && x.Id != id && x.Ativo
                && (x.Permissoes != null
                    ? x.Permissoes.Contains(marcador)
                    : x.Perfil == PerfilDeAtendente.Administrador), ct);

        if (souOUltimoAdministrador)
        {
            throw UsuariosServiceException.UltimoAdministrador();
        }

        var temHistorico =
            await db.Conversas.AnyAsync(x =>
                x.AtendenteId == id || x.EncerradaPorAtendenteId == id || x.ReabertaPorAtendenteId == id, ct)
            || await db.Mensagens.AnyAsync(x =>
                x.AtendenteId == id || x.FixadaPorAtendenteId == id || x.OcultaPorAtendenteId == id, ct)
            || await db.HistoricoEtapas.AnyAsync(x => x.AtendenteId == id, ct)
            || await db.CanalWhatsappNumeros.AnyAsync(x => x.CriadoPorAtendenteId == id, ct)
            || await db.CanalWhatsappConexoes.AnyAsync(x => x.AtendenteId == id, ct)
            || await db.ConhecimentosDeIa.AnyAsync(x =>
                x.CriadoPorAtendenteId == id || x.AtualizadoPorAtendenteId == id, ct)
            || await db.HistoricoDeConhecimentosDeIa.AnyAsync(x => x.AlteradoPorAtendenteId == id, ct)
            || await db.EventosDeSeguranca.AnyAsync(x => x.AtorAtendenteId == id, ct);

        if (temHistorico)
        {
            throw UsuariosServiceException.NaoPodeExcluirComHistorico();
        }

        // Limpeza segura: linhas que pertencem exclusivamente a esta conta e
        // desaparecem com ela — nao sao "historico" de terceiro.
        await db.DesafiosDeMfa.Where(x => x.AtendenteId == id).ExecuteDeleteAsync(ct);
        await db.CodigosDeRecuperacaoMfa.Where(x => x.AtendenteId == id).ExecuteDeleteAsync(ct);
        await db.DispositivosConfiaveis.Where(x => x.AtendenteId == id).ExecuteDeleteAsync(ct);
        await db.EventosDeSeguranca.Where(x => x.AtendenteId == id).ExecuteDeleteAsync(ct);

        db.Atendentes.Remove(atendente);
        await db.SaveChangesAsync(ct);
    }

    // --------------------------------------------------- Permissoes granulares

    public async Task<PermissoesDoAtendenteResponse> ObterPermissoesAsync(Guid id, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.EquipePermissoes);

        var atendente = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == ctx.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        var conjunto = atendente.Permissoes is null
            ? CatalogoDePermissoes.ConjuntoDaClasse(atendente.Perfil)
            : CatalogoDePermissoes.Desserializar(atendente.Permissoes);

        return new PermissoesDoAtendenteResponse(
            atendente.Id,
            atendente.ModeloDePermissao,
            [.. conjunto.Select(c => CatalogoDePermissoes.Descrever(c).ChaveTexto)],
            [.. CatalogoDePermissoes.Todas.Select(d =>
                new PermissaoDoCatalogoResponse(d.ChaveTexto, d.Rotulo, d.Descricao, d.Bloco, d.EhTela))],
            [.. Enum.GetValues<ModeloDePermissao>().Select(m =>
                {
                    var (rotulo, descricao) = CatalogoDePermissoes.DescreverModelo(m);
                    return new ModeloDePermissaoResponse(
                        m, rotulo, descricao,
                        [.. CatalogoDePermissoes.ConjuntoDoModelo(m).Select(c => CatalogoDePermissoes.Descrever(c).ChaveTexto)]);
                })]);
    }

    /// <summary>
    /// A meta-permissao. Ordem das travas, todas obrigatorias — ver
    /// Modules/Usuarios/README.md, "Permissoes granulares":
    /// (0) o ator precisa ter <see cref="PermissaoDoSistema.EquipePermissoes"/>;
    /// (a) ninguem edita as proprias permissoes;
    /// (b) anti-escalada — nao concede o que nao tem;
    /// (c) sobra pelo menos um administrador ativo, numa unica instrucao atomica;
    /// (d) TokenVersion NAO sobe (as permissoes ja saem do banco a cada
    ///     request — o efeito e imediato sem derrubar ninguem do meio de um
    ///     atendimento);
    /// (e) so um Titular altera permissao de outro Titular;
    /// (f) registra EventoDeSeguranca — nunca as chaves, so o fato.
    /// </summary>
    public async Task DefinirPermissoesAsync(
        Guid id, DefinirPermissoesRequest request, string? ip, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.EquipePermissoes);

        // (a) Ninguem edita as proprias permissoes — sem isto, EquipePermissoes
        // sozinha ja bastaria para qualquer um virar administrador.
        if (id == ctx.AtendenteId)
        {
            throw UsuariosServiceException.NaoPodeEditarAsPropriasPermissoes();
        }

        var alvo = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == ctx.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        var ator = await db.Atendentes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == ctx.AtendenteId && a.TenantId == ctx.TenantId, ct)
            ?? throw UsuariosServiceException.AtendenteNaoEncontrado();

        // (e) Titular protegido — espelha MfaService.GarantirTitularProtegido.
        // Sem isto, um Administrador nao-Titular tranca o Titular fora do
        // proprio cartorio contornando a protecao do SEC-03.
        if (alvo.Cargo == CargoNaServentia.Titular && ator.Cargo != CargoNaServentia.Titular)
        {
            throw UsuariosServiceException.ApenasTitularAlteraPermissaoDeTitular();
        }

        var chavesPedidas = CatalogoDePermissoes.DesserializarLista(request.Chaves);

        // (b) Anti-escalada: o ator so repassa o que ele mesmo tem, inclusive
        // EquipePermissoes em si.
        var conjuntoDoAtor = ator.Permissoes is null
            ? CatalogoDePermissoes.ConjuntoDaClasse(ator.Perfil)
            : CatalogoDePermissoes.Desserializar(ator.Permissoes);

        if (!chavesPedidas.IsSubsetOf(conjuntoDoAtor))
        {
            throw UsuariosServiceException.NaoPodeConcederOQueNaoTem();
        }

        var textoNovo = CatalogoDePermissoes.Serializar(chavesPedidas);
        var mantemEquipePermissoes = chavesPedidas.Contains(PermissaoDoSistema.EquipePermissoes);
        var marcadorDeEquipePermissoes = $"%|{CatalogoDePermissoes.Descrever(PermissaoDoSistema.EquipePermissoes).ChaveTexto}|%";
        var tenantId = ctx.TenantId;

        // (c) Sobra pelo menos um administrador ativo. Precisa de transacao
        // explicita com UPDLOCK+HOLDLOCK — nao de um EXISTS solto dentro do
        // WHERE do ExecuteUpdateAsync (a versao anterior disto), porque um
        // EXISTS que exclui so o proprio alvo NUNCA trava a linha do outro
        // lado de uma troca cruzada: A tira de B enquanto B tira de A, cada
        // leitura ve a outra conta ainda intacta, e as duas passam — zerando
        // quem tem equipe.permissoes no tenant inteiro. Provado ao vivo pelo
        // cenario 83-5 de verificar-autenticacao.cs antes desta correcao.
        //
        // A trava, quando existe, so entra em jogo perdendo a permissao
        // (mantemEquipePermissoes == false): trava com UPDLOCK+HOLDLOCK TODO
        // administrador ativo do tenant, alvo incluso, na MESMA ordem (ORDER
        // BY Id) em qualquer chamada concorrente — duas chamadas competindo
        // pelo mesmo conjunto na mesma ordem serializam por bloqueio comum,
        // sem depender de deadlock para se resolver. Quem entra primeiro ve o
        // estado de antes; quem espera ve o estado ja atualizado depois do
        // commit — nunca os dois veem "o outro ainda intacto" ao mesmo tempo.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!mantemEquipePermissoes)
        {
            var administradoresAtivos = await db.Database
                .SqlQuery<Guid>($"""
                    SELECT Id AS Value
                      FROM Atendentes WITH (UPDLOCK, HOLDLOCK)
                     WHERE TenantId = {tenantId}
                       AND Ativo = 1
                       AND (
                            (Permissoes IS NOT NULL AND Permissoes LIKE {marcadorDeEquipePermissoes})
                         OR (Permissoes IS NULL AND Perfil = {(byte)PerfilDeAtendente.Administrador})
                       )
                     ORDER BY Id
                    """)
                .ToListAsync(ct);

            if (!administradoresAtivos.Any(adminId => adminId != id))
            {
                throw UsuariosServiceException.UltimoAdministrador();
            }
        }

        var afetadas = await db.Atendentes
            .Where(a => a.Id == id && a.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Permissoes, textoNovo)
                .SetProperty(a => a.ModeloDePermissao, request.Modelo), ct);

        if (afetadas == 0)
        {
            throw UsuariosServiceException.AtendenteNaoEncontrado();
        }

        // (f) Registra o FATO — nunca as chaves. O estado atual ja esta na
        // propria coluna Permissoes; nao ha antes/depois auditavel aqui, so
        // "isto aconteceu, com quem e quando" (limitacao conhecida, ver README).
        //
        // (d) TokenVersion deliberadamente NAO sobe: ver o XML doc do metodo.
        await AutenticacaoService.RegistrarEventoAsync(
            db, ctx.TenantId, id, ctx.AtendenteId, atorExterno: null,
            TipoDeEventoDeSeguranca.PermissoesAlteradas, ip, motivo: null, ct);

        await tx.CommitAsync(ct);
    }

    private void GarantirPermissao(PermissaoDoSistema permissao)
    {
        if (!ctx.Pode(permissao))
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
            atendente.UltimoAcessoEm,
            atendente.MfaAtivo,
            atendente.ModeloDePermissao,
            CatalogoDePermissoes.ChavesEfetivas(atendente.Permissoes, atendente.Perfil));
    }
}
