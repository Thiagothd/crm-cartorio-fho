import { api, montarQuery } from './cliente-http'
import type {
  AlertaOperacional,
  AnaliseDaIa,
  AnalisarImportacaoResponse,
  AparenciaDaConversa,
  Atendente,
  CargoNaServentia,
  ChavesGerais,
  Cliente,
  ConexaoDoCanal,
  ConfirmarImportacaoResponse,
  ConhecimentoDeIa,
  Conversa,
  ModoDeRetomadaDoRobo,
  ModoDoRoboNoSetor,
  ConversaDetalhada,
  DadosDeCliente,
  DescartesDoCanal,
  ConfirmarMfaResposta,
  DesafioMfa,
  AcessoTecnicoAtivo,
  AutorizacaoDeAcessoTecnico,
  DestinoDeEncaminhamento,
  EmpresaConsultada,
  EventoDeSeguranca,
  FiltroDeAcessoTecnico,
  FiltroDeAcessos,
  FiltroDeEventos,
  EncerramentoAutomatico,
  EnderecoConsultado,
  Etapa,
  FiltroDeClientes,
  FiltroDeConversas,
  FonteDeImportacao,
  GatilhoDeMensagem,
  IniciarMfaResposta,
  LinhaParaGravar,
  LoginEtapa1Resposta,
  Mensagem,
  MensagemAutomatica,
  MfaSituacao,
  ModoDeRecebimento,
  NumeroDoCanal,
  OpcoesDeClientes,
  Paginado,
  PaginadoDeClientes,
  Painel,
  PerfilDeAtendente,
  ModeloDePermissao,
  PermissoesDoAtendente,
  ProtecaoAtiva,
  RegenerarCodigosResposta,
  RegistroDeAcesso,
  RegrasDoCanal,
  Relatorio,
  RelatorioDeAtendentes,
  RelatorioDeAtendimentos,
  RelatorioDeClientes,
  RespostaRapida,
  ResumoDoPainel,
  Sessao,
  Setor,
  SolicitarAcessoTecnico,
  SituacaoDaIa,
  SituacaoDoCanal,
  TesteDeEnvioDoCanal,
  TicketDeMfa,
  TipoDeNumeroDoCanal,
  TipoServico,
  VersaoDeConhecimentoDeIa,
  VisaoGeral,
  AbrirConversaComDevedorResposta,
  AtualizarPendenciaFinanceira,
  CobrarNoWhatsapp,
  CobrarNoWhatsappResposta,
  CriarPendenciaFinanceira,
  FiltroDeDevedores,
  ImportarDevedoresEmLote,
  ListagemDeDevedores,
  PendenciaFinanceira,
  RegistrarPagamentoDePendencia,
  ResultadoDeImportacaoDeDevedores,
  ResumoDeDividaDoCliente,
} from './tipos'

// Autenticacao --------------------------------------------------------------

export const autenticacaoApi = {
  /** SEC-03 — etapa 1. `requerMfa` decide se a resposta trouxe sessão ou desafio. */
  login: (email: string, senha: string) =>
    api.post<LoginEtapa1Resposta>('/api/autenticacao/login', { email, senha }),

  /**
   * SEC-03 — etapa 2. `desafio` é a credencial aqui, nunca um JWT.
   * `confiarNesteDispositivo` pede um cookie de dispositivo confiável de 30
   * dias — o backend decide tudo (hash, expiração, TokenVersion); o
   * frontend só manda a intenção.
   */
  loginMfa: (desafio: string, codigo: string, confiarNesteDispositivo = false) =>
    api.post<Sessao>('/api/autenticacao/login/mfa', { desafio, codigo, confiarNesteDispositivo }),

  sessao: () => api.get<Sessao>('/api/autenticacao/sessao'),

  /** Nome da serventia para a tela de login — anônimo, chamado antes da sessão. */
  serventia: () => api.get<{ nome: string }>('/api/autenticacao/serventia'),

  alterarSenha: (senhaAtual: string, novaSenha: string) =>
    api.post<void>('/api/autenticacao/senha', { senhaAtual, novaSenha }),
}

// MFA (SEC-03) ----------------------------------------------------------------

