using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Configuracoes.Domain;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Entities;
using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmCartorio.Api.Modules.Configuracoes.Application;

public class MensagemAutomaticaService(CrmDbContext db, IContextoDoUsuario ctx) : IMensagemAutomaticaService
{
    public async Task<IReadOnlyList<MensagemAutomaticaResponse>> ListarAsync(
        bool incluirInativas,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        var consulta = db.MensagensAutomaticas.AsNoTracking().Where(m => m.TenantId == tenantId);

        if (!incluirInativas)
        {
            consulta = consulta.Where(m => m.Ativo);
        }

        var itens = await consulta
            .OrderBy(m => m.Gatilho)
            .ThenBy(m => m.CriadoEm)
            .Select(m => new
            {
                m.Id,
                m.Gatilho,
                m.EtapaAlvoId,
                EtapaAlvoNome = db.EtapasFunil
                    .Where(e => e.Id == m.EtapaAlvoId)
                    .Select(e => e.Nome)
                    .FirstOrDefault(),
                m.Conteudo,
                m.Ativo
            })
            .ToListAsync(ct);

        // O rotulo do gatilho e resolvido na memoria: um switch de enum nao traduz
        // para SQL, e mapear isso em CASE WHEN por texto seria espalhar vocabulario
        // de interface dentro da consulta.
        return [.. itens.Select(m => new MensagemAutomaticaResponse(
            m.Id,
            m.Gatilho,
            DescreverGatilho(m.Gatilho),
            m.EtapaAlvoId,
            m.EtapaAlvoNome,
            m.Conteudo,
            m.Ativo))];
    }

    public async Task<MensagemAutomaticaResponse> CriarAsync(
        SalvarMensagemAutomaticaRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var conteudo = ValidarConteudo(request.Conteudo);
        var gatilho = ValidarGatilho(request.Gatilho);
        var etapaAlvoId = await ValidarEtapaAlvoAsync(gatilho, request.EtapaAlvoId, ct);

        var mensagem = new MensagemAutomatica
        {
            TenantId = ctx.TenantId,
            Gatilho = gatilho,
            EtapaAlvoId = etapaAlvoId,
            Conteudo = conteudo,
            Ativo = request.Ativo
        };

        db.MensagensAutomaticas.Add(mensagem);
        await db.SaveChangesAsync(ct);

        return await MapearAsync(mensagem, ct);
    }

    public async Task<MensagemAutomaticaResponse> AtualizarAsync(
        Guid id,
        SalvarMensagemAutomaticaRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao();

        var mensagem = await db.MensagensAutomaticas
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == ctx.TenantId, ct)
            ?? throw ConfiguracoesServiceException.MensagemAutomaticaNaoEncontrada();

        var conteudo = ValidarConteudo(request.Conteudo);
        var gatilho = ValidarGatilho(request.Gatilho);
        var etapaAlvoId = await ValidarEtapaAlvoAsync(gatilho, request.EtapaAlvoId, ct);

        mensagem.Gatilho = gatilho;
        mensagem.EtapaAlvoId = etapaAlvoId;
        mensagem.Conteudo = conteudo;

        // Desativar para de disparar de agora em diante. As mensagens que esta regra
        // ja colocou no fio das conversas continuam la — mensagem nao e apagada nem
        // editada em nenhum caminho do sistema (RNF-008).
        mensagem.Ativo = request.Ativo;

        await db.SaveChangesAsync(ct);

        return await MapearAsync(mensagem, ct);
    }

    private void GarantirPermissao()
    {
        if (!ctx.Pode(PermissaoDoSistema.AutomacaoMensagensEditar))
        {
            throw ConfiguracoesServiceException.SemPermissao();
        }
    }

    private static GatilhoDeMensagem ValidarGatilho(GatilhoDeMensagem gatilho) =>
        Enum.IsDefined(gatilho)
            ? gatilho
            : throw ConfiguracoesServiceException.GatilhoInvalido();

    private static string ValidarConteudo(string? conteudo)
    {
        var limpo = conteudo?.Trim();

        if (string.IsNullOrWhiteSpace(limpo))
        {
            throw ConfiguracoesServiceException.ConteudoObrigatorio();
        }

        var desconhecidos = PlaceholdersDeMensagem.MarcadoresDesconhecidos(limpo);

        return desconhecidos.Count > 0
            ? throw ConfiguracoesServiceException.MarcadorDesconhecido(desconhecidos)
            : limpo;
    }

    /// <summary>
    /// Etapa alvo obrigatoria no gatilho de etapa, e descartada em todos os outros.
    /// </summary>
    /// <remarks>
    /// Zerar o campo fora do gatilho de etapa, em vez de recusar, evita erro por algo
    /// que o usuario nao fez: a tela guarda a etapa escolhida antes e ele pode trocar
    /// o gatilho depois. Guardar o valor orfao deixaria a tabela dizendo que a regra
    /// depende de uma etapa que ela ignora.
    /// </remarks>
    private async Task<Guid?> ValidarEtapaAlvoAsync(
        GatilhoDeMensagem gatilho,
        Guid? etapaAlvoId,
        CancellationToken ct)
    {
        if (gatilho != GatilhoDeMensagem.EtapaAlterada)
        {
            return null;
        }

        if (etapaAlvoId is not { } id)
        {
            throw ConfiguracoesServiceException.EtapaAlvoObrigatoria();
        }

        var existe = await db.EtapasFunil.AsNoTracking()
            .AnyAsync(e => e.Id == id && e.TenantId == ctx.TenantId, ct);

        return existe ? id : throw ConfiguracoesServiceException.EtapaNaoEncontrada();
    }

    private async Task<MensagemAutomaticaResponse> MapearAsync(
        MensagemAutomatica mensagem,
        CancellationToken ct)
    {
        var etapaNome = mensagem.EtapaAlvoId is { } etapaId
            ? await db.EtapasFunil.AsNoTracking()
                .Where(e => e.Id == etapaId)
                .Select(e => e.Nome)
                .FirstOrDefaultAsync(ct)
            : null;

        return new MensagemAutomaticaResponse(
            mensagem.Id,
            mensagem.Gatilho,
            DescreverGatilho(mensagem.Gatilho),
            mensagem.EtapaAlvoId,
            etapaNome,
            mensagem.Conteudo,
            mensagem.Ativo);
    }

    /// <summary>
    /// Rotulo do gatilho no vocabulario de quem opera o cartorio, nao no do enum.
    /// </summary>
    private static string DescreverGatilho(GatilhoDeMensagem gatilho) => gatilho switch
    {
        GatilhoDeMensagem.NovoAtendimento => "Primeiro contato do cliente",
        GatilhoDeMensagem.EtapaAlterada => "Atendimento muda de etapa",
        GatilhoDeMensagem.PrazoProximoVencimento => "Prazo perto de vencer",
        GatilhoDeMensagem.PrazoVencido => "Prazo vencido",
        GatilhoDeMensagem.TriagemSemCorrespondencia => "Cliente nao escolheu setor no menu",
        GatilhoDeMensagem.RespostaSemCorrespondencia => "IA sem resposta especifica (cliente nao pediu atendente)",
        GatilhoDeMensagem.EncerramentoPeloAtendente => "Atendente encerrou o atendimento",
        GatilhoDeMensagem.PerguntaDeDuvida => "Depois da orientação do serviço: ficou alguma dúvida?",
        GatilhoDeMensagem.ContatoSemRobo => "Contato sem robô escreveu: recebemos, já vamos responder",
        _ => gatilho.ToString()
    };
}
