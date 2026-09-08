import { expect, test, type Page } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock-seguro'

/**
 * As quatro telas que migraram para a Fase 3.1 em DESIGN.md 17.5: Etapas,
 * Setores, WhatsApp e Conhecimento da IA.
 *
 * O que estes testes protegem não é o pixel — é o que a mudança de tabela para
 * cartão poderia quebrar em silêncio: a ORDEM (que no funil é significado), o
 * que cada filtro promete, e o fato de o painel do canal continuar mandando as
 * mesmas chamadas de antes.
 */
async function preparar(page: Page, rota: string, perfil: 'Administrador' | 'Supervisor' = 'Administrador') {
  const estado = criarEstadoE2E(perfil)
  await instalarApiMock(page, estado)
  await autenticarLocalmente(page, perfil)
  await page.goto(rota)
  return estado
}

/**
 * Escopado à lista pelo nome acessível: o toast do Sonner também é uma `<ol>`
 * de `<li>`, e um `getByRole('listitem')` solto passa a contar o aviso de
 * sucesso junto com as etapas.
 */
function itensDaLista(page: Page, nome: string) {
  return page.getByRole('list', { name: nome }).getByRole('listitem')
}

// ------------------------------------------------------------------ Etapas

test('Etapas: resumo conta o funil, a lista respeita a ordem e reordenar persiste', async ({ page }) => {
  const estado = await preparar(page, '/estrutura/etapas')

  await expect(page.getByText('Etapas ativas')).toBeVisible()
  await expect(page.getByText('Mais cheia')).toBeVisible()

  const linhas = itensDaLista(page, 'Etapas do funil')
  await expect(linhas).toHaveCount(2)
  await expect(linhas.first()).toContainText('Triagem')
  await expect(linhas.last()).toContainText('Concluído')

  // A etapa de conclusão é dado, não enfeite: some do resumo se ninguém marcar.
  await expect(linhas.last()).toContainText('Conclusão')

  await page.getByRole('button', { name: 'Mover Triagem para depois' }).click()

  await expect(linhas.first()).toContainText('Concluído')
  expect(estado.etapas.map((e) => e.nome)).toEqual(['Concluído', 'Triagem'])
})

test('Etapas: a primeira não sobe e a última não desce', async ({ page }) => {
  await preparar(page, '/estrutura/etapas')

  await expect(page.getByRole('button', { name: 'Mover Triagem para antes' })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Mover Concluído para depois' })).toBeDisabled()
})

test('Etapas: criar exige nome e a recusa aparece no diálogo, não em toast', async ({ page }) => {
  await preparar(page, '/estrutura/etapas')

  await page.getByRole('button', { name: /^Nova/ }).click()
  await page.getByRole('button', { name: 'Salvar' }).click()

  await expect(page.getByRole('alert')).toContainText('Informe o nome da etapa.')
  await expect(page.getByRole('dialog')).toBeVisible()

  await page.getByLabel('Nome').fill('Aguardando documento')
  await page.getByRole('button', { name: 'Salvar' }).click()

  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(itensDaLista(page, 'Etapas do funil')).toHaveCount(3)
})

// ----------------------------------------------------------------- Setores

test('Setores: resumo, lista e edição abrem com o cadastro carregado', async ({ page }) => {
  await preparar(page, '/estrutura/setores')

  await expect(page.getByText('Setores ativos')).toBeVisible()
  await expect(page.getByText('Opções no menu do primeiro contato')).toBeVisible()

  const linhas = itensDaLista(page, 'Setores da serventia')
  await expect(linhas).toHaveCount(2)
  await expect(linhas.first()).toContainText('Notas')

  await page.getByRole('button', { name: 'Editar Notas' }).click()
  await expect(page.getByRole('dialog')).toContainText('Editar setor')
  await expect(page.getByLabel('Nome')).toHaveValue('Notas')
})

// ---------------------------------------------------------------- WhatsApp

test('WhatsApp: canal conectado, regras, teste de envio e históricos na mesma tela', async ({ page }) => {
  const estado = await preparar(page, '/integracoes/whatsapp')

  await expect(page.getByRole('heading', { name: 'Conexão do canal' })).toBeVisible()
  await expect(page.getByText('Cartório de Gameleiras').first()).toBeVisible()
  await expect(page.getByText('Conectado', { exact: true })).toBeVisible()

  await expect(page.getByRole('heading', { name: 'Quem pode falar com este número' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Mensagens bloqueadas' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Histórico de conexões' })).toBeVisible()

  await page.getByLabel('Telefone').fill('38999997777')
  await page.getByRole('button', { name: 'Enviar' }).click()

  await expect(page.getByRole('status')).toContainText('Mensagem enviada')
  expect(estado.chamadasExternas).toContain('whatsapp:testar-envio')
})

// -------------------------------------------------------- Conhecimento da IA

test('Conhecimento: vigência separa o que a IA lê do que está só cadastrado', async ({ page }) => {
  await preparar(page, '/automacao/ia/conhecimento')

  const cartoes = page.getByRole('article')
  await expect(cartoes).toHaveCount(2)

  // O mutirão tem janela encerrada em janeiro: continua ativo e não é lido.
  await expect(page.getByRole('article', { name: 'Mutirão de firmas de janeiro' })).toContainText(
    'Fora de vigência',
  )

  await page.getByRole('button', { name: 'Em uso', exact: true }).click()
  await expect(cartoes).toHaveCount(1)
  await expect(cartoes.first()).toContainText('Política de reagendamento')

  await page.getByRole('button', { name: 'Fora de vigência', exact: true }).click()
  await expect(cartoes).toHaveCount(1)
  await expect(cartoes.first()).toContainText('Mutirão')
})

test('Conhecimento: a busca varre o texto, não só o título', async ({ page }) => {
  await preparar(page, '/automacao/ia/conhecimento')

  // "24h antes" só existe no CONTEÚDO do primeiro cadastro.
  await page.getByPlaceholder('Título, tema ou texto').fill('24h antes')

  await expect(page.getByRole('article')).toHaveCount(1)
  await expect(page.getByRole('article').first()).toContainText('Política de reagendamento')

  await page.getByPlaceholder('Título, tema ou texto').fill('nada com esse texto')
  await expect(page.getByText('Nenhum conhecimento encontrado')).toBeVisible()
})

test('Conhecimento: histórico mostra a versão anterior sem sair da tela', async ({ page }) => {
  await preparar(page, '/automacao/ia/conhecimento')

  await page.getByRole('button', { name: 'Ver histórico de Política de reagendamento' }).click()

  await expect(page.getByRole('dialog')).toContainText('Histórico — Política de reagendamento')
  await expect(page.getByRole('dialog')).toContainText('Texto da versão anterior.')
})

// -------------------------------------------------------------- Responsivo

test('as quatro telas cabem em 360px sem rolagem horizontal', async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 740 })

  for (const rota of [
    '/estrutura/etapas',
    '/estrutura/setores',
    '/integracoes/whatsapp',
    '/automacao/ia/conhecimento',
  ]) {
    await preparar(page, rota)
    await expect(page.getByRole('main')).toBeVisible()

    const transbordou = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
    )
    expect(transbordou, `${rota} transbordou na horizontal`).toBe(false)
  }
})
