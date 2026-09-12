import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela, capturarComDestaque } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

test('tela de login', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await prepararParaCaptura(page, AGORA)
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Entrar no CRM' })).toBeVisible()
  await capturarTela(page, 'login/tela-login.png')

  await capturarComDestaque(
    page,
    [
      { locator: page.getByLabel('E-mail'), numero: 1 },
      { locator: page.getByRole('textbox', { name: 'Senha', exact: true }), numero: 2 },
      { locator: page.getByRole('button', { name: 'Entrar' }), numero: 3 },
    ],
    'login/login-campos-destacados.png',
  )
})

test('tela de login — segunda etapa (código de verificação)', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await prepararParaCaptura(page, AGORA)
  await page.goto('/login')
  await page.getByLabel('E-mail').fill('beatriz@cartoriomodelo.exemplo')
  await page.getByRole('textbox', { name: 'Senha', exact: true }).fill('ComMfa123!')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByRole('heading', { name: 'Confirme sua identidade' })).toBeVisible()
  await capturarTela(page, 'login/login-segunda-etapa.png')
})
