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
import type { MotivosDeFallback } from './tipos'

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

export type StatusDoDisjuntorDoGemini = 'Disponivel' | 'Contingencia' | 'SemChave'

export interface EstadoGlobalDoGemini {
  status: StatusDoDisjuntorDoGemini
  voltaATentarEm: string | null
  motivo: string | null
}

export interface SituacaoPorTenant {
  tenantId: string
  nomeTenant: string
  totalDeChamadas: number
  sucessos: number
  falhas: number
  atendidasPeloGemini: number
  atendidasPeloHaiku: number
  custoEstimadoUsd: number
  custoEstimadoReais: number
  motivosDeFallback: MotivosDeFallback
}

export interface SituacaoGlobalDaIa {
  gemini: EstadoGlobalDoGemini
  porTenant: SituacaoPorTenant[]
  periodoDe: string
  periodoAte: string
}

/** `GET /api/central-ia/situacao`. `de`/`ate` omitidos caem no dia UTC corrente. */
export function situacao(de?: string, ate?: string): Promise<SituacaoGlobalDaIa> {
  return requisitar<SituacaoGlobalDaIa>(`/api/central-ia/situacao${montarQuery({ de, ate })}`)
}
