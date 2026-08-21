import { NavLink } from 'react-router-dom'
import { BarChart3, MessagesSquare, UserRound, Users } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'

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
 * (`/indicadores/atendentes`).
 *
 * Custa o mesmo e resolve três coisas que estado local não resolve: F5 volta na
 * mesma aba, o link compartilhado abre onde o colega quer mostrar, e o botão
 * "voltar" do navegador faz o que a pessoa espera. Foi a mesma razão que levou os
 * filtros da inbox a serem semeáveis por query string.
 *
 * Fora do menu do rail de propósito: são subdivisões de UMA tela, não quatro
 * destinos do sistema. Colocá-las em `navegacao.ts` inflaria o menu principal
 * com detalhe de uma página só.
 */
export function AbasDeRelatorio({ className }: { className?: string }) {
  return (
    <nav
      aria-label="Seções de Indicadores e Relatórios"
      className={cn(
        // Rola na horizontal no celular em vez de quebrar em duas linhas: quatro
        // abas com ícone não cabem em 360px, e uma segunda linha empurraria o
        // conteúdo para baixo justamente na tela que tem menos altura.
        'flex gap-1 overflow-x-auto rounded-full border border-linha bg-superficie p-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden',
        className,
      )}
    >
      {SECOES.map((secao) => (
        <NavLink
          key={secao.chave}
          to={`/indicadores/${secao.chave}`}
          className={({ isActive }) =>
            cn(
              // 44px no celular (DESIGN.md 12.2): este é o ÚNICO caminho entre
              // as quatro seções, e estava com 36px de alvo.
              'flex min-h-11 shrink-0 touch-manipulation items-center gap-2 rounded-full px-3.5 text-[13px] font-semibold transition-all duration-200 sm:min-h-9',
              isActive
                ? 'bg-tinta text-branco shadow-card'
                : 'text-tinta-media hover:bg-branco hover:text-tinta',
            )
          }
        >
          <secao.icone className="size-4 shrink-0" aria-hidden />
          {secao.rotulo}
        </NavLink>
      ))}
    </nav>
  )
}
