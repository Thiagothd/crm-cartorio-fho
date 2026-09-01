import type { Page } from '@playwright/test'
import {
  autenticarLocalmente,
  criarEstadoE2E,
  entrarPelaTela,
  instalarApiMock as instalarApiMockBase,
  type EstadoE2E,
} from './api-mock'

/**
 * Registra a API fake sem deixar o glob legado `**/api/**` capturar os
 * módulos-fonte do próprio Vite, por exemplo `/src/lib/api/cliente-http.ts`.
 *
 * No Playwright, a rota registrada por último tem precedência. Portanto esta
 * exceção deixa somente os arquivos locais de código seguirem para o Vite;
 * chamadas reais cujo pathname começa em `/api/` continuam sendo atendidas
 * pelo mock stateful registrado em `instalarApiMockBase`.
 */
export async function instalarApiMock(page: Page, estado = criarEstadoE2E()) {
  await instalarApiMockBase(page, estado)

  await page.route('**/src/lib/api/**', async (route) => {
    await route.continue()
  })

  return estado
}

export {
  autenticarLocalmente,
  criarEstadoE2E,
  entrarPelaTela,
  type EstadoE2E,
}
