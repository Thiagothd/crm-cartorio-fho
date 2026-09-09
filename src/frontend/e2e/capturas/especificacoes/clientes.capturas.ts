import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela, capturarComDestaque } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

test('lista de clientes', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/clientes')
  await expect(page.getByText('Maria Aparecida Souza')).toBeVisible()
  await capturarTela(page, 'clientes/lista-clientes.png')

  await capturarComDestaque(
    page,
    [
      { locator: page.getByRole('button', { name: 'Novo Cliente' }), numero: 1 },
      { locator: page.getByRole('textbox', { name: 'Buscar clientes' }), numero: 2 },
    ],
    'clientes/lista-botoes-principais.png',
  )
})

test('filtros rápidos de pendência', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/clientes')
  await page.getByRole('button', { name: 'Sem CPF/CNPJ' }).click()
  await capturarTela(page, 'clientes/filtro-sem-documento.png')
})

test('diálogo de novo cliente — abas', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/clientes')
  await page.getByRole('button', { name: 'Novo Cliente' }).click()
  await expect(page.getByRole('heading', { name: 'Novo cliente' })).toBeVisible()
  await capturarTela(page, 'clientes/dialogo-aba-contato.png')

  await page.getByRole('tab', { name: /Qualificação/i }).click()
  await capturarTela(page, 'clientes/dialogo-aba-qualificacao.png')

  await page.getByRole('tab', { name: 'Endereço' }).click()
  await capturarTela(page, 'clientes/dialogo-aba-endereco.png')
})

test('editar cliente — aba Observações', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/clientes')
  await page.getByRole('button', { name: /Editar Maria Aparecida Souza/ }).click()
  await page.getByRole('tab', { name: 'Observações' }).click()
  await capturarTela(page, 'clientes/dialogo-aba-observacoes.png')
})

test('importação de contatos', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/clientes')
  await page.getByRole('button', { name: 'Importar contatos', exact: true }).click()
  await expect(page.getByRole('tab', { name: 'Arquivo CSV' })).toBeVisible()
  await capturarTela(page, 'clientes/dialogo-importacao.png')

  await page.getByRole('dialog').getByRole('textbox').fill('Fernanda Cunha, (38) 99999-4444\nRoberto Dias, (38) 99999-5555')
  await page.getByRole('button', { name: 'Analisar lista' }).click()
  await capturarTela(page, 'clientes/dialogo-importacao-previa.png')
})

test('exclusão bloqueada por histórico', async ({ page }) => {
  await instalarMockDeCapturas(page, criarEstadoDeCaptura())
  await autenticarComoDemo(page)
  await prepararParaCaptura(page, AGORA)
  await page.goto('/clientes')
  await page.getByRole('button', { name: /Opções de remoção de Maria Aparecida Souza/ }).click()
  await expect(page.getByRole('heading', { name: 'Este cadastro tem histórico' })).toBeVisible()
  await capturarTela(page, 'clientes/dialogo-exclusao-bloqueada.png')
})
