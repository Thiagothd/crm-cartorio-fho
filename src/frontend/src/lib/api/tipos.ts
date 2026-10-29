/**
 * Espelho dos contratos do backend.
 *
 * Os enums viajam como texto (JsonStringEnumConverter no Program.cs), por isso
 * aparecem aqui como uniao de strings — o valor que chega e o valor que se ve.
 */

// Enums ---------------------------------------------------------------------

/**
 * Classe da conta — a exigência de segurança (verificação em duas etapas
 * obrigatória para Administrador, proteção do Titular). NÃO é o que a pessoa
 * acessa: isso é `Atendente.permissoes`, ajustável por tela em
 * `/equipe/:id/permissoes`. As duas coisas coincidem no dia 1 (é o preset que
 * cada Classe carrega quando ninguém personalizou nada), mas são eixos
 * independentes — ver Modules/Usuarios/README.md, "Permissões granulares".
 */
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

export type StatusEntrega = 'Pendente' | 'Enviando' | 'Enviada' | 'Entregue' | 'Lida' | 'Falha'

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
  | 'TriagemComCorrespondencia'
  | 'ServicoEscolhido'
  | 'MenuDeContinuacao'
  | 'PedidoDeAtendente'
  | 'Despedida'
  | 'RespostaSemCorrespondencia'
  | 'ConfirmacaoDeEncerramento'
  | 'EncerramentoPorInatividade'
  | 'PedidoDeNome'
  | 'EncerramentoPeloAtendente'
  | 'PerguntaDeDuvida'
  | 'ContatoSemRobo'

/**
 * Encerramento automático de conversa parada.
 *
 * Minutos nulos = recurso desligado. Horas nulas = a pergunta pode sair a
 * qualquer momento do dia.
 */
export interface EncerramentoAutomatico {
  minutosParaPerguntar: number | null
  minutosParaEncerrar: number | null
  horaInicial: number | null
  horaFinal: number | null
}

/**
 * Chaves gerais de liga/desliga do cartório (`Tenant`, migração 049).
 * Desligar mensagens automáticas cala TODOS os gatilhos, inclusive o bot de
 * triagem. Desligar a IA faz a mensagem cair no atendimento humano de sempre.
 */
export interface ChavesGerais {
  mensagensAutomaticasHabilitadas: boolean
  respostaInteligenteHabilitada: boolean
}

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

// Permissoes granulares -------------------------------------------------------

/**
 * Espelho de `PermissaoDoSistema`/`CatalogoDePermissoes` do backend (chave de
 * texto, não o enum). Fonte única de verdade: Modules/Usuarios/Domain/
 * CatalogoDePermissoes.cs — um item novo lá precisa entrar aqui também.
 */
export type ChaveDePermissao =
  // Atendimento
  | 'atendimento.ver'
  | 'atendimento.assumir'
  | 'atendimento.responder'
  | 'atendimento.abrir_novo'
  | 'atendimento.mover'
  | 'atendimento.vincular_servico'
  | 'atendimento.encerrar'
  | 'atendimento.reabrir'
  | 'atendimento.transferir'
  | 'atendimento.mexer_no_de_terceiro'
  | 'atendimento.simular_cliente'
  // Clientes
  | 'clientes.ver'
  | 'clientes.cadastrar'
  | 'clientes.editar'
  | 'clientes.excluir'
  | 'clientes.importar_agenda'
  // Devedores
  | 'devedores.ver'
  | 'devedores.editar'
  // Catálogo e estrutura
  | 'servicos.ver'
  | 'servicos.editar'
  | 'etapas.ver'
  | 'etapas.editar'
  | 'setores.ver'
  | 'setores.editar'
  // Automação e IA
  | 'automacao.mensagens.ver'
  | 'automacao.mensagens.editar'
  | 'automacao.respostas.ver'
  | 'automacao.respostas.editar'
  | 'automacao.encerramento.ver'
  | 'automacao.encerramento.editar'
  | 'ia.ver'
  | 'ia.conhecimento.ver'
  | 'ia.conhecimento.editar'
  // Relatórios
  | 'relatorios.ver'
  | 'relatorios.equipe'
  | 'relatorios.saude_ia'
  | 'relatorios.saude_whatsapp'
  // WhatsApp
  | 'whatsapp.ver'
  | 'whatsapp.parear'
  | 'whatsapp.regras'
  | 'whatsapp.testar_envio'
  // Aparência e serventia
  | 'aparencia.ver'
  | 'aparencia.editar'
  | 'serventia.editar'
  // Equipe e segurança
  | 'equipe.ver'
  | 'equipe.cadastrar'
  | 'equipe.editar'
  | 'equipe.redefinir_senha'
  | 'equipe.excluir'
  | 'equipe.permissoes'
  | 'equipe.mfa_administrar'
  | 'seguranca.eventos.ver'
  | 'seguranca.acesso_tecnico.aprovar'
  // Suporte
  | 'suporte.solicitar_acesso'

