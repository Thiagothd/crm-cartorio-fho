import type { ReactNode } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { Building2, FolderTree, LogOut, ShieldCheck, UserRound } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { itemPelaRota, itensForaDoMenu } from '@/app/navegacao'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { Avatar } from '@/components/ui/avatar'
import { cn } from '@/lib/utils'
import { BuscaGlobal } from './busca-global'
import { AvisoDeWhatsappDesconectado } from './aviso-de-whatsapp-desconectado'
import { SinoDeAlertas } from './sino-de-alertas'
import { IndicadorDoRobo } from './indicador-do-robo'
import { SeletorDeTema } from '@/components/ui/seletor-de-tema'
import { MarcaDoProduto } from '@/components/ui/marca-do-produto'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'

/**
 * Presente em toda área autenticada, com a mesma composição:
 * `[contexto do módulo] … [ações da página] · [busca] · [sino] · [perfil]`.
 *
 * O container de ações (`aoRegistrarContainerDeAcoes`) é o alvo do portal de
 * `<AcoesDaPagina>` — cada página manda suas próprias ações para lá, em vez de
 * desenhar o próprio cabeçalho como `CabecalhoDaPagina` fazia antes.
 */
export function CabecalhoGlobal({
  ocultoNoCelular,
  aoRegistrarContainerDeAcoes,
}: {
  ocultoNoCelular: boolean
  aoRegistrarContainerDeAcoes: (elemento: HTMLDivElement | null) => void
}) {
  const location = useLocation()
  const navegar = useNavigate()
  const { atendente, tenant, sair, pode } = usarAutenticacao()
  const item = itemPelaRota(location.pathname)

  // Telas que existem mas não moram no rail (Aparência, Segurança). Vêm da
  // MESMA `app/navegacao.ts` — rótulo, ícone, rota e permissão — para o menu
  // de perfil nunca virar uma segunda lista de navegação escrita à mão.
  const telasDaConta = itensForaDoMenu(pode)

  return (
    <header
      className={cn(
        'flex min-h-14 shrink-0 flex-wrap items-center gap-2 border-b border-linha-forte bg-branco px-3 py-2 md:min-h-16 md:gap-3 md:px-6 md:py-3 @min-[64rem]/conteudo:flex-nowrap',
        ocultoNoCelular && 'hidden md:flex',
      )}
    >
      <Link
        to="/"
        aria-label="CRM Cartório — ir para a Visão Geral"
        className="flex shrink-0 items-center gap-2.5 rounded-lg py-1 pr-1 transition-opacity hover:opacity-80"
      >
        <MarcaDoProduto
          className="hidden h-8 w-auto @min-[40rem]/conteudo:block 2xl:h-9"
        />
        <MarcaDoProduto
          simbolo
          className="size-9 @min-[40rem]/conteudo:hidden"
        />
      </Link>

      {/* Régua entre marca e contexto da página: separa duas informações de
          natureza diferente sem gastar uma linha de borda no cabeçalho todo. */}
      <span aria-hidden className="hidden h-7 w-px shrink-0 bg-linha @min-[40rem]/conteudo:block" />

      <div className="min-w-0 flex-1">
        {item?.grupoPai ? (
          <p className="truncate text-[10px] font-semibold uppercase tracking-wider text-tinta-fraca">
            {item.grupoPai}
          </p>
        ) : null}
        <h1 className="break-words text-base font-bold leading-tight tracking-tight text-tinta md:text-lg @min-[64rem]/conteudo:truncate">
          {item?.rotulo ?? 'CRM Cartório'}
        </h1>
      </div>

      {/* Agrupado e com flex-wrap: numa tela de 360px com duas ações de
          página, busca e sino, a soma pode não caber ao lado de um título
          longo. Preferir quebrar para uma segunda linha (o cabeçalho cresce)
          a cortar ou empurrar algo para fora da viewport — nunca rolagem
          horizontal da página. */}
      <div className="contents @min-[64rem]/conteudo:flex @min-[64rem]/conteudo:items-center @min-[64rem]/conteudo:justify-end @min-[64rem]/conteudo:gap-2">
        {/* Slot de ações da página — ver contexto-de-acoes-da-pagina.tsx.
            Vazio na maioria das telas; some sem custar layout quando não há
            nada dentro. */}
        <div ref={aoRegistrarContainerDeAcoes} className="order-last flex w-full min-w-0 flex-wrap items-center justify-end gap-2 empty:hidden @min-[64rem]/conteudo:order-none @min-[64rem]/conteudo:w-auto" />

        <AvisoDeWhatsappDesconectado habilitado={Boolean(atendente)} />

        <IndicadorDoRobo habilitado={Boolean(atendente)} />

        <BuscaGlobal />

        <SeletorDeTema />

        <SinoDeAlertas habilitado={Boolean(atendente)} />

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              type="button"
              aria-label="Perfil do atendente"
              className="hidden size-9 shrink-0 items-center justify-center rounded-lg border border-transparent text-tinta-media transition-colors hover:border-linha-forte hover:bg-superficie hover:text-tinta md:flex"
            >
              <UserRound className="size-4" aria-hidden />
            </button>
          </DropdownMenuTrigger>

          {/* Antes eram duas linhas de texto solto (nome e e-mail). Agora o
              painel responde as quatro perguntas que alguém abre este menu para
              conferir: quem sou eu, em que serventia estou, com que nível de
              acesso, e em que setor caio por padrão. Todos os campos já vinham
              na sessão — nenhuma requisição nova. */}
          <DropdownMenuContent align="end" className="w-80 p-0">
            <div className="flex items-center gap-3 border-b border-linha p-3">
              <Avatar nome={atendente?.nome ?? '?'} tamanho="lg" />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-bold text-tinta">{atendente?.nome}</p>
                {atendente?.cargo && atendente.cargo !== 'NaoInformado' ? (
                  <p className="truncate text-[11px] font-medium text-tinta-media">{atendente.cargoDescrito}</p>
                ) : null}
                <p className="truncate font-dado text-[11px] leading-relaxed text-tinta-fraca">{atendente?.email}</p>
              </div>
            </div>

            <dl className="flex flex-col gap-1.5 border-b border-linha p-3">
              <LinhaDoPerfil icone={Building2} rotulo="Serventia">
                {tenant?.nomeFantasia ?? tenant?.razaoSocial ?? '—'}
              </LinhaDoPerfil>
              <LinhaDoPerfil icone={ShieldCheck} rotulo="Acesso">
                {atendente?.perfil ?? '—'}
              </LinhaDoPerfil>
              {atendente?.setorPadraoNome ? (
                <LinhaDoPerfil icone={FolderTree} rotulo="Setor padrão">
                  {atendente.setorPadraoNome}
                </LinhaDoPerfil>
              ) : null}
            </dl>

            <div className="p-1">
              {telasDaConta.map((tela) => (
                <DropdownMenuItem
                  key={tela.rota}
                  onSelect={() => navegar(tela.rota)}
                  onPointerEnter={() => void tela.carregar()}
                >
                  <tela.icone />
                  {tela.rotulo}
                </DropdownMenuItem>
              ))}

              {telasDaConta.length > 0 ? <DropdownMenuSeparator /> : null}

              <DropdownMenuItem
                onSelect={() => {
                  sair()
                  navegar('/login')
                }}
              >
                <LogOut />
                Sair
              </DropdownMenuItem>
            </div>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </header>
  )
}

/** Uma linha do bloco de contexto do menu de perfil: ícone, rótulo e valor. */
function LinhaDoPerfil({
  icone: Icone,
  rotulo,
  children,
}: {
  icone: LucideIcon
  rotulo: string
  children: ReactNode
}) {
  return (
    <div className="flex items-center gap-2.5">
      <span
        aria-hidden
        className="flex size-6 shrink-0 items-center justify-center rounded-md bg-superficie text-tinta-media"
      >
        <Icone className="size-3.5" />
      </span>
      <dt className="shrink-0 text-[11px] font-medium uppercase tracking-wider text-tinta-fraca">{rotulo}</dt>
      <dd className="min-w-0 flex-1 truncate text-right text-xs font-medium text-tinta">{children}</dd>
    </div>
  )
}
