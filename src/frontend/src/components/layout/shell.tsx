import { NavLink, useNavigate } from 'react-router-dom'
import { Inbox, LogOut, Menu, Scale, Settings2, UserRound, Users, X, type LucideIcon } from 'lucide-react'
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
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

/**
 * Abre o menu principal. Só existe abaixo de `md`, onde o menu virou gaveta.
 *
 * Vai por contexto porque o `CabecalhoDaPagina` não é filho React do `Shell` —
 * cada página o renderiza dentro de `children`. Alternativa seria acrescentar
 * uma prop em todas as páginas para um botão que só aparece no celular.
 */
const ContextoDoMenu = createContext<(() => void) | null>(null)

export function Shell({ children }: { children: ReactNode }) {
  const { atendente, tenant, sair, podeGerirCadastros } = usarAutenticacao()
  const navegar = useNavigate()
  const [menuRecolhido, setMenuRecolhido] = useState(false)

  // Recolher (desktop) e abrir a gaveta (celular) são coisas diferentes: uma é
  // preferência de largura, a outra é presença na tela. Um estado só faria o
  // menu do celular nascer com 64px de rail.
  const [menuAberto, setMenuAberto] = useState(false)

  const visiveis = itens.filter((item) => !item.exigeGestao || podeGerirCadastros)

  useEffect(() => {
    if (!menuAberto) return

    function aoTeclar(evento: KeyboardEvent) {
      if (evento.key === 'Escape') {
        setMenuAberto(false)
      }
    }

    window.addEventListener('keydown', aoTeclar)
    return () => window.removeEventListener('keydown', aoTeclar)
  }, [menuAberto])

  return (
    <div className="flex h-dvh overflow-hidden bg-papel">
      {menuAberto ? (
        <button
          type="button"
          onClick={() => setMenuAberto(false)}
          aria-label="Fechar o menu principal"
          className="fixed inset-0 z-40 bg-tinta/40 md:hidden"
        />
      ) : null}

      <aside
        className={cn(
          'flex-col border-r border-linha-forte bg-tinta',
          // Celular: fora da tela por padrão, e por cima do conteúdo quando
          // aberto. Continua sendo a única superfície escura da aplicação —
          // ela é a navegação, e a navegação não divide espaço aqui.
          menuAberto ? 'fixed inset-y-0 left-0 z-50 flex w-64' : 'hidden',
          // md+: exatamente o de sempre.
          'md:static md:z-auto md:flex md:shrink-0',
          menuRecolhido ? 'md:w-16' : 'md:w-60',
        )}
      >
        {/* Cabecalho: nome da serventia em Spectral. Sem logotipo — a serventia
            nao passou identidade grafica (DESIGN.md, secao 9). A tinta escura
            lembra a lombada do livro de registro e separa a navegacao do papel. */}
        <div
          className={cn(
            'flex min-h-[72px] items-start border-b border-linha-forte/35',
            menuRecolhido
              ? 'justify-between gap-2 px-4 py-4 md:justify-center md:px-2 md:py-3'
              : 'justify-between gap-2 px-4 py-4',
          )}
        >
          <div className={cn('min-w-0', menuRecolhido && 'md:hidden')}>
            <p className="font-titulo text-[15px] font-semibold leading-tight text-branco">
              {tenant?.nomeFantasia ?? tenant?.razaoSocial ?? 'Cartório'}
            </p>
            <p className="mt-0.5 text-[11px] uppercase tracking-wider text-linha">
              Atendimento
            </p>
          </div>

          {/* Dois botões, um por modo, escolhidos por CSS: recolher para um rail
              de 64px não faz sentido numa gaveta que cobre a tela. */}
          <button
            type="button"
            onClick={() => setMenuAberto(false)}
            className="flex size-9 shrink-0 items-center justify-center rounded-md border border-linha-forte/40 text-papel transition-colors hover:border-linha-forte hover:bg-branco/10 hover:text-branco md:hidden"
            aria-label="Fechar menu principal"
            title="Fechar menu"
          >
            <X className="size-4" />
          </button>

          <button
            type="button"
            onClick={() => setMenuRecolhido((valor) => !valor)}
            className="hidden size-9 shrink-0 items-center justify-center rounded-md border border-linha-forte/40 text-papel transition-colors hover:border-linha-forte hover:bg-branco/10 hover:text-branco md:flex"
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
              // Escolher um destino fecha a gaveta. No desktop já está fechada,
              // então a chamada é inócua lá.
              onClick={() => setMenuAberto(false)}
              className={({ isActive }) =>
                cn(
                  'flex h-9 items-center gap-2.5 rounded-md border px-2.5 text-sm transition-colors',
                  menuRecolhido && 'md:justify-center md:gap-0 md:px-2',
                  isActive
                    ? // Item ativo: uma das ocorrencias de bordo permitidas.
                      'border-linha-forte bg-branco font-medium text-selo'
                    : 'border-transparent text-linha hover:bg-branco/10 hover:text-branco',
                )
              }
              title={menuRecolhido ? rotulo : undefined}
            >
              <Icone className="size-4 shrink-0" />
              <span className={cn(menuRecolhido && 'md:sr-only')}>{rotulo}</span>
            </NavLink>
          ))}
        </nav>

        <div className="border-t border-linha-forte/35 p-2">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button
                className={cn(
                  'flex min-h-9 w-full flex-col items-start gap-0.5 rounded-md border border-transparent px-2.5 py-2 text-left transition-colors hover:border-linha-forte/40 hover:bg-branco/10',
                  menuRecolhido && 'md:flex-row md:items-center md:justify-center md:px-2 md:text-linha',
                )}
                title={menuRecolhido ? atendente?.nome : undefined}
              >
                {menuRecolhido ? (
                  <UserRound className="hidden size-4 md:block" />
                ) : null}

                <span
                  className={cn(
                    'w-full truncate text-sm font-medium text-branco',
                    menuRecolhido && 'md:sr-only',
                  )}
                >
                  {atendente?.nome}
                </span>

                {/* Cargo REAL, não o perfil de acesso traduzido. São eixos
                    independentes desde a migração 007: um escrevente
                    autorizado pode ser Administrador do painel. O rótulo vem
                    pronto do backend, mesma fonte que a tela de Atendentes. */}
                <span
                  className={cn(
                    'w-full truncate text-[11px] uppercase tracking-wider text-linha',
                    menuRecolhido && 'md:hidden',
                  )}
                >
                  {atendente?.cargo && atendente.cargo !== 'NaoInformado'
                    ? atendente.cargoDescrito
                    : ''}
                </span>
              </button>
            </DropdownMenuTrigger>
            {/* w-72 e não w-56: o e-mail mais longo da serventia
                (helena@cartoriogameleiras.com.br, 36 caracteres em mono de
                11px) precisa de ~238px e o menu antigo oferecia ~200px úteis.
                Alargar resolve na origem; quebrar linha só disfarçava. */}
            <DropdownMenuContent align="start" className="w-72">
              {/* overflow-wrap:anywhere é salvaguarda, não o mecanismo: quebra
                  apenas quando o e-mail não couber de jeito nenhum. break-words
                  não quebra token sem espaço, e break-all quebrava sempre — foi
                  ele que partiu "gameleiras.com.b / r" no meio. Truncar está
                  fora: o e-mail é o login, e login pela metade não confere nada. */}
              <DropdownMenuLabel className="whitespace-normal [overflow-wrap:anywhere] font-dado text-[11px] font-normal leading-relaxed normal-case text-tinta-media">
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

      <ContextoDoMenu.Provider value={() => setMenuAberto(true)}>
        <main className="flex min-w-0 flex-1 flex-col overflow-hidden">{children}</main>
      </ContextoDoMenu.Provider>
    </div>
  )
}

