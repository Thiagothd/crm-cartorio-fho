import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela, capturarComDestaque } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

test('indicadores — dashboard', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/indicadores/dashboard')
  await page.waitForTimeout(300)
  await capturarTela(page, 'indicadores/dashboard.png')

  await capturarComDestaque(
    page,
    [{ locator: page.getByRole('navigation', { name: 'Seções de Indicadores e Relatórios' }), numero: 1 }],
    'indicadores/navegacao-abas.png',
  )
})

test('indicadores — atendimentos', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/indicadores/atendimentos')
  await expect(page.getByText('Abertos no período')).toBeVisible()
  await capturarTela(page, 'indicadores/aba-atendimentos.png')
})

test('indicadores — atendentes', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/indicadores/atendentes')
  await expect(page.getByRole('columnheader', { name: 'Atendente' })).toBeVisible()
  await capturarTela(page, 'indicadores/aba-atendentes.png')
})

test('indicadores — clientes', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/indicadores/clientes')
  await expect(page.getByText('Clientes cadastrados', { exact: true })).toBeVisible()
  await capturarTela(page, 'indicadores/aba-clientes.png')
})
