import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela, capturarComDestaque } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

test('visão geral — completa', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Ações rápidas' })).toBeVisible()
  await capturarTela(page, 'visao-geral/tela-completa.png', { fullPage: true })

  await capturarComDestaque(page, [{ locator: page.getByText('Agora', { exact: true }), numero: 1 }], 'visao-geral/cartao-agora.png')
})

test('visão geral — fluxo, requer atenção e carga da equipe', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Fluxo de atendimentos' })).toBeVisible()
  await capturarTela(page, 'visao-geral/grafico-fluxo.png')

  await expect(page.getByRole('heading', { name: 'Requer atenção' })).toBeVisible()
  await capturarTela(page, 'visao-geral/requer-atencao.png')

  await expect(page.getByRole('heading', { name: 'Carga da equipe agora' })).toBeVisible()
  await capturarTela(page, 'visao-geral/carga-da-equipe.png')
})
