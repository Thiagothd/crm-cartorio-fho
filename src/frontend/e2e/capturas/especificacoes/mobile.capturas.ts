import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

test('navegação inferior e folha "Mais"', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/')
  await expect(page.getByRole('navigation', { name: 'Navegação principal' })).toBeVisible()
  await capturarTela(page, 'mobile/navegacao-inferior.png')

  await page.getByRole('button', { name: 'Mais' }).click()
  await page.waitForTimeout(250)
  await capturarTela(page, 'mobile/folha-mais.png')
})

test('atendimento no celular — lista e conversa em telas separadas', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/atendimentos')
  await expect(page.getByText('Maria Aparecida Souza')).toBeVisible()
  await capturarTela(page, 'mobile/atendimento-lista.png')

  await page.getByRole('button', { name: /Maria Aparecida Souza/ }).click()
  await expect(page).toHaveURL(/\/atendimentos\/conversa-rica$/)
  await expect(page.getByText('Perfeito, recebi. Vou dar entrada agora.')).toBeVisible()
  await capturarTela(page, 'mobile/atendimento-conversa.png')
})

test('clientes no celular', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/clientes')
  await expect(page.getByText('Maria Aparecida Souza')).toBeVisible()
  await capturarTela(page, 'mobile/clientes-lista.png')
})
