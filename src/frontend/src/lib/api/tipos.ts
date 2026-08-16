/**
 * Espelho dos contratos do backend.
 *
 * Os enums viajam como texto (JsonStringEnumConverter no Program.cs), por isso
 * aparecem aqui como uniao de strings — o valor que chega e o valor que se ve.
 */

// Enums ---------------------------------------------------------------------

/** Permissão no sistema — o que a pessoa PODE FAZER aqui dentro. */
export type PerfilDeAtendente = 'Atendente' | 'Supervisor' | 'Administrador'

/**
 * Função na serventia — o que a pessoa É no cartório.
 *
 * Eixo independente do perfil: um escrevente autorizado pode ser Administrador do
 * painel, e o titular pode ter acesso restrito. Fundidos numa coluna só, nenhuma
 * das duas situações era representável.
 */
export type CargoNaServentia =
  | 'NaoInformado'
  | 'Titular'
  | 'Substituto'
  | 'EscreventeAutorizado'
  | 'Escrevente'
  | 'Auxiliar'

export type StatusConversa = 'Ativa' | 'Encerrada'

export type CanalOrigem = 'WhatsApp' | 'Simulado'

export type AutorTipo = 'Cliente' | 'Atendente' | 'Sistema'

export type TipoMensagem = 'Texto' | 'Imagem' | 'Documento' | 'Audio' | 'Video'

export type StatusEntrega = 'Pendente' | 'Enviada' | 'Entregue' | 'Lida' | 'Falha'

export type SituacaoPrazo = 'SemPrazo' | 'EmDia' | 'EmAlerta' | 'Atrasada'

export type TipoMudancaHistorico = 'Abertura' | 'Etapa' | 'Setor' | 'EtapaESetor'

/** Textura de fundo do fio de mensagens. Escolhida por tenant, não por atendente. */
export type AparenciaDaConversa = 'PapelLiso' | 'SeloRepetido' | 'PautaFina'

export type GatilhoDeMensagem =
  | 'NovoAtendimento'
  | 'EtapaAlterada'
  | 'PrazoProximoVencimento'
  | 'PrazoVencido'
  | 'TriagemSemCorrespondencia'

export type CategoriaServico =
  | 'Notas'
  | 'RegistroCivil'
  | 'RegistroImoveis'
  | 'TitulosDocumentos'
  | 'Protesto'
  | 'Outros'

export type TipoDePessoa = 'Fisica' | 'Juridica'

export type EstadoCivil =
  | 'NaoInformado'
  | 'Solteiro'
  | 'Casado'
  | 'Divorciado'
  | 'Viuvo'
  | 'Separado'
  | 'UniaoEstavel'

export type RegimeDeBens =
  | 'NaoInformado'
  | 'ComunhaoParcial'
  | 'ComunhaoUniversal'
  | 'SeparacaoTotal'
  | 'SeparacaoObrigatoria'
  | 'ParticipacaoFinalNosAquestos'

// Autenticacao --------------------------------------------------------------

export interface Atendente {
  id: string
  nome: string
  email: string
  perfil: PerfilDeAtendente
  cargo: CargoNaServentia
  /** Rótulo do cargo, vindo do backend — uma fonte só para o de-para. */
  cargoDescrito: string
  setorPadraoId: string | null
  setorPadraoNome: string | null
  ativo: boolean
  ultimoAcessoEm: string | null
}

export interface Tenant {
  id: string
  razaoSocial: string
  nomeFantasia: string | null
  cnpj: string
  whatsappNumeroExibicao: string | null
  whatsappConfigurado: boolean
}

export interface Sessao {
  token: string
  expiraEm: string
  atendente: Atendente
  tenant: Tenant
}

// Atendimento ---------------------------------------------------------------

