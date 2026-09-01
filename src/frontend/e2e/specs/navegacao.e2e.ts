import { expect, test } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock-seguro'

async function preparar(page: Parameters<typeof autenticarLocalmente>[0], perfil: 'Atendente' | 'Supervisor' | 'Administrador') {
  await instalarApiMock(page, criarEstadoE2E(perfil))
  await autenticarLocalmente(page, perfil)
}

test('Administrador enxerga a navegacao administrativa', async ({ page }) => {
  await preparar(page, 'Administrador')
  await page.goto('/clientes')
  await expect(page.getByRole('link', { name: 'Equipe' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Serviços' })).toBeVisible()
})

test('Atendente nao enxerga Equipe nem Servicos', async ({ page }) => {
  await preparar(page, 'Atendente')
  await page.goto('/clientes')
  await expect(page.getByRole('link', { name: 'Equipe' })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Serviços' })).toHaveCount(0)
})

test('acesso direto de Atendente a /equipe falha fechado e volta para inicio', async ({ page }) => {
  await preparar(page, 'Atendente')
  await page.goto('/equipe')
  await expect(page).toHaveURL(/\/$/)
})

test('Supervisor acessa cadastros de gestao mas nao Equipe', async ({ page }) => {
  await preparar(page, 'Supervisor')
  await page.goto('/servicos')
  await expect(page).toHaveURL(/\/servicos$/)
  await expect(page.getByRole('link', { name: 'Equipe' })).toHaveCount(0)
})

test('rota legada /configuracoes redireciona para etapas', async ({ page }) => {
  await preparar(page, 'Administrador')
  await page.goto('/configuracoes')
  await expect(page).toHaveURL(/\/estrutura\/etapas$/)
})
