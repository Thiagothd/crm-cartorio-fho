/**
 * API da Central de IA — área privada do desenvolvedor da equipe do projeto, fora do
 * sistema normal do cartório (ver `Modules/CentralDeIa` no backend).
 *
 * Deliberadamente SEPARADO de `cliente-http.ts`: a Central usa uma identidade
 * própria, sem `Atendente` nem `TenantId` por trás — reaproveitar `lerToken`/
 * `gravarToken`/`limparToken` de lá misturaria o token do Atendente com o
 * token da Central, e um 401 aqui limparia (ou seria limpo por) a sessão
 * errada. `baseDaApi` e `montarQuery` são funções puras, sem relação com
 * token nenhum — essas duas seguem seguras para importar.
 */
import { baseDaApi, montarQuery } from './cliente-http'

const CHAVE_DO_TOKEN_DA_CENTRAL = 'crm-cartorio.central-ia.token'

export function lerTokenDaCentral(): string | null {
  return localStorage.getItem(CHAVE_DO_TOKEN_DA_CENTRAL)
}

export function gravarTokenDaCentral(token: string) {
  localStorage.setItem(CHAVE_DO_TOKEN_DA_CENTRAL, token)
}

export function limparTokenDaCentral() {
  localStorage.removeItem(CHAVE_DO_TOKEN_DA_CENTRAL)
}

/** Erro vindo da API da Central. Separado de `ErroDaApi` pelo mesmo motivo do token. */
export class ErroDaCentralDeIa extends Error {
  readonly status: number
  readonly codigo: string

  constructor(status: number, codigo: string, mensagem: string) {
    super(mensagem)
    this.name = 'ErroDaCentralDeIa'
    this.status = status
    this.codigo = codigo
  }

  /** Token da Central ausente, expirado ou recusado — a tela volta ao formulário de segredo. */
  get naoAutenticado() {
    return this.status === 401
  }
}

async function requisitar<T>(
  caminho: string,
  opcoes: { metodo?: 'GET' | 'POST'; corpo?: unknown } = {},
): Promise<T> {
  const { metodo = 'GET', corpo } = opcoes
  const cabecalhos: Record<string, string> = { Accept: 'application/json' }

  const token = lerTokenDaCentral()
  if (token) {
    cabecalhos.Authorization = `Bearer ${token}`
  }

  if (corpo !== undefined) {
    cabecalhos['Content-Type'] = 'application/json'
  }

  const resposta = await fetch(`${baseDaApi}${caminho}`, {
    method: metodo,
    headers: cabecalhos,
    body: corpo === undefined ? undefined : JSON.stringify(corpo),
  })

  if (resposta.status === 401) {
    // Token da Central expirado, ausente ou recusado: limpa SÓ este token.
    // Nunca mexe em crm-cartorio.token (sessão do Atendente) nem redireciona
    // para /login — são dois sistemas de autenticação independentes.
    limparTokenDaCentral()
  }

  const texto = await resposta.text()
  const dados: unknown = texto ? JSON.parse(texto) : null

  if (!resposta.ok) {
    const problema = (dados ?? {}) as Record<string, unknown>
    const codigo = typeof problema.codigo === 'string' ? problema.codigo : 'erro_desconhecido'
    const mensagem = typeof problema.title === 'string' ? problema.title : 'Falha na requisição.'
    throw new ErroDaCentralDeIa(resposta.status, codigo, mensagem)
  }

  return dados as T
}

export interface EntrarNaCentralDeIaResposta {
  token: string
  expiraEm: string
}

/** `POST /api/central-ia/entrar`. Grava o token no sucesso. */
export async function entrar(segredo: string): Promise<EntrarNaCentralDeIaResposta> {
  const resposta = await requisitar<EntrarNaCentralDeIaResposta>('/api/central-ia/entrar', {
    metodo: 'POST',
    corpo: { segredo },
  })

  gravarTokenDaCentral(resposta.token)
  return resposta
}

// ---------------------------------------------------------------------------
// Observabilidade (Etapa C) — filtros globais compartilhados pelos 3 endpoints.
// ---------------------------------------------------------------------------

