import { expect, test } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock'

test.use({ viewport: { width: 390, height: 844 }, hasTouch: true })

async function preparar(page: any) {
  const estado = criarEstadoE2E('Administrador')
  await instalarApiMock(page, estado)
  await autenticarLocalmente(page, 'Administrador')
  return estado
}

test('mobile alterna lista e conversa sem perder navegacao', async ({ page }) => {
  await preparar(page)
  await page.goto('/atendimentos')
  const item = page.getByRole('button', { name: /Marina Souza/ })
  await expect(item).toBeVisible()
  await item.click()
  await expect(page).toHaveURL(/\/atendimentos\/conversa-1$/)
  await expect(page.getByRole('heading', { name: 'Marina Souza' })).toBeVisible()
  await expect(page.getByPlaceholder('Nome, telefone ou protocolo')).toBeHidden()
  await page.getByRole('button', { name: 'Voltar para a lista de conversas' }).click()
  await expect(page).toHaveURL(/\/atendimentos$/)
  await expect(page.getByPlaceholder('Nome, telefone ou protocolo')).toBeVisible()
})

test('mobile mantem cadastro de clientes utilizavel', async ({ page }) => {
  await preparar(page)
  await page.goto('/clientes')
  await expect(page.getByRole('textbox', { name: 'Buscar clientes' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Novo' })).toBeVisible()
  await page.getByRole('button', { name: 'Novo' }).click()
  await expect(page.getByRole('heading', { name: 'Novo cliente' })).toBeVisible()
  await expect(page.getByRole('tab', { name: 'Contato' })).toBeVisible()
  await expect(page.getByRole('tab', { name: 'Observações' })).toBeVisible()
})