export interface Conversa {
  id: string
  protocolo: string
  clienteId: string
  clienteNome: string
  clienteTelefone: string
  atendenteId: string | null
  atendenteNome: string | null
  setorId: string | null
  setorNome: string | null
  etapaId: string | null
  etapaNome: string | null
  etapaCor: string | null
  tipoServicoId: string | null
  tipoServicoNome: string | null
  status: StatusConversa
  iniciadaEm: string
  ultimaMensagemEm: string
  mensagensNaoLidas: number
  /** Recebeu o menu de triagem e espera a escolha do setor pelo cliente. */
  aguardandoTriagem: boolean
  prazoIniciadoEm: string | null
  prazoLimite: string | null
  situacaoPrazo: SituacaoPrazo
  previaUltimaMensagem: string | null
  encerradaEm: string | null
  encerradaPorNome: string | null
  motivoEncerramento: string | null
  reabertaEm: string | null
}

export interface Mensagem {
  id: string
  conversaId: string
  autorTipo: AutorTipo
  atendenteId: string | null
  atendenteNome: string | null
  conteudo: string
  tipo: TipoMensagem
  statusEntrega: StatusEntrega
  /**
   * Gerada por regra de mensagem automática. Sempre acompanha `autorTipo: 'Sistema'`.
   *
   * É o que separa a anotação interna do fio ("Conversa assumida por Fulano") da
   * mensagem endereçada AO CLIENTE — as duas são `Sistema` no banco, e só esta
   * segunda vira balão com o rótulo "Automática".
   */
  ehAutomatica: boolean
  /** Fixada no topo da conversa. Uma por conversa. */
  fixada: boolean
  /**
   * Ocultada do fio. A mensagem **continua no banco e continua vindo na API** —
   * quem oculta esconde de quem atende, não do registro. Não existe exclusão de
   * mensagem em caminho nenhum do sistema.
   */
  oculta: boolean
  /** Quem ocultou. Preenchido só quando `oculta`. */
  ocultaPorNome: string | null
  /** Protocolo da conversa de origem, quando é um encaminhamento. */
  encaminhadaDeProtocolo: string | null
  enviadaEm: string
  lidaEm: string | null
  mensagemRespondidaId: string | null
  /** Autor da mensagem citada — nome do atendente, nome do cliente, ou "Sistema". */
  respondidaAutor: string | null
  /** Texto (já cortado no servidor) da mensagem citada. */
  respondidaConteudo: string | null
}

/** Conversa candidata a receber um encaminhamento. */
export interface DestinoDeEncaminhamento {
  id: string
  protocolo: string
  clienteNome: string
  setorNome: string | null
  atendenteNome: string | null
}

export interface EventoDeHistorico {
  id: string
  tipoMudanca: TipoMudancaHistorico
  etapaAnterior: string | null
  etapaNova: string | null
  setorAnterior: string | null
  setorNovo: string | null
  atendenteNome: string | null
  ocorridoEm: string
  observacao: string | null
}

export interface ConversaDetalhada {
  conversa: Conversa
  mensagens: Mensagem[]
  historico: EventoDeHistorico[]
}

export interface ContagemPorChave {
  id: string | null
  nome: string
  cor: string | null
  total: number
}

export interface ResumoDoPainel {
  totalAtivas: number
  naFila: number
  minhasConversas: number
  encerradasHoje: number
  emAlerta: number
  atrasadas: number
  porEtapa: ContagemPorChave[]
  porSetor: ContagemPorChave[]
}

export interface FiltroDeConversas {
  status?: StatusConversa
  setorId?: string
  etapaId?: string
  atendenteId?: string
  tipoServicoId?: string
  apenasNaFila?: boolean
  apenasMinhas?: boolean
  apenasEncerradasHoje?: boolean
  situacaoPrazo?: SituacaoPrazo
  busca?: string
  pagina?: number
  tamanho?: number
}

export interface Paginado<T> {
  itens: T[]
  total: number
  pagina: number
  tamanho: number
}

// Clientes ------------------------------------------------------------------