/** Espelho de `ModeloDePermissao` — rótulo do último preset aplicado, nunca a verdade (ver `Atendente.permissoes`). */
export type ModeloDePermissao =
  | 'Recepcao'
  | 'Balcao'
  | 'Catalogo'
  | 'Supervisao'
  | 'Administracao'
  | 'Auditoria'

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
  /** Verificação em duas etapas confirmada e ativa nesta conta. */
  mfaAtivo: boolean
  /** Nulo quando a conta segue a Classe da conta (nunca personalizou permissões). */
  modelo: ModeloDePermissao | null
  /** O conjunto EFETIVO de chaves — já resolvido pelo backend (NULL na base vira o preset da Classe). */
  permissoes: ChaveDePermissao[]
  /** Blindagem LGPD — decidido só na criação, nunca editável depois. */
  ehContaTecnica: boolean
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

// MFA (SEC-03) ----------------------------------------------------------------

/** Tipo do desafio de MFA — decide qual tela a etapa 2 mostra. */
export type TipoDeDesafioMfa = 'SegundoFator' | 'MatriculaPrivilegiada' | 'Ticket'

export interface DesafioMfa {
  /** O DESAFIO em si — a credencial da etapa 2. Nunca um JWT. */
  desafio: string
  tipo: TipoDeDesafioMfa
  expiraEm: string
}

/**
 * Resposta única da etapa 1 do login. `requerMfa` decide qual dos dois campos
 * está preenchido — nunca os dois, nunca nenhum.
 */
export interface LoginEtapa1Resposta {
  requerMfa: boolean
  sessao: Sessao | null
  desafio: DesafioMfa | null
}

export interface IniciarMfaResposta {
  /** URI `otpauth://` — vira o QR Code, renderizado no navegador (o segredo nunca sai daqui). */
  otpauthUri: string
  /** Para quem prefere digitar manualmente em vez de escanear. */
  segredoBase32: string
  expiraEm: string
}

export interface ConfirmarMfaResposta {
  sessao: Sessao
  /** Os 10 códigos em texto — mostrados UMA única vez nesta resposta. */
  codigosDeRecuperacao: string[]
}

export interface RegenerarCodigosResposta {
  codigosDeRecuperacao: string[]
}

export interface MfaSituacao {
  ativo: boolean
  /** A política da serventia exige MFA para este perfil/cargo — quando true, `/mfa/desativar` é recusado. */
  obrigatorio: boolean
  /** `null` quando o MFA não está ativo. */
  codigosDeRecuperacaoRestantes: number | null
  confirmadoEm: string | null
  /** Quantos navegadores têm "confiar por 30 dias" ativo agora — 0 esconde a ação de esquecer. */
  dispositivosConfiaveisAtivos: number
}

export interface TicketDeMfa {
  ticket: string
  expiraEm: string
}

// Permissoes granulares (tela /equipe/:id/permissoes) ------------------------

export interface PermissaoDoCatalogo {
  chave: ChaveDePermissao
  rotulo: string
  descricao: string
  bloco: string
  ehTela: boolean
}

export interface ModeloDePermissaoCatalogado {
  modelo: ModeloDePermissao
  rotulo: string
  descricao: string
  chaves: ChaveDePermissao[]
}

export interface PermissoesDoAtendente {
  atendenteId: string
  /** Nulo quando a conta segue a Classe (nunca personalizou). */
  modelo: ModeloDePermissao | null
  chaves: ChaveDePermissao[]
  /** O catálogo inteiro (~49 itens) — igual para todo mundo, vem do backend para nunca duplicar rótulo/descrição aqui. */
  catalogo: PermissaoDoCatalogo[]
  /** Os 6 modelos prontos, cada um com o conjunto que aplicam. */
  modelos: ModeloDePermissaoCatalogado[]
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
  clienteFotoPerfilUrl: string | null
  /**
   * O nome do contato não serve para se dirigir a ele (".", emoji, o próprio
   * número). A tela mostra o telefone no lugar — a regra vem pronta do backend
   * para os dois lados não divergirem.
   */
  clienteNomePendente: boolean
  /**
   * Sem dono no painel, mas sendo atendida pelo WhatsApp do celular. A lista
   * troca "Na fila" por "No celular"; assumir continua possível.
   */
  atendidaPeloAparelho: boolean
  /**
   * O robô está calado porque uma pessoa assumiu a conversa ou o cliente pediu
   * atendente. O cabeçalho oferece devolver a conversa ao robô.
   */
  emAtendimentoHumano: boolean
  /** Quando alguém pausou o robô nesta conversa. Nulo = robô ligado. */
  roboPausadoEm: string | null
  /** Quem pausou. Nulo com a pausa automática de contato "sem robô". */
  roboPausadoPorNome: string | null
  /** O contato é atendido sempre sem robô. */
  clienteSemRobo: boolean
  /** Quantidade de pendências financeiras abertas deste cliente. */
  clientePendenciasAbertas?: number
  /** Saldo devedor total em aberto deste cliente, em reais. */
  clienteSaldoDevedor?: number
}

/** O que acontece ao ligar o robô de novo numa conversa. */
export type ModoDeRetomadaDoRobo = 'MenuInicial' | 'SoResponder'

/**
 * Como o robô trata um setor: `SoEquipe` aparece no menu mas manda direto para a
 * equipe; `ForaDoMenu` some do menu do robô.
 */
