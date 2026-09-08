import { test, expect } from '@playwright/test'
import { autenticarLocalmente, instalarApiMock } from '../fixtures/api-mock-seguro'

/**
 * A tela de Mensagens não tinha cobertura E2E nenhuma — o mock devolvia 501
 * para `/api/mensagens-automaticas` e para o encerramento automático, então
 * nem abria. O que se protege aqui é o que jsdom não alcança: as três seções
 * resolvendo pela URL, o desenho do caminho existindo sem rolagem horizontal
 * do documento (DESIGN.md 14.8 e 12.4), e a prévia do encerramento reagindo
 * ao campo em vez de ser ilustração fixa.
 *
 * Quatro casos densos, e não um por asserção, de propósito: cada teste custa
 * um worker e uma montagem do módulo lazy da página, e a suíte inteira roda em
 * paralelo na máquina de quem desenvolve.
 */

test.beforeEach(async ({ page }) => {
  await instalarApiMock(page)
  await autenticarLocalmente(page)
})

test('as três seções de Mensagens vivem na URL', async ({ page }) => {
  await page.goto('/automacao/mensagens')
  await expect(page).toHaveURL(/\/automacao\/mensagens\/automaticas$/)

  await page.getByRole('link', { name: 'Encerramento' }).click()
  await expect(page).toHaveURL(/\/automacao\/mensagens\/encerramento$/)
  await expect(page.getByLabel('Encerrar conversas paradas')).toBeVisible()

  await page.getByRole('link', { name: 'Respostas rápidas' }).click()
  await expect(page).toHaveURL(/\/automacao\/mensagens\/respostas$/)
  await expect(page.getByText('/docs')).toBeVisible()
})

test('o caminho automático cabe na largura, filtra pendências e abre o ponto certo', async ({
  page,
}) => {
  await page.goto('/automacao/mensagens/automaticas')

  await expect(page.getByRole('heading', { name: 'Roteiro guiado' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Prazo do serviço' })).toBeVisible()

  // A moldura nunca rola na horizontal — DESIGN.md 14.8 e 12.4.
  const excesso = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  )
  expect(excesso).toBeLessThanOrEqual(1)

  // "Primeiro contato" tem texto no mock; "Cliente encerrou" não tem.
  await expect(page.getByText('Primeiro contato', { exact: true })).toBeVisible()
  await page.getByLabel('Só o que falta escrever').click()
  await expect(page.getByText('Primeiro contato', { exact: true })).toBeHidden()

  const pontoVazio = page.getByRole('button', { name: /Cliente encerrou/ })
  await expect(pontoVazio).toBeVisible()
  // A numeração é a posição na TOPOLOGIA, não na lista filtrada: "Cliente
  // encerrou" é o 5º passo do roteiro mesmo quando os quatro antes dele saem.
  await expect(pontoVazio.getByText('5', { exact: true })).toBeVisible()

  await pontoVazio.click()
  const dialogo = page.getByRole('dialog')
  await expect(dialogo.getByText('Nova mensagem automática')).toBeVisible()
  await expect(dialogo.getByLabel('Quando dispara')).toHaveText(/Cliente escolheu encerrar/)
})

test('a lista de cadastradas busca pelo texto da mensagem', async ({ page }) => {
  await page.goto('/automacao/mensagens/automaticas')

  await page.getByRole('button', { name: 'Cadastradas' }).click()
  await expect(page.getByText('Primeiro contato do cliente')).toBeVisible()

  await page.getByLabel('Buscar nas mensagens cadastradas').fill('documentosnecessarios')

  await expect(page.getByText('Cliente escolheu o servico')).toBeVisible()
  await expect(page.getByText('Primeiro contato do cliente')).toBeHidden()
})

test('a prévia do encerramento acompanha o campo, e Salvar só acende com mudança', async ({
  page,
}) => {
  await page.goto('/automacao/mensagens/encerramento')

  const salvar = page.getByRole('button', { name: 'Salvar' })
  await expect(salvar).toBeDisabled()
  await expect(page.getByText('depois de 20 min')).toBeVisible()

  await page.getByLabel('Perguntar após (minutos de silêncio)').fill('90')

  await expect(page.getByText('depois de 1h30')).toBeVisible()
  await expect(salvar).toBeEnabled()
})
