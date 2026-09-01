import { expect, test } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock-seguro'

async function abrirInbox(page: any, estado: ReturnType<typeof criarEstadoE2E>) {
  await instalarApiMock(page, estado)
  await autenticarLocalmente(page, 'Administrador')

  // O ponteiro do Chromium nasce em (0, 0), exatamente sobre o rail. Em um
  // navegador real o usuário já tem o ponteiro em alguma posição da tela; no
  // runner isso podia acionar a expansão por hover antes do primeiro clique.
  // Neutralizamos a posição e esperamos o rail estar realmente recolhido, sem
  // force-click nem atraso arbitrário.
  await page.mouse.move(1000, 100)
  await page.goto('/atendimentos')
  await expect(page.getByRole('navigation', { name: 'Menu principal' })).toHaveCSS('width', '64px')
  await expect(page.getByRole('button', { name: /Marina Souza/ })).toBeVisible()
}

test('lista conversas e mostra contagem real do mock', async ({ page }) => {
  const estado = criarEstadoE2E()
  await abrirInbox(page, estado)
  await expect(page.getByText('1 de 1')).toBeVisible()
  await expect(page.getByText('Preciso reconhecer uma firma.')).toBeVisible()
})

test('abre conversa e mantém lista e detalhe lado a lado no desktop', async ({ page }) => {
  const estado = criarEstadoE2E()
  await abrirInbox(page, estado)
  await page.getByRole('button', { name: /Marina Souza/ }).click()
  await expect(page).toHaveURL(/\/atendimentos\/conversa-1$/)
  await expect(page.getByRole('heading', { name: 'Marina Souza' })).toBeVisible()
  await expect(page.getByText('Preciso reconhecer uma firma.').last()).toBeVisible()
  await expect(page.getByPlaceholder('Nome, telefone ou protocolo')).toBeVisible()
})

test('assumir conversa atualiza responsavel e exibe confirmação', async ({ page }) => {
  const estado = criarEstadoE2E()
  await abrirInbox(page, estado)
  await page.getByRole('button', { name: /Marina Souza/ }).click()
  await page.getByRole('button', { name: 'Assumir', exact: true }).click()
  await expect(page.getByText('Conversa assumida.')).toBeVisible()
  await expect.poll(() => estado.conversas[0].atendenteNome).toBe('Alice Administradora')
})

test('corrida perdida ao assumir mostra quem chegou primeiro', async ({ page }) => {
  const estado = criarEstadoE2E()
  estado.conflitoAoAssumir = true
  await abrirInbox(page, estado)
  await page.getByRole('button', { name: /Marina Souza/ }).click()
  await page.getByRole('button', { name: 'Assumir', exact: true }).click()
  await expect(page.getByText('Conversa já assumida')).toBeVisible()
  await expect(page.getByText(/Outro Atendente chegou primeiro/)).toBeVisible()
})

test('busca textual filtra a lista sem depender de ordem entre testes', async ({ page }) => {
  const estado = criarEstadoE2E()
  await abrirInbox(page, estado)
  await page.getByPlaceholder('Nome, telefone ou protocolo').fill('nao-existe')
  await expect(page.getByText('Nenhuma conversa aqui')).toBeVisible()
  await page.getByPlaceholder('Nome, telefone ou protocolo').fill('ATD-2026-0001')
  await expect(page.getByRole('button', { name: /Marina Souza/ })).toBeVisible()
})

test('resposta rapida insere texto no campo e nao envia sozinha', async ({ page }) => {
  const estado = criarEstadoE2E()
  await abrirInbox(page, estado)
  await page.getByRole('button', { name: /Marina Souza/ }).click()
  const campo = page.getByPlaceholder(/Escreva a resposta/)
  const quantidadeAntes = estado.detalhes['conversa-1'].mensagens.length
  await campo.fill('/docs')
  await expect(page.getByText('Documentos necessários')).toBeVisible()
  await campo.press('Enter')
  await expect(campo).toHaveValue('Por favor, envie seu documento com foto.')
  expect(estado.detalhes['conversa-1'].mensagens).toHaveLength(quantidadeAntes)
})
