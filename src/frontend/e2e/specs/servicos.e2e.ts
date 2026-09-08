import { expect, test, type Page } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock-seguro'

async function preparar(page: Page, perfil: 'Administrador' | 'Supervisor' | 'Atendente' = 'Administrador') {
  const estado = criarEstadoE2E(perfil)
  estado.servicos.push(
    { ...estado.servicos[0], id: 'servico-2', nome: 'Certidão de nascimento', codigo: 'RC-NASC', categoria: 'RegistroCivil', categoriaDescrita: 'Registro Civil', setorId: 'setor-civil', setorNome: 'Registro Civil', prazo: null, documentosNecessarios: null, conversasAtivas: 3 },
    { ...estado.servicos[0], id: 'servico-3', nome: 'Procuração antiga', codigo: null, ativo: false, setorId: null, setorNome: null, prazo: null, documentosNecessarios: null, conversasAtivas: 0 },
  )
  await instalarApiMock(page, estado)
  await autenticarLocalmente(page, perfil)
  await page.goto('/servicos')
  return estado
}

test('busca sem acentos, filtros combinados e resumo usam os serviços exibidos', async ({ page }) => {
  await preparar(page)
  await expect(page.getByRole('article')).toHaveCount(3)
  await page.getByRole('textbox', { name: 'Buscar serviços' }).fill('certidao')
  await expect(page.getByRole('article')).toHaveCount(1)
  await expect(page.getByRole('article', { name: 'Certidão de nascimento' })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Resumo dos serviços exibidos' })).toContainText('de 3 no catálogo')
  await page.getByRole('button', { name: 'Sem prazo', exact: true }).click()
  await page.getByRole('button', { name: 'Sem documentos', exact: true }).click()
  await page.getByRole('button', { name: 'Com atendimento aberto', exact: true }).click()
  await expect(page.getByRole('article')).toHaveCount(1)
  await page.getByRole('combobox', { name: 'Setor', exact: true }).click()
  await page.getByRole('option', { name: 'Notas', exact: true }).click()
  await expect(page.getByText('Nenhum serviço encontrado')).toBeVisible()
  await page.getByRole('button', { name: 'Limpar filtros', exact: true }).first().click()
  await expect(page.getByRole('article')).toHaveCount(3)
  await page.getByRole('button', { name: 'Mostrando inativos' }).click()
  await expect(page.getByRole('article')).toHaveCount(2)
  await page.getByRole('combobox', { name: 'Categoria', exact: true }).click()
  await page.getByRole('option', { name: 'Registro Civil', exact: true }).click()
  await expect(page.getByRole('article')).toHaveCount(1)
})

test('documentos expandem com teclado e edição preserva descrição, prazo e vínculos', async ({ page }) => {
  const estado = await preparar(page)
  const ficha = page.getByRole('article', { name: 'Reconhecimento de firma' })
  await ficha.locator('summary').focus()
  await page.keyboard.press('Enter')
  await expect(ficha.getByText('Documento com foto', { exact: true })).toBeVisible()
  await ficha.getByRole('button', { name: 'Editar Reconhecimento de firma' }).click()
  await expect(page.getByLabel('Prazo em horas')).toHaveValue('24')
  await expect(page.getByLabel('Alerta em (%)')).toHaveValue('80')
  await page.getByLabel('Nome', { exact: true }).fill('Firma revisada')
  await page.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByRole('article', { name: 'Firma revisada' })).toBeVisible()
  expect(estado.servicos[0]).toMatchObject({ descricao: 'Reconhecimento de assinatura', setorId: 'setor-notas', documentosNecessarios: 'Documento com foto', prazo: { prazoHoras: 24, percentualAlerta: 80, apenasDiasUteis: true } })
  expect(estado.chamadasExternas).toEqual([])
})

