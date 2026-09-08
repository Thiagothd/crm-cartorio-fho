import { BarChart3, MessagesSquare, UserRound, Users } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { AbasDeSecao } from '@/components/layout/abas-de-secao'

export type SecaoDeRelatorio = 'dashboard' | 'atendimentos' | 'atendentes' | 'clientes'

export const SECOES: readonly {
  chave: SecaoDeRelatorio
  rotulo: string
  icone: LucideIcon
  /** Aparece sob o título da aba, no cabeçalho da seção. */
  descricao: string
}[] = [
  {
    chave: 'dashboard',
    rotulo: 'Dashboard',
    icone: BarChart3,
    descricao: 'Doze meses de movimento e o retrato de hoje, da semana e do mês',
  },
  {
    chave: 'atendimentos',
    rotulo: 'Atendimentos',
    icone: MessagesSquare,
    descricao: 'Volume, distribuição e quais clientes concentram o movimento',
  },
  {
    chave: 'atendentes',
    rotulo: 'Atendentes',
    icone: UserRound,
    descricao: 'Como o trabalho está distribuído na equipe',
  },
  {
    chave: 'clientes',
    rotulo: 'Clientes',
    icone: Users,
    descricao: 'Tamanho e crescimento da base cadastrada',
  },
]

export function ehSecaoValida(valor: string | undefined): valor is SecaoDeRelatorio {
  return SECOES.some((s) => s.chave === valor)
}

/**
 * As abas são LINKS, não estado local — a seção vive na URL
 * (`/indicadores/atendentes`). O desenho e o comportamento vivem em
 * `components/layout/abas-de-secao.tsx`, compartilhados com as outras telas
 * que se dividem em seções (Mensagens e IA); aqui fica só a LISTA de seções
 * desta tela.
 *
 * Fora do menu do rail de propósito: são subdivisões de UMA tela, não quatro
 * destinos do sistema. Colocá-las em `navegacao.ts` inflaria o menu principal
 * com detalhe de uma página só.
 */
export function AbasDeRelatorio({ className }: { className?: string }) {
  return (
    <AbasDeSecao
      base="/indicadores"
      secoes={SECOES}
      rotuloDaNavegacao="Seções de Indicadores e Relatórios"
      className={className}
    />
  )
}
