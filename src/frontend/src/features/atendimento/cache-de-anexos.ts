import { useEffect, useState } from 'react'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { conversasApi } from '@/lib/api/endpoints'

/**
 * O único dono de object URL de anexo em todo o painel.
 *
 * POR QUE ELE EXISTE
 *     O endpoint de anexo responde `Cache-Control: private, no-store` — decisão
 *     deliberada, porque o painel roda num computador de balcão compartilhado e
 *     documento de cliente não fica em disco para o próximo turno. A
 *     consequência é que o navegador NÃO guarda nada: cada montagem de
 *     componente refaz o download inteiro.
 *
 *     Com o fio, a grade da galeria e o visualizador olhando para a mesma foto,
 *     isso seriam três downloads do mesmo JPEG. Pior: a cada mensagem nova o
 *     SignalR invalida a consulta da conversa, o fio re-renderiza, e tudo
 *     recomeçaria. Este módulo passa a ser o único cache que existe.
 *
 * A REGRA QUE NÃO SE QUEBRA
 *     `revokeObjectURL` acontece em UM lugar só: `descartar`, chamado por
 *     `recolher` e pelas limpezas explícitas — e sempre com zero referências.
 *     Fora deste arquivo ninguém chama `createObjectURL` nem `revokeObjectURL`,
 *     inclusive o download, que reaproveita o blob por `salvarAnexo`. Revogar
 *     uma URL enquanto uma imagem ainda aponta para ela é o defeito que este
 *     desenho existe para tornar impossível.
 */

interface EntradaDeAnexo {
  conversaId: string
  url: string | null
  bytes: number
  promessa: Promise<string> | null
  abortador: AbortController | null
  /** Quantos componentes MONTADOS dependem desta URL agora. */
  referencias: number
  /** Último uso, para a carência e para o descarte por menos-recente. */
  usadoEm: number
  erro: string | null
}

const entradas = new Map<string, EntradaDeAnexo>()

/**
 * 80MB e ~150 arquivos. O envio tem teto de 16MB por anexo, então o pior caso
 * realista são cinco fotos grandes na tela ao mesmo tempo — folga confortável.
 * O teto existe para a conversa de dois anos com trezentas fotos, não para o
 * uso normal.
 */
const TETO_DE_BYTES = 80 * 1024 * 1024
const TETO_DE_ITENS = 150

/**
 * Nada é descartado nem abortado antes disso.
 *
 * Cobre dois casos de uma vez: o StrictMode do React 19, que monta, desmonta e
 * remonta na mesma batida em desenvolvimento; e a rolagem rápida da grade, onde
 * uma célula sai e volta à viewport em milissegundos. Sem a carência, os dois
 * cancelariam e refariam a mesma requisição.
 */
const CARENCIA_MS = 400

export interface EstadoDoAnexo {
  url: string | null
  carregando: boolean
  erro: string | null
}

const VAZIO: EstadoDoAnexo = { url: null, carregando: false, erro: null }

function chaveDoAnexo(conversaId: string, mensagemId: string) {
  return conversaId + ':' + mensagemId
}

function ehCancelamento(falha: unknown) {
  return falha instanceof DOMException && falha.name === 'AbortError'
}

function mensagemDeErro(falha: unknown) {
  return falha instanceof ErroDaApi && falha.codigo === 'anexo_nao_encontrado'
    ? 'O arquivo não está no servidor.'
    : 'Não foi possível carregar o arquivo.'
}

function estadoAtualDe(conversaId: string, mensagemId: string): EstadoDoAnexo {
  const entrada = entradas.get(chaveDoAnexo(conversaId, mensagemId))

  if (!entrada) {
    return { url: null, carregando: true, erro: null }
  }

  return {
    url: entrada.url,
    carregando: entrada.promessa !== null,
    erro: entrada.erro,
  }
}

/**
 * Tira a entrada do mapa ANTES de revogar. A ordem importa: enquanto a chave
 * existir, um `adquirir` concorrente poderia devolver uma URL já morta.
 */
