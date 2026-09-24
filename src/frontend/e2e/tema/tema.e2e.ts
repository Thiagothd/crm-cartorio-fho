import { test, expect, type Page } from '@playwright/test'
import { instalarMockDeCapturas, criarEstadoDeCaptura, autenticarComoDemo } from '../capturas/mock-de-capturas'
import type { Perfil } from '../capturas/estado-de-demonstracao'

async function preparar(page: Page, autenticado = true, suporte = false, perfil: Perfil = 'Administrador') {
  const estado = criarEstadoDeCaptura(perfil)
  estado.atendentes = estado.atendentes.map((atendente) => ({
    ...atendente,
    permissoes: [...atendente.permissoes, 'seguranca.acessos.ver', 'seguranca.acesso_tecnico.aprovar', ...(suporte ? ['suporte.solicitar_acesso'] : [])],
  }))
  await instalarMockDeCapturas(page, estado)
  await page.route('**/api/seguranca/**', (route) => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/ativa')) return route.fulfill({ status: 204 })
    return route.fulfill({ json: path.endsWith('/protecao')
      ? { cifraAtiva: true, cifraDesde: '2026-09-01T12:00:00Z', versaoDaChave: 'demo', totalDeRegistrosDeAcesso: 12 }
      : { itens: [], total: 0, pagina: 1, tamanho: 20 } })
  })
  if (autenticado) await autenticarComoDemo(page, perfil)
  await page.addInitScript(() => {
    if (!localStorage.getItem('crm-cartorio.tema')) localStorage.setItem('crm-cartorio.tema', 'escuro')
  })
}

async function verificarTela(page: Page) {
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'escuro')
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(16, 17, 19)')
  await expect(page.getByText('Não foi possível abrir esta página', { exact: true })).toHaveCount(0)
  const tamanho = await page.evaluate(() => ({ largura: innerWidth, conteudo: document.documentElement.scrollWidth }))
  expect(tamanho.conteudo).toBeLessThanOrEqual(tamanho.largura)
}

const telas = [
  '/', '/atendimentos', '/atendimentos/conversa-rica', '/clientes', '/servicos',
  '/indicadores/dashboard', '/indicadores/atendimentos', '/indicadores/atendentes', '/indicadores/clientes',
  '/automacao/mensagens/automaticas', '/automacao/mensagens/respostas', '/automacao/mensagens/encerramento',
  '/automacao/ia/situacao', '/automacao/ia/conhecimento', '/equipe', '/equipe/atendente-juliana/permissoes',
  '/estrutura/etapas', '/estrutura/setores', '/integracoes/whatsapp', '/aparencia', '/seguranca',
  '/acesso-aos-dados', '/suporte',
]

for (const rota of telas) {
  test(`tema escuro ${rota}`, async ({ page }, info) => {
    const erros: string[] = []
    page.on('pageerror', (erro) => erros.push(erro.message))
    await preparar(page, true, rota === '/suporte')
    await page.goto(rota)
    await expect(page.getByRole('main')).toBeVisible()
    await page.waitForLoadState('networkidle')
    expect(new URL(page.url()).pathname).toBe(rota)
    await verificarTela(page)
    expect(erros).toEqual([])
    await page.screenshot({ path: info.outputPath('tela.png'), fullPage: true })
  })
}

for (const rota of ['/login', '/mfa/matricular', '/central-ia']) {
  test(`tema escuro publico ${rota}`, async ({ page }, info) => {
    await preparar(page, false)
    await page.goto(rota)
    await page.waitForLoadState('networkidle')
    await verificarTela(page)
    if (rota === '/login') await expect(page.getByRole('button', { name: 'Entrar', exact: true })).toHaveCSS('background-image', /40, 101, 188/)
    await page.screenshot({ path: info.outputPath('tela.png'), fullPage: true })
  })
}

test('troca, persistencia e preferencia do sistema', async ({ page }) => {
  await preparar(page)
  await page.goto('/aparencia')
  await page.getByRole('radio', { name: 'Claro', exact: true }).check()
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'claro')
  await page.reload()
  await expect(page.getByRole('radio', { name: 'Claro', exact: true })).toBeChecked()
  await page.getByRole('radio', { name: 'Sistema', exact: true }).check()
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'escuro')
  await page.emulateMedia({ colorScheme: 'light' })
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'claro')
  await page.getByRole('button', { name: 'Tema: Sistema' }).click()
  await page.getByRole('menuitemradio', { name: 'Escuro' }).click()
  await verificarTela(page)
})

test('dialogo, foco e menu em portal acompanham o tema', async ({ page }, info) => {
  await preparar(page)
  await page.goto('/clientes')
  const novo = page.getByRole('button', { name: /^Novo( cliente)?$/i })
  await novo.click()
  const dialogo = page.getByRole('dialog')
  await expect(dialogo).toBeVisible()
  await expect(dialogo).toHaveCSS('background-color', 'rgb(32, 35, 40)')
  const caixa = await dialogo.boundingBox()
  expect(caixa!.x).toBeGreaterThanOrEqual(0)
  expect(caixa!.x + caixa!.width).toBeLessThanOrEqual(page.viewportSize()!.width + 1)
  await page.screenshot({ path: info.outputPath('dialogo.png') })
  await page.keyboard.press('Escape')
  await expect(dialogo).toHaveCount(0)
  const seletor = page.getByRole('button', { name: 'Tema: Escuro' })
  await seletor.click()
  await expect(page.getByRole('menuitemradio', { name: 'Escuro' })).toHaveAttribute('aria-checked', 'true')
  await page.keyboard.press('Escape')
  await expect(seletor).toBeFocused()
})

