import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela, capturarElemento } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

test('busca global', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/')
  await page.getByRole('button', { name: 'Buscar atendimentos e clientes' }).click()
  await page.getByPlaceholder(/Protocolo, nome do cliente, telefone/).fill('Maria')
  await page.waitForTimeout(300)
  await capturarTela(page, 'shell/busca-global.png')
})

test('sino de alertas', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/')
  await page.getByRole('button', { name: /Alertas operacionais/ }).click()
  await page.waitForTimeout(200)
  await capturarTela(page, 'shell/sino-de-alertas.png')
})

test('menu de perfil', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/')
  await page.getByRole('button', { name: 'Perfil do atendente' }).click()
  await page.waitForTimeout(200)
  await capturarTela(page, 'shell/menu-de-perfil.png')
})

test('rail de navegação — expandido e recolhido', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/')
  await capturarElemento(page.getByRole('navigation', { name: 'Menu principal' }), 'shell/rail-expandido.png')

  await page.getByRole('button', { name: 'Recolher menu' }).click()
  await page.waitForTimeout(250)
  await capturarElemento(page.getByRole('navigation', { name: 'Menu principal' }), 'shell/rail-recolhido.png')
})