export const mfaApi = {
  situacao: () => api.get<MfaSituacao>('/api/mfa/situacao'),

  /** 1ª ativação: só `senhaAtual`. Trocar autenticador com MFA já ativo: `codigoAtual` também é exigido. */
  iniciar: (senhaAtual: string, codigoAtual?: string | null) =>
    api.post<IniciarMfaResposta>('/api/mfa/iniciar', { senhaAtual, codigoAtual }),

  confirmar: (codigo: string) =>
    api.post<ConfirmarMfaResposta>('/api/mfa/confirmar', { codigo }),

  desativar: (senhaAtual: string, codigo: string) =>
    api.post<void>('/api/mfa/desativar', { senhaAtual, codigo }),

  regenerarCodigos: (senhaAtual: string, codigo: string) =>
    api.post<RegenerarCodigosResposta>('/api/mfa/codigos', { senhaAtual, codigo }),

  /** Revoga TODOS os dispositivos confiáveis do próprio atendente. Sem step-up: só aumenta segurança. */
  esquecerDispositivosConfiaveis: () =>
    api.post<void>('/api/mfa/dispositivos-confiaveis/esquecer', undefined),

  // ------------------------------------------- matrícula privilegiada (ticket)

  iniciarMatriculaPrivilegiada: (email: string, senha: string, ticket: string) =>
    api.post<DesafioMfa>('/api/mfa/matricula-privilegiada/sessao', { email, senha, ticket }),

  iniciarAutenticadorViaDesafio: (desafio: string) =>
    api.post<IniciarMfaResposta>('/api/mfa/matricula-privilegiada/iniciar-autenticador', { desafio }),

  confirmarViaDesafio: (desafio: string, codigo: string) =>
    api.post<ConfirmarMfaResposta>('/api/mfa/matricula-privilegiada/confirmar', { desafio, codigo }),

  // ------------------------------------------------------------- política

  ativarObrigatoriedade: (senhaDoAtor: string, codigoDoAtor: string, motivo: string) =>
    api.post<Sessao>('/api/mfa/politica/obrigatorio-para-administradores', {
      senhaDoAtor,
      codigoDoAtor,
      motivo,
    }),
}

// Conversas -----------------------------------------------------------------

