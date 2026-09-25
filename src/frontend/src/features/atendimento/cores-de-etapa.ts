/**
 * Cores de identificacao da etapa do funil.
 *
 * O banco guarda UMA cor por etapa (`EtapaFunil.Cor`), que e a cor de TINTA —
 * a identidade da etapa. O fundo e a borda saem deste mapa. Foi feito assim
 * porque guardar tres colunas de cor exigiria mudar o schema, e uma cor de tinta
 * determina as outras duas sem ambiguidade.
 *
 * A cor acompanha a ETAPA, nao a conversa nem o cliente: mover a conversa de
 * etapa muda a cor na hora, em todos os tres lugares onde ela aparece.
 *
 * Paleta formal em docs/DESIGN.md, secao 2.2.
 */
export interface CoresDeEtapa {
  /** Fundo claro, para pilula e etiqueta. */
  fundo: string
  /** Tinta. E o valor gravado em EtapaFunil.Cor, e a chave deste mapa. */
  texto: string
  /** Borda, entre o fundo e a tinta. */
  borda: string
}

const PALETA: Record<string, CoresDeEtapa> = {
  // Atendimento inicial — azul acinzentado
  '#2E5170': { fundo: 'var(--etapa-azul-fundo)', texto: 'var(--etapa-azul-texto)', borda: 'var(--etapa-azul-borda)' },

  // Captacao do servico — roxo suave
  '#6B3F73': { fundo: 'var(--etapa-roxo-fundo)', texto: 'var(--etapa-roxo-texto)', borda: 'var(--etapa-roxo-borda)' },

  // Em processamento / exigencia — ambar
  '#8A6516': { fundo: 'var(--etapa-ambar-fundo)', texto: 'var(--etapa-ambar-texto)', borda: 'var(--etapa-ambar-borda)' },

  // Pronto / entrega — verde. O selo continua sendo tratamento proprio deste
  // status e NAO e substituido por esta cor; os dois convivem.
  '#3D5C3A': { fundo: 'var(--etapa-verde-fundo)', texto: 'var(--etapa-verde-texto)', borda: 'var(--etapa-verde-borda)' },
}

/**
 * Usada quando a conversa nao tem etapa, ou quando a etapa foi cadastrada com uma
 * cor fora da paleta. O seletor de cor do cadastro e fechado nas quatro acima,
 * mas dado antigo no banco pode ter outra — cair no neutro e melhor que quebrar.
 */
const NEUTRA: CoresDeEtapa = {
  fundo: 'var(--cor-superficie)',
  texto: 'var(--cor-tinta-media)',
  borda: 'var(--cor-linha-forte)',
}

export function coresDaEtapa(cor: string | null | undefined): CoresDeEtapa {
  if (!cor) {
    return NEUTRA
  }

  return PALETA[cor.toUpperCase()] ?? NEUTRA
}

/**
 * Fundo solido do avatar. Devolve a tinta da etapa — que tem contraste de sobra
 * com branco nas quatro cores (menor caso: ambar, 5,3:1). Sem etapa, devolve
 * nulo e o avatar usa o tratamento neutro de borda.
 */
export function corSolidaDaEtapa(cor: string | null | undefined): string | null {
  if (!cor) {
    return null
  }

  return PALETA[cor.toUpperCase()] ? cor.toUpperCase() : null
}
