import type { ConhecimentoDeIa } from '@/lib/api/tipos'

/**
 * `VigenteDesde`/`VigenteAte` chegam como "AAAA-MM-DD" puro (DateOnly, sem
 * hora). Formata por string, sem passar por `Date` — evitar
 * `new Date("AAAA-MM-DD")` aqui é o que evita o dia deslocar por fuso (ele é
 * interpretado como meia-noite UTC, e o Intl formata no fuso local).
 */
export function formatarDataOnly(iso: string) {
  const [ano, mes, dia] = iso.split('-')
  return `${dia}/${mes}/${ano}`
}

export function descreverVigencia(desde: string | null, ate: string | null) {
  if (!desde && !ate) return 'Sempre'
  if (desde && ate) return `${formatarDataOnly(desde)} – ${formatarDataOnly(ate)}`
  if (desde) return `A partir de ${formatarDataOnly(desde)}`
  return `Até ${formatarDataOnly(ate!)}`
}

/** Hoje em "AAAA-MM-DD" local — comparável direto com o DateOnly do backend. */
export function hojeIso(): string {
  const agora = new Date()
  const mes = String(agora.getMonth() + 1).padStart(2, '0')
  const dia = String(agora.getDate()).padStart(2, '0')
  return `${agora.getFullYear()}-${mes}-${dia}`
}

/**
 * A MESMA janela que `MontadorDeContexto` aplica ao montar o contexto da IA:
 * ativo e dentro do intervalo de datas. Strings ISO comparam na ordem certa,
 * então não é preciso converter para `Date`.
 *
 * O backend resolve "hoje" em UTC e aqui é o fuso do navegador — nas poucas
 * horas em que os dois discordam, esta tela pode antecipar ou atrasar em um
 * dia o rótulo de um cadastro que começa/termina exatamente hoje. É rótulo de
 * leitura, não decisão: quem decide o que a IA lê continua sendo o backend.
 */
export function estaEmUsoPelaIa(conhecimento: ConhecimentoDeIa, hoje = hojeIso()): boolean {
  if (!conhecimento.ativo) return false

  const comecou = !conhecimento.vigenteDesde || conhecimento.vigenteDesde <= hoje
  const naoTerminou = !conhecimento.vigenteAte || conhecimento.vigenteAte >= hoje

  return comecou && naoTerminou
}

/** Ativo, mas fora da janela de datas — cadastrado e mudo. */
export function estaForaDeVigencia(conhecimento: ConhecimentoDeIa, hoje = hojeIso()): boolean {
  return conhecimento.ativo && !estaEmUsoPelaIa(conhecimento, hoje)
}
