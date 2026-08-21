import { createContext, useContext, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

/**
 * O cabeçalho global tem UM slot de ações, e cada página manda o que quiser
 * para lá por portal — em vez de cada página renderizar o próprio cabeçalho
 * (como `CabecalhoDaPagina` fazia antes da Fase 3). O `Shell` é quem provê o
 * container real; sem ele montado (fora do Shell, ou antes do primeiro
 * render), `<AcoesDaPagina>` simplesmente não desenha nada.
 */
export const ContextoDoContainerDeAcoes = createContext<HTMLDivElement | null>(null)

export function AcoesDaPagina({ children }: { children: ReactNode }) {
  const container = useContext(ContextoDoContainerDeAcoes)

  if (!container) {
    return null
  }

  return createPortal(children, container)
}