function descartar(chave: string) {
  const entrada = entradas.get(chave)

  if (!entrada) {
    return
  }

  entradas.delete(chave)

  if (entrada.url) {
    URL.revokeObjectURL(entrada.url)
  }
}

/**
 * Descarta o que ninguém está usando até voltar aos tetos.
 *
 * Se não houver candidata elegível, PARA em vez de forçar: estourar o teto por
 * um instante é melhor do que apagar a foto que o atendente está olhando.
 */
function recolher() {
  const agora = Date.now()

  function medir() {
    let bytes = 0
    let itens = 0

    for (const entrada of entradas.values()) {
      if (entrada.url) {
        bytes += entrada.bytes
        itens += 1
      }
    }

    return { bytes, itens }
  }

  let medida = medir()

  while (medida.bytes > TETO_DE_BYTES || medida.itens > TETO_DE_ITENS) {
    let chaveAlvo: string | null = null
    let maisAntiga = Number.POSITIVE_INFINITY

    for (const [chave, entrada] of entradas) {
      if (!entrada.url || entrada.referencias > 0) {
        continue
      }

      if (agora - entrada.usadoEm <= CARENCIA_MS) {
        continue
      }

      if (entrada.usadoEm < maisAntiga) {
        maisAntiga = entrada.usadoEm
        chaveAlvo = chave
      }
    }

    if (chaveAlvo === null) {
      return
    }

    descartar(chaveAlvo)
    medida = medir()
  }
}

function adquirir(conversaId: string, mensagemId: string): Promise<string> {
  const chave = chaveDoAnexo(conversaId, mensagemId)
  let entrada = entradas.get(chave)

  if (!entrada) {
    entrada = {
      conversaId,
      url: null,
      bytes: 0,
      promessa: null,
      abortador: null,
      referencias: 0,
      usadoEm: Date.now(),
      erro: null,
    }

    entradas.set(chave, entrada)
  }

  // Síncrono, antes de qualquer espera: é o que garante que uma desmontagem
  // logo em seguida não encontre a contagem em zero por um instante.
  entrada.referencias += 1
  entrada.usadoEm = Date.now()

  if (entrada.url) {
    return Promise.resolve(entrada.url)
  }

  // Falha memorizada. Sem isto, uma conversa com quarenta arquivos perdidos no
  // disco dispararia quarenta requisições a cada re-render do painel.
  if (entrada.erro) {
    return Promise.reject(new Error(entrada.erro))
  }

  if (entrada.promessa) {
    return entrada.promessa
  }

  const abortador = new AbortController()
  const alvo = entrada

  alvo.abortador = abortador
  alvo.promessa = conversasApi
    .baixarAnexo(conversaId, mensagemId, abortador.signal)
    .then((blob) => {
      const url = URL.createObjectURL(blob)

      alvo.url = url
      alvo.bytes = blob.size
      alvo.promessa = null
      alvo.abortador = null
      alvo.usadoEm = Date.now()

      recolher()

      return url
    })
    .catch((falha: unknown) => {
      alvo.promessa = null
      alvo.abortador = null

      // Cancelamento não é falha: a entrada some inteira para que a próxima
      // montagem recomece limpa, em vez de herdar um erro que nunca houve.
      if (ehCancelamento(falha)) {
        entradas.delete(chave)
        throw falha
      }

      alvo.erro = mensagemDeErro(falha)
      throw falha
    })

  return alvo.promessa
}

/** Solta a referência. NUNCA revoga — ver a regra no topo do arquivo. */
function liberar(conversaId: string, mensagemId: string) {
  const chave = chaveDoAnexo(conversaId, mensagemId)
  const entrada = entradas.get(chave)

  if (!entrada) {
    return
  }

  entrada.referencias = Math.max(0, entrada.referencias - 1)
  entrada.usadoEm = Date.now()

  if (entrada.referencias > 0 || !entrada.abortador) {
    return
  }

  // Requisição em voo sem ninguém esperando. Abortar na hora cancelaria o
  // download a cada remontagem do StrictMode e a cada rolagem rápida da grade,
  // então a decisão é reconferida depois da carência.
  const abortador = entrada.abortador

  setTimeout(() => {
    const atual = entradas.get(chave)

    if (atual && atual.referencias === 0 && atual.abortador === abortador) {
      abortador.abort()
    }
  }, CARENCIA_MS)
}

