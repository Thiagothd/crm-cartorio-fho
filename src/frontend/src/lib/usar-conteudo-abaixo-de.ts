import { usarAbaixoDe } from './usar-abaixo-de'
import { usarRail, LARGURA_DO_RAIL_EXPANDIDO, LARGURA_DO_RAIL_RECOLHIDO } from '@/components/layout/contexto-do-rail'

/**
 * True quando a largura DISPONÍVEL PARA O CONTEÚDO está abaixo do valor
 * informado — a janela menos o rail de navegação, que agora empurra em vez
 * de sobrepor.
 *
 * Aritmética, e não `ResizeObserver`: o rail tem só duas larguras possíveis,
 * a escolha é por clique e fica no `localStorage` — é estado discreto e
 * conhecido antes do primeiro layout. Medir seria trocar essa certeza por um
 * quadro de atraso, e oscilaria durante a transição de largura do rail.
 */
export function usarConteudoAbaixoDe(largura: number) {
  const { expandido } = usarRail()
  const semRail = usarAbaixoDe(768) // rail é `hidden md:block`
  const rail = semRail ? 0 : expandido ? LARGURA_DO_RAIL_EXPANDIDO : LARGURA_DO_RAIL_RECOLHIDO

  return usarAbaixoDe(largura + rail)
}