test('supervisor cria serviço com SLA e depois desativa prazo e serviço', async ({ page }) => {
  const estado = await preparar(page, 'Supervisor')
  await page.getByRole('button', { name: 'Novo serviço', exact: true }).click()
  await page.getByLabel('Nome', { exact: true }).fill('Novo ato E2E')
  await page.getByLabel('Código interno (opcional)').fill('E2E-01')
  await page.getByLabel('Prazo em horas').fill('48')
  await page.getByLabel('Alerta em (%)').fill('75')
  await page.getByRole('switch', { name: 'Contar só dias úteis' }).click()
  await page.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByRole('article', { name: 'Novo ato E2E' })).toContainText('48 horas')
  expect(estado.servicos.at(-1).prazo).toMatchObject({ prazoHoras: 48, percentualAlerta: 75, apenasDiasUteis: false })
  await page.getByRole('button', { name: 'Editar Novo ato E2E' }).click()
  await page.getByRole('switch', { name: 'Controlar prazo (SLA)' }).click()
  await page.getByRole('switch', { name: 'Serviço ativo' }).click()
  await page.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByRole('article', { name: 'Novo ato E2E' })).toContainText('Inativo')
  expect(estado.servicos.at(-1)).toMatchObject({ ativo: false, prazo: null })
})

test('valida campos e mantém edição após falha ao salvar', async ({ page }) => {
  await preparar(page)
  await page.getByRole('button', { name: 'Novo serviço', exact: true }).click()
  await page.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByLabel('Nome', { exact: true })).toBeFocused()
  await page.getByLabel('Nome', { exact: true }).fill('Nome preservado')
  await page.getByLabel('Alerta em (%)').fill('100')
  await page.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByLabel('Alerta em (%)')).toBeFocused()
  await page.getByLabel('Alerta em (%)').fill('80')
  await page.route('**/api/tipos-servico', (route) => route.fulfill({ status: 409, json: { codigo: 'nome_em_uso', title: 'Já existe um serviço com este nome.' } }))
  await page.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByText('Já existe um serviço com este nome.')).toBeVisible()
  await expect(page.getByLabel('Nome', { exact: true })).toHaveValue('Nome preservado')
})

test('erro de listagem pode ser repetido sem parecer catálogo vazio', async ({ page }) => {
  await instalarApiMock(page)
  await autenticarLocalmente(page)
  await page.route('**/api/tipos-servico?*', (route) => route.fulfill({ status: 500, json: { title: 'Falha simulada.' } }))
  await page.goto('/servicos')
  await expect(page.getByText('Não foi possível carregar os serviços')).toBeVisible({ timeout: 15000 })
  await page.unroute('**/api/tipos-servico?*')
  await page.getByRole('button', { name: 'Tentar novamente' }).click()
  await expect(page.getByRole('article')).toHaveCount(1)
})

test('atendente não recebe acesso de gestão por URL direta', async ({ page }) => {
  await preparar(page, 'Atendente')
  await expect(page.getByRole('button', { name: 'Novo serviço', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /^Editar / })).toHaveCount(0)
})

for (const largura of [360, 768, 1024, 1366, 1920]) {
  test(`serviços e formulário sem transbordamento em ${largura}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: largura, height: 900 })
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await preparar(page)
    await expect(page.getByRole('article')).toHaveCount(3)
    if (largura < 768) {
      await expect(page.getByRole('heading', { name: 'Reconhecimento de firma' })).toBeInViewport()
      await page.getByRole('button', { name: 'Categoria e setor' }).click()
      await expect(page.getByRole('combobox', { name: 'Categoria', exact: true })).toBeVisible()
      await page.getByRole('button', { name: 'Categoria e setor' }).click()
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth && document.documentElement.scrollHeight <= innerHeight)).toBe(true)
    await page.screenshot({ path: testInfo.outputPath(`servicos-${largura}.png`) })
    await page.getByRole('button', { name: 'Editar Reconhecimento de firma' }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    expect(await page.getByRole('dialog').evaluate((el) => el.scrollWidth <= el.clientWidth)).toBe(true)
    await page.getByRole('button', { name: 'Salvar', exact: true }).scrollIntoViewIfNeeded()
    await expect(page.getByRole('button', { name: 'Salvar', exact: true })).toBeInViewport()
    await page.screenshot({ path: testInfo.outputPath(`servico-formulario-${largura}.png`) })
  })
}
