import { api, montarQuery } from './cliente-http'
import type {
  AlertaOperacional,
  AnalisarImportacaoResponse,
  AparenciaDaConversa,
  Atendente,
  CargoNaServentia,
  Cliente,
  ConexaoDoCanal,
  ConfirmarImportacaoResponse,
  Conversa,
  ConversaDetalhada,
  DadosDeCliente,
  DescartesDoCanal,
  DestinoDeEncaminhamento,
  EmpresaConsultada,
  EncerramentoAutomatico,
  EnderecoConsultado,
  Etapa,
  FiltroDeClientes,
  FiltroDeConversas,
  FonteDeImportacao,
  GatilhoDeMensagem,
  LinhaParaGravar,
  Mensagem,
  MensagemAutomatica,
  ModoDeRecebimento,
  NumeroDoCanal,
  OpcoesDeClientes,
  Paginado,
  PaginadoDeClientes,
  Painel,
  PerfilDeAtendente,
  RegrasDoCanal,
  Relatorio,
  RelatorioDeAtendentes,
  RelatorioDeAtendimentos,
  RelatorioDeClientes,
  RespostaRapida,
  ResumoDoPainel,
  Sessao,
  Setor,
  SituacaoDaIa,
  SituacaoDoCanal,
  TesteDeEnvioDoCanal,
  TipoDeNumeroDoCanal,
  TipoServico,
  VisaoGeral,
} from './tipos'

// Autenticacao --------------------------------------------------------------

export const autenticacaoApi = {
  login: (email: string, senha: string) =>
    api.post<Sessao>('/api/autenticacao/login', { email, senha }),

  sessao: () => api.get<Sessao>('/api/autenticacao/sessao'),

  /** Nome da serventia para a tela de login — anônimo, chamado antes da sessão. */
  serventia: () => api.get<{ nome: string }>('/api/autenticacao/serventia'),

  alterarSenha: (senhaAtual: string, novaSenha: string) =>
    api.post<void>('/api/autenticacao/senha', { senhaAtual, novaSenha }),
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
  }) => api.post<Conversa>('/api/conversas', dados),

  assumir: (id: string) => api.post<Conversa>(`/api/conversas/${id}/assumir`),

  liberar: (id: string) => api.post<Conversa>(`/api/conversas/${id}/liberar`),

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

// Setores e etapas ----------------------------------------------------------

export const setoresApi = {
  listar: (incluirInativos = false) =>
    api.get<Setor[]>(`/api/setores${montarQuery({ incluirInativos })}`),

  criar: (dados: { nome: string; descricao?: string | null; ordem: number; ativo: boolean }) =>
    api.post<Setor>('/api/setores', dados),

  atualizar: (
    id: string,
    dados: { nome: string; descricao?: string | null; ordem: number; ativo: boolean },
  ) => api.put<Setor>(`/api/setores/${id}`, dados),
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
  situacao: () => api.get<SituacaoDaIa>('/api/ia/situacao'),
}

// Atendentes ----------------------------------------------------------------

export const atendentesApi = {
  listar: (incluirInativos = false) =>
    api.get<Atendente[]>(`/api/atendentes${montarQuery({ incluirInativos })}`),

  criar: (dados: {
    nome: string
    email: string
    senha: string
    perfil: PerfilDeAtendente
    cargo: CargoNaServentia
    setorPadraoId?: string | null
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
}
