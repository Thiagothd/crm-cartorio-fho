import { mkdirSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import type { Locator, Page } from '@playwright/test'

const AQUI = path.dirname(fileURLToPath(import.meta.url))

/** Raiz onde as imagens do roteiro público ficam — fora de `e2e/`, dentro do `roteiro/` versionado. */
const RAIZ_DE_ASSETS = path.resolve(AQUI, '../../../../roteiro/assets/telas')

function caminhoDaImagem(nomeArquivo: string) {
  const destino = path.join(RAIZ_DE_ASSETS, nomeArquivo)
  mkdirSync(path.dirname(destino), { recursive: true })
  return destino
}

/** Prepara a página para captura determinística: sem animação, relógio travado, sem cursor de foco residual. */
export async function prepararParaCaptura(page: Page, dataFixa: string) {
  await page.clock.install({ time: new Date(dataFixa) })
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.addStyleTag({
    content: `
      *, *::before, *::after { animation-duration: 0s !important; animation-delay: 0s !important; transition-duration: 0s !important; caret-color: transparent !important; }
      ::-webkit-scrollbar { display: none; }
    `,
  })
}

/** Screenshot de página inteira — para contextualizar a tela. Nome estável: o HTML do manual referencia exatamente este caminho. */
export async function capturarTela(page: Page, nomeArquivo: string, opcoes: { fullPage?: boolean } = {}) {
  await page.waitForLoadState('networkidle')
  await page.screenshot({ path: caminhoDaImagem(nomeArquivo), fullPage: opcoes.fullPage ?? false })
}

/** Screenshot recortado num elemento específico — para destacar um campo/botão sem mostrar a tela toda. */
export async function capturarElemento(locator: Locator, nomeArquivo: string) {
  await locator.scrollIntoViewIfNeeded()
  await locator.screenshot({ path: caminhoDaImagem(nomeArquivo) })
}

export interface AlvoDeDestaque {
  locator: Locator
  numero?: number
  rotulo?: string
}

/**
 * Desenha um contorno numerado sobre cada elemento (posição calculada na hora,
 * via `getBoundingClientRect` — nunca desalinha porque não é desenhado à mão
 * sobre a imagem depois), tira a screenshot, e remove os nós injetados.
 */
export async function capturarComDestaque(page: Page, alvos: AlvoDeDestaque[], nomeArquivo: string, opcoes: { fullPage?: boolean } = {}) {
  const retangulos = []
  for (const alvo of alvos) {
    await alvo.locator.scrollIntoViewIfNeeded()
    const caixa = await alvo.locator.boundingBox()
    if (caixa) retangulos.push({ ...caixa, numero: alvo.numero, rotulo: alvo.rotulo })
  }

  await page.evaluate((itens) => {
    const camada = document.createElement('div')
    camada.id = 'captura-camada-de-destaque'
    camada.style.cssText = 'position:fixed; inset:0; z-index:2147483647; pointer-events:none;'
    for (const item of itens) {
      const contorno = document.createElement('div')
      contorno.style.cssText = `position:fixed; left:${item.x - 4}px; top:${item.y - 4}px; width:${item.width + 8}px; height:${item.height + 8}px; border:3px solid #E8590C; border-radius:8px; box-shadow:0 0 0 9999px rgba(15,15,15,0.18);`
      camada.appendChild(contorno)
      if (item.numero != null) {
        const selo = document.createElement('div')
        selo.textContent = String(item.numero)
        selo.style.cssText = `position:fixed; left:${item.x - 16}px; top:${item.y - 16}px; width:26px; height:26px; border-radius:50%; background:#E8590C; color:#fff; font:700 14px/26px system-ui, sans-serif; text-align:center;`
        camada.appendChild(selo)
      }
    }
    document.body.appendChild(camada)
  }, retangulos)

  await capturarTela(page, nomeArquivo, opcoes)

  await page.evaluate(() => document.getElementById('captura-camada-de-destaque')?.remove())
}
