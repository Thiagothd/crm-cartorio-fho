import { NavLink, useNavigate } from 'react-router-dom'
import { Inbox, LogOut, Menu, Scale, Settings2, UserRound, Users, type LucideIcon } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { cn } from '@/lib/utils'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'

interface ItemDeMenu {
  para: string
  rotulo: string
  icone: LucideIcon
  exigeGestao?: boolean
}

const itens: ItemDeMenu[] = [
  { para: '/', rotulo: 'Atendimento', icone: Inbox },
  { para: '/clientes', rotulo: 'Clientes', icone: Users },
  { para: '/servicos', rotulo: 'Serviços', icone: Scale, exigeGestao: true },
  { para: '/configuracoes', rotulo: 'Configurações', icone: Settings2, exigeGestao: true },
]

export function Shell({ children }: { children: ReactNode }) {
  const { atendente, tenant, sair, podeGerirCadastros } = usarAutenticacao()
  const navegar = useNavigate()
  const [menuRecolhido, setMenuRecolhido] = useState(false)

  const visiveis = itens.filter((item) => !item.exigeGestao || podeGerirCadastros)

  return (
    <div className="flex h-dvh overflow-hidden bg-papel">
      <aside
        className={cn(
          'flex shrink-0 flex-col border-r border-linha-forte bg-tinta',
          menuRecolhido ? 'w-16' : 'w-60',
        )}
      >
        {/* Cabecalho: nome da serventia em Spectral. Sem logotipo — a serventia
            nao passou identidade grafica (DESIGN.md, secao 9). A tinta escura
            lembra a lombada do livro de registro e separa a navegacao do papel. */}
        <div
          className={cn(
            'flex min-h-[72px] items-start border-b border-linha-forte/35',
            menuRecolhido ? 'justify-center px-2 py-3' : 'justify-between gap-2 px-4 py-4',
          )}
        >
          {!menuRecolhido ? (
            <div className="min-w-0">
              <p className="font-titulo text-[15px] font-semibold leading-tight text-branco">
                {tenant?.nomeFantasia ?? tenant?.razaoSocial ?? 'Cartório'}
              </p>
              <p className="mt-0.5 text-[11px] uppercase tracking-wider text-linha">
                Atendimento
              </p>
            </div>
          ) : null}

          <button
            type="button"
            onClick={() => setMenuRecolhido((valor) => !valor)}
            className="flex size-9 shrink-0 items-center justify-center rounded-md border border-linha-forte/40 text-papel transition-colors hover:border-linha-forte hover:bg-branco/10 hover:text-branco"
            aria-label={menuRecolhido ? 'Abrir menu principal' : 'Recolher menu principal'}
            aria-expanded={!menuRecolhido}
            title={menuRecolhido ? 'Abrir menu' : 'Recolher menu'}
          >
            <Menu className="size-4" />
          </button>
        </div>

        <nav className="flex flex-1 flex-col gap-0.5 p-2">
          {visiveis.map(({ para, rotulo, icone: Icone }) => (
            <NavLink
              key={para}
              to={para}
              end={para === '/'}
              className={({ isActive }) =>
                cn(
                  'flex h-9 items-center rounded-md border text-sm transition-colors',
                  menuRecolhido ? 'justify-center px-2' : 'gap-2.5 px-2.5',
                  isActive
                    ? // Item ativo: uma das ocorrencias de bordo permitidas.
                      'border-linha-forte bg-branco font-medium text-selo'
                    : 'border-transparent text-linha hover:bg-branco/10 hover:text-branco',
                )
              }
              title={menuRecolhido ? rotulo : undefined}
            >
              <Icone className="size-4 shrink-0" />
              <span className={cn(menuRecolhido && 'sr-only')}>{rotulo}</span>
            </NavLink>
          ))}
        </nav>

        <div className="border-t border-linha-forte/35 p-2">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button
                className={cn(
                  'flex min-h-9 w-full rounded-md border border-transparent text-left transition-colors hover:border-linha-forte/40 hover:bg-branco/10',
                  menuRecolhido
                    ? 'items-center justify-center px-2 py-2 text-linha'
                    : 'flex-col items-start gap-0.5 px-2.5 py-2',
                )}
                title={menuRecolhido ? atendente?.nome : undefined}
              >
                {menuRecolhido ? (
                  <>
                    <UserRound className="size-4" />
                    <span className="sr-only">Abrir menu de {atendente?.nome}</span>
                  </>
                ) : (
                  <>
                    <span className="w-full truncate text-sm font-medium text-branco">
                      {atendente?.nome}
                    </span>
                    {/* Cargo REAL, não o perfil de acesso traduzido. São eixos
                        independentes desde a migração 007: um escrevente
                        autorizado pode ser Administrador do painel. O rótulo vem
                        pronto do backend, mesma fonte que a tela de Atendentes. */}
                    <span className="w-full truncate text-[11px] uppercase tracking-wider text-linha">
                      {atendente?.cargo && atendente.cargo !== 'NaoInformado'
                        ? atendente.cargoDescrito
                        : ''}
                    </span>
                  </>
                )}
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" className="w-56">
              {/* break-words e whitespace-normal: e-mail longo em fonte
                  monoespaçada estourava a largura fixa do menu, porque
                  DropdownMenuLabel não quebra por padrão. Quebrar é melhor que
                  truncar — o e-mail é o login, e login pela metade não serve
                  para conferir nada. */}
              <DropdownMenuLabel className="whitespace-normal break-words font-dado text-[11px] font-normal leading-relaxed normal-case text-tinta-media">
                {atendente?.email}
              </DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem
                onSelect={() => {
                  sair()
                  navegar('/login')
                }}
              >
                <LogOut />
                Sair
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </aside>

      <main className="flex min-w-0 flex-1 flex-col overflow-hidden">{children}</main>
    </div>
  )
}

export function CabecalhoDaPagina({
  titulo,
  descricao,
  acoes,
}: {
  titulo: string
  descricao?: string
  acoes?: ReactNode
}) {
  return (
    <header className="flex shrink-0 items-center justify-between gap-4 border-b border-linha-forte bg-branco px-6 py-3.5">
      <div className="min-w-0">
        <h1 className="truncate font-titulo text-lg leading-tight text-tinta">{titulo}</h1>
        {descricao ? <p className="mt-0.5 text-xs text-tinta-media">{descricao}</p> : null}
      </div>
      {acoes ? <div className="flex shrink-0 items-center gap-2">{acoes}</div> : null}
    </header>
  )
}

export function EstadoVazio({
  icone: Icone,
  titulo,
  descricao,
  acao,
}: {
  icone: LucideIcon
  titulo: string
  descricao?: string
  acao?: ReactNode
}) {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-3 p-10 text-center">
      <div className="flex size-11 items-center justify-center rounded-md border border-linha-forte text-tinta-fraca">
        <Icone className="size-5" />
      </div>
      <div>
        <p className="font-titulo text-[15px] text-tinta">{titulo}</p>
        {descricao ? (
          <p className="mt-1 max-w-sm text-sm text-tinta-media">{descricao}</p>
        ) : null}
      </div>
      {acao ? <div className="mt-1">{acao}</div> : null}
    </div>
  )
}