export const conversasApi = {
  listar: (filtro: FiltroDeConversas) =>
    api.get<Paginado<Conversa>>(`/api/conversas${montarQuery({ ...filtro })}`),

  resumo: () => api.get<ResumoDoPainel>('/api/conversas/resumo'),

  obter: (id: string) => api.get<ConversaDetalhada>(`/api/conversas/${id}`),

  criar: (dados: {
    clienteId: string
    setorId?: string | null
    etapaId?: string | null
    primeiraMensagem?: string | null
    comecarComRobo?: boolean
  }) => api.post<Conversa>('/api/conversas', dados),

  assumir: (id: string) => api.post<Conversa>(`/api/conversas/${id}/assumir`),

  liberar: (id: string) => api.post<Conversa>(`/api/conversas/${id}/liberar`),

  /** Pausa o robô nesta conversa; com `sempreSemRoboParaOContato`, para as próximas também. */
  pausarRobo: (id: string, sempreSemRoboParaOContato: boolean) =>
    api.post<Conversa>(`/api/conversas/${id}/robo/pausar`, { sempreSemRoboParaOContato }),

  /** Liga o robô de novo: a conversa volta para a fila. */
  ligarRobo: (id: string, modo: ModoDeRetomadaDoRobo, tirarMarcaDoContato: boolean) =>
    api.post<Conversa>(`/api/conversas/${id}/robo/ligar`, { modo, tirarMarcaDoContato }),

  transferir: (id: string, atendenteDestinoId: string, observacao?: string) =>
    api.post<Conversa>(`/api/conversas/${id}/transferir`, { atendenteDestinoId, observacao }),

  mover: (id: string, dados: { etapaId?: string | null; setorId?: string | null; observacao?: string }) =>
    api.post<Conversa>(`/api/conversas/${id}/mover`, dados),

  vincularServico: (id: string, tipoServicoId: string) =>
    api.post<Conversa>(`/api/conversas/${id}/servico`, { tipoServicoId }),

  encerrar: (id: string, motivo?: string) =>
    api.post<Conversa>(`/api/conversas/${id}/encerrar`, { motivo }),

  reabrir: (id: string) => api.post<Conversa>(`/api/conversas/${id}/reabrir`),

  marcarComoLida: (id: string) => api.post<Conversa>(`/api/conversas/${id}/ler`),

  enviarMensagem: (id: string, conteudo: string, mensagemRespondidaId?: string | null) =>
    api.post<Mensagem>(`/api/conversas/${id}/mensagens`, { conteudo, mensagemRespondidaId }),

  /** FASE 1 apenas — some quando o webhook da Cloud API entrar. */
  simularMensagemDeCliente: (id: string, conteudo: string) =>
    api.post<Mensagem>(`/api/conversas/${id}/mensagens/simular-cliente`, { conteudo }),

  enviarAnexo: (
    id: string,
    arquivo: File,
    legenda?: string,
    mensagemRespondidaId?: string | null,
  ) => {
    const dados = new FormData()
    dados.append('arquivo', arquivo)

    if (legenda) {
      dados.append('legenda', legenda)
    }

    if (mensagemRespondidaId) {
      dados.append('mensagemRespondidaId', mensagemRespondidaId)
    }

    return api.formulario<Mensagem>(`/api/conversas/${id}/mensagens/anexo`, dados)
  },

  /**
   * Binário do anexo. Autenticado — anexo de cartório é documento de cliente, e
   * pasta servida por URL adivinhável entregaria RG de terceiro a quem tentasse.
   */
  baixarAnexo: (id: string, mensagemId: string, sinal?: AbortSignal) =>
    api.blob(`/api/conversas/${id}/mensagens/${mensagemId}/anexo`, sinal),

  // Ações do menu de contexto da mensagem -----------------------------------

  fixarMensagem: (id: string, mensagemId: string, fixar: boolean) =>
    api.post<Mensagem>(
      `/api/conversas/${id}/mensagens/${mensagemId}/fixar${montarQuery({ fixar })}`,
    ),

  /**
   * Troca o texto de uma mensagem já enviada, inclusive no aparelho do cliente.
   * O WhatsApp só aceita nos primeiros 15 minutos e só em texto.
   */
  corrigirMensagem: (id: string, mensagemId: string, conteudo: string) =>
    api.post<Mensagem>(`/api/conversas/${id}/mensagens/${mensagemId}/corrigir`, { conteudo }),

  /**
   * Apaga no aparelho do cliente. NÃO apaga a linha: ela continua no fio,
   * marcada, com o texto preservado.
   */
  apagarMensagemParaTodos: (id: string, mensagemId: string) =>
    api.post<Mensagem>(`/api/conversas/${id}/mensagens/${mensagemId}/apagar-para-todos`),

  /** Oculta ou reexibe. Nunca apaga — a linha permanece no banco. */
  ocultarMensagens: (id: string, mensagemIds: string[], ocultar: boolean) =>
    api.post<{ afetadas: number }>(`/api/conversas/${id}/mensagens/ocultar`, {
      mensagemIds,
      ocultar,
    }),

  encaminharMensagens: (id: string, conversaDestinoId: string, mensagemIds: string[]) =>
    api.post<{ encaminhadas: number }>(`/api/conversas/${id}/mensagens/encaminhar`, {
      conversaDestinoId,
      mensagemIds,
    }),

  destinos: (id: string, busca?: string) =>
    api.get<DestinoDeEncaminhamento[]>(`/api/conversas/${id}/destinos${montarQuery({ busca })}`),
}

// Clientes ------------------------------------------------------------------

export const clientesApi = {
  listar: (filtro: FiltroDeClientes) =>
    api.get<PaginadoDeClientes>(`/api/clientes${montarQuery({ ...filtro })}`),

  opcoes: () => api.get<OpcoesDeClientes>('/api/clientes/opcoes'),

  criar: (dados: DadosDeCliente) => api.post<Cliente>('/api/clientes', dados),

  atualizar: (id: string, dados: DadosDeCliente) =>
    api.put<Cliente>(`/api/clientes/${id}`, dados),

  /** Exclusão física — recusada pelo backend se o cliente tiver atendimento vinculado. */
  excluir: (id: string) => api.excluir<void>(`/api/clientes/${id}`),
}

/**
 * Duas rotas em vez de um `?apenasPrevia=` porque a prévia é editável: o
 * operador desmarca linhas antes de confirmar. Ver docs/PROCESSO-DE-AJUSTE.md.
 */