export type ProvedorDeIa = 'Gemini' | 'Anthropic'
export type ResultadoDaChamadaDeIa = 'Entregue' | 'NaoSoubeResponder' | 'Falha'
export type TipoDeFallbackDeIa =
  | 'NaoEhFallback'
  | 'Executado'
  | 'PreventivoDesligado'
  | 'PreventivoDisjuntor'
  | 'PreventivoMargemDeCota'
  | 'Indeterminado'
export type TipoDeFalhaDeIa =
  | 'Nenhuma'
  | 'RateLimit'
  | 'QuotaEsgotada'
  | 'Indisponibilidade'
  | 'Timeout'
  | 'Rede'
  | 'Configuracao'
  | 'RespostaInvalida'

export interface FiltroDeObservabilidade {
  de: string
  ate: string
  tenantId?: string
  provedor?: ProvedorDeIa
  modelo?: string
  resultado?: ResultadoDaChamadaDeIa
  tipoDeFallback?: TipoDeFallbackDeIa
  tipoDeFalha?: TipoDeFalhaDeIa
  codigoHttp?: number
}

export interface SaudeDoProvedor {
  provedor: ProvedorDeIa
  configurado: boolean
  modelo: string | null
  ultimaChamadaEm: string | null
  ultimaChamadaComSucessoEm: string | null
  ultimoErroTipo: string | null
  ultimoErroCodigoHttp: number | null
  ultimoErroEm: string | null
  total429Hoje: number
  ultimo429Em: string | null
  latenciaMediaMsHoje: number | null
  taxaDeSucessoHoje: number | null
  estadoDoCircuito: 'Fechado' | 'Aberto' | 'MeioAberto' | 'SemChave' | 'NaoSeAplica'
  circuitoVoltaATentarEm: string | null
  fallbacksOriginadosHoje: number
  tokensEntradaHoje: number
  tokensSaidaHoje: number
  custoEstimadoUsdHoje: number | null
  limiteRpdInformado: number | null
  requisicoesHoje: number
}

export interface SaudeAgora {
  gemini: SaudeDoProvedor
  anthropic: SaudeDoProvedor
}

export interface KpisDaObservabilidade {
  tentativas: number
  rodadas: number
  entregues: number
  naoSoubeResponder: number
  falhas: number
  taxaDeSucesso: number
  taxaDeFalha: number
  fallbackExecutado: number
  fallbackPreventivoDesligado: number
  fallbackPreventivoDisjuntor: number
  fallbackPreventivoMargemDeCota: number
  tokensEntrada: number
  tokensSaida: number
  tokensPensamento: number
  custoEstimadoUsdTotal: number
  custoEstimadoReaisTotal: number
  custoMedioUsdPorRodada: number | null
  tenantsAtivos: number
  total429: number
  totalTimeouts: number
  larguraDoBaldeDeLatenciaMs: number
  p50LatenciaMs: number | null
  p95LatenciaMs: number | null
  p99LatenciaMs: number | null
}

export interface PontoDaLinhaDoTempo {
  inicio: string
  tentativasGemini: number
  tentativasAnthropic: number
  entregues: number
  naoSoubeResponder: number
  falhas: number
  fallbackExecutado: number
  custoEstimadoUsd: number
  tokensEntrada: number
  tokensSaida: number
}

export interface BaldeDeLatencia {
  inicioMs: number
  total: number
}

export interface HistogramaDeLatencia {
  provedor: string
  larguraDoBaldeMs: number
  baldes: BaldeDeLatencia[]
}

export interface ErroPorCategoria {
  provedor: string
  tipoDeFalha: string
  total: number
}

export interface FallbackPorTipo {
  tipo: string
  total: number
}

export interface DistribuicaoPorModelo {
  provedor: string
  modelo: string
  total: number
  custoEstimadoUsd: number
}

export interface TenantDaObservabilidade {
  tenantId: string
  nome: string
  tentativas: number
  falhas: number
  fallbackExecutado: number
  custoEstimadoUsd: number
  custoEstimadoReais: number
  latenciaMediaMs: number | null
}

