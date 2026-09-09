import { expect, test } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock-seguro'

async function abrirInbox(page: any, estado: ReturnType<typeof criarEstadoE2E>) {
  await instalarApiMock(page, estado)
  await autenticarLocalmente(page, 'Administrador')
  await page.goto('/atendimentos')

  // Os cenários de produto usam a preferência real de menu fixado para que o
  // rail ocupe a própria coluna no desktop e não sobreponha o conteúdo durante
  // a animação de hover. A animação em si tem cobertura unitária separada.
  await expect(page.getByRole('navigation', { name: 'Menu principal' })).toHaveCSS('width', '256px')
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

/**
 * Regressão do achado em produção de 2026-09-09: "Encerradas" só trocava o
 * status e mantinha o escopo anterior, então quem vinha de Minhas ou Fila via
 * apenas parte das encerradas — sem nenhuma pílula acesa explicando o recorte.
 *
 * A asserção é sobre a REQUISIÇÃO, e não sobre a lista: o mock ignora
 * apenasMinhas/apenasNaFila (só honra `busca`), então contar linhas na tela
 * passaria mesmo com o bug de volta. O contrato que quebrou é o da query.
 */
test('trocar de filtro nao acumula o recorte anterior na consulta', async ({ page }) => {
  const estado = criarEstadoE2E()
  const consultas: string[] = []
  page.on('request', (req) => {
    const url = new URL(req.url())
    if (url.pathname === '/api/conversas') consultas.push(url.search)
  })

  await abrirInbox(page, estado)

  // A inbox abre na fila — quem chega ao balcão vê primeiro o que não tem dono.
  await expect.poll(() => consultas.some((c) => c.includes('apenasNaFila=true'))).toBe(true)

  const escopos = page.getByRole('group', { name: 'Escopo das conversas' })
  await escopos.getByRole('button', { name: 'Minhas' }).click()
  await expect.poll(() => consultas.at(-1)).toContain('apenasMinhas=true')

  await escopos.getByRole('button', { name: 'Encerradas' }).click()
  await expect.poll(() => consultas.at(-1)).toContain('status=Encerrada')

  const encerradas = consultas.at(-1) ?? ''
  expect(encerradas).not.toContain('apenasMinhas')
  expect(encerradas).not.toContain('apenasNaFila')

  // E o inverso: sair de Encerradas volta para uma vista limpa de ativas.
  await escopos.getByRole('button', { name: 'Todas' }).click()
  await expect.poll(() => consultas.at(-1)).toContain('status=Ativa')
  expect(consultas.at(-1) ?? '').not.toContain('apenasMinhas')
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
