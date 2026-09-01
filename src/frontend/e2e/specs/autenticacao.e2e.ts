import { expect, test } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock-seguro'

test.beforeEach(async ({ page }) => {
  await instalarApiMock(page, criarEstadoE2E())
})

test('exibe o formulario de login com campos acessiveis', async ({ page }) => {
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Entrar no CRM' })).toBeVisible()
  await expect(page.getByLabel('E-mail')).toBeVisible()
  await expect(page.getByLabel('Senha')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Entrar' })).toBeEnabled()
})

test('mantem validacao nativa para campos obrigatorios', async ({ page }) => {
  await page.goto('/login')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByLabel('E-mail')).toBeFocused()
  await expect(page).toHaveURL(/\/login$/)
})

test('mostra ProblemDetails realista quando as credenciais sao invalidas', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('E-mail').fill('admin@teste.local')
  await page.getByLabel('Senha').fill('senha-incorreta')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByRole('alert')).toContainText('E-mail ou senha inválidos.')
  await expect(page).toHaveURL(/\/login$/)
})

test('login valido grava sessao e entra no CRM', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('E-mail').fill('admin@teste.local')
  await page.getByLabel('Senha').fill('Teste123!')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page).toHaveURL(/\/$/)
  await expect.poll(() => page.evaluate(() => localStorage.getItem('crm-cartorio.token'))).toBe('token-administrador')
})

test('rota autenticada sem token redireciona para login', async ({ page }) => {
  await page.goto('/clientes')
  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByRole('heading', { name: 'Entrar no CRM' })).toBeVisible()
})

test('token salvo e revalidado permite recarregar uma rota protegida', async ({ page }) => {
  await autenticarLocalmente(page, 'Administrador')
  await page.goto('/clientes')
  await expect(page).toHaveURL(/\/clientes$/)
  await expect(page.getByText('Marina Souza')).toBeVisible()
})
