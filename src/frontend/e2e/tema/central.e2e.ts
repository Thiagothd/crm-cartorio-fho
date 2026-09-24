import { test, expect } from '@playwright/test'
import type { ObservabilidadeDaIa, SaudeDoProvedor } from '../../src/lib/api/central-ia'
import { instalarMockDeCapturas } from '../capturas/mock-de-capturas'

const saude: SaudeDoProvedor = {
  provedor: 'Gemini', configurado: true, modelo: 'modelo-de-teste',
  ultimaChamadaEm: null, ultimaChamadaComSucessoEm: null, ultimoErroTipo: null,
  ultimoErroCodigoHttp: null, ultimoErroEm: null, total429Hoje: 0, ultimo429Em: null,
  latenciaMediaMsHoje: null, taxaDeSucessoHoje: null, estadoDoCircuito: 'Fechado',
  circuitoVoltaATentarEm: null, fallbacksOriginadosHoje: 0, tokensEntradaHoje: 0,
  tokensSaidaHoje: 0, custoEstimadoUsdHoje: 0, limiteRpdInformado: null, requisicoesHoje: 0,
}

const dados: ObservabilidadeDaIa = {
  periodoDe: '2026-09-01T00:00:00Z', periodoAte: '2026-09-24T23:59:59Z',
  saudeAgora: { gemini: saude, anthropic: { ...saude, provedor: 'Anthropic' } },
  kpis: {
    tentativas: 0, rodadas: 0, entregues: 0, naoSoubeResponder: 0, falhas: 0,
    taxaDeSucesso: 0, taxaDeFalha: 0, fallbackExecutado: 0, fallbackPreventivoDesligado: 0,
    fallbackPreventivoDisjuntor: 0, fallbackPreventivoMargemDeCota: 0,
    tokensEntrada: 0, tokensSaida: 0, tokensPensamento: 0, custoEstimadoUsdTotal: 0,
    custoEstimadoReaisTotal: 0, custoMedioUsdPorRodada: null, tenantsAtivos: 0,
    total429: 0, totalTimeouts: 0, larguraDoBaldeDeLatenciaMs: 100,
    p50LatenciaMs: null, p95LatenciaMs: null, p99LatenciaMs: null,
  },
  linhaDoTempo: [], histogramaDeLatencia: [], errosPorCategoria: [],
  fallbacksPorTipo: [], distribuicaoPorModelo: [], topTenantsPorCusto: [],
}

test('central de IA autenticada e estados vazios', async ({ page }, info) => {
  const erros: string[] = []
  page.on('pageerror', (erro) => erros.push(erro.message))
  await instalarMockDeCapturas(page)
  await page.route('**/api/central-ia/**', (route) => route.fulfill({
    json: new URL(route.request().url()).pathname.endsWith('/observabilidade')
      ? dados : { itens: [], total: 0, pagina: 1, tamanhoDaPagina: 20 },
  }))
  await page.addInitScript(() => {
    localStorage.setItem('crm-cartorio.tema', 'escuro')
    localStorage.setItem('crm-cartorio.central-ia.token', 'demo-sem-acesso-real')
  })
  await page.goto('/central-ia')
  await expect(page.getByText('Volume por provedor', { exact: true })).toBeVisible()
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(16, 17, 19)')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  expect(erros).toEqual([])
  await page.screenshot({ path: info.outputPath('central.png'), fullPage: true })
})
