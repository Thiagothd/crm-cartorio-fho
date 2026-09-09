import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

test('aparência — as três texturas do fio de mensagens', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/aparencia')
  await expect(page.getByRole('radiogroup', { name: 'Aparência da conversa' })).toBeVisible()
  await capturarTela(page, 'aparencia/tela-aparencia.png')
})
