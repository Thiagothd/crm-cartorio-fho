import type { Page } from '@playwright/test'
import {
  autenticarLocalmente as autenticarLocalmenteBase,
  criarEstadoE2E,
  entrarPelaTela,
  instalarApiMock as instalarApiMockBase,
  type EstadoE2E,
} from './api-mock'

/**
 * Registra a API fake sem deixar o glob legado da camada `/api/` capturar os
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

/**
 * Os fluxos E2E de produto não estão testando a animação de hover do rail
 * (ela já possui cobertura unitária própria). Fixamos o menu no desktop para
 * iniciar cada cenário em um estado visual conhecido e impedir que a posição
 * sintética do ponteiro do Chromium transforme a animação em interferência
 * sobre botões do conteúdo. Em viewport estreito o próprio CRM ignora essa
 * preferência, preservando os cenários mobile.
 */
export async function autenticarLocalmente(page: Page, perfil: Parameters<typeof autenticarLocalmenteBase>[1] = 'Administrador') {
  await page.addInitScript(() => localStorage.setItem('crm-cartorio.menu-fixado', 'true'))
  await autenticarLocalmenteBase(page, perfil)
}

export {
  criarEstadoE2E,
  entrarPelaTela,
  type EstadoE2E,
}
