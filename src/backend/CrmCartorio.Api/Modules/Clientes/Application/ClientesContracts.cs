using CrmCartorio.Api.Modules.Clientes.Domain.Enums;

namespace CrmCartorio.Api.Modules.Clientes.Application;

/// <summary>
/// Dados de qualificacao. Um record so para entrada, usado tanto na criacao
/// quanto na atualizacao — os campos sao os mesmos e duplicar a lista de 25
/// propriedades em dois records seria convite a esquecer um dos lados.
/// </summary>
public record DadosDeCliente(
    string Nome,
    string TelefoneWhatsapp,
    TipoDePessoa Tipo = TipoDePessoa.Fisica,
    string? TelefoneAlternativo = null,
    string? Email = null,
    string? Documento = null,
    string? Rg = null,
    string? OrgaoEmissor = null,
    string? UfOrgaoEmissor = null,
    DateOnly? DataNascimento = null,
    EstadoCivil EstadoCivil = EstadoCivil.NaoInformado,
    RegimeDeBens RegimeDeBens = RegimeDeBens.NaoInformado,
    string? Profissao = null,
    string? Nacionalidade = null,
    string? Naturalidade = null,
    string? NomeDoPai = null,
    string? NomeDaMae = null,
    string? RazaoSocial = null,
    string? RepresentanteLegal = null,
    string? Cep = null,
    string? Logradouro = null,
    string? Numero = null,
    string? Complemento = null,
    string? Bairro = null,
    string? Cidade = null,
    string? Uf = null,
    string? Observacoes = null,
    bool Ativo = true);

public record FiltroDeClientes
{
    /// <summary>Nome, telefone ou documento.</summary>
    public string? Busca { get; init; }

    public bool IncluirInativos { get; init; }

    public TipoDePessoa? Tipo { get; init; }

    public EstadoCivil? EstadoCivil { get; init; }

    public string? Cidade { get; init; }

    /// <summary>Sem CPF/CNPJ cadastrado — a fila de "falta documento".</summary>
    public bool SemDocumento { get; init; }

    /// <summary>Sem endereco completo, que trava a lavratura de ato.</summary>
    public bool SemEndereco { get; init; }

    /// <summary>Apenas quem tem atendimento em aberto agora.</summary>
    public bool ComConversaAtiva { get; init; }

    /// <summary>Ordenacao: `nome`, `recentes` ou `qualificacao`.</summary>
    public string? Ordenar { get; init; }

    public int Pagina { get; init; } = 1;
    public int Tamanho { get; init; } = 30;
}

public record ClienteResponse(
    Guid Id,
    string Nome,
    TipoDePessoa Tipo,
    string TelefoneWhatsapp,
    string? TelefoneAlternativo,
    string? Email,
    string? Documento,
    string? Rg,
    string? OrgaoEmissor,
    string? UfOrgaoEmissor,
    DateOnly? DataNascimento,
    EstadoCivil EstadoCivil,
    RegimeDeBens RegimeDeBens,
    string? Profissao,
    string? Nacionalidade,
    string? Naturalidade,
    string? NomeDoPai,
    string? NomeDaMae,
    string? RazaoSocial,
    string? RepresentanteLegal,
    string? Cep,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cidade,
    string? Uf,
    string? Observacoes,
    bool Ativo,
    DateTime PrimeiroContatoEm,
    int TotalConversas,
    bool TemConversaAtiva,
    /// <summary>0 a 100. Quanto da qualificacao ja foi coletado.</summary>
    int PercentualDeQualificacao,
    /// <summary>Idade em anos, quando ha data de nascimento.</summary>
    int? Idade);

public record ResultadoPaginadoDeClientes(
    IReadOnlyList<ClienteResponse> Itens,
    int Total,
    int Pagina,
    int Tamanho);

/// <summary>Valores para montar os seletores da tela, vindos do que existe na base.</summary>
public record OpcoesDeClientesResponse(IReadOnlyList<string> Cidades);
