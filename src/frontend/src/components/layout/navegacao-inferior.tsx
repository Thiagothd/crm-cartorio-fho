import { NavLink } from 'react-router-dom'
import { Home, Inbox, Menu, Users } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'

function Celula({ to, fim, icone: Icone, rotulo }: { to: string; fim?: boolean; icone: LucideIcon; rotulo: string }) {
  return (
    <NavLink
      to={to}
      end={fim}
      className={({ isActive }) =>
        cn(
          'relative flex flex-1 touch-manipulation flex-col items-center justify-center gap-0.5 text-[10px] transition-colors',
          isActive ? 'text-branco' : 'text-linha',
        )
      }
    >
      {({ isActive }) => (
        <>
          {isActive ? <span aria-hidden className="absolute top-0 h-0.5 w-8 rounded-full bg-selo" /> : null}
          <Icone className="size-5" aria-hidden />
          {rotulo}
        </>
      )}
    </NavLink>
  )
}

/**
 * Navegação do celular, projetada para celular — não é o rail encolhido.
 * Sem hambúrguer: os 3 destinos mais frequentes ficam direto na barra, e
 * "Mais" abre a folha com o menu inteiro (`FolhaMais`). Continua sendo a
 * única superfície escura da aplicação, junto do rail (DESIGN.md §10.3).
 */
export function NavegacaoInferior({ aoAbrirMais }: { aoAbrirMais: () => void }) {
  return (
    <nav
      aria-label="Navegação principal"
      className="flex h-14 shrink-0 items-stretch border-t border-linha-forte bg-tinta pb-[env(safe-area-inset-bottom)] md:hidden"
    >
      <Celula to="/" fim icone={Home} rotulo="Visão Geral" />
      <Celula to="/atendimentos" icone={Inbox} rotulo="Atendimento" />
      <Celula to="/clientes" icone={Users} rotulo="Clientes" />
      <button
        type="button"
        onClick={aoAbrirMais}
        className="flex flex-1 touch-manipulation flex-col items-center justify-center gap-0.5 text-[10px] text-linha transition-colors"
      >
        <Menu className="size-5" aria-hidden />
        Mais
      </button>
    </nav>
  )
}
