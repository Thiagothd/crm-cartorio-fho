using CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

namespace CrmCartorio.Api.Modules.Usuarios.Domain;

/// <summary>
/// Chave, rótulo de balcão, descrição e bloco de cada <see cref="PermissaoDoSistema"/>,
/// e os 6 modelos prontos — a fonte única de verdade que o backend usa para
/// checar (<c>Atendente.Permissoes</c>) e que o frontend usa para desenhar a
/// tela de permissões.
/// </summary>
/// <remarks>
/// Vive em <c>Domain</c>, não em <c>Application</c>: é lido tanto por
/// <c>Infrastructure.Security.ContextoDoUsuario</c> (fora do módulo Usuarios)
/// quanto pelos verificadores — nenhum dos dois pode referenciar o
/// <c>Application</c> de um módulo (CLAUDE.md 7.1).
/// </remarks>
public static class CatalogoDePermissoes
{
    /// <summary>Uma linha do catálogo.</summary>
    public sealed record Definicao(
        PermissaoDoSistema Chave,
        string ChaveTexto,
        string Rotulo,
        string Descricao,
        string Bloco,
        bool EhTela);

    public static readonly IReadOnlyList<Definicao> Todas =
    [
        // ------------------------------------------------------ Atendimento
        D(PermissaoDoSistema.AtendimentoVer, "atendimento.ver", "Abrir a tela de Atendimento",
            "Ver a fila e as conversas do WhatsApp.", "Atendimento", ehTela: true),
        D(PermissaoDoSistema.AtendimentoAssumir, "atendimento.assumir", "Puxar conversa da fila",
            "Assumir um atendimento que está esperando responsável.", "Atendimento"),
        D(PermissaoDoSistema.AtendimentoResponder, "atendimento.responder", "Escrever e enviar mensagem",
            "Responder o cliente e enviar anexo.", "Atendimento"),
        D(PermissaoDoSistema.AtendimentoAbrirNovo, "atendimento.abrir_novo", "Começar um atendimento pelo painel",
            "Abrir uma conversa nova sem esperar o cliente escrever primeiro.", "Atendimento"),
        D(PermissaoDoSistema.AtendimentoMover, "atendimento.mover", "Mudar etapa e setor do atendimento",
            "Mover o atendimento no funil ou trocar de setor.", "Atendimento"),
        D(PermissaoDoSistema.AtendimentoVincularServico, "atendimento.vincular_servico", "Dizer qual serviço é o atendimento",
            "Vincular o serviço solicitado — é isso que inicia o prazo.", "Atendimento"),
        D(PermissaoDoSistema.AtendimentoEncerrar, "atendimento.encerrar", "Encerrar atendimento",
            "Finalizar e arquivar o atendimento.", "Atendimento"),
        D(PermissaoDoSistema.AtendimentoReabrir, "atendimento.reabrir", "Reabrir atendimento encerrado",
            "Trazer de volta uma conversa já finalizada.", "Atendimento"),
        D(PermissaoDoSistema.AtendimentoTransferir, "atendimento.transferir", "Passar a conversa para outra pessoa",
            "Transferir o atendimento para outro atendente.", "Atendimento"),
        D(PermissaoDoSistema.AtendimentoMexerNoDeTerceiro, "atendimento.mexer_no_de_terceiro", "Mexer em conversa que é de outro",
            "Responder, mover ou encerrar um atendimento que não é seu.", "Atendimento"),
        D(PermissaoDoSistema.AtendimentoSimularCliente, "atendimento.simular_cliente", "Simular mensagem de cliente",
            "Ferramenta de demonstração — nunca entra em nenhum modelo pronto.", "Atendimento"),

        // ---------------------------------------------------------- Clientes
        D(PermissaoDoSistema.ClientesVer, "clientes.ver", "Abrir a tela de Clientes",
            "Ver a lista e a ficha de cada cliente.", "Clientes", ehTela: true),
        D(PermissaoDoSistema.ClientesCadastrar, "clientes.cadastrar", "Cadastrar cliente novo",
            "Criar a ficha de um cliente.", "Clientes"),
        D(PermissaoDoSistema.ClientesEditar, "clientes.editar", "Corrigir dados de cliente",
            "Editar ou desativar a ficha de um cliente.", "Clientes"),
        D(PermissaoDoSistema.ClientesExcluir, "clientes.excluir", "Excluir cliente",
            "Apagar definitivamente a ficha de um cliente.", "Clientes"),
        D(PermissaoDoSistema.ClientesImportarAgenda, "clientes.importar_agenda", "Importar contatos em lote",
            "Puxar a agenda do WhatsApp do cartório ou importar uma lista/CSV.", "Clientes"),

        // ------------------------------------------------- Catálogo e estrutura
        D(PermissaoDoSistema.ServicosVer, "servicos.ver", "Ver o catálogo de serviços",
            "Ver os atos oferecidos e o prazo de cada um.", "Catálogo e estrutura", ehTela: true),
        D(PermissaoDoSistema.ServicosEditar, "servicos.editar", "Cadastrar e alterar serviço",
            "Criar, renomear e ajustar prazo de serviço.", "Catálogo e estrutura"),
        D(PermissaoDoSistema.EtapasVer, "etapas.ver", "Ver as etapas do funil",
            "Ver como o atendimento progride.", "Catálogo e estrutura", ehTela: true),
        D(PermissaoDoSistema.EtapasEditar, "etapas.editar", "Criar e reordenar etapa",
            "Criar, renomear e reordenar etapa do funil.", "Catálogo e estrutura"),
        D(PermissaoDoSistema.SetoresVer, "setores.ver", "Ver os setores",
            "Ver quem atende o quê.", "Catálogo e estrutura", ehTela: true),
        D(PermissaoDoSistema.SetoresEditar, "setores.editar", "Criar e alterar setor",
            "Criar e renomear setor.", "Catálogo e estrutura"),

        // -------------------------------------------------------- Automação e IA
        D(PermissaoDoSistema.AutomacaoMensagensVer, "automacao.mensagens.ver", "Ver as mensagens automáticas",
            "Ver as regras de resposta por gatilho.", "Automação e IA", ehTela: true),
        D(PermissaoDoSistema.AutomacaoMensagensEditar, "automacao.mensagens.editar", "Alterar mensagem automática",
            "Criar e alterar texto e gatilho da automação.", "Automação e IA"),
        D(PermissaoDoSistema.AutomacaoRespostasVer, "automacao.respostas.ver", "Ver as respostas rápidas",
            "Ver os atalhos de texto disponíveis.", "Automação e IA", ehTela: true),
        D(PermissaoDoSistema.AutomacaoRespostasEditar, "automacao.respostas.editar", "Criar resposta rápida",
            "Criar e alterar atalho de texto.", "Automação e IA"),
        D(PermissaoDoSistema.AutomacaoEncerramentoVer, "automacao.encerramento.ver", "Ver o encerramento automático",
            "Ver os tempos de inatividade configurados.", "Automação e IA", ehTela: true),
        D(PermissaoDoSistema.AutomacaoEncerramentoEditar, "automacao.encerramento.editar", "Mudar os tempos de encerramento",
            "Ajustar quando o sistema pergunta e quando encerra sozinho.", "Automação e IA"),
        D(PermissaoDoSistema.IaVer, "ia.ver", "Ver o painel da IA",
            "Ver a situação e o uso do dia da Resposta Inteligente.", "Automação e IA", ehTela: true),
        D(PermissaoDoSistema.IaConhecimentoVer, "ia.conhecimento.ver", "Ver o conhecimento da IA",
            "Ver o conteúdo aprovado que a IA usa para responder.", "Automação e IA", ehTela: true),
        D(PermissaoDoSistema.IaConhecimentoEditar, "ia.conhecimento.editar", "Aprovar conteúdo da IA",
            "Aprovar e alterar o conteúdo que a IA usa para responder.", "Automação e IA"),

        // ------------------------------------------------------------ Relatórios
        D(PermissaoDoSistema.RelatoriosVer, "relatorios.ver", "Abrir Indicadores e Relatórios",
            "Ver dashboard, atendimentos e clientes por período.", "Relatórios", ehTela: true),
        D(PermissaoDoSistema.RelatoriosEquipe, "relatorios.equipe", "Ver o desempenho da equipe",
            "Ver o relatório pessoa por pessoa e a carga da equipe agora.", "Relatórios"),
        D(PermissaoDoSistema.RelatoriosSaudeIa, "relatorios.saude_ia", "Ver a saúde da IA na Visão Geral",
            "Ver se a Resposta Inteligente está funcionando normalmente.", "Relatórios"),
        D(PermissaoDoSistema.RelatoriosSaudeWhatsapp, "relatorios.saude_whatsapp", "Ver o estado do canal na Visão Geral",
            "Ver se o WhatsApp do cartório está conectado.", "Relatórios"),

        // -------------------------------------------------------------- WhatsApp
        D(PermissaoDoSistema.WhatsappVer, "whatsapp.ver", "Abrir a tela do WhatsApp",
            "Ver o status da conexão do número do cartório.", "WhatsApp", ehTela: true),
        D(PermissaoDoSistema.WhatsappParear, "whatsapp.parear", "Conectar e trocar o número",
            "Parear, trocar ou desconectar o canal.", "WhatsApp"),
        D(PermissaoDoSistema.WhatsappRegras, "whatsapp.regras", "Definir quem fala com o número",
            "Liberar e bloquear quem consegue falar com o cartório.", "WhatsApp"),
        D(PermissaoDoSistema.WhatsappTestarEnvio, "whatsapp.testar_envio", "Disparar mensagem de teste",
            "Enviar uma mensagem de teste real pelo canal.", "WhatsApp"),

        // ------------------------------------------------ Aparência e serventia
        D(PermissaoDoSistema.AparenciaVer, "aparencia.ver", "Abrir a tela de Aparência",
            "Ver a textura de fundo do fio de mensagens.", "Aparência e serventia", ehTela: true),
        D(PermissaoDoSistema.AparenciaEditar, "aparencia.editar", "Trocar a aparência da conversa",
            "Trocar a textura de fundo do fio de mensagens.", "Aparência e serventia"),
        D(PermissaoDoSistema.ServentiaEditar, "serventia.editar", "Alterar dados do cartório",
            "Alterar razão social, CNPJ e demais dados da serventia.", "Aparência e serventia"),

        // ------------------------------------------------------ Equipe e segurança
        D(PermissaoDoSistema.EquipeVer, "equipe.ver", "Abrir a tela de Equipe",
            "Ver a lista de atendentes e seus dados.", "Equipe e segurança", ehTela: true),
        D(PermissaoDoSistema.EquipeCadastrar, "equipe.cadastrar", "Cadastrar pessoa nova",
            "Cadastrar um atendente novo.", "Equipe e segurança"),
        D(PermissaoDoSistema.EquipeEditar, "equipe.editar", "Alterar dados de atendente",
            "Alterar nome, e-mail, cargo, setor e ativar/desativar acesso.", "Equipe e segurança"),
        D(PermissaoDoSistema.EquipeRedefinirSenha, "equipe.redefinir_senha", "Redefinir senha de alguém",
            "Redefinir a senha de outro atendente.", "Equipe e segurança"),
        D(PermissaoDoSistema.EquipeExcluir, "equipe.excluir", "Excluir atendente",
            "Apagar definitivamente o cadastro de um atendente sem histórico.", "Equipe e segurança"),
        D(PermissaoDoSistema.EquipePermissoes, "equipe.permissoes", "Decidir o que cada pessoa acessa",
            "Montar e ajustar as permissões de outro atendente.", "Equipe e segurança"),
        D(PermissaoDoSistema.EquipeMfaAdministrar, "equipe.mfa_administrar", "Administrar verificação em duas etapas",
            "Emitir ticket de matrícula e resetar a verificação em duas etapas de alguém.", "Equipe e segurança"),
        D(PermissaoDoSistema.SegurancaEventosVer, "seguranca.eventos.ver", "Ver o histórico de segurança",
            "Ver os eventos de segurança registrados do cartório.", "Equipe e segurança", ehTela: true),
    ];

