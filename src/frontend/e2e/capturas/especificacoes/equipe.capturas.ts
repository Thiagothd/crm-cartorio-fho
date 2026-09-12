import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela, capturarComDestaque } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

function linha(page: any, nome: string) {
  return page.locator('div.rounded-card').filter({ hasText: nome })
}

test('lista de equipe', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/equipe')
  await expect(page.getByText('Carla Menezes')).toBeVisible()
  await capturarTela(page, 'equipe/lista-equipe.png')

  await capturarComDestaque(page, [{ locator: page.getByRole('button', { name: /^Novo/ }), numero: 1 }], 'equipe/lista-botao-novo.png')
})

test('diálogo de novo atendente', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/equipe')
  await page.getByRole('button', { name: /^Novo/ }).click()
  await expect(page.getByRole('dialog')).toContainText('Novo atendente')
  await capturarTela(page, 'equipe/dialogo-novo-atendente.png')
})

test('redefinir senha', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/equipe')
  await linha(page, 'Paula Ribeiro').getByRole('button', { name: 'Redefinir senha' }).click()
  await capturarTela(page, 'equipe/dialogo-redefinir-senha.png')
})

test('segurança (MFA) administrativo', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/equipe')
  await linha(page, 'Roberta Ximenes').getByRole('button', { name: 'Segurança (MFA)' }).click()
  await capturarTela(page, 'equipe/dialogo-mfa.png')
})

test('permissões — modelos prontos e efeito de dependência', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/equipe')
  await linha(page, 'Paula Ribeiro').getByRole('button', { name: 'Permissões' }).click()
  await expect(page).toHaveURL(/\/equipe\/.+\/permissoes/)
  await capturarTela(page, 'equipe/tela-permissoes.png')

  await capturarComDestaque(page, [{ locator: page.getByRole('button', { name: 'Supervisão' }), numero: 1 }], 'equipe/permissoes-modelos.png')
  await page.getByRole('button', { name: 'Supervisão' }).click()
  await expect(page.getByText(/\d+ mudança/)).toBeVisible()
  await capturarTela(page, 'equipe/permissoes-com-mudancas.png')
})

test('exclusão de atendente', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/equipe')
  await linha(page, 'Carlos Nogueira').getByRole('button', { name: 'Excluir' }).click()
  await capturarTela(page, 'equipe/dialogo-exclusao.png')
})
