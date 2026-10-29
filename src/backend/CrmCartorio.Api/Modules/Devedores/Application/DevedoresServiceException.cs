using CrmCartorio.Api.Domain.Common;

namespace CrmCartorio.Api.Modules.Devedores.Application;

public sealed class DevedoresServiceException(string codigo, string mensagem, int statusHttp = 400)
    : ExcecaoDeAplicacao(codigo, mensagem, statusHttp)
{
    public static DevedoresServiceException SemPermissao() =>
        new("sem_permissao", "Você não tem permissão para executar esta ação em Devedores.", 403);

    public static DevedoresServiceException PendenciaNaoEncontrada() =>
        new("pendencia_nao_encontrada", "Pendência financeira não encontrada.", 404);

    public static DevedoresServiceException ClienteNaoEncontrado() =>
        new("cliente_nao_encontrado", "Cliente não encontrado.", 404);

    public static DevedoresServiceException ClienteObrigatorio() =>
        new("cliente_obrigatorio", "Selecione um contato existente ou informe nome e WhatsApp para cadastrar agora.");

    public static DevedoresServiceException TelefoneInvalido() =>
        new("telefone_invalido", "Informe um telefone de WhatsApp válido com DDD.");

    public static DevedoresServiceException TituloObrigatorio() =>
        new("titulo_obrigatorio", "Informe o motivo ou serviço referente à pendência.");

    public static DevedoresServiceException ValorInvalido() =>
        new("valor_invalido", "O valor informado não pode ser negativo.");

    public static DevedoresServiceException ValorDePagamentoInvalido() =>
        new("valor_pagamento_invalido", "Informe um valor de pagamento maior que zero ou marque como quitação total.");

    public static DevedoresServiceException PendenciaJaEncerrada() =>
        new("pendencia_ja_encerrada", "Esta pendência já foi quitada ou cancelada.");

    public static DevedoresServiceException MensagemDeCobrancaVazia() =>
        new("mensagem_vazia", "Escreva a mensagem que será enviada no WhatsApp.");
}
