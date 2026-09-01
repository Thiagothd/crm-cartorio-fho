import { expect, test, type Page } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock-seguro'

const sessaoAdministrador = {
  token: 'token-administrador',
  expiraEm: '2026-09-01T20:00:00.000Z',
  atendente: {
    id: 'admin-e2e',
    nome: 'Alice Administradora',
    email: 'admin@teste.local',
    perfil: 'Administrador',
    cargo: 'Titular',
    cargoDescrito: 'Titular',
    setorPadraoId: 'setor-notas',
    setorPadraoNome: 'Notas',
    ativo: true,
    ultimoAcessoEm: '2026-08-31T20:00:00.000Z',
  },
  tenant: {
    id: 'tenant-e2e',
    razaoSocial: 'Cartório E2E Ltda.',
    nomeFantasia: 'Cartório Modelo E2E',
    cnpj: '12345678000199',
    whatsappNumeroExibicao: '(11) 99999-0000',
    whatsappConfigurado: false,
  },
}

async function responderLoginSemMfa(page: Page) {
  await page.route('**/api/autenticacao/login', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify({ requerMfa: false, sessao: sessaoAdministrador, desafio: null }),
    })
  })
}

test.beforeEach(async ({ page }) => {
  await instalarApiMock(page, criarEstadoE2E())
})

test('exibe o formulario de login com campos acessiveis', async ({ page }) => {
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Entrar no CRM' })).toBeVisible()
  await expect(page.getByLabel('E-mail')).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Senha', exact: true })).toBeVisible()
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
  await page.getByRole('textbox', { name: 'Senha', exact: true }).fill('senha-incorreta')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByRole('alert')).toContainText('E-mail ou senha inválidos.')
  await expect(page).toHaveURL(/\/login$/)
})

test('login valido sem MFA grava sessao e entra no CRM', async ({ page }) => {
  await responderLoginSemMfa(page)

  await page.goto('/login')
  await page.getByLabel('E-mail').fill('admin@teste.local')
  await page.getByRole('textbox', { name: 'Senha', exact: true }).fill('Teste123!')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page).toHaveURL(/\/$/)
  await expect.poll(() => page.evaluate(() => localStorage.getItem('crm-cartorio.token'))).toBe('token-administrador')
})

test('login com MFA so grava sessao depois do segundo fator', async ({ page }) => {
  await page.route('**/api/autenticacao/login', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify({
        requerMfa: true,
        sessao: null,
        desafio: {
          desafio: 'desafio-mfa-e2e',
          tipo: 'SegundoFator',
          expiraEm: '2026-09-01T20:00:00.000Z',
        },
      }),
    })
  })

  await page.route('**/api/autenticacao/login/mfa', async (route) => {
    const corpo = route.request().postDataJSON() as { desafio?: string; codigo?: string }
    expect(corpo).toEqual({ desafio: 'desafio-mfa-e2e', codigo: '123456' })
    await route.fulfill({
      status: 200,
      contentType: 'application/json; charset=utf-8',
      body: JSON.stringify(sessaoAdministrador),
    })
  })

  await page.goto('/login')
  await page.getByLabel('E-mail').fill('admin@teste.local')
  await page.getByRole('textbox', { name: 'Senha', exact: true }).fill('Teste123!')
  await page.getByRole('button', { name: 'Entrar' }).click()

  await expect(page.getByRole('heading', { name: 'Verificação em duas etapas' })).toBeVisible()
  await expect.poll(() => page.evaluate(() => localStorage.getItem('crm-cartorio.token'))).toBeNull()

  await page.getByLabel('Código').fill('123456')
  await page.getByRole('button', { name: 'Confirmar' }).click()

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