export const importacaoDeContatosApi = {
  analisarTexto: (texto: string) =>
    api.post<AnalisarImportacaoResponse>('/api/clientes/importacao/analisar', {
      fonte: 'Texto' satisfies FonteDeImportacao,
      texto,
    }),

  /** Lê a agenda do WhatsApp pareado agora — número do canal, seja qual for. */
  analisarWhatsApp: () =>
    api.post<AnalisarImportacaoResponse>('/api/clientes/importacao/analisar', {
      fonte: 'WhatsApp' satisfies FonteDeImportacao,
    }),

  confirmar: (fonte: FonteDeImportacao, linhas: LinhaParaGravar[]) =>
    api.post<ConfirmarImportacaoResponse>('/api/clientes/importacao/confirmar', { fonte, linhas }),
}

// Consultas públicas --------------------------------------------------------

/**
 * Preenchimento assistido do cadastro. Sai pelo servidor da serventia, não pelo
 * navegador — o cache é de todo mundo e só uma máquina precisa de internet.
 *
 * Não há consulta por CPF, e não é esquecimento: não existe fonte pública e legal
 * para dado pessoal a partir de CPF. Ver Modules/Consultas/README.md.
 */
export const consultasApi = {
  cep: (cep: string, sinal?: AbortSignal) =>
    api.get<EnderecoConsultado>(`/api/consultas/cep/${cep}`, sinal),

  cnpj: (cnpj: string, sinal?: AbortSignal) =>
    api.get<EmpresaConsultada>(`/api/consultas/cnpj/${cnpj}`, sinal),
}

// Canal de WhatsApp ---------------------------------------------------------

/**
 * Pareamento do número. Só Administrador — conectar o WhatsApp da serventia é
 * decisão de quem responde por ela.
 */
export const whatsappApi = {
  situacao: () => api.get<SituacaoDoCanal>('/api/whatsapp/situacao'),

  /** Confirma webhook/vínculo e só devolve QR se a sessão estiver fechada. */
  parear: () => api.post<SituacaoDoCanal>('/api/whatsapp/parear'),

  /** Encerra a sessão atual e devolve um QR novo. Histórico de conversas não é tocado. */
  trocarNumero: () => api.post<SituacaoDoCanal>('/api/whatsapp/trocar-numero'),

  /** Encerra a sessão atual sem parear um número novo. */
  desconectar: () => api.post<SituacaoDoCanal>('/api/whatsapp/desconectar'),

  testarEnvio: (telefone: string) =>
    api.post<TesteDeEnvioDoCanal>('/api/whatsapp/testar-envio', { telefone }),

  conexoes: () => api.get<ConexaoDoCanal[]>('/api/whatsapp/conexoes'),

  regras: () => api.get<RegrasDoCanal>('/api/whatsapp/regras'),

  definirModo: (modo: ModoDeRecebimento) =>
    api.put<RegrasDoCanal>('/api/whatsapp/regras/modo', { modo }),

  adicionarNumero: (dados: { telefone: string; rotulo: string | null; tipo: TipoDeNumeroDoCanal }) =>
    api.post<NumeroDoCanal>('/api/whatsapp/regras/numeros', dados),

  removerNumero: (id: string) => api.excluir<void>(`/api/whatsapp/regras/numeros/${id}`),

  descartes: () => api.get<DescartesDoCanal>('/api/whatsapp/descartes'),
}

// Acesso aos dados (blindagem LGPD) -----------------------------------------

export const segurancaApi = {
  acessos: (filtro: FiltroDeAcessos) =>
    api.get<Paginado<RegistroDeAcesso>>(`/api/seguranca/acessos${montarQuery({ ...filtro })}`),

  eventos: (filtro: FiltroDeEventos) =>
    api.get<Paginado<EventoDeSeguranca>>(`/api/seguranca/eventos${montarQuery({ ...filtro })}`),

  protecao: () => api.get<ProtecaoAtiva>('/api/seguranca/protecao'),
}

// Acesso técnico autorizado ---------------------------------------------------

export const acessoTecnicoApi = {
  solicitar: (dados: SolicitarAcessoTecnico) =>
    api.post<AutorizacaoDeAcessoTecnico>('/api/seguranca/acesso-tecnico/solicitacoes', dados),

  aprovar: (id: string) =>
    api.post<AutorizacaoDeAcessoTecnico>(`/api/seguranca/acesso-tecnico/solicitacoes/${id}/aprovar`, {}),

  negar: (id: string) =>
    api.post<AutorizacaoDeAcessoTecnico>(`/api/seguranca/acesso-tecnico/solicitacoes/${id}/negar`),

  revogar: (id: string) =>
    api.post<AutorizacaoDeAcessoTecnico>(`/api/seguranca/acesso-tecnico/solicitacoes/${id}/revogar`),

  /** `undefined` quando não há nada ativo (o backend devolve 204, sem corpo). */
  ativa: () => api.get<AcessoTecnicoAtivo | undefined>('/api/seguranca/acesso-tecnico/ativa'),

  listar: (filtro: FiltroDeAcessoTecnico) =>
    api.get<Paginado<AutorizacaoDeAcessoTecnico>>(
      `/api/seguranca/acesso-tecnico/solicitacoes${montarQuery({ ...filtro })}`,
    ),
}

