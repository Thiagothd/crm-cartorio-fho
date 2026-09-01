import { expect, test } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock'

async function preparar(page: any) {
  const estado = criarEstadoE2E('Administrador')
  await instalarApiMock(page, estado)
  await autenticarLocalmente(page, 'Administrador')
  return estado
}

test('Equipe apresenta os perfis cadastrados para Administrador', async ({ page }) => {
  await preparar(page)
  await page.goto('/equipe')
  await expect(page.getByText('Alice Administradora')).toBeVisible()
  await expect(page.getByText('Ana Atendente')).toBeVisible()
  await expect(page.getByText('Sofia Supervisora')).toBeVisible()
})

test('cadastra novo setor e reflete o estado atualizado da API fake', async ({ page }) => {
  const estado = await preparar(page)
  await page.goto('/estrutura/setores')
  await page.getByRole('button', { name: 'Novo setor' }).click()
  await expect(page.getByRole('heading', { name: 'Novo setor' })).toBeVisible()
  await page.getByLabel('Nome').fill('Protesto E2E')
  await page.getByLabel('Descrição (opcional)').fill('Setor fictício de teste')
  await page.getByRole('button', { name: 'Salvar' }).click()
  await expect(page.getByText('Setor criado.')).toBeVisible()
  await expect.poll(() => estado.setores.some((s) => s.nome === 'Protesto E2E')).toBe(true)
  await expect(page.getByText('Protesto E2E')).toBeVisible()
})