test('contraste dos textos, estados e etiquetas no escuro', async ({ page }) => {
  await preparar(page)
  await page.goto('/aparencia')
  const contrastes = await page.evaluate(() => {
    const css = getComputedStyle(document.documentElement)
    const luminancia = (token: string) => {
      const hex = css.getPropertyValue(token).trim().replace('#', '')
      const canais = [0, 2, 4].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
        .map((valor) => valor <= 0.04045 ? valor / 12.92 : ((valor + 0.055) / 1.055) ** 2.4)
      return canais[0] * 0.2126 + canais[1] * 0.7152 + canais[2] * 0.0722
    }
    const pares = ['--cor-papel', '--cor-superficie', '--cor-branco'].flatMap((fundo) =>
      ['--cor-tinta', '--cor-tinta-media', '--cor-tinta-fraca', '--cor-selo', '--cor-ambar', '--cor-verde', '--cor-informacao'].map((texto) => [texto, fundo]))
    pares.push(['--primary-foreground', '--primary'], ['--destructive-foreground', '--destructive'], ['--cor-sobre-selecao', '--cor-selecao'])
    for (const etapa of ['azul', 'roxo', 'ambar', 'verde']) pares.push([`--etapa-${etapa}-texto`, `--etapa-${etapa}-fundo`])
    return pares.map(([texto, fundo]) => {
      const valores = [luminancia(texto), luminancia(fundo)].sort((a, b) => b - a)
      return { texto, fundo, contraste: (valores[0] + 0.05) / (valores[1] + 0.05) }
    })
  })
  for (const par of contrastes) expect(par.contraste, `${par.texto} sobre ${par.fundo}`).toBeGreaterThanOrEqual(4.5)
})

test('tema salvo e aplicado antes do JavaScript do aplicativo', async ({ page }) => {
  await preparar(page, false)
  await page.route('**/src/main.tsx', (route) => route.abort())
  await page.goto('/login')
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'escuro')
  await expect(page.locator('html')).toHaveCSS('color-scheme', 'dark')
})

test('sincroniza a preferencia entre abas', async ({ page, context }) => {
  await preparar(page)
  await page.goto('/aparencia')
  const outra = await context.newPage()
  await preparar(outra)
  await outra.goto('/aparencia')
  await page.getByRole('radio', { name: 'Claro', exact: true }).check()
  await expect(outra.locator('html')).toHaveAttribute('data-tema', 'claro')
  await expect(outra.getByRole('radio', { name: 'Claro', exact: true })).toBeChecked()
})

test('cabecalho e menu completos em 360px e paisagem', async ({ page }, info) => {
  await preparar(page)
  for (const viewport of [{ width: 360, height: 800 }, { width: 844, height: 390 }]) {
    await page.setViewportSize(viewport)
    await page.goto('/clientes')
    await expect(page.getByRole('heading', { name: 'Clientes', exact: true })).toBeVisible()
    const titulo = page.getByRole('heading', { name: 'Clientes', exact: true })
    expect(await titulo.evaluate((el) => el.scrollWidth <= el.clientWidth)).toBe(true)
    await verificarTela(page)
    if (viewport.width === 360) {
      await page.getByRole('button', { name: 'Mais', exact: true }).click()
      await expect(page.getByRole('dialog')).toBeVisible()
      await page.screenshot({ path: info.outputPath('menu-celular.png') })
      await page.keyboard.press('Escape')
    }
  }
})

test('resposta preserva o rascunho e usa a acao azul', async ({ page }, info) => {
  await preparar(page, true, false, 'Atendente')
  await page.goto('/atendimentos/conversa-rica')
  const campo = page.getByPlaceholder(/Escreva a resposta/)
  await campo.fill('Rascunho de verificacao, sem envio.')
  const enviar = page.getByRole('button', { name: /^Enviar/ })
  await expect(enviar).toBeEnabled()
  await expect(enviar).toHaveCSS('background-image', /40, 101, 188/)
  await page.screenshot({ path: info.outputPath('resposta.png') })
  await page.evaluate(() => {
    localStorage.setItem('crm-cartorio.tema', 'claro')
    window.dispatchEvent(new StorageEvent('storage', { key: 'crm-cartorio.tema' }))
  })
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'claro')
  await expect(campo).toHaveValue('Rascunho de verificacao, sem envio.')
  await expect(enviar).toBeEnabled()
})

test('tema claro preservado na visao geral, atendimento e clientes', async ({ page }, info) => {
  await preparar(page)
  await page.goto('/aparencia')
  await page.getByRole('radio', { name: 'Claro', exact: true }).check()
  for (const rota of ['/', '/atendimentos', '/clientes']) {
    await page.goto(rota)
    await page.waitForLoadState('networkidle')
    await expect(page.locator('html')).toHaveAttribute('data-tema', 'claro')
    await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(237, 235, 230)')
    await expect(page.getByText('Não foi possível abrir esta página', { exact: true })).toHaveCount(0)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: info.outputPath(`claro-${rota.replaceAll('/', '') || 'visao-geral'}.png`) })
  }
})
