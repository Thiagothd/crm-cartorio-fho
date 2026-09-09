import { test, expect } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../mock-de-capturas'
import { prepararParaCaptura, capturarTela, capturarElemento, capturarComDestaque } from '../ajudantes-de-captura'
import { AGORA } from '../estado-de-demonstracao'

async function abrir(page: any, perfil: 'Administrador' | 'Supervisor' | 'Atendente' = 'Administrador') {
  const estado = criarEstadoDeCaptura(perfil)
  await instalarMockDeCapturas(page, estado)
  await autenticarComoDemo(page, perfil)
  await prepararParaCaptura(page, AGORA)
  return estado
}

test('inbox — visão geral da fila', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos')
  await expect(page.getByText('Maria Aparecida Souza')).toBeVisible()
  await capturarTela(page, 'atendimento/inbox-fila.png')

  await capturarComDestaque(
    page,
    [
      { locator: page.getByRole('button', { name: 'Novo atendimento' }).first(), numero: 1, rotulo: 'Novo atendimento' },
      { locator: page.getByPlaceholder('Nome, telefone ou protocolo'), numero: 2, rotulo: 'Buscar' },
    ],
    'atendimento/inbox-botoes-principais.png',
  )
})

test('inbox — indicadores do painel de resumo', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos')
  await expect(page.getByText('Na fila').first()).toBeVisible()
  await capturarTela(page, 'atendimento/inbox-indicadores.png')
})

test('inbox — diálogo de novo atendimento', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos')
  await page.getByRole('button', { name: 'Novo atendimento' }).first().click()
  await expect(page.getByRole('dialog')).toContainText('Novo atendimento')
  await capturarTela(page, 'atendimento/dialogo-novo-atendimento-buscar-cliente.png')

  await page.getByText('Cadastrar cliente novo').click()
  await capturarTela(page, 'atendimento/dialogo-novo-atendimento-cliente-novo.png')
})

test('conversa aberta — visão geral, com anexo e citação', async ({ page }) => {
  // conversa-rica pertence a Paula Ribeiro — só o responsável vê a caixa de
  // texto (qualquer outro atendente veria a trava "Em atendimento por...").
  await abrir(page, 'Atendente')
  await page.goto('/atendimentos/conversa-rica')
  await expect(page.getByRole('heading', { name: 'Maria Aparecida Souza' })).toBeVisible()
  await capturarTela(page, 'atendimento/conversa-aberta-completa.png')

  await capturarComDestaque(
    page,
    [{ locator: page.getByPlaceholder(/Escreva a resposta/), numero: 1, rotulo: 'Caixa de envio' }],
    'atendimento/conversa-caixa-de-envio.png',
  )
})

test('conversa — respostas rápidas com atalho "/"', async ({ page }) => {
  await abrir(page, 'Atendente')
  await page.goto('/atendimentos/conversa-rica')
  const campo = page.getByPlaceholder(/Escreva a resposta/)
  await campo.fill('/')
  await page.waitForTimeout(150)
  await capturarTela(page, 'atendimento/resposta-rapida-seletor.png')
})

test('conversa — menu de ações (transferir, devolver, encerrar)', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos/conversa-rica')
  await page.getByRole('button', { name: 'Ações' }).click()
  await expect(page.getByText('Transferir atendimento')).toBeVisible()
  await capturarTela(page, 'atendimento/menu-de-acoes.png')

  await page.getByText('Transferir atendimento').click()
  await expect(page.getByRole('dialog')).toContainText('Transferir atendimento')
  await capturarTela(page, 'atendimento/dialogo-transferir.png')
  await page.getByRole('button', { name: 'Cancelar' }).click()

  await page.getByRole('button', { name: 'Ações' }).click()
  await page.getByText('Encerrar atendimento').click()
  await expect(page.getByRole('dialog')).toContainText('Encerrar atendimento')
  await capturarTela(page, 'atendimento/dialogo-encerrar.png')
})

test('conversa — assumir da fila e trava por outro atendente', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos/conversa-fila-alerta')
  await expect(page.getByRole('button', { name: 'Assumir', exact: true })).toBeVisible()
  await capturarComDestaque(page, [{ locator: page.getByRole('button', { name: 'Assumir', exact: true }), numero: 1 }], 'atendimento/conversa-assumir.png')
})

test('conversa — travada por outro atendente (sem caixa de texto)', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos/conversa-atrasada')
  await expect(page.getByText(/Em atendimento por/)).toBeVisible()
  await capturarTela(page, 'atendimento/conversa-travada-outro-atendente.png')
})

test('conversa encerrada — selo de concluído e reabrir', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos/conversa-encerrada')
  await capturarTela(page, 'atendimento/conversa-encerrada.png')
  await page.getByRole('button', { name: 'Ações' }).click()
  await expect(page.getByText('Reabrir atendimento')).toBeVisible()
  await capturarTela(page, 'atendimento/menu-reabrir.png')
})

test('conversa — mensagem gerada pela IA', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos/conversa-ia')
  await capturarTela(page, 'atendimento/conversa-mensagem-ia.png')
})

test('conversa — mensagem fixada e mensagem oculta', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos/conversa-fixada')
  await capturarTela(page, 'atendimento/conversa-fixada-oculta.png')
})

test('conversa — painel de histórico e de mídias', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos/conversa-rica')
  await page.getByTitle('Histórico de etapas').click()
  await page.waitForTimeout(200)
  await capturarTela(page, 'atendimento/painel-historico.png')
  await page.getByTitle('Mídias e arquivos').click()
  await page.waitForTimeout(200)
  await capturarTela(page, 'atendimento/painel-midias.png')
})

test('conversa — copiar e responder mensagem (hover)', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos/conversa-rica')
  const mensagem = page.locator('#mensagem-m10')
  await mensagem.hover()
  await page.waitForTimeout(150)
  await capturarTela(page, 'atendimento/mensagem-acoes-hover.png')
})

test('inbox — filtros avançados e busca', async ({ page }) => {
  await abrir(page)
  await page.goto('/atendimentos')
  await page.getByPlaceholder('Nome, telefone ou protocolo').fill('202609')
  await capturarTela(page, 'atendimento/inbox-busca-protocolo.png')
})

for (const perfil of ['Atendente'] as const) {
  test(`inbox — lista recolhida (coluna estreita)`, async ({ page }) => {
    await abrir(page, perfil)
    await page.goto('/atendimentos/conversa-rica')
    const recolher = page.getByRole('button').filter({ hasText: '' }).first()
    await capturarTela(page, 'atendimento/inbox-com-conversa-aberta.png')
  })
}
