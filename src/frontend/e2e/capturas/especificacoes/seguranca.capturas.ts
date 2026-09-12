import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

test('tela de segurança (MFA) — ativo', async ({ page }) => {
  const estado = criarEstadoDeCaptura('Administrador')
  estado.mfaAtiva = true
  await instalarMockDeCapturas(page, estado)
  await autenticarComoDemo(page, 'Administrador')
  await prepararParaCaptura(page, AGORA)
  await page.goto('/seguranca')
  await expect(page.getByText(/recuperação restante/i)).toBeVisible()
  await capturarTela(page, 'seguranca/tela-seguranca-ativo.png')
})

test('tela de segurança (MFA) — inativo, ativação', async ({ page }) => {
  const estado = criarEstadoDeCaptura('Supervisor')
  estado.mfaAtiva = false
  await instalarMockDeCapturas(page, estado)
  await autenticarComoDemo(page, 'Supervisor')
  await prepararParaCaptura(page, AGORA)
  await page.goto('/seguranca')
  await capturarTela(page, 'seguranca/tela-seguranca-inativo.png')

  const ativar = page.getByRole('button', { name: /Ativar/i })
  if (await ativar.count()) {
    await ativar.click()
    await page.waitForTimeout(300)
    await capturarTela(page, 'seguranca/ativacao-qrcode.png')
  }
})

test('matrícula de MFA privilegiada', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await prepararParaCaptura(page, AGORA)
  await page.goto('/mfa/matricular')
  await capturarTela(page, 'seguranca/matricula-ticket.png')

  const email = page.getByLabel(/E-mail/i)
  if (await email.count()) {
    await email.fill('beatriz@cartoriomodelo.exemplo')
    const senha = page.getByLabel(/^Senha/i).first()
    if (await senha.count()) await senha.fill('senhaforte123')
    const ticket = page.getByLabel(/Ticket/i)
    if (await ticket.count()) await ticket.fill('TICKET-DEMO-1234-5678')
    const continuar = page.getByRole('button', { name: /Continuar|Avançar|Confirmar/i }).first()
    if (await continuar.count()) {
      await continuar.click()
      await page.waitForTimeout(300)
      await capturarTela(page, 'seguranca/matricula-qrcode.png')
    }
  }
})
