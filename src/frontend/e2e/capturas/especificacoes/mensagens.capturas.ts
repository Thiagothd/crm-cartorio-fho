import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela, capturarComDestaque } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

test('mensagens automáticas — caminho guiado', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/automacao/mensagens/automaticas')
  await expect(page.getByRole('heading', { name: 'Roteiro guiado' })).toBeVisible()
  await capturarTela(page, 'mensagens/aba-automaticas-caminho.png')
})

test('mensagens automáticas — lista cadastradas e edição', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/automacao/mensagens/automaticas')
  await page.getByRole('button', { name: 'Cadastradas' }).click()
  await capturarTela(page, 'mensagens/aba-automaticas-cadastradas.png')

  await page.getByText('Primeiro contato do cliente').click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await capturarTela(page, 'mensagens/dialogo-mensagem-automatica.png')
})

test('encerramento automático — configuração e prévia', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/automacao/mensagens/encerramento')
  await expect(page.getByLabel('Encerrar conversas paradas')).toBeVisible()
  await capturarTela(page, 'mensagens/aba-encerramento.png')
})

test('respostas rápidas — lista e novo atalho', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/automacao/mensagens/respostas')
  await expect(page.getByText('/horario')).toBeVisible()
  await capturarTela(page, 'mensagens/aba-respostas-rapidas.png')

  await page.getByText('Horário de atendimento').click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await capturarTela(page, 'mensagens/dialogo-resposta-rapida.png')
})

test('IA — situação e análise', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/automacao/ia/situacao')
  await page.waitForTimeout(300)
  await capturarTela(page, 'ia/aba-situacao.png')
})
