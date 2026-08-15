using CrmCartorio.Api.Modules.Configuracoes.Domain.Enums;

namespace CrmCartorio.Api.Modules.Configuracoes.Application;

public record SalvarSetorRequest(string Nome, string? Descricao, int Ordem, bool Ativo);

public record SalvarEtapaRequest(
    string Nome,
    string? Descricao,
    int Ordem,
    string Cor,
    bool EhFinal,
    bool Ativo);

public record SetorResponse(
    Guid Id,
    string Nome,
    string? Descricao,
    int Ordem,
    bool Ativo,
    int ConversasAtivas);

public record EtapaResponse(
    Guid Id,
    string Nome,
    string? Descricao,
    int Ordem,
    string Cor,
    bool EhFinal,
    bool Ativo,
    int ConversasAtivas);

// Mensagens automaticas -----------------------------------------------------

public record SalvarMensagemAutomaticaRequest(
    GatilhoDeMensagem Gatilho,
    Guid? EtapaAlvoId,
    string Conteudo,
    bool Ativo);

public record MensagemAutomaticaResponse(
    Guid Id,
    GatilhoDeMensagem Gatilho,
    /// <summary>Rotulo do gatilho em vocabulario de cartorio, para a tela.</summary>
    string GatilhoDescrito,
    Guid? EtapaAlvoId,
    string? EtapaAlvoNome,
    string Conteudo,
    bool Ativo);

// Respostas rapidas ---------------------------------------------------------

public record SalvarRespostaRapidaRequest(string Atalho, string Titulo, string Conteudo, bool Ativo);

public record RespostaRapidaResponse(
    Guid Id,
    string Atalho,
    string Titulo,
    string Conteudo,
    bool Ativo);
