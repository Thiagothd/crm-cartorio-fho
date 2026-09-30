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
    bool Ativo = true,
    /// <summary>Nulo mantém o que está gravado.</summary>
    bool? AtendimentoSemRobo = null);

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
    int? Idade,
    string? FotoPerfilUrl = null,
    /// <summary>
    /// O nome cadastrado nao serve para se dirigir ao cliente (".", emoji, o
    /// proprio numero). Calculado na leitura, nunca persistido.
    /// </summary>
    bool NomePendente = false,
    /// <summary>
    /// Quando o sistema ja perguntou o nome a este contato pelo WhatsApp. Nulo
    /// significa que ninguem tentou ainda — informacao diferente de "ele nao
    /// quis responder".
    /// </summary>
    DateTime? NomePerguntadoEm = null,
    /// <summary>O contato é atendido sempre sem robô (Cliente.AtendimentoSemRobo).</summary>
    bool AtendimentoSemRobo = false,
    DateTime? AtendimentoSemRoboDesde = null);

public record ResultadoPaginadoDeClientes(
    IReadOnlyList<ClienteResponse> Itens,
    int Total,
    int Pagina,
    int Tamanho);

/// <summary>Valores para montar os seletores da tela, vindos do que existe na base.</summary>
public record OpcoesDeClientesResponse(IReadOnlyList<string> Cidades);

// --- Importacao de contatos -------------------------------------------------

/// <summary>De onde a lista de contatos veio.</summary>
public enum FonteDeImportacao
{
    /// <summary>Texto colado — lista ou CSV, o parser trata os dois igual.</summary>
    Texto,

    /// <summary>Agenda de contatos do WhatsApp pareado, via Evolution API.</summary>
    WhatsApp
}

/// <summary>O que aconteceria com esta linha se a importação for confirmada.</summary>
public enum SituacaoDaLinhaImportada
{
    /// <summary>Cliente novo, será criado.</summary>
    Nova,

    /// <summary>Telefone já pertence a um cliente — nada muda, a linha é informativa.</summary>
    JaCadastrada,

    /// <summary>Telefone vazio, com letras, ou fora da faixa de dígitos aceita.</summary>
    TelefoneInvalido,

    /// <summary>Prefixo 55 com DDD que não existe.</summary>
    DddInvalido,

    /// <summary>
    /// 12 dígitos com DDD válido — pode ser fixo ou celular sem o nono dígito.
    /// Só entra se o operador desmarcar "revisar" e confirmar assim mesmo.
    /// </summary>
    Ambigua,

    /// <summary>Mesmo telefone de uma linha anterior, dentro do mesmo arquivo.</summary>
    DuplicadaNoArquivo
}

/// <summary>Uma linha da prévia — o que o operador vê e decide incluir ou não.</summary>
public record LinhaDeImportacaoResponse(
    int NumeroDaLinha,
    string? Nome,
    string TelefoneBruto,
    /// <summary>E.164, só quando <see cref="Situacao"/> permite gravar.</summary>
    string? TelefoneE164,
    SituacaoDaLinhaImportada Situacao,
    /// <summary>Preenchido só quando <see cref="Situacao"/> é <see cref="SituacaoDaLinhaImportada.JaCadastrada"/>.</summary>
    string? NomeDoCadastroExistente);

public record ResumoDaImportacaoResponse(
    int TotalLidas,
    int Novas,
    int JaCadastradas,
    int Invalidas,
    int Ambiguas,
    int Duplicadas);

public record AnalisarImportacaoResponse(
    ResumoDaImportacaoResponse Resumo,
    IReadOnlyList<LinhaDeImportacaoResponse> Linhas);

/// <summary>
/// Pedido de análise. <see cref="Texto"/> é usado tanto para lista colada
/// quanto para o conteúdo de um CSV — o parser não distingue os dois.
/// </summary>
public record AnalisarImportacaoRequest(FonteDeImportacao Fonte, string? Texto = null);

/// <summary>
/// Confirma a gravação de um subconjunto das linhas já analisadas — o operador
/// pode ter desmarcado algumas na prévia. Cada linha volta a ser enviada
/// inteira (não só o telefone) porque o servidor não guarda a análise entre as
/// duas chamadas.
/// </summary>
public record ConfirmarImportacaoRequest(FonteDeImportacao Fonte, IReadOnlyList<LinhaParaGravar> Linhas);

public record LinhaParaGravar(string? Nome, string TelefoneE164);

public record ConfirmarImportacaoResponse(int Criados, int Ignorados);