// Setores e etapas ----------------------------------------------------------

interface DadosDeSetor {
  nome: string
  descricao?: string | null
  ordem: number
  ativo: boolean
  /** Ausente mantém o que está gravado. */
  modoDoRobo?: ModoDoRoboNoSetor
}

export const setoresApi = {
  listar: (incluirInativos = false) =>
    api.get<Setor[]>(`/api/setores${montarQuery({ incluirInativos })}`),

  criar: (dados: DadosDeSetor) => api.post<Setor>('/api/setores', dados),

  atualizar: (id: string, dados: DadosDeSetor) => api.put<Setor>(`/api/setores/${id}`, dados),
}

export const etapasApi = {
  listar: (incluirInativas = false) =>
    api.get<Etapa[]>(`/api/etapas${montarQuery({ incluirInativas })}`),

  criar: (dados: {
    nome: string
    descricao?: string | null
    ordem: number
    cor: string
    ehFinal: boolean
    ativo: boolean
  }) => api.post<Etapa>('/api/etapas', dados),

  atualizar: (
    id: string,
    dados: {
      nome: string
      descricao?: string | null
      ordem: number
      cor: string
      ehFinal: boolean
      ativo: boolean
    },
  ) => api.put<Etapa>(`/api/etapas/${id}`, dados),

  reordenar: (idsNaOrdem: string[]) => api.post<Etapa[]>('/api/etapas/reordenar', idsNaOrdem),
}

// Serventia -----------------------------------------------------------------

export const serventiaApi = {
  aparencia: () =>
    api.get<{ aparenciaDaConversa: AparenciaDaConversa }>('/api/serventia/aparencia'),

  definirAparencia: (aparenciaDaConversa: AparenciaDaConversa) =>
    api.put<{ aparenciaDaConversa: AparenciaDaConversa }>('/api/serventia/aparencia', {
      aparenciaDaConversa,
    }),

  encerramentoAutomatico: () =>
    api.get<EncerramentoAutomatico>('/api/serventia/encerramento-automatico'),

  definirEncerramentoAutomatico: (dados: EncerramentoAutomatico) =>
    api.put<EncerramentoAutomatico>('/api/serventia/encerramento-automatico', dados),

  chavesGerais: () => api.get<ChavesGerais>('/api/serventia/chaves-gerais'),

  definirMensagensAutomaticas: (habilitada: boolean) =>
    api.put<ChavesGerais>('/api/serventia/chaves-gerais/mensagens-automaticas', { habilitada }),

  definirRespostaInteligente: (habilitada: boolean) =>
    api.put<ChavesGerais>('/api/serventia/chaves-gerais/resposta-inteligente', { habilitada }),
}

// Automacao -----------------------------------------------------------------

interface DadosDeMensagemAutomatica {
  gatilho: GatilhoDeMensagem
  etapaAlvoId?: string | null
  conteudo: string
  ativo: boolean
}

export const mensagensAutomaticasApi = {
  listar: (incluirInativas = false) =>
    api.get<MensagemAutomatica[]>(`/api/mensagens-automaticas${montarQuery({ incluirInativas })}`),

  criar: (dados: DadosDeMensagemAutomatica) =>
    api.post<MensagemAutomatica>('/api/mensagens-automaticas', dados),

  atualizar: (id: string, dados: DadosDeMensagemAutomatica) =>
    api.put<MensagemAutomatica>(`/api/mensagens-automaticas/${id}`, dados),
}

interface DadosDeRespostaRapida {
  atalho: string
  titulo: string
  conteudo: string
  ativo: boolean
}