export type ModoDoRoboNoSetor = 'Ligado' | 'SoEquipe' | 'ForaDoMenu'

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
  /**
   * Nome do arquivo anexado, quando há um. `null` na imensa maioria.
   *
   * O caminho no disco **não** vem na resposta: o binário é pedido pelo id da
   * mensagem, num endpoint autenticado que confere tenant e conversa.
   */
  arquivoNome: string | null
  arquivoTipoMime: string | null
  arquivoTamanhoBytes: number | null
  /**
   * A automática veio da Resposta Inteligente (IA), não de uma regra cadastrada.
   * Sempre acompanha `ehAutomatica`. É o que separa "IA" de "Automática" no rótulo.
   */
  geradaPorIa: boolean
  /**
   * O texto foi corrigido depois de enviado. `conteudo` já é a versão vigente —
   * é ela que o cliente vê no WhatsApp.
   */
  corrigida: boolean
  corrigidaEm: string | null
  /** Quem corrigiu. Preenchido só quando `corrigida`. */
  corrigidaPorNome: string | null
  /** O texto como saiu da primeira vez. A correção é auditável por desenho. */
  conteudoOriginal: string | null
  /**
   * Apagada no aparelho do cliente. A linha **continua no fio** com o `conteudo`
   * intacto: o cliente deixou de ver, o registro da serventia não.
   */
  apagadaParaTodos: boolean
  apagadaParaTodosEm: string | null
  /** Quem apagou. Preenchido só quando `apagadaParaTodos`. */
  apagadaParaTodosPorNome: string | null
  /**
   * Resposta dada direto pelo WhatsApp do celular, fora do painel. Vem como
   * `autorTipo: 'Atendente'` sem `atendenteNome`: a serventia respondeu, mas o
   * aparelho não diz quem.
   */
  enviadaPeloAparelho: boolean
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
  aguardandoResposta: number
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
  apenasAguardandoResposta?: boolean
  apenasDevedores?: boolean
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
  fotoPerfilUrl: string | null
  /** O nome cadastrado não serve para chamar o cliente (".", emoji, o número). */
  nomePendente: boolean
  /** Quando o sistema já pediu o nome pelo WhatsApp. Nulo = ninguém tentou. */
  nomePerguntadoEm: string | null
  /** Atendido sempre sem robô: toda conversa nova dele nasce com o robô pausado. */
  atendimentoSemRobo: boolean
  atendimentoSemRoboDesde: string | null
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
  /** Ausente mantém o que está gravado. */
  atendimentoSemRobo?: boolean
}

/** Situação do canal real de WhatsApp — ver docs/DEPLOY-WHATSAPP.md. */
export interface SituacaoDoCanal {
  /** O canal está ligado na configuração do servidor. */
  habilitado: boolean
  /** A instância está pareada e operante. */
  conectado: boolean
  /** Estado: `open`, `connecting`, `close`, `desligado` ou `nao_vinculado`. */
  estado: string
  /** PNG em data-URI enquanto espera a leitura; nulo quando já pareado. */
  qrCodeBase64: string | null
  instancia: string | null
  /** Número pareado (E.164, só dígitos). Nulo enquanto desconectado. */
  numero: string | null
  perfilNome: string | null
  perfilFotoUrl: string | null
  /** Quando a sessão atual pareou. Nulo enquanto desconectado. */
  conectadoEm: string | null
}

export type ModoDeRecebimento = 'Todos' | 'SomenteLiberados' | 'TodosMenosBloqueados'

export type TipoDeNumeroDoCanal = 'Liberado' | 'Bloqueado'

export type MotivoDaConexaoDoCanal = 'Pareamento' | 'Troca' | 'Desconexao' | 'QuedaDoGateway'

export type MotivoDoDescarteDoCanal = 'ForaDaLista' | 'Bloqueado'

/** De onde vem a regra que está valendo agora — ver `RegrasDoCanal.origem`. */
export type OrigemDaRegraDoCanal = 'Banco' | 'Appsettings'

export interface NumeroDoCanal {
  id: string
  telefone: string
  rotulo: string | null
  tipo: TipoDeNumeroDoCanal
  criadoEm: string
  criadoPorNome: string | null
}

export interface RegrasDoCanal {
  modo: ModoDeRecebimento
  /** `Appsettings` enquanto ninguém salvou um modo pela tela — quem manda ainda é o servidor. */
  origem: OrigemDaRegraDoCanal
  liberados: NumeroDoCanal[]
  bloqueados: NumeroDoCanal[]
}

export interface ConexaoDoCanal {
  numero: string | null
  perfilNome: string | null
  conectadoEm: string
  desconectadoEm: string | null
  atendenteNome: string | null
  motivo: MotivoDaConexaoDoCanal
}

export interface TesteDeEnvioDoCanal {
  enviado: boolean
  motivo: string | null
}

export interface DescarteDoCanal {
  telefone: string
  motivo: MotivoDoDescarteDoCanal
  criadoEm: string
}