export interface ObservabilidadeDaIa {
  periodoDe: string
  periodoAte: string
  saudeAgora: SaudeAgora
  kpis: KpisDaObservabilidade
  linhaDoTempo: PontoDaLinhaDoTempo[]
  histogramaDeLatencia: HistogramaDeLatencia[]
  errosPorCategoria: ErroPorCategoria[]
  fallbacksPorTipo: FallbackPorTipo[]
  distribuicaoPorModelo: DistribuicaoPorModelo[]
  topTenantsPorCusto: TenantDaObservabilidade[]
}

export interface PaginaDeTenants {
  itens: TenantDaObservabilidade[]
  total: number
  pagina: number
  tamanhoDaPagina: number
}

export type OrdenarTenantsPor = 'Tentativas' | 'Custo' | 'Falhas' | 'FallbackExecutado' | 'Latencia'

export interface LinhaDoHistorico {
  id: string
  iniciadaEmUtc: string
  tenantId: string
  tenantNome: string
  provedor: string
  modelo: string
  resultado: string
  tipoDeFallback: string
  tipoDeFalha: string
  codigoHttp: number | null
  latenciaMs: number
  tokensEntrada: number
  tokensSaida: number
  custoEstimadoUsd: number | null
  rodadaId: string | null
}

export interface PaginaDeHistorico {
  itens: LinhaDoHistorico[]
  total: number
  pagina: number
  tamanhoDaPagina: number
}

function paramsDoFiltro(filtro: FiltroDeObservabilidade) {
  return {
    de: filtro.de,
    ate: filtro.ate,
    tenantId: filtro.tenantId,
    provedor: filtro.provedor,
    modelo: filtro.modelo,
    resultado: filtro.resultado,
    tipoDeFallback: filtro.tipoDeFallback,
    tipoDeFalha: filtro.tipoDeFalha,
    codigoHttp: filtro.codigoHttp,
  }
}

/** `GET /api/central-ia/observabilidade`. */
export function observabilidade(filtro: FiltroDeObservabilidade): Promise<ObservabilidadeDaIa> {
  return requisitar<ObservabilidadeDaIa>(`/api/central-ia/observabilidade${montarQuery(paramsDoFiltro(filtro))}`)
}

/** `GET /api/central-ia/tenants` — tabela ordenável, buscável, paginada (o drill-down). */
export function tenants(
  filtro: FiltroDeObservabilidade,
  opcoes: { busca?: string; ordenarPor?: OrdenarTenantsPor; decrescente?: boolean; pagina?: number; tamanhoDaPagina?: number } = {},
): Promise<PaginaDeTenants> {
  return requisitar<PaginaDeTenants>(
    `/api/central-ia/tenants${montarQuery({
      ...paramsDoFiltro(filtro),
      busca: opcoes.busca,
      ordenarPor: opcoes.ordenarPor,
      // `montarQuery` descarta `false` (trata como "ausente") — aqui `false` é um
      // valor válido (ordem ascendente), então vira string explícita para não
      // ser removido do querystring e a ordenação não voltar ao padrão do backend.
      decrescente: opcoes.decrescente === undefined ? undefined : String(opcoes.decrescente),
      pagina: opcoes.pagina,
      tamanhoDaPagina: opcoes.tamanhoDaPagina,
    })}`,
  )
}

/** `GET /api/central-ia/historico` — uma linha por tentativa, paginada. `rodadaId` traz as duas tentativas de uma pergunta lado a lado. */
export function historico(
  filtro: FiltroDeObservabilidade,
  opcoes: { rodadaId?: string; pagina?: number; tamanhoDaPagina?: number } = {},
): Promise<PaginaDeHistorico> {
  return requisitar<PaginaDeHistorico>(
    `/api/central-ia/historico${montarQuery({
      ...paramsDoFiltro(filtro),
      rodadaId: opcoes.rodadaId,
      pagina: opcoes.pagina,
      tamanhoDaPagina: opcoes.tamanhoDaPagina,
    })}`,
  )
}
