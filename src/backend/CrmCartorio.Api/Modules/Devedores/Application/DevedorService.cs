using System.Text.RegularExpressions;
using CrmCartorio.Api.Domain.Common;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Application;
using CrmCartorio.Api.Modules.Atendimento.Domain;
using CrmCartorio.Api.Modules.Atendimento.Domain.Entities;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using CrmCartorio.Api.Modules.Clientes.Domain;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Clientes.Domain.Enums;
using CrmCartorio.Api.Modules.Devedores.Domain.Entities;
using CrmCartorio.Api.Modules.Devedores.Domain.Enums;
using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrmCartorio.Api.Modules.Devedores.Application;

public partial class DevedorService(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IConversaService conversas,
    IMensagemService mensagens,
    IGeradorDeProtocolo protocolos,
    INotificadorDeAtendimento notificador,
    IProvedorDeChaveDeDados provedorDeChave,
    IndiceCego indiceCego,
    IOptions<OpcoesDoCanalWhatsApp> opcoesDoCanal) : IDevedorService
{
    private readonly OpcoesDoCanalWhatsApp _opcoesDoCanal = opcoesDoCanal.Value;

    public async Task<ListagemDeDevedoresResponse> ListarAsync(
        FiltroDeDevedores filtro,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.DevedoresVer);

        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;
        var hoje = DateOnly.FromDateTime(DiaLocal.InicioDoDia(agora));
        var inicioMesUtc = new DateTime(hoje.Year, hoje.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var resumo = await CalcularResumoAsync(tenantId, hoje, inicioMesUtc, ct);

        var consulta = db.PendenciasFinanceiras.AsNoTracking()
            .Where(p => p.TenantId == tenantId);

        if (filtro.ClienteId is { } clienteId)
        {
            consulta = consulta.Where(p => p.ClienteId == clienteId);
        }

        if (filtro.TipoServicoId is { } tipoServicoId)
        {
            consulta = consulta.Where(p => p.TipoServicoId == tipoServicoId);
        }

        if (filtro.Status is { } statusExplicito)
        {
            consulta = consulta.Where(p => p.Status == statusExplicito);
        }
        else
        {
            var recorte = (filtro.Recorte ?? "abertas").Trim().ToLowerInvariant();
            consulta = recorte switch
            {
                "vencidas" => consulta.Where(p =>
                    p.Status == StatusPendencia.Aberta
                    && p.DataPromessaPagamento != null
                    && p.DataPromessaPagamento < hoje),

                "hoje" => consulta.Where(p =>
                    p.Status == StatusPendencia.Aberta
                    && p.DataPromessaPagamento == hoje),

                "sem_cobranca" => consulta.Where(p =>
                    p.Status == StatusPendencia.Aberta
                    && p.TotalCobrancas == 0),

                "parciais" => consulta.Where(p =>
                    p.Status == StatusPendencia.Aberta
                    && p.ValorPago > 0),

                "sem_valor" => consulta.Where(p =>
                    p.Status == StatusPendencia.Aberta
                    && p.ValorOriginal == null),

                "quitadas" => consulta.Where(p =>
                    p.Status == StatusPendencia.Quitada),

                "todas" => consulta.Where(p =>
                    p.Status != StatusPendencia.Cancelada),

                _ => consulta.Where(p =>
                    p.Status == StatusPendencia.Aberta),
            };
        }

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var busca = filtro.Busca.Trim();
            var soDigitos = SomenteDigitos(busca);

            consulta = consulta.Where(p =>
                p.Titulo.Contains(busca) ||
                db.Clientes.Any(c => c.Id == p.ClienteId && (
                    c.Nome.Contains(busca) ||
                    (soDigitos.Length > 0 && c.TelefoneWhatsapp.Contains(soDigitos)))));
        }

        var itensProjetados = await ProjetarAsync(consulta, hoje, ct);

        var ordenar = (filtro.Ordenar ?? "maior_valor").Trim().ToLowerInvariant();
        var lancamentosOrdenados = OrdenarLancamentos(itensProjetados, ordenar);
        var devedoresConsolidados = ConsolidarPorCliente(lancamentosOrdenados, ordenar, hoje);

        return new ListagemDeDevedoresResponse(
            resumo,
            devedoresConsolidados,
            lancamentosOrdenados,
            lancamentosOrdenados.Count);
    }

    public async Task<ResumoDeDividaDoClienteResponse> ObterPorClienteAsync(
        Guid clienteId,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.DevedoresVer);

        var tenantId = ctx.TenantId;
        var cliente = await db.Clientes.AsNoTracking()
            .Where(c => c.Id == clienteId && c.TenantId == tenantId)
            .Select(c => new { c.Id, c.Nome, c.TelefoneWhatsapp })
            .FirstOrDefaultAsync(ct)
            ?? throw DevedoresServiceException.ClienteNaoEncontrado();

        var hoje = DateOnly.FromDateTime(DiaLocal.InicioDoDia(DateTime.UtcNow));

        var pendencias = await ProjetarAsync(
            db.PendenciasFinanceiras.AsNoTracking()
                .Where(p => p.TenantId == tenantId
                         && p.ClienteId == clienteId
                         && p.Status != StatusPendencia.Cancelada)
                .OrderBy(p => p.Status)
                .ThenByDescending(p => p.CriadoEm),
            hoje,
            ct);

        var abertas = pendencias.Where(p => p.Status == StatusPendencia.Aberta).ToList();
        var saldo = abertas.Sum(p => p.SaldoRestante ?? 0m);

        return new ResumoDeDividaDoClienteResponse(
            cliente.Id,
            cliente.Nome,
            cliente.TelefoneWhatsapp,
            abertas.Count,
            saldo,
            pendencias);
    }

    public async Task<PendenciaFinanceiraResponse> ObterAsync(Guid id, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.DevedoresVer);

        var hoje = DateOnly.FromDateTime(DiaLocal.InicioDoDia(DateTime.UtcNow));
        var lista = await ProjetarAsync(
            db.PendenciasFinanceiras.AsNoTracking()
                .Where(p => p.Id == id && p.TenantId == ctx.TenantId),
            hoje,
            ct);

        return lista.FirstOrDefault() ?? throw DevedoresServiceException.PendenciaNaoEncontrada();
    }

    public async Task<PendenciaFinanceiraResponse> CriarAsync(
        CriarPendenciaFinanceiraRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.DevedoresEditar);

        var titulo = request.Titulo?.Trim();
        if (string.IsNullOrWhiteSpace(titulo))
        {
            throw DevedoresServiceException.TituloObrigatorio();
        }

        if (request.ValorOriginal is < 0)
        {
            throw DevedoresServiceException.ValorInvalido();
        }

        var clienteId = await ResolverOuCriarClienteAsync(
            request.ClienteId,
            request.NovoClienteNome,
            request.NovoClienteTelefone,
            request.NovoClienteDocumento,
            ct);

        var hoje = DateOnly.FromDateTime(DiaLocal.InicioDoDia(DateTime.UtcNow));

        var pendencia = new PendenciaFinanceira
        {
            TenantId = ctx.TenantId,
            ClienteId = clienteId,
            ConversaOrigemId = request.ConversaOrigemId,
            TipoServicoId = request.TipoServicoId,
            RegistradoPorAtendenteId = ctx.AtendenteId,
            Titulo = titulo.Length > 200 ? titulo[..200] : titulo,
            Observacoes = LimparTexto(request.Observacoes, 1000),
            ValorOriginal = request.ValorOriginal is { } v ? Math.Round(v, 2) : null,
            ValorPago = 0m,
            DataReferencia = request.DataReferencia ?? hoje,
            DataPromessaPagamento = request.DataPromessaPagamento,
            Status = StatusPendencia.Aberta,
        };

        db.PendenciasFinanceiras.Add(pendencia);
        await db.SaveChangesAsync(ct);

        await NotificarConversasAtivasDoClienteAsync(clienteId, ct);

        return await ObterAsync(pendencia.Id, ct);
    }

    public async Task<PendenciaFinanceiraResponse> AtualizarAsync(
        Guid id,
        AtualizarPendenciaFinanceiraRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.DevedoresEditar);

        var titulo = request.Titulo?.Trim();
        if (string.IsNullOrWhiteSpace(titulo))
        {
            throw DevedoresServiceException.TituloObrigatorio();
        }

        if (request.ValorOriginal is < 0)
        {
            throw DevedoresServiceException.ValorInvalido();
        }

        var pendencia = await db.PendenciasFinanceiras
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == ctx.TenantId, ct)
            ?? throw DevedoresServiceException.PendenciaNaoEncontrada();

        pendencia.TipoServicoId = request.TipoServicoId;
        pendencia.Titulo = titulo.Length > 200 ? titulo[..200] : titulo;
        pendencia.Observacoes = LimparTexto(request.Observacoes, 1000);
        pendencia.ValorOriginal = request.ValorOriginal is { } v ? Math.Round(v, 2) : null;
        pendencia.DataReferencia = request.DataReferencia;
        pendencia.DataPromessaPagamento = request.DataPromessaPagamento;

        if (pendencia.Status == StatusPendencia.Aberta
            && pendencia.ValorOriginal is { } vo
            && vo > 0
            && pendencia.ValorPago >= vo)
        {
            pendencia.Status = StatusPendencia.Quitada;
            pendencia.QuitadaEm = DateTime.UtcNow;
            pendencia.QuitadoPorAtendenteId = ctx.AtendenteId;
        }

        await db.SaveChangesAsync(ct);
        await NotificarConversasAtivasDoClienteAsync(pendencia.ClienteId, ct);

        return await ObterAsync(pendencia.Id, ct);
    }

    public async Task<PendenciaFinanceiraResponse> RegistrarPagamentoAsync(
        Guid id,
        RegistrarPagamentoRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.DevedoresEditar);

        var pendencia = await db.PendenciasFinanceiras
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == ctx.TenantId, ct)
            ?? throw DevedoresServiceException.PendenciaNaoEncontrada();

        if (pendencia.Status != StatusPendencia.Aberta)
        {
            throw DevedoresServiceException.PendenciaJaEncerrada();
        }

        var agora = DateTime.UtcNow;
        var saldoAtual = pendencia.ValorOriginal is { } vo
            ? Math.Max(0m, vo - pendencia.ValorPago)
            : (decimal?)null;

        decimal valorPagamento;
        if (request.Valor is { } informado && informado > 0)
        {
            valorPagamento = Math.Round(informado, 2);
        }
        else if (request.QuitarTotalmente && saldoAtual is > 0)
        {
            valorPagamento = saldoAtual.Value;
        }
        else if (request.QuitarTotalmente)
        {
            valorPagamento = 0m;
        }
        else
        {
            throw DevedoresServiceException.ValorDePagamentoInvalido();
        }

        if (valorPagamento > 0)
        {
            var pagamento = new PagamentoDePendencia
            {
                TenantId = ctx.TenantId,
                PendenciaFinanceiraId = pendencia.Id,
                RegistradoPorAtendenteId = ctx.AtendenteId,
                Valor = valorPagamento,
                MeioPagamento = request.MeioPagamento,
                Observacao = LimparTexto(request.Observacao, 500),
                PagoEm = agora,
            };

            db.PagamentosDePendencia.Add(pagamento);
            pendencia.ValorPago += valorPagamento;

            if (pendencia.ValorOriginal is null)
            {
                pendencia.ValorOriginal = pendencia.ValorPago;
            }
        }

        var atingiuValorTotal = pendencia.ValorOriginal is { } total && pendencia.ValorPago >= total;
        if (request.QuitarTotalmente || atingiuValorTotal)
        {
            pendencia.Status = StatusPendencia.Quitada;
            pendencia.QuitadaEm = agora;
            pendencia.QuitadoPorAtendenteId = ctx.AtendenteId;
        }

        await db.SaveChangesAsync(ct);

        if (request.EnviarComprovanteWhatsapp && !string.IsNullOrWhiteSpace(request.MensagemComprovante))
        {
            var (conversaId, _) = await ObterOuCriarConversaEmAtendimentoHumanoAsync(pendencia.ClienteId, ct);
            await mensagens.EnviarAsync(
                conversaId,
                new EnviarMensagemRequest(request.MensagemComprovante.Trim()),
                ct);
        }

        await NotificarConversasAtivasDoClienteAsync(pendencia.ClienteId, ct);

        return await ObterAsync(pendencia.Id, ct);
    }

    public async Task<PendenciaFinanceiraResponse> ReabrirAsync(Guid id, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.DevedoresEditar);

        var pendencia = await db.PendenciasFinanceiras
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == ctx.TenantId, ct)
            ?? throw DevedoresServiceException.PendenciaNaoEncontrada();

        pendencia.Status = StatusPendencia.Aberta;
        pendencia.QuitadaEm = null;
        pendencia.QuitadoPorAtendenteId = null;
        pendencia.CanceladaEm = null;

        await db.SaveChangesAsync(ct);
        await NotificarConversasAtivasDoClienteAsync(pendencia.ClienteId, ct);

        return await ObterAsync(pendencia.Id, ct);
    }

    public async Task CancelarAsync(Guid id, CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.DevedoresEditar);

        var pendencia = await db.PendenciasFinanceiras
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == ctx.TenantId, ct)
            ?? throw DevedoresServiceException.PendenciaNaoEncontrada();

        pendencia.Status = StatusPendencia.Cancelada;
        pendencia.CanceladaEm = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await NotificarConversasAtivasDoClienteAsync(pendencia.ClienteId, ct);
    }

    public async Task<CobrarNoWhatsappResponse> CobrarNoWhatsappAsync(
        CobrarNoWhatsappRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.DevedoresEditar);

        var texto = request.Mensagem?.Trim();
        if (string.IsNullOrWhiteSpace(texto))
        {
            throw DevedoresServiceException.MensagemDeCobrancaVazia();
        }

        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;

        var pendenciasQuery = db.PendenciasFinanceiras
            .Where(p => p.TenantId == tenantId
                     && p.ClienteId == request.ClienteId
                     && p.Status == StatusPendencia.Aberta);

        if (request.PendenciaIds is { Count: > 0 } idsEspecificos)
        {
            pendenciasQuery = pendenciasQuery.Where(p => idsEspecificos.Contains(p.Id));
        }

        var pendenciasAlvo = await pendenciasQuery.ToListAsync(ct);

        var (conversaId, _) = await ObterOuCriarConversaEmAtendimentoHumanoAsync(request.ClienteId, ct);
        var enviada = await mensagens.EnviarAsync(conversaId, new EnviarMensagemRequest(texto), ct);

        foreach (var pendencia in pendenciasAlvo)
        {
            pendencia.UltimaCobrancaEm = agora;
            pendencia.TotalCobrancas += 1;
        }

        await db.SaveChangesAsync(ct);

        return new CobrarNoWhatsappResponse(conversaId, enviada.Id, pendenciasAlvo.Count);
    }

    public async Task<AbrirConversaComDevedorResponse> AbrirConversaAsync(
        Guid clienteId,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.DevedoresVer);

        var (conversaId, criadaAgora) = await ObterOuCriarConversaEmAtendimentoHumanoAsync(clienteId, ct);
        return new AbrirConversaComDevedorResponse(conversaId, criadaAgora);
    }

    public async Task<ResultadoDeImportacaoDeDevedoresResponse> ImportarEmLoteAsync(
        ImportarDevedoresEmLoteRequest request,
        CancellationToken ct = default)
    {
        GarantirPermissao(PermissaoDoSistema.DevedoresEditar);

        var tenantId = ctx.TenantId;
        var hoje = DateOnly.FromDateTime(DiaLocal.InicioDoDia(DateTime.UtcNow));
        var tituloPadrao = string.IsNullOrWhiteSpace(request.TituloPadrao)
            ? "Pendência de emolumentos / serviço"
            : request.TituloPadrao.Trim();

        var pendenciasCriadas = 0;
        var clientesNovosCriados = 0;
        var clientesExistentesVinculados = 0;
        var ignorados = 0;
        var avisos = new List<string>();
        var clientesAfetados = new HashSet<Guid>();

        foreach (var item in request.Itens)
        {
            Guid? clienteId = null;

            if (item.ClienteId is { } idInformado)
            {
                var existe = await db.Clientes.AsNoTracking()
                    .AnyAsync(c => c.Id == idInformado && c.TenantId == tenantId, ct);

                if (existe)
                {
                    clienteId = idInformado;
                    clientesExistentesVinculados++;
                }
            }

            if (clienteId is null)
            {
                var telefoneNormalizado = TelefoneDeContato.ParaE164Basico(item.Telefone);
                if (telefoneNormalizado is null)
                {
                    ignorados++;
                    avisos.Add($"Contato \"{item.Nome ?? "(sem nome)"}\" ignorado: telefone inválido ({item.Telefone ?? "vazio"}).");
                    continue;
                }

                var existentePorTelefone = await db.Clientes
                    .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.TelefoneWhatsapp == telefoneNormalizado, ct);

                if (existentePorTelefone is not null)
                {
                    clienteId = existentePorTelefone.Id;
                    clientesExistentesVinculados++;
                }
                else
                {
                    var nomeLimpo = string.IsNullOrWhiteSpace(item.Nome)
                        ? telefoneNormalizado
                        : item.Nome.Trim();

                    var docDigitos = LimparDocumentoOpcional(item.Documento);

                    var novoCliente = new Cliente
                    {
                        TenantId = tenantId,
                        Nome = nomeLimpo.Length > 200 ? nomeLimpo[..200] : nomeLimpo,
                        TelefoneWhatsapp = telefoneNormalizado,
                        Documento = docDigitos,
                        DocumentoHash = docDigitos is not null && provedorDeChave.CifraAtiva
                            ? indiceCego.Calcular(tenantId, docDigitos)
                            : null,
                        Tipo = docDigitos?.Length == 14 ? TipoDePessoa.Juridica : TipoDePessoa.Fisica,
                        Ativo = true,
                        PrimeiroContatoEm = DateTime.UtcNow,
                        OrigemDoCadastro = OrigemDoCadastro.ImportacaoPlanilha,
                    };

                    db.Clientes.Add(novoCliente);
                    await db.SaveChangesAsync(ct);

                    clienteId = novoCliente.Id;
                    clientesNovosCriados++;
                }
            }

            var tituloItem = string.IsNullOrWhiteSpace(item.Titulo)
                ? tituloPadrao
                : item.Titulo.Trim();

            decimal? valor = item.ValorOriginal is >= 0 ? Math.Round(item.ValorOriginal.Value, 2) : null;

            var pendencia = new PendenciaFinanceira
            {
                TenantId = tenantId,
                ClienteId = clienteId.Value,
                TipoServicoId = request.TipoServicoPadraoId,
                RegistradoPorAtendenteId = ctx.AtendenteId,
                Titulo = tituloItem.Length > 200 ? tituloItem[..200] : tituloItem,
                Observacoes = LimparTexto(item.Observacoes, 1000),
                ValorOriginal = valor,
                ValorPago = 0m,
                DataReferencia = hoje,
                DataPromessaPagamento = item.DataPromessaPagamento ?? request.DataPromessaPadrao,
                Status = StatusPendencia.Aberta,
            };

            db.PendenciasFinanceiras.Add(pendencia);
            pendenciasCriadas++;
            clientesAfetados.Add(clienteId.Value);
        }

        if (pendenciasCriadas > 0)
        {
            await db.SaveChangesAsync(ct);

            foreach (var idAfetado in clientesAfetados)
            {
                await NotificarConversasAtivasDoClienteAsync(idAfetado, ct);
            }
        }

        return new ResultadoDeImportacaoDeDevedoresResponse(
            pendenciasCriadas,
            clientesNovosCriados,
            clientesExistentesVinculados,
            ignorados,
            avisos);
    }

    // -------------------------------------------------------------- auxiliares

    private async Task<ResumoDeDevedoresResponse> CalcularResumoAsync(
        Guid tenantId,
        DateOnly hoje,
        DateTime inicioMesUtc,
        CancellationToken ct)
    {
        var abertas = await db.PendenciasFinanceiras.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Status == StatusPendencia.Aberta)
            .Select(p => new
            {
                p.ClienteId,
                p.ValorOriginal,
                p.ValorPago,
                p.DataPromessaPagamento,
            })
            .ToListAsync(ct);

        var pagamentosMes = await db.PagamentosDePendencia.AsNoTracking()
            .Where(pg => pg.TenantId == tenantId && pg.PagoEm >= inicioMesUtc)
            .Select(pg => pg.Valor)
            .ToListAsync(ct);

        decimal totalEmAberto = 0m;
        var pendenciasSemValor = 0;
        decimal vencidasTotal = 0m;
        var vencidasQtd = 0;
        decimal hojeTotal = 0m;
        var hojeQtd = 0;

        foreach (var item in abertas)
        {
            var saldo = item.ValorOriginal is { } vo ? Math.Max(0m, vo - item.ValorPago) : 0m;
            totalEmAberto += saldo;

            if (item.ValorOriginal is null)
            {
                pendenciasSemValor++;
            }

            if (item.DataPromessaPagamento is { } promessa)
            {
                if (promessa < hoje)
                {
                    vencidasQtd++;
                    vencidasTotal += saldo;
                }
                else if (promessa == hoje)
                {
                    hojeQtd++;
                    hojeTotal += saldo;
                }
            }
        }

        var clientesDevedores = abertas.Select(a => a.ClienteId).Distinct().Count();

        return new ResumoDeDevedoresResponse(
            totalEmAberto,
            clientesDevedores,
            abertas.Count,
            pendenciasSemValor,
            vencidasTotal,
            vencidasQtd,
            hojeTotal,
            hojeQtd,
            pagamentosMes.Sum(),
            pagamentosMes.Count);
    }

    private async Task<List<PendenciaFinanceiraResponse>> ProjetarAsync(
        IQueryable<PendenciaFinanceira> query,
        DateOnly hoje,
        CancellationToken ct)
    {
        var linhas = await query
            .Select(p => new
            {
                Pendencia = p,
                ClienteNome = db.Clientes.Where(c => c.Id == p.ClienteId).Select(c => c.Nome).FirstOrDefault() ?? string.Empty,
                ClienteTelefone = db.Clientes.Where(c => c.Id == p.ClienteId).Select(c => c.TelefoneWhatsapp).FirstOrDefault() ?? string.Empty,
                ClienteDocumento = db.Clientes.Where(c => c.Id == p.ClienteId).Select(c => c.Documento).FirstOrDefault(),
                TipoServicoNome = db.TiposServico.Where(t => t.Id == p.TipoServicoId).Select(t => t.Nome).FirstOrDefault(),
                RegistradoPorNome = db.Atendentes.Where(a => a.Id == p.RegistradoPorAtendenteId).Select(a => a.Nome).FirstOrDefault(),
                QuitadoPorNome = db.Atendentes.Where(a => a.Id == p.QuitadoPorAtendenteId).Select(a => a.Nome).FirstOrDefault(),
                ConversaAtivaId = db.Conversas
                    .Where(c => c.TenantId == p.TenantId && c.ClienteId == p.ClienteId && c.Status == StatusConversa.Ativa)
                    .OrderByDescending(c => c.UltimaMensagemEm)
                    .Select(c => (Guid?)c.Id)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        if (linhas.Count == 0)
        {
            return [];
        }

        var ids = linhas.Select(l => l.Pendencia.Id).ToList();

        var pagamentos = await db.PagamentosDePendencia.AsNoTracking()
            .Where(pg => ids.Contains(pg.PendenciaFinanceiraId))
            .OrderByDescending(pg => pg.PagoEm)
            .Select(pg => new PagamentoDePendenciaResponse(
                pg.Id,
                pg.PendenciaFinanceiraId,
                pg.Valor,
                pg.MeioPagamento,
                pg.Observacao,
                pg.PagoEm,
                pg.RegistradoPorAtendenteId,
                db.Atendentes.Where(a => a.Id == pg.RegistradoPorAtendenteId).Select(a => a.Nome).FirstOrDefault()))
            .ToListAsync(ct);

        var pagamentosPorPendencia = pagamentos
            .GroupBy(pg => pg.PendenciaFinanceiraId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PagamentoDePendenciaResponse>)[.. g]);

        return [.. linhas.Select(l =>
        {
            var p = l.Pendencia;
            var saldoRestante = p.ValorOriginal is { } vo
                ? Math.Max(0m, vo - p.ValorPago)
                : (decimal?)null;

            var diasEmAberto = Math.Max(0, hoje.DayNumber - p.DataReferencia.DayNumber);
            int? diasDeAtraso = p.Status == StatusPendencia.Aberta
                && p.DataPromessaPagamento is { } promessa
                && promessa < hoje
                ? hoje.DayNumber - promessa.DayNumber
                : null;

            return new PendenciaFinanceiraResponse(
                p.Id,
                p.ClienteId,
                l.ClienteNome,
                l.ClienteTelefone,
                l.ClienteDocumento,
                $"/api/clientes/{p.ClienteId}/foto-perfil",
                p.ConversaOrigemId,
                l.ConversaAtivaId,
                p.TipoServicoId,
                l.TipoServicoNome,
                p.Titulo,
                p.Observacoes,
                p.ValorOriginal,
                p.ValorPago,
                saldoRestante,
                p.DataReferencia,
                p.DataPromessaPagamento,
                diasEmAberto,
                diasDeAtraso,
                p.Status,
                p.UltimaCobrancaEm,
                p.TotalCobrancas,
                p.RegistradoPorAtendenteId,
                l.RegistradoPorNome,
                p.QuitadaEm,
                p.QuitadoPorAtendenteId,
                l.QuitadoPorNome,
                p.CriadoEm,
                pagamentosPorPendencia.GetValueOrDefault(p.Id, []));
        })];
    }

    private static List<PendenciaFinanceiraResponse> OrdenarLancamentos(
        List<PendenciaFinanceiraResponse> itens,
        string ordenar) =>
        ordenar switch
        {
            "mais_antigas" => [.. itens.OrderBy(i => i.DataReferencia).ThenByDescending(i => i.SaldoRestante ?? 0m)],
            "promessa_proxima" => [.. itens
                .OrderBy(i => i.DataPromessaPagamento is null ? 1 : 0)
                .ThenBy(i => i.DataPromessaPagamento)
                .ThenByDescending(i => i.SaldoRestante ?? 0m)],
            "recentes" => [.. itens.OrderByDescending(i => i.CriadoEm)],
            "nome" => [.. itens.OrderBy(i => i.ClienteNome).ThenByDescending(i => i.CriadoEm)],
            _ => [.. itens.OrderByDescending(i => i.SaldoRestante ?? -1m).ThenBy(i => i.DataReferencia)],
        };

    private static List<DevedorConsolidadoResponse> ConsolidarPorCliente(
        List<PendenciaFinanceiraResponse> itens,
        string ordenar,
        DateOnly hoje)
    {
        var grupos = itens
            .GroupBy(i => i.ClienteId)
            .Select(g =>
            {
                var primeiro = g.First();
                var abertas = g.Where(p => p.Status == StatusPendencia.Aberta).ToList();
                var baseCalculo = abertas.Count > 0 ? abertas : [.. g];

                var saldoRestanteTotal = abertas.Sum(p => p.SaldoRestante ?? 0m);
                var valorOriginalTotal = baseCalculo.Sum(p => p.ValorOriginal ?? 0m);
                var valorPagoTotal = baseCalculo.Sum(p => p.ValorPago);
                var semValor = abertas.Count(p => p.ValorOriginal is null);

                var maisAntiga = baseCalculo.Min(p => p.DataReferencia);
                var proximaPromessa = abertas
                    .Where(p => p.DataPromessaPagamento is not null)
                    .Select(p => p.DataPromessaPagamento)
                    .Min();

                var temVencida = abertas.Any(p => p.DataPromessaPagamento is { } d && d < hoje);
                var temHoje = abertas.Any(p => p.DataPromessaPagamento == hoje);

                var ultimaCobranca = g
                    .Where(p => p.UltimaCobrancaEm is not null)
                    .Select(p => p.UltimaCobrancaEm)
                    .Max();

                var totalCobrancas = g.Sum(p => p.TotalCobrancas);

                return new DevedorConsolidadoResponse(
                    primeiro.ClienteId,
                    primeiro.ClienteNome,
                    primeiro.ClienteTelefone,
                    primeiro.ClienteDocumento,
                    primeiro.ClienteFotoPerfilUrl,
                    primeiro.ConversaAtivaId,
                    abertas.Count,
                    semValor,
                    valorOriginalTotal,
                    valorPagoTotal,
                    saldoRestanteTotal,
                    maisAntiga,
                    proximaPromessa,
                    temVencida,
                    temHoje,
                    ultimaCobranca,
                    totalCobrancas,
                    [.. g]);
            });

        return ordenar switch
        {
            "mais_antigas" => [.. grupos.OrderBy(d => d.DataReferenciaMaisAntiga).ThenByDescending(d => d.SaldoRestanteTotal)],
            "promessa_proxima" => [.. grupos
                .OrderBy(d => d.ProximaPromessaPagamento is null ? 1 : 0)
                .ThenBy(d => d.ProximaPromessaPagamento)
                .ThenByDescending(d => d.SaldoRestanteTotal)],
            "recentes" => [.. grupos.OrderByDescending(d => d.Pendencias.Max(p => p.CriadoEm))],
            "nome" => [.. grupos.OrderBy(d => d.ClienteNome)],
            _ => [.. grupos
                .OrderByDescending(d => d.TemPromessaVencida ? 1 : 0)
                .ThenByDescending(d => d.SaldoRestanteTotal)
                .ThenBy(d => d.DataReferenciaMaisAntiga)],
        };
    }

    private async Task<Guid> ResolverOuCriarClienteAsync(
        Guid? clienteId,
        string? novoNome,
        string? novoTelefone,
        string? novoDocumento,
        CancellationToken ct)
    {
        var tenantId = ctx.TenantId;

        if (clienteId is { } idExistente)
        {
            var existe = await db.Clientes.AsNoTracking()
                .AnyAsync(c => c.Id == idExistente && c.TenantId == tenantId, ct);

            if (!existe)
            {
                throw DevedoresServiceException.ClienteNaoEncontrado();
            }

            return idExistente;
        }

        var nomeLimpo = novoNome?.Trim();
        if (string.IsNullOrWhiteSpace(nomeLimpo))
        {
            throw DevedoresServiceException.ClienteObrigatorio();
        }

        var telefoneE164 = TelefoneDeContato.ParaE164Basico(novoTelefone)
            ?? throw DevedoresServiceException.TelefoneInvalido();

        var jaCadastrado = await db.Clientes
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.TelefoneWhatsapp == telefoneE164, ct);

        if (jaCadastrado is not null)
        {
            return jaCadastrado.Id;
        }

        var docDigitos = LimparDocumentoOpcional(novoDocumento);

        var cliente = new Cliente
        {
            TenantId = tenantId,
            Nome = nomeLimpo.Length > 200 ? nomeLimpo[..200] : nomeLimpo,
            TelefoneWhatsapp = telefoneE164,
            Documento = docDigitos,
            DocumentoHash = docDigitos is not null && provedorDeChave.CifraAtiva
                ? indiceCego.Calcular(tenantId, docDigitos)
                : null,
            Tipo = docDigitos?.Length == 14 ? TipoDePessoa.Juridica : TipoDePessoa.Fisica,
            Ativo = true,
            PrimeiroContatoEm = DateTime.UtcNow,
            OrigemDoCadastro = OrigemDoCadastro.Manual,
        };

        db.Clientes.Add(cliente);
        await db.SaveChangesAsync(ct);

        return cliente.Id;
    }

    /// <summary>
    /// Resolve a conversa ativa do cliente ou abre uma nova já em atendimento
    /// humano (<see cref="Conversa.EmAtendimentoHumano"/> = <c>true</c>), de modo
    /// que qualquer resposta do cliente no WhatsApp não acione o robô de triagem
    /// nem a IA no meio de uma tratativa financeira.
    /// </summary>
    private async Task<(Guid ConversaId, bool CriadaAgora)> ObterOuCriarConversaEmAtendimentoHumanoAsync(
        Guid clienteId,
        CancellationToken ct)
    {
        var tenantId = ctx.TenantId;
        var agora = DateTime.UtcNow;

        var conversaAtiva = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                     && c.ClienteId == clienteId
                     && c.Status == StatusConversa.Ativa)
            .OrderByDescending(c => c.Canal == CanalOrigem.WhatsApp ? 1 : 0)
            .ThenByDescending(c => c.UltimaMensagemEm)
            .Select(c => new
            {
                c.Id,
                c.AtendenteId,
                c.EmAtendimentoHumano,
                c.AguardandoTriagem,
                c.AguardandoEscolhaDeServico,
                c.AguardandoRespostaInteligente,
                c.AguardandoNome,
            })
            .FirstOrDefaultAsync(ct);

        if (conversaAtiva is not null)
        {
            if (conversaAtiva.AtendenteId != ctx.AtendenteId
                || !conversaAtiva.EmAtendimentoHumano
                || conversaAtiva.AguardandoTriagem
                || conversaAtiva.AguardandoEscolhaDeServico
                || conversaAtiva.AguardandoRespostaInteligente
                || conversaAtiva.AguardandoNome)
            {
                await db.Conversas
                    .Where(c => c.Id == conversaAtiva.Id && c.TenantId == tenantId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.AtendenteId, ctx.AtendenteId)
                        .SetProperty(c => c.EmAtendimentoHumano, true)
                        .SetProperty(c => c.AguardandoTriagem, false)
                        .SetProperty(c => c.AguardandoEscolhaDeServico, false)
                        .SetProperty(c => c.AguardandoRespostaInteligente, false)
                        .SetProperty(c => c.AguardandoNome, false)
                        .SetProperty(c => c.AtendidaPeloAparelhoEm, (DateTime?)null)
                        .SetProperty(c => c.AtualizadoEm, agora), ct);

                var rastreada = db.ChangeTracker.Entries<Conversa>()
                    .FirstOrDefault(e => e.Entity.Id == conversaAtiva.Id);

                if (rastreada is not null)
                {
                    await rastreada.ReloadAsync(ct);
                }

                var resumoExistente = await conversas.ObterResumoAsync(conversaAtiva.Id, ct);
                await notificador.ConversaAtualizadaAsync(tenantId, resumoExistente, ct);
            }

            return (conversaAtiva.Id, false);
        }

        var clienteExiste = await db.Clientes.AsNoTracking()
            .AnyAsync(c => c.Id == clienteId && c.TenantId == tenantId, ct);

        if (!clienteExiste)
        {
            throw DevedoresServiceException.ClienteNaoEncontrado();
        }

        var jaTeveWhatsapp = await db.Conversas.AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId
                        && c.ClienteId == clienteId
                        && c.Canal == CanalOrigem.WhatsApp, ct);

        var canal = _opcoesDoCanal.EstaConfigurado || jaTeveWhatsapp
            ? CanalOrigem.WhatsApp
            : CanalOrigem.Simulado;

        var primeiraEtapaId = await db.EtapasFunil.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Ativo)
            .OrderBy(e => e.Ordem)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

        var protocolo = await protocolos.GerarAsync(tenantId, agora, ct);

        var novaConversa = new Conversa
        {
            TenantId = tenantId,
            ClienteId = clienteId,
            Protocolo = protocolo,
            AtendenteId = ctx.AtendenteId,
            EtapaId = primeiraEtapaId,
            Status = StatusConversa.Ativa,
            Canal = canal,
            EmAtendimentoHumano = true,
            AguardandoTriagem = false,
            IniciadaEm = agora,
            UltimaMensagemEm = agora,
        };

        db.Conversas.Add(novaConversa);
        db.Mensagens.Add(new Mensagem
        {
            TenantId = tenantId,
            ConversaId = novaConversa.Id,
            AutorTipo = AutorTipo.Sistema,
            Conteudo = $"Atendimento aberto por {ctx.Nome} a partir de Devedores.",
            Tipo = TipoMensagem.Texto,
            StatusEntrega = StatusEntrega.Entregue,
            EnviadaEm = agora,
            LidaEm = agora,
        });

        await db.SaveChangesAsync(ct);

        var resumo = await conversas.ObterResumoAsync(novaConversa.Id, ct);
        await notificador.ConversaAtualizadaAsync(tenantId, resumo, ct);

        return (novaConversa.Id, true);
    }

    private async Task NotificarConversasAtivasDoClienteAsync(Guid clienteId, CancellationToken ct)
    {
        var idsAtivos = await db.Conversas.AsNoTracking()
            .Where(c => c.TenantId == ctx.TenantId
                     && c.ClienteId == clienteId
                     && c.Status == StatusConversa.Ativa)
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (var conversaId in idsAtivos)
        {
            var resumo = await conversas.ObterResumoAsync(conversaId, ct);
            await notificador.ConversaAtualizadaAsync(ctx.TenantId, resumo, ct);
        }
    }

    private void GarantirPermissao(PermissaoDoSistema permissao)
    {
        if (!ctx.Pode(permissao))
        {
            throw DevedoresServiceException.SemPermissao();
        }
    }

    private static string? LimparTexto(string? valor, int limite)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var limpo = valor.Trim();
        return limpo.Length > limite ? limpo[..limite] : limpo;
    }

    private static string? LimparDocumentoOpcional(string? doc)
    {
        if (string.IsNullOrWhiteSpace(doc))
        {
            return null;
        }

        var digitos = SomenteDigitos(doc);
        return digitos.Length is 11 or 14 ? digitos : null;
    }

    private static string SomenteDigitos(string valor) => RegexDeDigitos().Replace(valor, string.Empty);

    [GeneratedRegex(@"\D")]
    private static partial Regex RegexDeDigitos();
}