export interface DescartesDoCanal {
  /** Descartes nas últimas 24h — não desde meia-noite. */
  totalHoje: number
  recentes: DescarteDoCanal[]
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

// Importação de contatos ------------------------------------------------------

/** De onde a lista de contatos veio. `WhatsApp` ainda não está disponível — ver `docs/DEPLOY-WHATSAPP.md`. */
export type FonteDeImportacao = 'Texto' | 'WhatsApp'

/** O que aconteceria com esta linha se a importação for confirmada. */
export type SituacaoDaLinhaImportada =
  | 'Nova'
  | 'JaCadastrada'
  | 'TelefoneInvalido'
  | 'DddInvalido'
  | 'Ambigua'
  | 'DuplicadaNoArquivo'

export interface LinhaDeImportacao {
  numeroDaLinha: number
  nome: string | null
  telefoneBruto: string
  /** Só preenchido quando `situacao` permite gravar (Nova ou JaCadastrada). */
  telefoneE164: string | null
  situacao: SituacaoDaLinhaImportada
  /** Só preenchido quando `situacao` é `JaCadastrada`. */
  nomeDoCadastroExistente: string | null
}

export interface ResumoDaImportacao {
  totalLidas: number
  novas: number
  jaCadastradas: number
  invalidas: number
  ambiguas: number
  duplicadas: number
}

export interface AnalisarImportacaoResponse {
  resumo: ResumoDaImportacao
  linhas: LinhaDeImportacao[]
}

export interface LinhaParaGravar {
  nome: string | null
  telefoneE164: string
}

export interface ConfirmarImportacaoResponse {
  criados: number
  ignorados: number
}

// Configuracoes -------------------------------------------------------------

export interface Setor {
  id: string
  nome: string
  descricao: string | null
  ordem: number
  ativo: boolean
  conversasAtivas: number
  modoDoRobo: ModoDoRoboNoSetor
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

// Conhecimento de IA ----------------------------------------------------------

export interface ConhecimentoDeIa {
  id: string
  titulo: string
  conteudo: string
  tema: string
  tipoServicoId: string | null
  tipoServicoNome: string | null
  /** Datas ISO ("AAAA-MM-DD"). Nulo = sem limite. */
  vigenteDesde: string | null
  vigenteAte: string | null
  versao: number
  criadoPorNome: string | null
  atualizadoPorNome: string | null
  ativo: boolean
  criadoEm: string
  atualizadoEm: string
}

export interface VersaoDeConhecimentoDeIa {
  versao: number
  titulo: string
  conteudo: string
  tema: string
  tipoServicoNome: string | null
  vigenteDesde: string | null
  vigenteAte: string | null
  ativo: boolean
  alteradoPorNome: string | null
  ocorridoEm: string
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
  setorId: string | null
  setorNome: string | null
  documentosNecessarios: string | null
  /** Posição no menu do WhatsApp; empate cai na ordem alfabética. */
  ordem: number
  /** Serviço dentro do qual este aparece (submenu de um nível). */
  servicoPaiId: string | null
  servicoPaiNome: string | null
  /** Texto do robô ao escolher o serviço, no lugar da regra geral. */
  mensagemAoEscolher: string | null
  /** Texto do robô depois que o cliente envia os dados (ex.: chave Pix). */
  mensagemAposDados: string | null
  mensagemAposComprovante: string | null
}

// Indicadores (Visão Geral, Indicadores e Relatórios, sino) --------------------

export interface PeriodoDeIndicadores {
  de: string
  ate: string
  granularidade: 'hora' | 'dia'
}

export interface IndicadoresAgora {
  ativas: number
  naFila: number
  emAlerta: number
  atrasadas: number
  aguardandoResposta: number
}

export interface IndicadoresDoPeriodo {
  abertos: number
  encerrados: number
  /** Métrica honesta no volume de um cartório — ver docs/DESIGN.md. */
  primeiraRespostaMedianaMinutos: number | null
  primeiraRespostaMediaMinutos: number | null
  amostraPrimeiraResposta: number
}

export interface PontoDoFluxo {
  inicio: string
  /** Fluxo: entrou neste dia. */
  abertos: number
  /** Fluxo: saiu neste dia. */
  encerrados: number
  /** Estoque: seguiam abertas ao fim do dia. É a série da linha do gráfico. */
  emAberto: number
  /**
   * `null` quando nenhuma conversa aberta neste dia recebeu resposta humana —
   * nunca zero, que significaria resposta instantânea. Aparece só na dica de
   * hover, não como série, justamente porque não é definida em todo dia.
   */
  primeiraRespostaMedianaMinutos: number | null
}

export interface ItemRequerAtencao {
  conversaId: string
  protocolo: string
  clienteNome: string
  clienteTelefone: string
  clienteFotoPerfilUrl: string | null
  etapaNome: string | null
  etapaCor: string | null
  situacaoPrazo: SituacaoPrazo
  prazoLimite: string | null
  ultimaMensagemEm: string
  /** `null` significa "na fila" — conversa ativa sem responsável. */
  atendenteNome: string | null
}

export interface GrupoRequerAtencao {
  chave: string
  rotulo: string
  total: number
  itens: ItemRequerAtencao[]
}

export interface ServicoMaisProcurado {
  tipoServicoId: string | null
  nome: string
  total: number
  percentual: number
}

export interface SaudeDoWhatsApp {
  habilitado: boolean
  conectado: boolean
  estado: string
}

export interface SaudeDaIa {
  /** Chave geral do cartório, ligada pela tela (IA › Situação). */
  habilitada: boolean
  estaConfigurada: boolean
  modelo: string
  limiteDiario: number
  usadoHoje: number
}

export interface SaudeDaAutomacao {
  regrasAtivas: number
  regrasInativas: number
  /** Chave geral do cartório. Desligada, nenhuma regra ativa dispara. */
  habilitadas: boolean
}

/** Cada campo é `null` quando o perfil não tem permissão para aquele dado. */
export interface SaudeDaOperacao {
  whatsApp: SaudeDoWhatsApp | null
  ia: SaudeDaIa | null
  automacao: SaudeDaAutomacao | null
}

export interface VisaoGeral {
  periodo: PeriodoDeIndicadores
  agora: IndicadoresAgora
  noPeriodo: IndicadoresDoPeriodo
  fluxoDeAtendimentos: PontoDoFluxo[]
  requerAtencao: GrupoRequerAtencao[]
  /** `null` para quem não gere cadastros — o painel simplesmente não aparece. */
  cargaDaEquipe: CargaDoAtendente[] | null
  servicosMaisProcurados: ServicoMaisProcurado[]
  distribuicaoPorEtapa: ContagemPorChave[]
  saude: SaudeDaOperacao
  minhaDistribuicaoPorEtapa: ContagemPorChave[]
}

export interface EvolucaoDaPrimeiraResposta {
  dia: string
  medianaMinutos: number
  amostra: number
}

export interface CargaDoAtendente {
  atendenteId: string
  atendenteNome: string
  ativas: number
  emAlerta: number
  atrasadas: number
}

export interface Encerramentos {
  porAtendente: number
  peloCliente: number
}

export interface AbsorcaoDaAutomacao {
  humanas: number
  automaticas: number
  ia: number
  percentualAutomatizado: number
}

export interface Relatorio {
  periodo: PeriodoDeIndicadores
  evolucaoDaPrimeiraResposta: EvolucaoDaPrimeiraResposta[]
  distribuicaoPorSetor: ContagemPorChave[]
  distribuicaoPorEtapa: ContagemPorChave[]
  /** 24 posições, uma por hora do dia (0 a 23), no fuso local do cartório. */
  horariosDeMaiorDemanda: number[]
  cargaDaEquipe: CargaDoAtendente[]
  encerramentos: Encerramentos
  absorcaoDaAutomacao: AbsorcaoDaAutomacao
}

// ============================== Relatórios — as quatro abas de /indicadores

/** Um mês da série de fluxo. Mesmas grandezas de `PontoDoFluxo`, em outra escala. */
export interface MesDoFluxo {
  mes: string
  abertos: number
  encerrados: number
  /** Estoque ao FIM do mês — é a série da linha. */
  emAberto: number
}

/** Série mensal de uma grandeza só (ex.: clientes novos por mês). */
export interface PontoMensal {
  mes: string
  total: number
}

/** Resumo de uma janela fixa: "Hoje", "Últimos 7 dias", "Este mês". */
export interface ResumoDeJanela {
  chave: string
  rotulo: string
  de: string
  ate: string
  abertos: number
  encerrados: number
  primeiraRespostaMedianaMinutos: number | null
  amostraPrimeiraResposta: number
}

export interface Painel {
  dozeMeses: MesDoFluxo[]
  janelas: ResumoDeJanela[]
}

export interface ClienteNoRanking {
  clienteId: string
  nome: string
  fotoPerfilUrl: string | null
  telefoneWhatsapp: string
  total: number
  /** Sobre o total de atendimentos do período, não sobre o topo da lista. */
  percentual: number
  ultimoAtendimentoEm: string
}

export interface RelatorioDeAtendimentos {
  periodo: PeriodoDeIndicadores
  abertos: number
  encerrados: number
  emAbertoAgora: number
  primeiraRespostaMedianaMinutos: number | null
  amostraPrimeiraResposta: number
  totalMensal: MesDoFluxo[]
  clientesMaisAtendidos: ClienteNoRanking[]
  clientesAtendidosNoPeriodo: number
  distribuicaoPorSetor: ContagemPorChave[]
  distribuicaoPorEtapa: ContagemPorChave[]
  /** 24 posições, uma por hora do dia (0 a 23), no fuso local do cartório. */
  horariosDeMaiorDemanda: number[]
  absorcaoDaAutomacao: AbsorcaoDaAutomacao
}

export interface AtendenteNoRelatorio {
  atendenteId: string
  nome: string
  cargoDescrito: string
  perfil: PerfilDeAtendente
  ativas: number
  emAlerta: number
  atrasadas: number
  encerradosNoPeriodo: number
  primeirasRespostasNoPeriodo: number
  /** `null` quando não respondeu nada de primeira no período — nunca zero. */
  primeiraRespostaMedianaMinutos: number | null
  ultimoAcessoEm: string | null
}

export interface RelatorioDeAtendentes {
  periodo: PeriodoDeIndicadores
  totalDeAtendentes: number
  comConversaAtiva: number
  naFilaSemResponsavel: number
  encerramentos: Encerramentos
  atendentes: AtendenteNoRelatorio[]
}

export interface RelatorioDeClientes {
  periodo: PeriodoDeIndicadores
  total: number
  novosNoPeriodo: number
  comAtendimentoAtivo: number
  pessoaFisica: number
  pessoaJuridica: number
  semDocumento: number
  novosPorMes: PontoMensal[]
  maisAtendidos: ClienteNoRanking[]
}

export type SeveridadeDeAlerta = 'Neutra' | 'Alerta' | 'Critica'

/**
 * Alerta operacional ativo — não é notificação persistida. Não existe "lido"
 * no servidor; o front decide o que é novo comparando `quando` com a última
 * vez que o painel foi aberto neste navegador.
 */
export interface AlertaOperacional {
  id: string
  tipo: string
  severidade: SeveridadeDeAlerta
  titulo: string
  descricao: string | null
  quando: string
  rota: string
}

/** `Disponivel`, `Contingencia`, `SemChave` ou `TetoDiarioAtingido`. */
export type StatusDoProvedorDeIa = 'Disponivel' | 'Contingencia' | 'SemChave' | 'TetoDiarioAtingido'

export interface EstadoDoProvedorDeIa {
  nome: string
  modelo: string
  configurado: boolean
  status: StatusDoProvedorDeIa
  voltaATentarEm: string | null
  motivo: string | null
}

export interface FranquiaDoGemini {
  limitesCadastrados: boolean
  limiteRpd: number | null
  requisicoesHoje: number
  limiteRpm: number | null
  requisicoesNaJanela: number
  limiteTpm: number | null
  tokensNaJanela: number
  percentualDeSeguranca: number
  sugestaoDeLimiteRpd: number | null
}

export interface HojeDaIa {
  totalDeChamadas: number
  atendidasPeloGemini: number
  caidasNoHaiku: number
  percentualGratuito: number
  custoEstimadoHaikuReais: number
  tokensDoGemini: number
}

export interface MotivosDeFallback {
  quota: number
  rateLimit: number
  indisponibilidade: number
  configuracao: number
  outros: number
  /** Gemini foi discado e falhou, e o Haiku assumiu — soma dos 5 campos acima. */
  fallbackExecutado: number
  /** Gemini nunca foi discado (desligado, disjuntor aberto, margem de cota) e o Haiku respondeu sozinho. */
  fallbackPreventivo: number
}

export interface EconomiaDaIa {
  custoRealHaikuReais: number
  custoSeFosseTudoHaikuReais: number
  economiaEstimadaReais: number
  percentualDeEconomia: number
}

export interface SituacaoDaIa {
  habilitada: boolean
  estaConfigurada: boolean
  gemini: EstadoDoProvedorDeIa
  anthropic: EstadoDoProvedorDeIa
  limiteDiario: number
  usadoHojeGeral: number
  limiteDiarioHaiku: number
  usadoHojeHaiku: number
  franquia: FranquiaDoGemini
  hoje: HojeDaIa
  motivosDeFallback: MotivosDeFallback
  economia: EconomiaDaIa
  periodoDe: string
  periodoAte: string
}

// --- Análise da IA (Etapa B — /automacao/ia do gestor) ----------------------

export interface PeriodoDaAnaliseDeIa {
  de: string
  ate: string
  granularidade: 'hora' | 'dia'
}

/** `status`: `Atendendo` | `Contingencia` | `Desligada` — nunca nome de provedor. */
export interface SituacaoAgoraDaIa {
  status: 'Atendendo' | 'Contingencia' | 'Desligada'
  detalhe: string | null
}

export interface ValorEntregueDaIa {
  /** `null` quando o período começa antes de `rodadasMedidasDesde` — a tela mostra "—", nunca um número sub-contado. */
  perguntasAtendidas: number | null
  perguntasAtendidasDeltaPercentual: number | null
  taxaDeResolucao: number
  taxaDeResolucaoDeltaPontos: number | null
  naoSoubeResponder: number
  naoSoubeResponderDeltaPercentual: number | null
  tempoTipicoDeRespostaSegundos: number | null
  tempoTipicoDeRespostaDeltaPercentual: number | null
}

export interface PontoDeVolumeDeIa {
  inicio: string
  entregues: number
  naoSoubeResponder: number
  falhas: number
}

export interface ResultadoDasTentativasDeIa {
  entregues: number
  naoSoubeResponder: number
  falhas: number
}

export interface AssuntoMaisPerguntado {
  tipoServicoId: string | null
  nome: string
  total: number
  percentual: number
}

export interface LacunaDeCatalogo {
  categoria: string
  tipoServicoId: string | null
  servicoNome: string
  total: number
}

/** `severidade`: `atencao` | `informativo` — só para o ícone; o texto já carrega o número. */
export interface InsightDaIa {
  severidade: 'atencao' | 'informativo'
  texto: string
}

export interface AnaliseDaIa {
  periodo: PeriodoDaAnaliseDeIa
  situacaoAgora: SituacaoAgoraDaIa
  valorEntregue: ValorEntregueDaIa
  volumeAoLongoDoTempo: PontoDeVolumeDeIa[]
  resultadoDasTentativas: ResultadoDasTentativasDeIa
  assuntosMaisPerguntados: AssuntoMaisPerguntado[]
  horariosDeMaiorUso: number[]
  lacunasDeCatalogo: LacunaDeCatalogo[]
  insights: InsightDaIa[]
  rodadasMedidasDesde: string | null
}

// Acesso aos dados (blindagem LGPD) -----------------------------------------

/** Espelho de `TipoDeAcesso` (Domain/Enums) — o que uma linha da trilha registra. */
export type TipoDeAcesso =
  | 'LeituraDeConversa'
  | 'DownloadDeAnexo'
  | 'LeituraDeFichaDeCliente'
  | 'AcessoTecnicoSolicitado'
  | 'AcessoTecnicoAprovado'
  | 'AcessoTecnicoNegado'
  | 'AcessoTecnicoRevogado'
  | 'ChaveDeDadosRotacionada'
  | 'CifraDeLegadoConcluida'

/** Uma linha da tela "Acesso aos dados" — nomes já resolvidos, nunca conteúdo. */
export interface RegistroDeAcesso {
  id: string
  ocorridoEm: string
  tipo: TipoDeAcesso
  atendenteNome: string | null
  atorExterno: string | null
  conversaProtocolo: string | null
  clienteNome: string | null
  temAnexo: boolean
  autorizacaoDeAcessoTecnicoId: string | null
  ipDeOrigem: string | null
}

export interface FiltroDeAcessos {
  de?: string
  ate?: string
  atendenteId?: string
  conversaId?: string
  pagina?: number
  tamanho?: number
}

/** Espelho de `TipoDeEventoDeSeguranca` (SEC-03) — fato registrado em `EventosDeSeguranca`. */
export type TipoDeEventoDeSeguranca =
  | 'MfaAtivado'
  | 'MfaDesativado'
  | 'CodigoDeRecuperacaoUsado'
  | 'CodigosDeRecuperacaoRegenerados'
  | 'ResetAdministrativo'
  | 'ResetDeEmergencia'
  | 'BloqueioDeMfaPorFalhas'
  | 'TicketDeMatriculaEmitido'
  | 'TicketDeBootstrapEmitido'
  | 'PoliticaDeMfaAtivada'
  | 'TicketDeRematriculaReemitido'
  | 'PermissoesAlteradas'
  | 'LoginRealizado'

export interface EventoDeSeguranca {
  id: string
  ocorridoEm: string
  tipo: TipoDeEventoDeSeguranca
  atendenteNome: string | null
  atorAtendenteNome: string | null
  atorExterno: string | null
  ipDeOrigem: string | null
  motivo: string | null
}

export interface FiltroDeEventos {
  de?: string
  ate?: string
  pagina?: number
  tamanho?: number
}

/** O bloco "Proteção ativa" da tela — o que já está em vigor, sem exigir leitura de código. */
export interface ProtecaoAtiva {
  cifraAtiva: boolean
  versaoDaChave: string | null
  cifraDesde: string | null
  totalDeRegistrosDeAcesso: number
}

// Acesso técnico autorizado ---------------------------------------------------

/** Espelho de `EscopoDeAcesso` (Domain/Enums). */
export type EscopoDeAcesso = 'Geral' | 'Conversa'

/** Espelho de `EstadoDeAutorizacao` (Application) — sempre derivado, nunca uma coluna. */
export type EstadoDeAutorizacao = 'Pendente' | 'Ativa' | 'Expirada' | 'Negada' | 'Revogada'

export interface SolicitarAcessoTecnico {
  motivo: string
  escopo: EscopoDeAcesso
  /** Só quando `escopo` é `'Conversa'` — protocolo, não o id interno (ver backend). */
  conversaProtocolo?: string | null
  duracaoMinutos: number
}

export interface AutorizacaoDeAcessoTecnico {
  id: string
  contaTecnicaId: string
  contaTecnicaNome: string
  motivo: string
  escopo: EscopoDeAcesso
  conversaId: string | null
  conversaProtocolo: string | null
  duracaoSolicitadaMinutos: number
  solicitadaEm: string
  estado: EstadoDeAutorizacao
  aprovadaPorNome: string | null
  validaAte: string | null
  negadaPorNome: string | null
  revogadaPorNome: string | null
}

export interface FiltroDeAcessoTecnico {
  de?: string
  ate?: string
  pagina?: number
  tamanho?: number
}

/** O que a faixa global mostra a QUALQUER usuário do tenant — nunca detalhe sensível. */
export interface AcessoTecnicoAtivo {
  validaAte: string
  motivo: string
}

// Devedores -------------------------------------------------------------------

export type StatusPendencia = 'Aberta' | 'Quitada' | 'Cancelada'

export type MeioDePagamento = 'Pix' | 'Dinheiro' | 'Cartao' | 'Transferencia' | 'Outro'

export type RecorteDeDevedores =
  | 'abertas'
  | 'vencidas'
  | 'hoje'
  | 'sem_cobranca'
  | 'parciais'
  | 'sem_valor'
  | 'quitadas'
  | 'todas'

export type OrdenacaoDeDevedores =
  | 'maior_valor'
  | 'mais_antigas'
  | 'promessa_proxima'
  | 'recentes'
  | 'nome'

export interface FiltroDeDevedores {
  recorte?: RecorteDeDevedores
  status?: StatusPendencia
  clienteId?: string
  tipoServicoId?: string
  busca?: string
  ordenar?: OrdenacaoDeDevedores
}

export interface ResumoDeDevedores {
  totalEmAberto: number
  clientesDevedores: number
  pendenciasAbertas: number
  pendenciasSemValor: number
  promessasVencidasTotal: number
  promessasVencidasQuantidade: number
  promessasParaHojeTotal: number
  promessasParaHojeQuantidade: number
  recuperadoNoMesTotal: number
  recuperadoNoMesQuantidade: number
}

export interface PagamentoDePendencia {
  id: string
  pendenciaFinanceiraId: string
  valor: number
  meioPagamento: MeioDePagamento
  observacao: string | null
  pagoEm: string
  registradoPorAtendenteId: string
  registradoPorNome: string | null
}

export interface PendenciaFinanceira {
  id: string
  clienteId: string
  clienteNome: string
  clienteTelefone: string
  clienteDocumento: string | null
  clienteFotoPerfilUrl: string
  conversaOrigemId: string | null
  conversaAtivaId: string | null
  tipoServicoId: string | null
  tipoServicoNome: string | null
  titulo: string
  observacoes: string | null
  valorOriginal: number | null
  valorPago: number
  saldoRestante: number | null
  dataReferencia: string
  dataPromessaPagamento: string | null
  diasEmAberto: number
  diasDeAtraso: number | null
  status: StatusPendencia
  ultimaCobrancaEm: string | null
  totalCobrancas: number
  registradoPorAtendenteId: string
  registradoPorNome: string | null
  quitadaEm: string | null
  quitadoPorAtendenteId: string | null
  quitadoPorNome: string | null
  criadoEm: string
  pagamentos: PagamentoDePendencia[]
}

export interface DevedorConsolidado {
  clienteId: string
  clienteNome: string
  clienteTelefone: string
  clienteDocumento: string | null
  clienteFotoPerfilUrl: string
  conversaAtivaId: string | null
  quantidadePendenciasAbertas: number
  quantidadeSemValor: number
  valorOriginalTotal: number
  valorPagoTotal: number
  saldoRestanteTotal: number
  dataReferenciaMaisAntiga: string
  proximaPromessaPagamento: string | null
  temPromessaVencida: boolean
  temPromessaHoje: boolean
  ultimaCobrancaEm: string | null
  totalCobrancas: number
  pendencias: PendenciaFinanceira[]
}

export interface ListagemDeDevedores {
  resumo: ResumoDeDevedores
  devedores: DevedorConsolidado[]
  lancamentos: PendenciaFinanceira[]
  totalLancamentos: number
}

export interface ResumoDeDividaDoCliente {
  clienteId: string
  clienteNome: string
  clienteTelefone: string
  pendenciasAbertas: number
  saldoDevedor: number
  pendencias: PendenciaFinanceira[]
}

export interface CriarPendenciaFinanceira {
  clienteId?: string | null
  novoClienteNome?: string | null
  novoClienteTelefone?: string | null
  novoClienteDocumento?: string | null
  conversaOrigemId?: string | null
  tipoServicoId?: string | null
  titulo: string
  observacoes?: string | null
  valorOriginal?: number | null
  dataReferencia?: string | null
  dataPromessaPagamento?: string | null
}

export interface AtualizarPendenciaFinanceira {
  tipoServicoId?: string | null
  titulo: string
  observacoes?: string | null
  valorOriginal?: number | null
  dataReferencia: string
  dataPromessaPagamento?: string | null
}

export interface RegistrarPagamentoDePendencia {
  valor?: number | null
  meioPagamento: MeioDePagamento
  observacao?: string | null
  quitarTotalmente?: boolean
  enviarComprovanteWhatsapp?: boolean
  mensagemComprovante?: string | null
}

export interface CobrarNoWhatsapp {
  clienteId: string
  pendenciaIds?: string[] | null
  mensagem: string
}

export interface CobrarNoWhatsappResposta {
  conversaId: string
  mensagemId: string
  pendenciasAtualizadas: number
}

export interface AbrirConversaComDevedorResposta {
  conversaId: string
  criadaAgora: boolean
}

export interface ItemDeImportacaoDeDevedor {
  clienteId?: string | null
  nome?: string | null
  telefone?: string | null
  documento?: string | null
  titulo?: string | null
  valorOriginal?: number | null
  dataPromessaPagamento?: string | null
  observacoes?: string | null
}

export interface ImportarDevedoresEmLote {
  tituloPadrao?: string | null
  tipoServicoPadraoId?: string | null
  dataPromessaPadrao?: string | null
  itens: ItemDeImportacaoDeDevedor[]
}

export interface ResultadoDeImportacaoDeDevedores {
  pendenciasCriadas: number
  clientesNovosCriados: number
  clientesExistentesVinculados: number
  ignorados: number
  avisos: string[]
}

