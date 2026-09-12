import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela, capturarComDestaque } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

test('catálogo de serviços', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/servicos')
  await expect(page.getByRole('article', { name: 'Reconhecimento de firma' })).toBeVisible()
  await capturarTela(page, 'servicos/lista-servicos.png')

  await capturarComDestaque(page, [{ locator: page.getByRole('button', { name: 'Novo serviço', exact: true }), numero: 1 }], 'servicos/lista-botao-novo.png')
})

test('ficha de serviço expandida (documentos)', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/servicos')
  const ficha = page.getByRole('article', { name: 'Reconhecimento de firma' })
  await ficha.locator('summary').click()
  await capturarTela(page, 'servicos/ficha-expandida.png')
})

test('diálogo de novo serviço', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/servicos')
  await page.getByRole('button', { name: 'Novo serviço', exact: true }).click()
  await capturarTela(page, 'servicos/dialogo-novo-servico.png')
})

test('editar serviço com prazo (SLA)', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/servicos')
  await page.getByRole('button', { name: 'Editar Reconhecimento de firma' }).click()
  await expect(page.getByLabel('Prazo em horas')).toBeVisible()
  await capturarTela(page, 'servicos/dialogo-editar-prazo.png')
})
