using System.Text.RegularExpressions;
using CrmCartorio.Api.Infrastructure.Persistence;
using CrmCartorio.Api.Infrastructure.Security;
using CrmCartorio.Api.Modules.Atendimento.Domain.Enums;
using CrmCartorio.Api.Modules.CanalWhatsApp.Application;
using CrmCartorio.Api.Modules.Clientes.Domain.Entities;
using CrmCartorio.Api.Modules.Clientes.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CrmCartorio.Api.Modules.Clientes.Application;

public partial class ClienteService(
    CrmDbContext db,
    IContextoDoUsuario ctx,
    IEvolutionClient gateway,
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    ILogger<ClienteService> log) : IClienteService
{
    public async Task<ResultadoPaginadoDeClientes> ListarAsync(
        FiltroDeClientes filtro,
        CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;

        var consulta = db.Clientes.AsNoTracking().Where(c => c.TenantId == tenantId);

        if (!filtro.IncluirInativos)
        {
            consulta = consulta.Where(c => c.Ativo);
        }

        if (filtro.Tipo is { } tipo)
        {
            consulta = consulta.Where(c => c.Tipo == tipo);
        }

        if (filtro.EstadoCivil is { } estadoCivil)
        {
            consulta = consulta.Where(c => c.EstadoCivil == estadoCivil);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Cidade))
        {
            var cidade = filtro.Cidade.Trim();
            consulta = consulta.Where(c => c.Cidade == cidade);
        }

        if (filtro.SemDocumento)
        {
            consulta = consulta.Where(c => c.Documento == null || c.Documento == string.Empty);
        }

        if (filtro.SemEndereco)
        {
            // "Sem endereco" e o que trava a lavratura: sem logradouro, numero,
            // cidade ou UF nao da para qualificar a parte no ato.
            consulta = consulta.Where(c =>
                c.Logradouro == null || c.Logradouro == string.Empty ||
                c.Numero == null || c.Numero == string.Empty ||
                c.Cidade == null || c.Cidade == string.Empty ||
                c.Uf == null || c.Uf == string.Empty);
        }

        if (filtro.ComConversaAtiva)
        {
            consulta = consulta.Where(c =>
                db.Conversas.Any(x => x.ClienteId == c.Id && x.Status == StatusConversa.Ativa));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var busca = filtro.Busca.Trim();
            var soDigitos = SomenteDigitos(busca);

            consulta = consulta.Where(c =>
                c.Nome.Contains(busca) ||
                (c.RazaoSocial != null && c.RazaoSocial.Contains(busca)) ||
                (soDigitos.Length > 0 && c.TelefoneWhatsapp.Contains(soDigitos)) ||
                (soDigitos.Length > 0 && c.Documento != null && c.Documento.Contains(soDigitos)));
        }

        var total = await consulta.CountAsync(ct);

        var pagina = filtro.Pagina < 1 ? 1 : filtro.Pagina;
        var tamanho = Math.Clamp(filtro.Tamanho, 1, 200);

        // A ordenacao por qualificacao usa o mesmo criterio do filtro "sem
        // endereco": documento e endereco sao o que falta com mais frequencia, e
        // sao o que impede o ato de andar. Ordenar pelo percentual exato exigiria
        // calcular em SQL o que a entidade calcula em memoria.
        consulta = filtro.Ordenar switch
        {
            "recentes" => consulta.OrderByDescending(c => c.PrimeiroContatoEm),

            "qualificacao" => consulta
                .OrderBy(c => c.Documento == null ? 0 : 1)
                .ThenBy(c => c.Logradouro == null ? 0 : 1)
                .ThenBy(c => c.Nome),

            _ => consulta.OrderBy(c => c.Nome)
        };

        var pagina_ = await consulta
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync(ct);

        var ids = pagina_.Select(c => c.Id).ToList();

        var contagens = await db.Conversas.AsNoTracking()
            .Where(x => ids.Contains(x.ClienteId))
            .GroupBy(x => x.ClienteId)
            .Select(g => new
            {
                ClienteId = g.Key,
                Total = g.Count(),
                Ativas = g.Count(x => x.Status == StatusConversa.Ativa)
            })
            .ToListAsync(ct);

        var itens = pagina_
            .Select(c =>
            {
                var contagem = contagens.FirstOrDefault(x => x.ClienteId == c.Id);
                return Mapear(c, contagem?.Total ?? 0, (contagem?.Ativas ?? 0) > 0);
            })
            .ToList();

        return new ResultadoPaginadoDeClientes(itens, total, pagina, tamanho);
    }

    public async Task<ClienteResponse> ObterAsync(Guid id, CancellationToken ct = default)
    {
        var cliente = await db.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == ctx.TenantId, ct)
            ?? throw ClientesServiceException.NaoEncontrado();

        var total = await db.Conversas.CountAsync(x => x.ClienteId == id, ct);
        var ativa = await db.Conversas.AnyAsync(
            x => x.ClienteId == id && x.Status == StatusConversa.Ativa, ct);

        return Mapear(cliente, total, ativa);
    }

    public async Task<ClienteResponse> CriarAsync(DadosDeCliente dados, CancellationToken ct = default)
    {
        var limpos = Validar(dados);

        await GarantirTelefoneLivreAsync(limpos.TelefoneWhatsapp, null, ct);

        var cliente = new Cliente
        {
            TenantId = ctx.TenantId,
            Nome = limpos.Nome,
            TelefoneWhatsapp = limpos.TelefoneWhatsapp,
            PrimeiroContatoEm = DateTime.UtcNow,
            Ativo = true
        };

        Aplicar(cliente, limpos);

        db.Clientes.Add(cliente);
        await db.SaveChangesAsync(ct);

        return await ObterAsync(cliente.Id, ct);
    }

    public async Task<ClienteResponse> AtualizarAsync(
        Guid id,
        DadosDeCliente dados,
        CancellationToken ct = default)
    {
        var cliente = await db.Clientes
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == ctx.TenantId, ct)
            ?? throw ClientesServiceException.NaoEncontrado();

        var limpos = Validar(dados);

        await GarantirTelefoneLivreAsync(limpos.TelefoneWhatsapp, id, ct);

        Aplicar(cliente, limpos);
        cliente.Ativo = limpos.Ativo;

        await db.SaveChangesAsync(ct);

        return await ObterAsync(cliente.Id, ct);
    }

    public async Task<OpcoesDeClientesResponse> ObterOpcoesAsync(CancellationToken ct = default)
    {
        var cidades = await db.Clientes.AsNoTracking()
            .Where(c => c.TenantId == ctx.TenantId && c.Cidade != null && c.Cidade != string.Empty)
            .Select(c => c.Cidade!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

        return new OpcoesDeClientesResponse(cidades);
    }

    public async Task<byte[]?> ObterFotoPerfilAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = ctx.TenantId;
        var cacheKey = $"foto_cliente_{tenantId}_{id}";

        if (cache.TryGetValue(cacheKey, out byte[]? cachedBytes))
        {
            return cachedBytes;
        }

        var cliente = await db.Clientes
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);

        if (cliente is null)
        {
            return null;
        }

        var agora = DateTime.UtcNow;
        var url = cliente.FotoPerfilUrl;

        // Se nunca consultamos ou expirou (24h), tenta renovar junto a Evolution API
        if (string.IsNullOrWhiteSpace(url) || cliente.FotoPerfilAtualizadaEm is null || cliente.FotoPerfilAtualizadaEm < agora.AddHours(-24))
        {
            var novaUrl = await gateway.ObterFotoPerfilUrlAsync(cliente.TelefoneWhatsapp, ct);
            if (!string.IsNullOrWhiteSpace(novaUrl) && novaUrl != cliente.FotoPerfilUrl)
            {
                cliente.FotoPerfilUrl = novaUrl;
                cliente.FotoPerfilAtualizadaEm = agora;
                cliente.AtualizadoEm = agora;
                await db.SaveChangesAsync(ct);
                url = novaUrl;
            }
            else if (cliente.FotoPerfilAtualizadaEm is null || cliente.FotoPerfilAtualizadaEm < agora.AddHours(-24))
            {
                cliente.FotoPerfilAtualizadaEm = agora;
                await db.SaveChangesAsync(ct);
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        // Baixa a imagem do CDN do WhatsApp de forma defensiva
        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            using var resposta = await http.GetAsync(url, ct);

            if (resposta.IsSuccessStatusCode)
            {
                var bytes = await resposta.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length > 0)
                {
                    // Cache em memoria por 1 hora
                    cache.Set(cacheKey, bytes, TimeSpan.FromHours(1));
                    return bytes;
                }
            }
            else if (resposta.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
            {
                // URL expirou no CDN do WhatsApp. Tenta consultar a Evolution API mais uma vez
                var urlFresca = await gateway.ObterFotoPerfilUrlAsync(cliente.TelefoneWhatsapp, ct);
                if (!string.IsNullOrWhiteSpace(urlFresca) && urlFresca != url)
                {
                    cliente.FotoPerfilUrl = urlFresca;
                    cliente.FotoPerfilAtualizadaEm = agora;
                    cliente.AtualizadoEm = agora;
                    await db.SaveChangesAsync(ct);

                    using var resposta2 = await http.GetAsync(urlFresca, ct);
                    if (resposta2.IsSuccessStatusCode)
                    {
                        var bytes2 = await resposta2.Content.ReadAsByteArrayAsync(ct);
                        if (bytes2.Length > 0)
                        {
                            cache.Set(cacheKey, bytes2, TimeSpan.FromHours(1));
                            return bytes2;
                        }
                    }
                }
            }
        }
        catch (Exception falha) when (falha is not OperationCanceledException)
        {
            log.LogDebug(falha, "Nao foi possivel baixar foto de perfil do CDN para o cliente {Id}.", id);
        }

        return null;
    }

    // ------------------------------------------------------------- auxiliares

    private static void Aplicar(Cliente cliente, DadosDeCliente d)
    {
        cliente.Nome = d.Nome;
        cliente.Tipo = d.Tipo;
        cliente.TelefoneWhatsapp = d.TelefoneWhatsapp;
        cliente.TelefoneAlternativo = d.TelefoneAlternativo;
        cliente.Email = d.Email;
        cliente.Documento = d.Documento;

        cliente.Rg = d.Rg;
        cliente.OrgaoEmissor = d.OrgaoEmissor;
        cliente.UfOrgaoEmissor = d.UfOrgaoEmissor;
        cliente.DataNascimento = d.DataNascimento;
        cliente.EstadoCivil = d.EstadoCivil;

        // Regime de bens so faz sentido em casamento. Guardar regime para solteiro
        // deixaria lixo que apareceria na qualificacao de um ato futuro.
        cliente.RegimeDeBens = d.EstadoCivil == EstadoCivil.Casado
            ? d.RegimeDeBens
            : RegimeDeBens.NaoInformado;

        cliente.Profissao = d.Profissao;
        cliente.Nacionalidade = d.Nacionalidade;
        cliente.Naturalidade = d.Naturalidade;
        cliente.NomeDoPai = d.NomeDoPai;
        cliente.NomeDaMae = d.NomeDaMae;

        cliente.RazaoSocial = d.RazaoSocial;
        cliente.RepresentanteLegal = d.RepresentanteLegal;

        cliente.Cep = d.Cep;
        cliente.Logradouro = d.Logradouro;
        cliente.Numero = d.Numero;
        cliente.Complemento = d.Complemento;
        cliente.Bairro = d.Bairro;
        cliente.Cidade = d.Cidade;
        cliente.Uf = d.Uf;

        cliente.Observacoes = d.Observacoes;
    }

    /// <summary>
    /// Normaliza e valida, devolvendo o mesmo record com os campos limpos.
    /// </summary>
    private static DadosDeCliente Validar(DadosDeCliente d) => d with
    {
        Nome = ValidarNome(d.Nome),
        TelefoneWhatsapp = ValidarTelefone(d.TelefoneWhatsapp),
        TelefoneAlternativo = LimparTelefoneOpcional(d.TelefoneAlternativo),
        Email = Limpar(d.Email),
        Documento = ValidarDocumento(d.Documento, d.Tipo),
        Rg = Limpar(d.Rg),
        OrgaoEmissor = Limpar(d.OrgaoEmissor),
        UfOrgaoEmissor = ValidarUf(d.UfOrgaoEmissor),
        DataNascimento = ValidarNascimento(d.DataNascimento),
        Profissao = Limpar(d.Profissao),
        Nacionalidade = Limpar(d.Nacionalidade),
        Naturalidade = Limpar(d.Naturalidade),
        NomeDoPai = Limpar(d.NomeDoPai),
        NomeDaMae = Limpar(d.NomeDaMae),
        RazaoSocial = Limpar(d.RazaoSocial),
        RepresentanteLegal = Limpar(d.RepresentanteLegal),
        Cep = ValidarCep(d.Cep),
        Logradouro = Limpar(d.Logradouro),
        Numero = Limpar(d.Numero),
        Complemento = Limpar(d.Complemento),
        Bairro = Limpar(d.Bairro),
        Cidade = Limpar(d.Cidade),
        Uf = ValidarUf(d.Uf),
        Observacoes = Limpar(d.Observacoes)
    };

    private async Task GarantirTelefoneLivreAsync(string telefone, Guid? ignorarId, CancellationToken ct)
    {
        var conflito = await db.Clientes.AsNoTracking()
            .Where(c => c.TenantId == ctx.TenantId
                     && c.TelefoneWhatsapp == telefone
                     && (ignorarId == null || c.Id != ignorarId))
            .Select(c => c.Nome)
            .FirstOrDefaultAsync(ct);

        if (conflito is not null)
        {
            throw ClientesServiceException.TelefoneEmUso(conflito);
        }
    }

    private static string ValidarNome(string? nome)
    {
        var limpo = nome?.Trim();

        return string.IsNullOrWhiteSpace(limpo)
            ? throw ClientesServiceException.NomeObrigatorio()
            : limpo;
    }

    /// <summary>
    /// Normaliza para E.164 sem simbolos. Numero brasileiro digitado sem o codigo
    /// do pais recebe o 55 automaticamente — no balcao ninguem digita "+55".
    /// </summary>
    private static string ValidarTelefone(string? telefone)
    {
        var digitos = SomenteDigitos(telefone ?? string.Empty);

        if (digitos.Length is 10 or 11)
        {
            digitos = "55" + digitos;
        }

        if (digitos.Length is < 12 or > 15)
        {
            throw ClientesServiceException.TelefoneInvalido();
        }

        return digitos;
    }

    private static string? LimparTelefoneOpcional(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone))
        {
            return null;
        }

        var digitos = SomenteDigitos(telefone);

        if (digitos.Length is 10 or 11)
        {
            digitos = "55" + digitos;
        }

        return digitos.Length is < 12 or > 15
            ? throw ClientesServiceException.TelefoneInvalido()
            : digitos;
    }

    private static string? ValidarDocumento(string? documento, TipoDePessoa tipo)
    {
        if (string.IsNullOrWhiteSpace(documento))
        {
            return null;
        }

        var digitos = SomenteDigitos(documento);

        // Nao validamos digito verificador: no balcao aparece documento antigo e
        // documento de terceiro, e recusar por conta disso travaria atendimento.
        // O que se valida e o tamanho, que pega erro de digitacao.
        var esperado = tipo == TipoDePessoa.Juridica ? 14 : 11;

        if (digitos.Length != esperado)
        {
            throw tipo == TipoDePessoa.Juridica
                ? ClientesServiceException.CnpjInvalido()
                : ClientesServiceException.CpfInvalido();
        }

        return digitos;
    }

    private static DateOnly? ValidarNascimento(DateOnly? data)
    {
        if (data is null)
        {
            return null;
        }

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        if (data > hoje)
        {
            throw ClientesServiceException.NascimentoNoFuturo();
        }

        return data < hoje.AddYears(-130)
            ? throw ClientesServiceException.NascimentoImplausivel()
            : data;
    }

    private static string? ValidarUf(string? uf)
    {
        var limpo = uf?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(limpo))
        {
            return null;
        }

        return limpo.Length == 2 && limpo.All(char.IsLetter)
            ? limpo
            : throw ClientesServiceException.UfInvalida();
    }

    private static string? ValidarCep(string? cep)
    {
        if (string.IsNullOrWhiteSpace(cep))
        {
            return null;
        }

        var digitos = SomenteDigitos(cep);

        return digitos.Length == 8
            ? digitos
            : throw ClientesServiceException.CepInvalido();
    }

    private static string? Limpar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string SomenteDigitos(string valor) => RegexDeDigitos().Replace(valor, string.Empty);

    private static ClienteResponse Mapear(Cliente c, int totalConversas, bool temConversaAtiva)
    {
        int? idade = null;

        if (c.DataNascimento is { } nascimento)
        {
            var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
            idade = hoje.Year - nascimento.Year;

            if (nascimento > hoje.AddYears(-idade.Value))
            {
                idade--;
            }
        }

        return new ClienteResponse(
            c.Id,
            c.Nome,
            c.Tipo,
            c.TelefoneWhatsapp,
            c.TelefoneAlternativo,
            c.Email,
            c.Documento,
            c.Rg,
            c.OrgaoEmissor,
            c.UfOrgaoEmissor,
            c.DataNascimento,
            c.EstadoCivil,
            c.RegimeDeBens,
            c.Profissao,
            c.Nacionalidade,
            c.Naturalidade,
            c.NomeDoPai,
            c.NomeDaMae,
            c.RazaoSocial,
            c.RepresentanteLegal,
            c.Cep,
            c.Logradouro,
            c.Numero,
            c.Complemento,
            c.Bairro,
            c.Cidade,
            c.Uf,
            c.Observacoes,
            c.Ativo,
            c.PrimeiroContatoEm,
            totalConversas,
            temConversaAtiva,
            c.PercentualDeQualificacao(),
            idade,
            $"/api/clientes/{c.Id}/foto-perfil");
    }


    [GeneratedRegex(@"\D")]
    private static partial Regex RegexDeDigitos();
}
