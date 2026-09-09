import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela, capturarComDestaque } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

test('etapas do funil — lista e reordenar', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/estrutura/etapas')
  await expect(page.getByText('Etapas ativas')).toBeVisible()
  await capturarTela(page, 'etapas/lista-etapas.png')

  await capturarComDestaque(page, [{ locator: page.getByRole('button', { name: /^Nova/ }), numero: 1 }], 'etapas/lista-botao-nova.png')
  await page.getByRole('button', { name: /^Nova/ }).click()
  await capturarTela(page, 'etapas/dialogo-nova-etapa.png')
})

test('setores — lista e edição', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/estrutura/setores')
  await expect(page.getByText('Setores ativos')).toBeVisible()
  await capturarTela(page, 'setores/lista-setores.png')

  await page.getByRole('button', { name: 'Editar Tabelionato de Notas' }).click()
  await capturarTela(page, 'setores/dialogo-editar-setor.png')
})

test('WhatsApp — canal conectado, regras e históricos', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/integracoes/whatsapp')
  await expect(page.getByRole('heading', { name: 'Conexão do canal' })).toBeVisible()
  await capturarTela(page, 'whatsapp/tela-completa-conectado.png')

  await capturarComDestaque(page, [{ locator: page.getByRole('heading', { name: 'Quem pode falar com este número' }), numero: 1 }], 'whatsapp/regras-de-numero.png')
  await capturarTela(page, 'whatsapp/mensagens-bloqueadas.png')
})

test('WhatsApp — desconectado, tela de pareamento por QR', async ({ page }) => {
  const estado = criarEstadoDeCaptura()
  estado.canal.situacao = { ...estado.canal.situacao, conectado: false, estado: 'close', numero: null, perfilNome: null }
  await instalarMockDeCapturas(page, estado)
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/integracoes/whatsapp')
  await page.getByRole('button', { name: /Conectar número/i }).click().catch(() => {})
  await page.waitForTimeout(200)
  await capturarTela(page, 'whatsapp/tela-pareamento-qr.png')
})

test('WhatsApp — testar envio', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/integracoes/whatsapp')
  await page.getByLabel('Telefone').fill('38999997777')
  await capturarTela(page, 'whatsapp/teste-de-envio.png')
})

test('Conhecimento da IA — vigência, busca e histórico', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/automacao/ia/conhecimento')
  await expect(page.getByRole('article')).toHaveCount(2)
  await capturarTela(page, 'ia/aba-conhecimento.png')

  await page.getByRole('button', { name: 'Ver histórico de Política de reagendamento' }).click()
  await expect(page.getByRole('dialog')).toContainText('Histórico')
  await capturarTela(page, 'ia/dialogo-historico.png')
  await page.keyboard.press('Escape')

  await page.getByPlaceholder('Título, tema ou texto').fill('reagendamento')
  await capturarTela(page, 'ia/aba-conhecimento-busca.png')
})

test('Conhecimento da IA — novo cadastro', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/automacao/ia/conhecimento')
  await page.getByRole('button', { name: /^Novo/ }).click()
  await capturarTela(page, 'ia/dialogo-novo-conhecimento.png')
})
