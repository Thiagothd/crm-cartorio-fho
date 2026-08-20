import { useEffect, useState } from 'react'

/**
 * True quando a largura da janela está abaixo do valor informado, em pixels.
 *
 * `matchMedia`, e não um listener de `resize` cru: dispara só quando o
 * resultado da consulta muda, não a cada pixel arrastado — é a mesma
 * primitiva que o CSS usa por baixo dos breakpoints, só que legível em JS.
 *
 * Existe para o caso que uma classe `md:` sozinha não resolve: decidir um
 * ESTADO (menu e lista de conversas recolhidos automaticamente conforme a
 * janela encolhe), que depois se combina com a preferência manual que já é
 * salva no localStorage. Puro CSS não teria como alimentar esse estado.
 */
export function usarAbaixoDe(largura: number) {
  const consulta = `(max-width: ${largura - 1}px)`

  const [abaixo, setAbaixo] = useState(() =>
    typeof window === 'undefined' ? false : window.matchMedia(consulta).matches,
  )

  useEffect(() => {
    const mql = window.matchMedia(consulta)
    setAbaixo(mql.matches)

    function aoMudar(evento: MediaQueryListEvent) {
      setAbaixo(evento.matches)
    }

    mql.addEventListener('change', aoMudar)
    return () => mql.removeEventListener('change', aoMudar)
  }, [consulta])

  return abaixo
}
