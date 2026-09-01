import { expect, test } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock-seguro'

async function abrirClientes(page: any, estado = criarEstadoE2E()) {
  await instalarApiMock(page, estado)
  await autenticarLocalmente(page, 'Administrador')
  await page.goto('/clientes')
  await expect(page.getByText('Marina Souza')).toBeVisible()
  return estado
}

test('lista clientes e permite busca textual', async ({ page }) => {
  await abrirClientes(page)
  const busca = page.getByRole('textbox', { name: 'Buscar clientes' })
  await busca.fill('Carlos')
  await expect(page.getByText('Carlos Lima')).toBeVisible()
  await expect(page.getByText('Marina Souza')).toHaveCount(0)
})

test('filtro rapido Sem CPF/CNPJ mostra somente pendencia correspondente', async ({ page }) => {
  await abrirClientes(page)
  await page.getByRole('button', { name: 'Sem CPF/CNPJ' }).click()
  await expect(page.getByText('Carlos Lima')).toBeVisible()
  await expect(page.getByText('Marina Souza')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /Limpar pendências/ })).toBeVisible()
})

test('cadastra cliente ficticio com os campos minimos', async ({ page }) => {
  const estado = await abrirClientes(page)
  await page.getByRole('button', { name: 'Novo Cliente' }).click()
  await expect(page.getByRole('heading', { name: 'Novo cliente' })).toBeVisible()
  await page.getByLabel('Nome').fill('Beatriz E2E')
  await page.getByLabel('WhatsApp').fill('11988887777')
  await page.getByRole('button', { name: 'Salvar' }).click()
  await expect(page.getByText('Cliente cadastrado.')).toBeVisible()
  await expect.poll(() => estado.clientes.some((c) => c.nome === 'Beatriz E2E')).toBe(true)
  await expect(page.getByText('Beatriz E2E')).toBeVisible()
})

test('recusa exclusao fisica de cliente com atendimento vinculado', async ({ page }) => {
  const estado = await abrirClientes(page)
  await page.getByRole('button', { name: 'Excluir Marina Souza' }).click()
  await expect(page.getByRole('heading', { name: 'Excluir Marina Souza?' })).toBeVisible()
  await page.getByRole('button', { name: 'Excluir', exact: true }).click()
  await expect(page.getByText('Cliente possui atendimento vinculado.')).toBeVisible()
  expect(estado.clientes.some((c) => c.nome === 'Marina Souza')).toBe(true)
})