export function CabecalhoDaPagina({
  titulo,
  descricao,
  acoes,
  className,
}: {
  titulo: string
  descricao?: string
  acoes?: ReactNode
  className?: string
}) {
  const abrirMenu = useContext(ContextoDoMenu)

  return (
    <header
      className={cn(
        'flex shrink-0 items-center justify-between gap-3 border-b border-linha-forte bg-branco px-3 py-2.5 md:gap-4 md:px-6 md:py-3.5',
        className,
      )}
    >
      {/* O único acesso ao menu principal no celular, já que o menu virou
          gaveta. Aparece em todas as telas de uma vez, sem tocar em nenhuma
          delas. */}
      {abrirMenu ? (
        <button
          type="button"
          onClick={abrirMenu}
          aria-label="Abrir o menu principal"
          title="Menu"
          className="flex size-9 shrink-0 items-center justify-center rounded-md border border-linha text-tinta-media transition-colors hover:border-linha-forte hover:bg-superficie hover:text-tinta md:hidden"
        >
          <Menu className="size-4" />
        </button>
      ) : null}

      <div className="min-w-0 flex-1">
        <h1 className="truncate font-titulo text-lg leading-tight text-tinta">{titulo}</h1>
        {/* A descrição é contexto, não navegação: some no celular em vez de
            gastar uma linha da faixa fixa do topo. */}
        {descricao ? (
          <p className="mt-0.5 hidden text-xs text-tinta-media sm:block">{descricao}</p>
        ) : null}
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
    <div className="flex flex-1 flex-col items-center justify-center gap-3 p-6 text-center md:p-10">
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