    private static readonly Dictionary<PermissaoDoSistema, Definicao> PorChave =
        Todas.ToDictionary(d => d.Chave);

    private static readonly Dictionary<string, PermissaoDoSistema> PorTexto =
        Todas.ToDictionary(d => d.ChaveTexto, d => d.Chave);

    public static Definicao Descrever(PermissaoDoSistema chave) => PorChave[chave];

    /// <summary>Como <see cref="Desserializar"/>, mas a partir de uma lista de chaves de texto já separadas (o corpo de <c>DefinirPermissoesRequest</c>).</summary>
    public static IReadOnlySet<PermissaoDoSistema> DesserializarLista(IEnumerable<string> chavesTexto) =>
        chavesTexto.Where(PorTexto.ContainsKey).Select(t => PorTexto[t]).ToHashSet();

    private const char Cerca = '|';

    /// <summary>
    /// Serializa como lista com pipe de cerca nas duas pontas — faz
    /// <c>LIKE '%|chave|%'</c> ser teste de contenção correto em SQL, sem
    /// casar `equipe.ver` dentro de `equipe.ver_algo`.
    /// </summary>
    public static string Serializar(IEnumerable<PermissaoDoSistema> chaves)
    {
        var texto = string.Join(Cerca, chaves.Select(c => PorChave[c].ChaveTexto));
        return texto.Length == 0 ? $"{Cerca}{Cerca}" : $"{Cerca}{texto}{Cerca}";
    }