export interface Cliente {
  id: string
  nome: string
  tipo: TipoDePessoa
  telefoneWhatsapp: string
  telefoneAlternativo: string | null
  email: string | null
  documento: string | null
  rg: string | null
  orgaoEmissor: string | null
  ufOrgaoEmissor: string | null
  dataNascimento: string | null
  estadoCivil: EstadoCivil
  regimeDeBens: RegimeDeBens
  profissao: string | null
  nacionalidade: string | null
  naturalidade: string | null
  nomeDoPai: string | null
  nomeDaMae: string | null
  razaoSocial: string | null
  representanteLegal: string | null
  cep: string | null
  logradouro: string | null
  numero: string | null
  complemento: string | null
  bairro: string | null
  cidade: string | null
  uf: string | null
  observacoes: string | null
  ativo: boolean
  primeiroContatoEm: string
  totalConversas: number
  temConversaAtiva: boolean
  percentualDeQualificacao: number
  idade: number | null
}

/** Espelha DadosDeCliente do backend — entrada de criação e edição. */
export interface DadosDeCliente {
  nome: string
  telefoneWhatsapp: string
  tipo?: TipoDePessoa
  telefoneAlternativo?: string | null
  email?: string | null
  documento?: string | null
  rg?: string | null
  orgaoEmissor?: string | null
  ufOrgaoEmissor?: string | null
  dataNascimento?: string | null
  estadoCivil?: EstadoCivil
  regimeDeBens?: RegimeDeBens
  profissao?: string | null
  nacionalidade?: string | null
  naturalidade?: string | null
  nomeDoPai?: string | null
  nomeDaMae?: string | null
  razaoSocial?: string | null
  representanteLegal?: string | null
  cep?: string | null
  logradouro?: string | null
  numero?: string | null
  complemento?: string | null
  bairro?: string | null
  cidade?: string | null
  uf?: string | null
  observacoes?: string | null
  ativo?: boolean
}

/**
 * Resultado da consulta pública de CEP e de CNPJ. Não é o formato de nenhum
 * provedor: o backend normaliza os dois antes de entregar.
 */
export interface EnderecoConsultado {
  cep: string
  logradouro: string | null
  bairro: string | null
  cidade: string | null
  uf: string | null
}

export interface EmpresaConsultada {
  cnpj: string
  razaoSocial: string | null
  nomeFantasia: string | null
  endereco: EnderecoConsultado | null
}

export interface FiltroDeClientes {
  busca?: string
  incluirInativos?: boolean
  tipo?: TipoDePessoa
  estadoCivil?: EstadoCivil
  cidade?: string
  semDocumento?: boolean
  semEndereco?: boolean
  comConversaAtiva?: boolean
  ordenar?: 'nome' | 'recentes' | 'qualificacao'
  pagina?: number
  tamanho?: number
}

export interface PaginadoDeClientes {
  itens: Cliente[]
  total: number
  pagina: number
  tamanho: number
}

export interface OpcoesDeClientes {
  cidades: string[]
}

// Configuracoes -------------------------------------------------------------

export interface Setor {
  id: string
  nome: string
  descricao: string | null
  ordem: number
  ativo: boolean
  conversasAtivas: number
}

export interface Etapa {
  id: string
  nome: string
  descricao: string | null
  ordem: number
  cor: string
  ehFinal: boolean
  ativo: boolean
  conversasAtivas: number
}

export interface MensagemAutomatica {
  id: string
  gatilho: GatilhoDeMensagem
  /** Rótulo do gatilho em vocabulário de cartório, vindo do backend. */
  gatilhoDescrito: string
  /** Preenchido apenas quando `gatilho === 'EtapaAlterada'`. */
  etapaAlvoId: string | null
  etapaAlvoNome: string | null
  conteudo: string
  ativo: boolean
}

export interface RespostaRapida {
  id: string
  /** Sem a barra, em minúsculas e sem espaço. O que se digita depois de "/". */
  atalho: string
  titulo: string
  conteudo: string
  ativo: boolean
}

// Servicos ------------------------------------------------------------------

export interface PrazoServico {
  id: string
  prazoHoras: number
  percentualAlerta: number
  apenasDiasUteis: boolean
  prazoDescrito: string
}

export interface TipoServico {
  id: string
  nome: string
  codigo: string | null
  categoria: CategoriaServico
  categoriaDescrita: string
  descricao: string | null
  ativo: boolean
  prazo: PrazoServico | null
  conversasAtivas: number
}
