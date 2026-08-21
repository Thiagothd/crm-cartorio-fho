import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach } from 'vitest'

afterEach(() => {
  cleanup()
})

// -----------------------------------------------------------------------------
// matchMedia
//
// O mock original sempre devolvia matches:false e nunca disparava 'change' —
// bastava para os testes que não dependiam de breakpoint. A Fase 3 introduz
// componentes (rail, barra inferior do celular) cuja decisão inteira é
// `usarAbaixoDe`, então este mock parseia "(max-width: Npx)"/"(min-width: Npx)"
// contra uma largura de teste ajustável e dispara os listeners registrados
// quando o resultado muda — a mesma disciplina do `matchMedia` real.
// -----------------------------------------------------------------------------

let larguraDeTeste = 1440

const escutadoresDeMedia = new Set<{
  query: string
  ouvinte: (evento: MediaQueryListEvent) => void
}>()

function bateComALargura(query: string, largura: number): boolean {
  const maximo = /max-width:\s*(\d+)px/.exec(query)
  const minimo = /min-width:\s*(\d+)px/.exec(query)

  if (maximo && largura > Number(maximo[1])) return false
  if (minimo && largura < Number(minimo[1])) return false

  return true
}

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    get matches() {
      return bateComALargura(query, larguraDeTeste)
    },
    media: query,
    onchange: null,
    addListener: (ouvinte: (evento: MediaQueryListEvent) => void) => {
      escutadoresDeMedia.add({ query, ouvinte })
    },
    removeListener: (ouvinte: (evento: MediaQueryListEvent) => void) => {
      for (const item of escutadoresDeMedia) {
        if (item.query === query && item.ouvinte === ouvinte) escutadoresDeMedia.delete(item)
      }
    },
    addEventListener: (_tipo: string, ouvinte: (evento: MediaQueryListEvent) => void) => {
      escutadoresDeMedia.add({ query, ouvinte })
    },
    removeEventListener: (_tipo: string, ouvinte: (evento: MediaQueryListEvent) => void) => {
      for (const item of escutadoresDeMedia) {
        if (item.query === query && item.ouvinte === ouvinte) escutadoresDeMedia.delete(item)
      }
    },
    dispatchEvent: () => false,
  }),
})

/**
 * Simula a janela mudando de largura — para testar o que depende de
 * `usarAbaixoDe` (rail que recolhe sozinho, lista de conversas, barra
 * inferior do celular). Só dispara o listener cuja consulta MUDOU de
 * resultado, igual ao `matchMedia` do navegador.
 */
export function definirLarguraDeTeste(largura: number) {
  const anterior = larguraDeTeste
  larguraDeTeste = largura

  for (const { query, ouvinte } of escutadoresDeMedia) {
    const batiaAntes = bateComALargura(query, anterior)
    const bateAgora = bateComALargura(query, largura)

    if (batiaAntes !== bateAgora) {
      ouvinte({ matches: bateAgora, media: query } as MediaQueryListEvent)
    }
  }
}

beforeEach(() => {
  larguraDeTeste = 1440
  escutadoresDeMedia.clear()
})

// -----------------------------------------------------------------------------
// ResizeObserver / IntersectionObserver
//
// jsdom não implementa nenhum dos dois. Ficam aqui, e não no teste que
// precisar — um componente que só monta um observador (sem exercitar o
// callback) já quebra sem isso presente no ambiente global.
// -----------------------------------------------------------------------------

class ObservadorDeRedimensionamento {
  observe() {}
  unobserve() {}
  disconnect() {}
}

window.ResizeObserver = ObservadorDeRedimensionamento as unknown as typeof ResizeObserver

class ObservadorDeIntersecao {
  observe() {}
  unobserve() {}
  disconnect() {}
  takeRecords() {
    return []
  }
}

window.IntersectionObserver = ObservadorDeIntersecao as unknown as typeof IntersectionObserver
