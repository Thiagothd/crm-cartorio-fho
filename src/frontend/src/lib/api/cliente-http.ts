/**
 * Ponto unico de saida para a API. Componente nunca chama fetch direto —
 * assim token, tratamento de erro e formato de resposta ficam num lugar so.
 */

const CHAVE_DO_TOKEN = 'crm-cartorio.token'

export const baseDaApi = import.meta.env.VITE_API_URL ?? ''

export function lerToken(): string | null {
  return localStorage.getItem(CHAVE_DO_TOKEN)
}

export function gravarToken(token: string) {
  localStorage.setItem(CHAVE_DO_TOKEN, token)
}

export function limparToken() {
  localStorage.removeItem(CHAVE_DO_TOKEN)
}

/**
 * Erro vindo da API com o `codigo` estavel do ProblemDetails.
 * A interface reage ao codigo, nunca ao texto da mensagem.
 */
export class ErroDaApi extends Error {
  readonly status: number
  readonly codigo: string
  readonly detalhes: Record<string, unknown>

  constructor(
    status: number,
    codigo: string,
    mensagem: string,
    detalhes: Record<string, unknown> = {},
  ) {
    super(mensagem)
    this.name = 'ErroDaApi'
    this.status = status
    this.codigo = codigo
    this.detalhes = detalhes
  }

  /** Outro atendente assumiu a conversa primeiro. */
  get ehConflitoDeAtribuicao() {
    return this.codigo === 'conversa_ja_assumida'
  }

  get atendenteQueAssumiu(): string | null {
    const nome = this.detalhes.atendenteNome
    return typeof nome === 'string' ? nome : null
  }
}

type Opcoes = {
  metodo?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  corpo?: unknown
  sinal?: AbortSignal
}

async function requisitar<T>(caminho: string, opcoes: Opcoes = {}): Promise<T> {
  const { metodo = 'GET', corpo, sinal } = opcoes

  const cabecalhos: Record<string, string> = { Accept: 'application/json' }

  const token = lerToken()
  if (token) {
    cabecalhos.Authorization = `Bearer ${token}`
  }

  // FormData vai cru: o navegador precisa escrever o próprio Content-Type com o
  // boundary do multipart. Definir 'application/json' aqui faria o servidor não
  // achar arquivo nenhum no corpo.
  const ehFormulario = corpo instanceof FormData

  if (corpo !== undefined && !ehFormulario) {
    cabecalhos['Content-Type'] = 'application/json'
  }

  const resposta = await fetch(`${baseDaApi}${caminho}`, {
    method: metodo,
    headers: cabecalhos,
    body: corpo === undefined ? undefined : ehFormulario ? corpo : JSON.stringify(corpo),
    signal: sinal,
  })

  if (resposta.status === 401) {
    // Token expirado ou usuario desativado: derruba a sessao e volta ao login.
    limparToken()
    if (!location.pathname.startsWith('/login')) {
      location.assign('/login')
    }
    throw new ErroDaApi(401, 'nao_autenticado', 'Sessao expirada. Entre novamente.')
  }

  if (resposta.status === 204) {
    return undefined as T
  }

  const texto = await resposta.text()
  const dados: unknown = texto ? JSON.parse(texto) : null

  if (!resposta.ok) {
    throw montarErro(resposta.status, dados)
  }

  return dados as T
}

function montarErro(status: number, dados: unknown): ErroDaApi {
  if (dados && typeof dados === 'object') {
    const problema = dados as Record<string, unknown>
    const codigo = typeof problema.codigo === 'string' ? problema.codigo : 'erro_desconhecido'
    const titulo = typeof problema.title === 'string' ? problema.title : 'Falha na requisicao.'

    return new ErroDaApi(status, codigo, titulo, problema)
  }

  return new ErroDaApi(status, 'erro_desconhecido', 'Falha na requisicao.')
}

/**
 * Baixa um arquivo do backend como blob.
 *
 * Existe separado de `requisitar` porque a resposta não é JSON — e porque a
 * alternativa seria pôr o token na URL de um `<img src>`, o que gravaria a
 * credencial no histórico do navegador e em qualquer log de acesso pelo caminho.
 */
async function baixar(caminho: string, sinal?: AbortSignal): Promise<Blob> {
  const cabecalhos: Record<string, string> = {}

  const token = lerToken()
  if (token) {
    cabecalhos.Authorization = `Bearer ${token}`
  }

  const resposta = await fetch(`${baseDaApi}${caminho}`, { headers: cabecalhos, signal: sinal })

  if (resposta.status === 401) {
    limparToken()
    if (!location.pathname.startsWith('/login')) {
      location.assign('/login')
    }
    throw new ErroDaApi(401, 'nao_autenticado', 'Sessao expirada. Entre novamente.')
  }

  if (!resposta.ok) {
    // O erro vem em ProblemDetails mesmo neste endpoint, então vale tentar ler.
    let dados: unknown = null
    try {
      dados = await resposta.json()
    } catch {
      // Corpo vazio ou não-JSON: montarErro devolve o genérico.
    }

    throw montarErro(resposta.status, dados)
  }

  return await resposta.blob()
}

export const api = {
  get: <T>(caminho: string, sinal?: AbortSignal) => requisitar<T>(caminho, { sinal }),

  post: <T>(caminho: string, corpo?: unknown) => requisitar<T>(caminho, { metodo: 'POST', corpo }),

  put: <T>(caminho: string, corpo?: unknown) => requisitar<T>(caminho, { metodo: 'PUT', corpo }),

  /** POST multipart. O navegador escreve o Content-Type com o boundary. */
  formulario: <T>(caminho: string, dados: FormData) =>
    requisitar<T>(caminho, { metodo: 'POST', corpo: dados }),

  blob: baixar,
}

/** Monta query string ignorando valores vazios, nulos ou indefinidos. */
export function montarQuery(parametros: Record<string, unknown>): string {
  const busca = new URLSearchParams()

  for (const [chave, valor] of Object.entries(parametros)) {
    if (valor === undefined || valor === null || valor === '' || valor === false) {
      continue
    }
    busca.set(chave, String(valor))
  }

  const texto = busca.toString()
  return texto ? `?${texto}` : ''
}
