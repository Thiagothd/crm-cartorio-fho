import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { render } from '@testing-library/react'
import type { RenderOptions } from '@testing-library/react'
import type { ReactElement, ReactNode } from 'react'
import { TooltipProvider } from '@/components/ui/tooltip'

/**
 * Nenhum teste existente precisava de React Query — a suíte nova (shell, rail,
 * Visão Geral) precisa. `retry: false` evita que uma consulta que falha de
 * propósito num teste fique tentando de novo e estoure o timeout do Vitest.
 */
function criarClienteDeTeste() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 0 },
      mutations: { retry: false },
    },
  })
}

export function renderizarComProvedores(
  ui: ReactElement,
  { rota = '/', ...opcoes }: { rota?: string } & RenderOptions = {},
) {
  const clienteDeConsultas = criarClienteDeTeste()

  function Envolvedor({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={clienteDeConsultas}>
        <MemoryRouter initialEntries={[rota]}>
          <TooltipProvider delayDuration={0} skipDelayDuration={0}>
            {children}
          </TooltipProvider>
        </MemoryRouter>
      </QueryClientProvider>
    )
  }

  return { clienteDeConsultas, ...render(ui, { wrapper: Envolvedor, ...opcoes }) }
}
