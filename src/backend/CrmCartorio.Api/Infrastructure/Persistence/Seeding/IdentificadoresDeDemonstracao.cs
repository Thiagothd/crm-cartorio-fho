namespace CrmCartorio.Api.Infrastructure.Persistence.Seeding;

/// <summary>
/// Guids fixos dos registros de configuracao da demonstracao.
/// </summary>
/// <remarks>
/// Sao fixos de proposito: o semeador em C# e o script sql/002-seed-simulado.sql
/// precisam produzir exatamente os mesmos identificadores, senao a base montada
/// pelo SQL e a montada pela aplicacao divergem e qualquer comparacao entre as
/// duas vira caca ao fantasma.
/// </remarks>
public static class IdentificadoresDeDemonstracao
{
    public static readonly Guid Tenant = new("0199b2c0-0000-7000-8000-000000000001");

    /// <summary>
    /// Apenas dois setores, porque a serventia tem apenas duas atribuicoes:
    /// Tabelionato de Notas e Registro Civil das Pessoas Naturais.
    /// Registro de Imoveis, Titulos e Documentos e Protesto NAO sao atribuicao
    /// deste cartorio — os Guids 03, 04 e 05 existiam na versao anterior, quando
    /// se supunha serventia de oficio unico, e foram removidos.
    /// </summary>
    public static class Setores
    {
        public static readonly Guid Notas = new("0199b2c0-0001-7000-8000-000000000001");
        public static readonly Guid RegistroCivil = new("0199b2c0-0001-7000-8000-000000000002");
    }

    public static class Etapas
    {
        public static readonly Guid AtendimentoInicial = new("0199b2c0-0002-7000-8000-000000000001");
        public static readonly Guid Captacao = new("0199b2c0-0002-7000-8000-000000000002");
        public static readonly Guid EmProcessamento = new("0199b2c0-0002-7000-8000-000000000003");
        public static readonly Guid ProntoEntrega = new("0199b2c0-0002-7000-8000-000000000004");
    }

    /// <summary>
    /// Regras de mensagem automatica semeadas. O TEXTO de todas aguarda a voz do
    /// cartório parceiro (docs/REQUISITOS.md, 5.7) — sao cadastro, e ele reescreve pela tela.
    /// </summary>
    public static class MensagensAutomaticas
    {
        public static readonly Guid BoasVindas = new("0199b2c0-0006-7000-8000-000000000001");
        public static readonly Guid TriagemSemResposta = new("0199b2c0-0006-7000-8000-000000000002");
        public static readonly Guid AtoConcluido = new("0199b2c0-0006-7000-8000-000000000003");
        public static readonly Guid PrazoPerto = new("0199b2c0-0006-7000-8000-000000000004");
        public static readonly Guid PrazoVencido = new("0199b2c0-0006-7000-8000-000000000005");
        public static readonly Guid TriagemComResposta = new("0199b2c0-0006-7000-8000-000000000006");
        public static readonly Guid ServicoEscolhido = new("0199b2c0-0006-7000-8000-000000000007");
        public static readonly Guid EmAndamento = new("0199b2c0-0006-7000-8000-000000000008");
        public static readonly Guid MenuDeContinuacao = new("0199b2c0-0006-7000-8000-000000000009");
        public static readonly Guid PedidoDeAtendente = new("0199b2c0-0006-7000-8000-000000000010");
        public static readonly Guid Despedida = new("0199b2c0-0006-7000-8000-000000000011");
        public static readonly Guid RespostaSemCorrespondencia = new("0199b2c0-0006-7000-8000-000000000012");
        public static readonly Guid ConfirmacaoDeEncerramento = new("0199b2c0-0006-7000-8000-000000000013");
        public static readonly Guid EncerramentoPorInatividade = new("0199b2c0-0006-7000-8000-000000000014");
        public static readonly Guid PedidoDeNome = new("0199b2c0-0006-7000-8000-000000000015");
    }

    public static class RespostasRapidas
    {
        public static readonly Guid Horario = new("0199b2c0-0007-7000-8000-000000000001");
        public static readonly Guid Prazo = new("0199b2c0-0007-7000-8000-000000000002");
        public static readonly Guid Emolumentos = new("0199b2c0-0007-7000-8000-000000000003");
        public static readonly Guid Firma = new("0199b2c0-0007-7000-8000-000000000004");
        public static readonly Guid Escritura = new("0199b2c0-0007-7000-8000-000000000005");
        public static readonly Guid Certidao = new("0199b2c0-0007-7000-8000-000000000006");
    }

    public static class Atendentes
    {
        /// <summary>Helena Campos Nogueira — titular da serventia.</summary>
        public static readonly Guid Titular = new("0199b2c0-0003-7000-8000-000000000005");

        /// <summary>Rafael Campos Nogueira — substituto.</summary>
        public static readonly Guid Substituto = new("0199b2c0-0003-7000-8000-000000000001");

        /// <summary>Escrevente autorizada — nome ficticio, aguarda confirmacao.</summary>
        public static readonly Guid Escrevente1 = new("0199b2c0-0003-7000-8000-000000000002");

        /// <summary>Escrevente — nome ficticio, aguarda confirmacao.</summary>
        public static readonly Guid Escrevente2 = new("0199b2c0-0003-7000-8000-000000000003");
    }
}