export const respostasRapidasApi = {
  listar: (incluirInativas = false) =>
    api.get<RespostaRapida[]>(`/api/respostas-rapidas${montarQuery({ incluirInativas })}`),

  criar: (dados: DadosDeRespostaRapida) =>
    api.post<RespostaRapida>('/api/respostas-rapidas', dados),

  atualizar: (id: string, dados: DadosDeRespostaRapida) =>
    api.put<RespostaRapida>(`/api/respostas-rapidas/${id}`, dados),
}

interface DadosDeConhecimentoDeIa {
  titulo: string
  conteudo: string
  tema: string
  tipoServicoId: string | null
  vigenteDesde: string | null
  vigenteAte: string | null
  ativo: boolean
}

export const conhecimentoDeIaApi = {
  listar: (incluirInativos = false) =>
    api.get<ConhecimentoDeIa[]>(`/api/conhecimento-de-ia${montarQuery({ incluirInativos })}`),

  criar: (dados: DadosDeConhecimentoDeIa) =>
    api.post<ConhecimentoDeIa>('/api/conhecimento-de-ia', dados),

  atualizar: (id: string, dados: DadosDeConhecimentoDeIa) =>
    api.put<ConhecimentoDeIa>(`/api/conhecimento-de-ia/${id}`, dados),

  historico: (id: string) =>
    api.get<VersaoDeConhecimentoDeIa[]>(`/api/conhecimento-de-ia/${id}/historico`),
}

// Servicos ------------------------------------------------------------------

export const servicosApi = {
  listar: (incluirInativos = false) =>
    api.get<TipoServico[]>(`/api/tipos-servico${montarQuery({ incluirInativos })}`),

  criar: (dados: unknown) => api.post<TipoServico>('/api/tipos-servico', dados),

  atualizar: (id: string, dados: unknown) => api.put<TipoServico>(`/api/tipos-servico/${id}`, dados),
}

// Indicadores -----------------------------------------------------------------

interface FiltroDeIndicadores {
  de?: string
  ate?: string
  escopo?: 'todas' | 'minhas'
}

export const indicadoresApi = {
  /** Aberto a qualquer autenticado — cada perfil vê os campos de saúde que sua permissão libera. */
  visaoGeral: (filtro: FiltroDeIndicadores) =>
    api.get<VisaoGeral>(`/api/indicadores/visao-geral${montarQuery({ ...filtro })}`),

  /** Restrito a Supervisor/Administrador — o backend confere, não só a tela. */
  relatorio: (filtro: Omit<FiltroDeIndicadores, 'escopo'>) =>
    api.get<Relatorio>(`/api/indicadores/relatorio${montarQuery({ ...filtro })}`),

  alertas: () => api.get<AlertaOperacional[]>('/api/indicadores/alertas'),

  // --------------------------- Abas da tela de Indicadores e Relatórios
  // Uma chamada por aba: trocar de aba não recarrega o que já está na tela, e
  // nenhuma aba paga a agregação das outras três.

  /** Aba Dashboard. Sem filtro — as janelas do painel são fixas por definição. */
  painel: () => api.get<Painel>('/api/indicadores/painel'),

  relatorioDeAtendimentos: (filtro: Omit<FiltroDeIndicadores, 'escopo'>) =>
    api.get<RelatorioDeAtendimentos>(`/api/indicadores/atendimentos${montarQuery({ ...filtro })}`),

  relatorioDeAtendentes: (filtro: Omit<FiltroDeIndicadores, 'escopo'>) =>
    api.get<RelatorioDeAtendentes>(`/api/indicadores/atendentes${montarQuery({ ...filtro })}`),

  relatorioDeClientes: (filtro: Omit<FiltroDeIndicadores, 'escopo'>) =>
    api.get<RelatorioDeClientes>(`/api/indicadores/clientes${montarQuery({ ...filtro })}`),
}

// IA --------------------------------------------------------------------------

export const iaApi = {
  /** `de`/`ate` omitidos caem no dia UTC corrente — o mesmo recorte de sempre. */
  situacao: (filtro?: { de?: string; ate?: string }) =>
    api.get<SituacaoDaIa>(`/api/ia/situacao${montarQuery({ ...filtro })}`),
  /** A tela do gestor (`/automacao/ia`). `de`/`ate` omitidos caem nos últimos 7 dias. */
  analise: (filtro?: { de?: string; ate?: string }) =>
    api.get<AnaliseDaIa>(`/api/ia/analise${montarQuery({ ...filtro })}`),
}

// Atendentes ----------------------------------------------------------------