/**
 * O binário de um anexo, compartilhado entre fio, grade e visualizador.
 *
 * `habilitado: false` é o que permite carregamento preguiçoso: a célula fora da
 * viewport não pede nada, e solta a referência que porventura tivesse.
 */
export function usarAnexo(
  conversaId: string,
  mensagemId: string,
  habilitado = true,
): EstadoDoAnexo {
  const [estado, setEstado] = useState<EstadoDoAnexo>(() =>
    habilitado ? estadoAtualDe(conversaId, mensagemId) : VAZIO,
  )

  useEffect(() => {
    if (!habilitado) {
      setEstado(VAZIO)
      return
    }

    let ativo = true

    // Já em cache? O estado sai pronto no primeiro render, sem piscar skeleton.
    setEstado(estadoAtualDe(conversaId, mensagemId))

    adquirir(conversaId, mensagemId)
      .then((url) => {
        if (ativo) {
          setEstado({ url, carregando: false, erro: null })
        }
      })
      .catch((falha: unknown) => {
        if (ativo && !ehCancelamento(falha)) {
          setEstado({ url: null, carregando: false, erro: mensagemDeErro(falha) })
        }
      })

    return () => {
      ativo = false
      liberar(conversaId, mensagemId)
    }
  }, [conversaId, mensagemId, habilitado])

  return estado
}

/**
 * Download explícito, reaproveitando o blob que já está em memória.
 *
 * O código anterior criava uma URL própria e a revogava logo depois do clique —
 * o que, com URL compartilhada, derrubaria a imagem exibida no fio. Aqui a
 * referência é devolvida pelo caminho normal, e quem revoga continua sendo só o
 * `recolher`.
 */
export async function salvarAnexo(conversaId: string, mensagemId: string, nome: string) {
  const url = await adquirir(conversaId, mensagemId)

  try {
    const link = document.createElement('a')
    link.href = url
    link.download = nome
    link.click()
  } finally {
    liberar(conversaId, mensagemId)
  }
}

/** Apaga a falha memorizada para que "tentar de novo" volte a pedir o arquivo. */
export function esquecerAnexo(conversaId: string, mensagemId: string) {
  const chave = chaveDoAnexo(conversaId, mensagemId)
  const entrada = entradas.get(chave)

  if (!entrada) {
    return
  }

  // Entrada com erro nunca tem binário, então não há URL a proteger — e exigir
  // zero referências aqui tornaria "tentar de novo" um clique que não faz nada,
  // já que quem mostra o erro é justamente um componente montado, segurando uma
  // referência. A referência órfã que sobra é inofensiva: o `liberar` dela não
  // encontra mais a chave e não faz nada.
  if (entrada.erro && !entrada.url) {
    entradas.delete(chave)
    return
  }

  if (entrada.referencias === 0) {
    descartar(chave)
  }
}

/**
 * Ao trocar de conversa, o que era da anterior não tem por que segurar memória.
 * Não espera o teto: uma conversa fechada com 40MB de foto é desperdício puro.
 */
export function descartarOutrasConversas(conversaIdAtual: string | null) {
  for (const [chave, entrada] of [...entradas]) {
    if (entrada.conversaId !== conversaIdAtual && entrada.referencias === 0) {
      descartar(chave)
    }
  }
}

/**
 * Sair leva tudo junto. Anexo de cartório é documento de cliente — deixar blob
 * vivo depois do logout anularia a razão de o endpoint ser autenticado.
 */
export function limparCacheDeAnexos() {
  for (const chave of [...entradas.keys()]) {
    entradas.get(chave)?.abortador?.abort()
    descartar(chave)
  }
}
