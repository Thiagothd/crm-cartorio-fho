namespace CrmCartorio.Api.Modules.Usuarios.Domain.Enums;

/// <summary>
/// Catálogo de permissões granulares — o que cada tela ou ação exige. Ver
/// <see cref="Usuarios.Domain.CatalogoDePermissoes"/> para chave de texto,
/// rótulo de balcão, descrição, bloco e os modelos prontos de cada uma.
/// </summary>
/// <remarks>
/// A ordem/valor deste enum NUNCA é persistida — <see cref="Entities.Atendente.Permissoes"/>
/// grava a CHAVE DE TEXTO (ver <see cref="Usuarios.Domain.CatalogoDePermissoes"/>),
/// nunca o valor numérico. Por isso um membro novo pode entrar no meio desta
/// lista sem migração: a posição aqui é só organização de código.
/// </remarks>
public enum PermissaoDoSistema
{
    // ------------------------------------------------------------ Atendimento
    AtendimentoVer,
    AtendimentoAssumir,
    AtendimentoResponder,
    AtendimentoAbrirNovo,
    AtendimentoMover,
    AtendimentoVincularServico,
    AtendimentoEncerrar,
    AtendimentoReabrir,
    AtendimentoTransferir,
    AtendimentoMexerNoDeTerceiro,

    /// <summary>Ferramenta de demonstração — fica fora de todos os modelos prontos, de propósito.</summary>
    AtendimentoSimularCliente,

    // ---------------------------------------------------------------- Clientes
    ClientesVer,
    ClientesCadastrar,
    ClientesEditar,
    ClientesExcluir,
    ClientesImportarAgenda,

    // --------------------------------------------------------------- Devedores
    DevedoresVer,
    DevedoresEditar,

    // ------------------------------------------------- Catálogo e estrutura
    ServicosVer,
    ServicosEditar,
    EtapasVer,
    EtapasEditar,
    SetoresVer,
    SetoresEditar,

    // -------------------------------------------------------- Automação e IA
    AutomacaoMensagensVer,
    AutomacaoMensagensEditar,
    AutomacaoRespostasVer,
    AutomacaoRespostasEditar,
    AutomacaoEncerramentoVer,
    AutomacaoEncerramentoEditar,
    IaVer,
    IaConhecimentoVer,
    IaConhecimentoEditar,

    // ------------------------------------------------------------ Relatórios
    RelatoriosVer,
    RelatoriosEquipe,
    RelatoriosSaudeIa,
    RelatoriosSaudeWhatsapp,

    // -------------------------------------------------------------- WhatsApp
    WhatsappVer,
    WhatsappParear,
    WhatsappRegras,
    WhatsappTestarEnvio,

    // ------------------------------------------------ Aparência e serventia
    AparenciaVer,
    AparenciaEditar,
    ServentiaEditar,

    // ------------------------------------------------------ Equipe e segurança
    EquipeVer,
    EquipeCadastrar,
    EquipeEditar,
    EquipeRedefinirSenha,
    EquipeExcluir,

    /// <summary>A meta-permissão: decide o que cada colega acessa.</summary>
    EquipePermissoes,
    EquipeMfaAdministrar,
    SegurancaEventosVer,

    /// <summary>Aprovar/negar/revogar uma solicitação de acesso técnico — só o preset Administração.</summary>
    SegurancaAcessoTecnicoAprovar,

    // ----------------------------------------------------------------- Suporte

    /// <summary>Da conta de suporte técnico: pedir acesso, nunca ler dado sozinha.</summary>
    SuporteSolicitarAcesso,
}
