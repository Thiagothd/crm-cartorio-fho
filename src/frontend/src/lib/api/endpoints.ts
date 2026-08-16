import { api, montarQuery } from './cliente-http'
import type {
  AparenciaDaConversa,
  Atendente,
  CargoNaServentia,
  Cliente,
  Conversa,
  ConversaDetalhada,
  DadosDeCliente,
  DestinoDeEncaminhamento,
  EmpresaConsultada,
  EnderecoConsultado,
  Etapa,
  FiltroDeClientes,
  FiltroDeConversas,
  GatilhoDeMensagem,
  Mensagem,
  MensagemAutomatica,
  OpcoesDeClientes,
  Paginado,
  PaginadoDeClientes,
  PerfilDeAtendente,
  RespostaRapida,
  ResumoDoPainel,
  Sessao,
  Setor,
  TipoServico,
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