export const atendentesApi = {
  listar: (incluirInativos = false) =>
    api.get<Atendente[]>(`/api/atendentes${montarQuery({ incluirInativos })}`),

  obter: (id: string) => api.get<Atendente>(`/api/atendentes/${id}`),

  criar: (dados: {
    nome: string
    email: string
    senha: string
    perfil: PerfilDeAtendente
    cargo: CargoNaServentia
    setorPadraoId?: string | null
    /** Blindagem LGPD — decidido só aqui, nunca editável depois. */
    ehContaTecnica?: boolean
  }) => api.post<Atendente>('/api/atendentes', dados),

  atualizar: (
    id: string,
    dados: {
      nome: string
      email: string
      perfil: PerfilDeAtendente
      cargo: CargoNaServentia
      setorPadraoId?: string | null
      ativo: boolean
    },
  ) => api.put<Atendente>(`/api/atendentes/${id}`, dados),

  redefinirSenha: (id: string, novaSenha: string) =>
    api.post<void>(`/api/atendentes/${id}/senha`, { novaSenha }),

  /** Exclusão física — só permitida quando o atendente não tem nenhum histórico no sistema. */
  excluir: (id: string) => api.excluir<void>(`/api/atendentes/${id}`),

  /** SEC-03 — emite ticket de matrícula privilegiada fora de banda. Step-up do próprio ator. */
  emitirTicketDeMfa: (id: string, senhaDoAtor: string, codigoDoAtor: string) =>
    api.post<TicketDeMfa>(`/api/atendentes/${id}/mfa/ticket`, { senhaDoAtor, codigoDoAtor }),

  /** SEC-03 — reset administrativo: remove o MFA do alvo, sobe TokenVersion. Alvo Titular exige ator Titular. */
  resetarMfa: (id: string, senhaDoAtor: string, codigoDoAtor: string, motivo: string) =>
    api.post<void>(`/api/atendentes/${id}/mfa/reset`, { senhaDoAtor, codigoDoAtor, motivo }),

  /** Permissões granulares: o conjunto efetivo do alvo, o catálogo inteiro e os 6 modelos prontos. */
  obterPermissoes: (id: string) => api.get<PermissoesDoAtendente>(`/api/atendentes/${id}/permissoes`),

  /**
   * A meta-permissão. `equipe.permissoes` decide quem chega aqui — o backend
   * aplica as travas de verdade (não editar a si mesmo, não conceder o que
   * não tem, sobra um administrador, Titular protegido).
   */
  definirPermissoes: (id: string, dados: { modelo: ModeloDePermissao | null; chaves: string[] }) =>
    api.put<void>(`/api/atendentes/${id}/permissoes`, dados),
}

// Devedores -------------------------------------------------------------------

export const devedoresApi = {
  listar: (filtro?: FiltroDeDevedores) =>
    api.get<ListagemDeDevedores>(`/api/devedores${montarQuery({ ...filtro })}`),

  obterPorCliente: (clienteId: string) =>
    api.get<ResumoDeDividaDoCliente>(`/api/devedores/cliente/${clienteId}`),

  obter: (id: string) => api.get<PendenciaFinanceira>(`/api/devedores/${id}`),

  criar: (dados: CriarPendenciaFinanceira) =>
    api.post<PendenciaFinanceira>('/api/devedores', dados),

  atualizar: (id: string, dados: AtualizarPendenciaFinanceira) =>
    api.put<PendenciaFinanceira>(`/api/devedores/${id}`, dados),

  registrarPagamento: (id: string, dados: RegistrarPagamentoDePendencia) =>
    api.post<PendenciaFinanceira>(`/api/devedores/${id}/pagamentos`, dados),

  reabrir: (id: string) => api.post<PendenciaFinanceira>(`/api/devedores/${id}/reabrir`),

  cancelar: (id: string) => api.excluir<void>(`/api/devedores/${id}`),

  cobrarNoWhatsapp: (dados: CobrarNoWhatsapp) =>
    api.post<CobrarNoWhatsappResposta>('/api/devedores/cobrar-whatsapp', dados),

  abrirConversa: (clienteId: string) =>
    api.post<AbrirConversaComDevedorResposta>(`/api/devedores/cliente/${clienteId}/abrir-conversa`),

  importarEmLote: (dados: ImportarDevedoresEmLote) =>
    api.post<ResultadoDeImportacaoDeDevedores>('/api/devedores/importacao', dados),
}

