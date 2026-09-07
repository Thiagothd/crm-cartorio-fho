import { expect, test, type Page } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock-seguro'

async function abrirClientes(page: Page, estado = criarEstadoE2E()) {
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
  const exclusoes: string[] = []
  page.on('request', (r) => { if (r.method() === 'DELETE') exclusoes.push(r.url()) })
  await page.getByRole('button', { name: 'Opções de remoção de Marina Souza' }).click()
  await expect(page.getByRole('heading', { name: 'Este cadastro tem histórico' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Excluir', exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: 'Abrir cadastro para desativar' }).click()
  await expect(page.getByRole('tab', { name: 'Observações' })).toHaveAttribute('data-state', 'active')
  await page.getByRole('switch', { name: 'Cliente ativo' }).click()
  await page.getByRole('button', { name: 'Salvar', exact: true }).click()
  await expect(page.getByText('Cliente atualizado.')).toBeVisible()
  await expect(page.getByText('Marina Souza')).toHaveCount(0)
  await page.getByRole('button', { name: 'Só ativos' }).click()
  await expect(page.getByText('Marina Souza')).toBeVisible()
  expect(estado.clientes[0].totalConversas).toBe(1)
  expect(estado.clientes[0].ativo).toBe(false)
  expect(exclusoes).toEqual([])
  expect(estado.clientes.some((c) => c.nome === 'Marina Souza')).toBe(true)
})

test('historico encerrado continua protegido e cadastro inativo nao oferece desativar', async ({ page }) => {
  const estado = criarEstadoE2E()
  estado.clientes[0].temConversaAtiva = false
  estado.clientes[0].ativo = false
  await instalarApiMock(page, estado)
  await autenticarLocalmente(page)
  await page.goto('/clientes')
  await page.getByRole('button', { name: 'Só ativos' }).click()
  await page.getByRole('button', { name: 'Opções de remoção de Marina Souza' }).click()
  await expect(page.getByText('Este cliente já está inativo. O histórico permanece disponível.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Excluir', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Abrir cadastro para desativar' })).toHaveCount(0)
})

test('exclui sem historico, aceita 204 e atualiza listagem e contagem', async ({ page }) => {
  const estado = await abrirClientes(page)
  await page.getByRole('button', { name: 'Excluir Carlos Lima' }).click()
  await page.getByRole('button', { name: 'Excluir', exact: true }).click()
  await expect(page.getByText('Carlos Lima excluído.')).toBeVisible()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByText('Carlos Lima', { exact: true })).toHaveCount(0)
  await expect(page.getByText('Exibindo 1 de 1 cliente')).toBeVisible()
  expect(estado.clientes.some((c) => c.nome === 'Carlos Lima')).toBe(false)
})

test('atendente mantem edicao mas nao recebe acao de exclusao', async ({ page }) => {
  await instalarApiMock(page)
  await autenticarLocalmente(page, 'Atendente')
  await page.goto('/clientes')
  await expect(page.getByRole('button', { name: 'Editar Carlos Lima' })).toBeVisible()
  await expect(page.getByRole('button', { name: /^Excluir |^Opções de remoção/ })).toHaveCount(0)
})

test('supervisor pode excluir contato sem historico', async ({ page }) => {
  await instalarApiMock(page)
  await autenticarLocalmente(page, 'Supervisor')
  await page.goto('/clientes')
  await page.getByRole('button', { name: 'Excluir Carlos Lima' }).click()
  await page.getByRole('button', { name: 'Excluir', exact: true }).click()
  await expect(page.getByText('Carlos Lima excluído.')).toBeVisible()
})

test('historico que surgiu depois da listagem transforma 409 em orientacao persistente', async ({ page }) => {
  const estado = await abrirClientes(page)
  await page.getByRole('button', { name: 'Excluir Carlos Lima' }).click()
  estado.clientes[1].totalConversas = 1
  await page.getByRole('button', { name: 'Excluir', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Este cadastro tem histórico' })).toBeVisible()
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('não pode ser excluído')
  await expect(page.getByRole('button', { name: 'Excluir', exact: true })).toHaveCount(0)
  expect(estado.clientes).toHaveLength(2)
})

for (const status of [403, 404, 500]) {
  test(`exclusao com erro ${status} mantem mensagem e permite somente recuperacao adequada`, async ({ page }) => {
    await abrirClientes(page)
    await page.route('**/api/clientes/cliente-2', (route) => route.fulfill({ status, json: { codigo: status === 403 ? 'sem_permissao' : status === 404 ? 'cliente_nao_encontrado' : 'erro_interno', title: 'Falha simulada na exclusão.' } }))
    await page.getByRole('button', { name: 'Excluir Carlos Lima' }).click()
    await page.getByRole('button', { name: 'Excluir', exact: true }).click()
    await expect(page.getByRole('dialog').getByRole('alert')).toHaveText('Falha simulada na exclusão.')
    if (status === 500) {
      await expect(page.getByRole('button', { name: 'Excluir', exact: true })).toBeEnabled()
    } else {
      await expect(page.getByRole('button', { name: 'Excluir', exact: true })).toHaveCount(0)
    }
  })
}

test('erro de rede pode ser repetido e nao apaga o cliente', async ({ page }) => {
  const estado = await abrirClientes(page)
  await page.route('**/api/clientes/cliente-2', (route) => route.abort('failed'))
  await page.getByRole('button', { name: 'Excluir Carlos Lima' }).click()
  await page.getByRole('button', { name: 'Excluir', exact: true }).click()
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Tente novamente')
  await expect(page.getByRole('button', { name: 'Excluir', exact: true })).toBeEnabled()
  expect(estado.clientes).toHaveLength(2)
})

test('nao fecha com Escape nem duplica exclusao enquanto aguarda a API', async ({ page }) => {
  await abrirClientes(page)
  let liberar!: () => void
  const pendente = new Promise<void>((resolve) => { liberar = resolve })
  let chamadas = 0
  await page.route('**/api/clientes/cliente-2', async (route) => {
    chamadas++
    await pendente
    await route.fulfill({ status: 204 })
  })
  await page.getByRole('button', { name: 'Excluir Carlos Lima' }).click()
  await page.getByRole('button', { name: 'Excluir', exact: true }).click()
  try {
    await expect(page.getByRole('button', { name: 'Excluindo…' })).toBeDisabled()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toBeVisible()
    expect(chamadas).toBe(1)
  } finally {
    liberar()
  }
  await expect(page.getByRole('dialog')).toHaveCount(0)
})

test('falha da listagem oferece repetir em vez de mostrar vazio', async ({ page }) => {
  await instalarApiMock(page)
  await autenticarLocalmente(page)
  let falhar = true
  await page.route('**/api/clientes?*', (route) => falhar ? route.fulfill({ status: 500, json: { codigo: 'erro_interno', title: 'Não foi possível consultar.' } }) : route.fallback())
  await page.goto('/clientes')
  await expect(page.getByText('Não foi possível carregar os clientes')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('Nenhum cliente encontrado')).toHaveCount(0)
  falhar = false
  await page.getByRole('button', { name: 'Tentar novamente' }).click()
  await expect(page.getByText('Marina Souza')).toBeVisible()
})

test('busca por URL, filtros e ordenacao preservam os parametros da API', async ({ page }) => {
  await abrirClientes(page)
  await page.goto('/clientes?busca=Carlos')
  await expect(page.getByRole('textbox', { name: 'Buscar clientes' })).toHaveValue('Carlos')
  await page.getByRole('button', { name: 'Limpar filtros', exact: true }).click()
  for (const [campo, opcao, parametro, valor] of [
    ['Tipo de pessoa', 'Pessoa física', 'tipo', 'Fisica'],
    ['Estado civil', 'Solteiro(a)', 'estadoCivil', 'Solteiro'],
    ['Cidade', 'São Paulo', 'cidade', 'São Paulo'],
    ['Ordenação', 'Contato recente', 'ordenar', 'recentes'],
  ]) {
    await page.getByRole('combobox', { name: campo, exact: true }).click()
    const requisicao = page.waitForRequest((r) => new URL(r.url()).pathname === '/api/clientes' && new URL(r.url()).searchParams.get(parametro) === valor)
    await page.getByRole('option', { name: opcao, exact: true }).click()
    await requisicao
  }
  await page.getByRole('button', { name: 'Limpar filtros', exact: true }).click()
  await page.getByRole('button', { name: 'Com atendimento aberto', exact: true }).click()
  await expect(page.getByText('Marina Souza')).toBeVisible()
  await expect(page.getByText('Carlos Lima')).toHaveCount(0)
})

test('importacao preserva previa, selecao e confirmacao sem envio de mensagem', async ({ page }) => {
  const estado = await abrirClientes(page)
  const chamadas: { fonte: string; linhas?: unknown[]; texto?: string }[] = []
  await page.route('**/api/clientes/importacao/*', async (route) => {
    const corpo = route.request().postDataJSON()
    chamadas.push(corpo)
    if (route.request().url().endsWith('/analisar')) {
      await route.fulfill({ json: {
        linhas: [
          { numeroDaLinha: 1, nome: 'Contato importado E2E', telefoneBruto: '(11) 98888-7777', telefoneE164: '5511988887777', situacao: 'Nova', nomeDoCadastroExistente: null },
          { numeroDaLinha: 2, nome: 'Marina Souza', telefoneBruto: '(11) 99999-1111', telefoneE164: '5511999991111', situacao: 'JaCadastrada', nomeDoCadastroExistente: 'Marina Souza' },
        ],
        resumo: { totalLidas: 2, novas: 1, jaCadastradas: 1, invalidas: 0, ambiguas: 0, duplicadas: 0 },
      } })
    } else {
      estado.clientes.push({ ...estado.clientes[1], id: 'importado', nome: 'Contato importado E2E', telefoneWhatsapp: '5511988887777' })
      await route.fulfill({ json: { criados: 1, ignorados: 0 } })
    }
  })
  await page.getByRole('button', { name: 'Importar contatos', exact: true }).click()
  await expect(page.getByRole('tab', { name: 'Arquivo CSV' })).toBeVisible()
  await expect(page.getByRole('tab', { name: 'Contatos do WhatsApp' })).toBeVisible()
  await page.getByRole('dialog').getByRole('textbox').fill('Contato importado E2E, (11) 98888-7777\nMarina Souza, (11) 99999-1111')
  await page.getByRole('button', { name: 'Analisar lista' }).click()
  await expect(page.getByRole('checkbox', { name: 'Incluir Contato importado E2E' })).toBeChecked()
  await page.getByRole('checkbox', { name: 'Incluir Contato importado E2E' }).uncheck()
  await expect(page.getByRole('dialog').getByRole('button', { name: 'Importar', exact: true })).toBeDisabled()
  await page.getByRole('checkbox', { name: 'Incluir Contato importado E2E' }).check()
  await page.getByRole('button', { name: 'Importar (1)', exact: true }).click()
  await expect(page.getByText('Contato importado E2E', { exact: true })).toBeVisible()
  expect(chamadas[0].fonte).toBe('Texto')
  expect(chamadas[1]).toEqual({ fonte: 'Texto', linhas: [{ nome: 'Contato importado E2E', telefoneE164: '5511988887777' }] })
  expect(estado.chamadasExternas).toEqual([])
})

test('teclado e movimento reduzido preservam filtros e acesso ao cadastro apos resize', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await abrirClientes(page)
  const editar = page.getByRole('button', { name: 'Editar Carlos Lima', exact: true })
  await editar.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('dialog', { name: 'Editar cliente' })).toBeVisible()
  await page.keyboard.press('Escape')
  await page.getByRole('button', { name: 'Sem CPF/CNPJ' }).click()
  await page.setViewportSize({ width: 390, height: 844 })
  await expect(page.getByRole('button', { name: 'Sem CPF/CNPJ' })).toHaveAttribute('aria-pressed', 'true')
  await expect(page.getByText('Carlos Lima', { exact: true })).toBeVisible()
  await expect(page.getByRole('progressbar', { name: 'Qualificação de Carlos Lima' })).toBeVisible()
})

for (const largura of [360, 768, 1024, 1366, 1920]) {
  test(`clientes sem transbordamento e com edicao acessivel em ${largura}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: largura, height: 900 })
    const estado = criarEstadoE2E()
    estado.clientes[1].nome = 'Carlos Lima de Albuquerque e Vasconcelos'
    for (let i = 3; i <= 15; i++) estado.clientes.push({ ...estado.clientes[1], id: `cliente-${i}`, nome: `Contato de exemplo ${i}` })
    await abrirClientes(page, estado)
    await expect(page.getByRole('list', { name: 'Clientes cadastrados' })).toBeVisible()
    const dimensoes = await page.evaluate(() => ({ largura: innerWidth, scroll: document.documentElement.scrollWidth, vertical: document.documentElement.scrollHeight, altura: innerHeight }))
    expect(dimensoes.scroll).toBeLessThanOrEqual(dimensoes.largura)
    expect(dimensoes.vertical).toBeLessThanOrEqual(dimensoes.altura)
    await page.screenshot({ path: testInfo.outputPath(`clientes-${largura}.png`), fullPage: true })
    await page.getByRole('button', { name: 'Editar Marina Souza', exact: true }).click()
    await expect(page.getByRole('textbox', { name: 'Nome *', exact: true })).toHaveValue('Marina Souza')
    await page.getByRole('tab', { name: 'Endereço' }).click()
    await expect(page.getByRole('dialog').getByRole('textbox', { name: 'Cidade', exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Cancelar', exact: true }).click()
    if (largura < 1024) {
      await page.getByRole('button', { name: 'Abrir filtros avançados' }).click()
      await expect(page.getByRole('combobox', { name: 'Cidade', exact: true })).toBeVisible()
    }
    expect(estado.chamadasExternas).toEqual([])
  })
}