    /// <summary>
    /// Nunca lança. Chave desconhecida (coluna gravada por um binário mais
    /// novo, ou corrompida) é ignorada em silêncio — um binário antigo não
    /// pode conceder o que não sabe o que é.
    /// </summary>
    public static IReadOnlySet<PermissaoDoSistema> Desserializar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return new HashSet<PermissaoDoSistema>();
        }

        return texto
            .Split(Cerca, StringSplitOptions.RemoveEmptyEntries)
            .Where(PorTexto.ContainsKey)
            .Select(t => PorTexto[t])
            .ToHashSet();
    }

    /// <summary>
    /// O conjunto efetivo (chave de texto, ordenado) de um atendente — NULL
    /// segue a Classe da conta, texto explícito vence. É isto que
    /// <see cref="Application.AtendenteResponse.Permissoes"/> expõe ao
    /// frontend; a MESMA regra que <see cref="ContextoDoUsuario"/> e
    /// <see cref="ValidadorDeSessao"/> aplicam do lado do backend.
    /// </summary>
    public static IReadOnlyList<string> ChavesEfetivas(string? permissoes, PerfilDeAtendente perfil)
    {
        var conjunto = permissoes is null ? ConjuntoDaClasse(perfil) : Desserializar(permissoes);
        return [.. conjunto.Select(c => PorChave[c].ChaveTexto).OrderBy(c => c, StringComparer.Ordinal)];
    }

    /// <summary>
    /// O conjunto derivado da Classe da conta — usado quando
    /// <see cref="Entities.Atendente.Permissoes"/> é <c>NULL</c> ("segue a
    /// classe"). É EXATAMENTE o preset correspondente (ver <see cref="ConjuntoDoModelo"/>):
    /// Atendente → Balcão, Supervisor → Supervisão, Administrador →
    /// Administração — para o dia 1 desta entrega não mudar o comportamento
    /// de ninguém.
    /// </summary>
    public static IReadOnlySet<PermissaoDoSistema> ConjuntoDaClasse(PerfilDeAtendente perfil) => perfil switch
    {
        PerfilDeAtendente.Administrador => ConjuntoDoModelo(ModeloDePermissao.Administracao),
        PerfilDeAtendente.Supervisor => ConjuntoDoModelo(ModeloDePermissao.Supervisao),
        _ => ConjuntoDoModelo(ModeloDePermissao.Balcao),
    };

    private static readonly PermissaoDoSistema[] Recepcao =
    [
        PermissaoDoSistema.AtendimentoVer,
        PermissaoDoSistema.AtendimentoAssumir,
        PermissaoDoSistema.AtendimentoResponder,
        PermissaoDoSistema.AtendimentoMover,
        PermissaoDoSistema.AtendimentoTransferir,
        PermissaoDoSistema.ClientesVer,
    ];

    private static readonly PermissaoDoSistema[] Balcao =
    [
        .. Recepcao,
        PermissaoDoSistema.AtendimentoAbrirNovo,
        PermissaoDoSistema.AtendimentoVincularServico,
        PermissaoDoSistema.AtendimentoEncerrar,
        PermissaoDoSistema.ClientesCadastrar,
        PermissaoDoSistema.ClientesEditar,
    ];

    private static readonly PermissaoDoSistema[] Catalogo =
    [
        PermissaoDoSistema.ServicosVer, PermissaoDoSistema.ServicosEditar,
        PermissaoDoSistema.EtapasVer, PermissaoDoSistema.EtapasEditar,
        PermissaoDoSistema.SetoresVer, PermissaoDoSistema.SetoresEditar,
        PermissaoDoSistema.AutomacaoMensagensVer, PermissaoDoSistema.AutomacaoMensagensEditar,
        PermissaoDoSistema.AutomacaoRespostasVer, PermissaoDoSistema.AutomacaoRespostasEditar,
        PermissaoDoSistema.AutomacaoEncerramentoVer, PermissaoDoSistema.AutomacaoEncerramentoEditar,
        PermissaoDoSistema.IaVer,
        PermissaoDoSistema.IaConhecimentoVer, PermissaoDoSistema.IaConhecimentoEditar,
        PermissaoDoSistema.AparenciaVer, PermissaoDoSistema.AparenciaEditar,
        PermissaoDoSistema.ClientesVer,
    ];

    private static readonly PermissaoDoSistema[] Supervisao =
    [
        .. Balcao,
        .. Catalogo,
        PermissaoDoSistema.AtendimentoReabrir,
        PermissaoDoSistema.AtendimentoMexerNoDeTerceiro,
        PermissaoDoSistema.ClientesExcluir,
        PermissaoDoSistema.ClientesImportarAgenda,
        PermissaoDoSistema.RelatoriosVer,
        PermissaoDoSistema.RelatoriosEquipe,
        PermissaoDoSistema.RelatoriosSaudeIa,
    ];

    /// <summary>Tudo, exceto <see cref="PermissaoDoSistema.AtendimentoSimularCliente"/> — de propósito, ver o enum.</summary>
    private static readonly PermissaoDoSistema[] Administracao =
    [
        .. Enum.GetValues<PermissaoDoSistema>().Where(p => p != PermissaoDoSistema.AtendimentoSimularCliente),
    ];

    private static readonly PermissaoDoSistema[] Auditoria =
    [
        .. Todas.Where(d => d.EhTela).Select(d => d.Chave),
        PermissaoDoSistema.RelatoriosEquipe,
        PermissaoDoSistema.RelatoriosSaudeIa,
        PermissaoDoSistema.RelatoriosSaudeWhatsapp,
    ];

    public static (string Rotulo, string Descricao) DescreverModelo(ModeloDePermissao modelo) => modelo switch
    {
        ModeloDePermissao.Recepcao => ("Recepção", "Só recebe e encaminha — a mais enxuta."),
        ModeloDePermissao.Balcao => ("Balcão", "Atende do começo ao fim."),
        ModeloDePermissao.Catalogo => ("Catálogo", "Cuida do cadastro e da automação, não atende."),
        ModeloDePermissao.Supervisao => ("Supervisão", "Toca a equipe do dia — reabre, transfere e vê os relatórios."),
        ModeloDePermissao.Administracao => ("Administração", "Responde pelo sistema."),
        ModeloDePermissao.Auditoria => ("Auditoria", "Vê tudo, não muda nada."),
        _ => throw new ArgumentOutOfRangeException(nameof(modelo)),
    };

    public static IReadOnlySet<PermissaoDoSistema> ConjuntoDoModelo(ModeloDePermissao modelo) => modelo switch
    {
        ModeloDePermissao.Recepcao => Recepcao.ToHashSet(),
        ModeloDePermissao.Balcao => Balcao.ToHashSet(),
        ModeloDePermissao.Catalogo => Catalogo.ToHashSet(),
        ModeloDePermissao.Supervisao => Supervisao.ToHashSet(),
        ModeloDePermissao.Administracao => Administracao.ToHashSet(),
        ModeloDePermissao.Auditoria => Auditoria.ToHashSet(),
        _ => throw new ArgumentOutOfRangeException(nameof(modelo)),
    };

    private static Definicao D(
        PermissaoDoSistema chave, string chaveTexto, string rotulo, string descricao, string bloco, bool ehTela = false) =>
        new(chave, chaveTexto, rotulo, descricao, bloco, ehTela);
}
