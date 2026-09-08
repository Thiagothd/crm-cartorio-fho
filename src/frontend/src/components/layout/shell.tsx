import { useEffect, useState, type ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import type { LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'
import { RailDeNavegacao } from './rail-de-navegacao'
import { CabecalhoGlobal } from './cabecalho-global'
import { NavegacaoInferior } from './navegacao-inferior'
import { FolhaMais } from './folha-mais'
import { ContextoDoContainerDeAcoes } from './contexto-de-acoes-da-pagina'
import { ProvedorDaBuscaGlobal } from './contexto-de-busca-global'
import { ProvedorDoRail, usarRail } from './contexto-do-rail'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'

/** `/atendimentos/:id` — a conversa já tem cabeçalho e navegação próprios. */
function ehRotaDeConversa(pathname: string) {
  return /^\/atendimentos\/[^/]+/.test(pathname)
}

/**
 * Avisa no console, **só em desenvolvimento**, quando o documento ganha
 * rolagem — a moldura do sistema nunca deve rolar.
 *
 * Por que existe: em 2026-08-24 a tela de mensagens automáticas rolava o
 * documento inteiro, levando rail e cabeçalho para fora da tela. A causa
 * (`.sr-only` sem bloco container, ver o `relative` abaixo) era latente em
 * TODA tela e só aparecia quando o conteúdo passava da altura da janela —
 * ou seja, o mesmo defeito voltaria calado assim que outra tela crescesse.
 *
 * Isto é a rede: qualquer tela nova que reintroduza o problema grita no
 * console de quem está desenvolvendo, em vez de esperar alguém notar em
 * produção. Não roda em produção (`import.meta.env.DEV`), e não roda em
 * teste (jsdom não faz layout — `scrollHeight` seria sempre 0 e o aviso
 * nunca dispararia, o que só daria falsa segurança).
 */
function useAvisoDeMolduraRolando(pathname: string) {
  useEffect(() => {
    if (!import.meta.env.DEV) {
      return
    }

    // Um quadro depois: a rota acabou de trocar e o conteúdo ainda não mediu.
    const id = window.requestAnimationFrame(() => {
      const raiz = document.documentElement
      const sobraY = raiz.scrollHeight - raiz.clientHeight
      const sobraX = raiz.scrollWidth - raiz.clientWidth

      if (sobraY > 1 || sobraX > 1) {
        console.error(
          `[moldura] A rota "${pathname}" está fazendo o documento rolar ` +
            `(${sobraY}px na vertical, ${sobraX}px na horizontal). O rail e o ` +
            `cabeçalho vão sair da tela. Suspeito nº 1: algum elemento ` +
            `"position: absolute" sem ancestral posicionado — procure com ` +
            `[...document.querySelectorAll('*')].filter(e => ` +
            `getComputedStyle(e).position === 'absolute' && ` +
            `(!e.offsetParent || e.offsetParent === document.body))`,
        )
      }
    })

    return () => window.cancelAnimationFrame(id)
  }, [pathname])
}

/**
 * Shell global: rail de navegação (desktop/tablet), cabeçalho global, o
 * conteúdo da rota, e a navegação do celular (barra inferior + folha "Mais").
 *
 * Em `/atendimentos/:id` no celular, cabeçalho global e barra inferior somem
 * — a conversa já tem "voltar" e cabeçalho próprios, e as duas faixas juntas
 * custariam ~50px do fio de mensagens num aparelho de 360px. É exatamente o
 * comportamento que a inbox já tinha antes da Fase 3, só que decidido aqui em
 * vez de dentro da página.
 *
 * `Shell` só monta o `ProvedorDoRail` — precisa estar ACIMA da moldura, que lê
 * o estado para publicar `--largura-do-rail`. `MolduraDoShell` é o corpo de
 * verdade, sem elemento a mais entre os dois (`shell.test.tsx` depende de
 * `container.firstElementChild` ser a `<div>` da moldura).
 */
export function Shell({ children }: { children: ReactNode }) {
  return (
    <ProvedorDoRail>
      <MolduraDoShell>{children}</MolduraDoShell>
    </ProvedorDoRail>
  )
}

function MolduraDoShell({ children }: { children: ReactNode }) {
  const location = useLocation()
  const [containerDeAcoes, setContainerDeAcoes] = useState<HTMLDivElement | null>(null)
  const [folhaMaisAberta, setFolhaMaisAberta] = useState(false)
  const { expandido } = usarRail()

  const emConversa = ehRotaDeConversa(location.pathname)

  useAvisoDeMolduraRolando(location.pathname)

  return (
    // `relative` não é decoração: é o que impede a moldura inteira de rolar.
    //
    // Medido em 2026-08-24, com a tela de mensagens automáticas: o documento
    // ganhava 980px de rolagem, e arrastar essa barra levava rail e cabeçalho
    // para fora da tela, deixando uma faixa vazia embaixo. A causa é o
    // utilitário `.sr-only` do Tailwind, que é `position: absolute` — e um
    // absoluto SEM ancestral posicionado ancora no bloco inicial (a viewport),
    // onde nenhum `overflow: hidden` o alcança: overflow só recorta descendente
    // cujo bloco container esteja dentro dele. Cada botão "Editar" da tabela
    // carrega um desses, e o mais baixo ficava em 1700px.
    //
    // Isto era LATENTE EM TODA TELA: só aparecia onde o conteúdo passa da
    // altura da janela, então bastava o cartório cadastrar mais linhas para
    // outra tela começar a fazer o mesmo. Um `position: relative` aqui dá o
    // bloco container que faltava, e o recorte que já existia passa a valer.
    // Sem z-index: `relative` sozinho não cria contexto de empilhamento, então
    // nada muda de ordem.
    //
    // A var `--largura-do-rail` é publicada AQUI, não no rail: é o `Shell`
    // quem sabe que abaixo de `md` o rail nem existe (`0px`, sem prefixo), e é
    // esta mesma var que o rail lê para a própria largura
    // (`w-[var(--largura-do-rail,4rem)]`) — uma fonte só, impossível
    // dessincronizar.
    <ProvedorDaBuscaGlobal>
      <div
        className={cn(
          'relative flex h-dvh min-h-dvh overflow-hidden bg-papel',
          '[--largura-do-rail:0px]',
          expandido
            ? 'md:[--largura-do-rail:var(--largura-do-rail-expandido)]'
            : 'md:[--largura-do-rail:var(--largura-do-rail-recolhido)]',
        )}
      >
        <RailDeNavegacao />

        {/* `@container/conteudo`: o rail empurra em vez de cobrir, então esta
            coluna perde até 192px de largura útil sem a JANELA mudar de
            tamanho. Nomeado (`/conteudo`) para qualquer tela poder perguntar
            "quanto sobrou depois do rail" com `@.../conteudo:`, mesmo de dentro
            de um container aninhado sem nome (como o da coluna da conversa em
            `pagina-inbox.tsx`). Fica aqui, não no <main>: o cabeçalho global
            também perde os mesmos 192px, e este wrapper já é `min-w-0 flex-1` —
            a largura vem de fora, condição segura para `container-type`. */}
        <div className="@container/conteudo flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden">
          <CabecalhoGlobal ocultoNoCelular={emConversa} aoRegistrarContainerDeAcoes={setContainerDeAcoes} />

          <ContextoDoContainerDeAcoes.Provider value={containerDeAcoes}>
            <main className="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden">{children}</main>
          </ContextoDoContainerDeAcoes.Provider>

          {!emConversa ? <NavegacaoInferior aoAbrirMais={() => setFolhaMaisAberta(true)} /> : null}
        </div>

        <FolhaMais aberta={folhaMaisAberta} aoFechar={() => setFolhaMaisAberta(false)} />
      </div>
    </ProvedorDaBuscaGlobal>
  )
}

export function EstadoVazio({
  icone,
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
    <EstadoDaInterface
      tipo="vazio"
      icone={icone}
      titulo={titulo}
      descricao={descricao}
      acao={acao}
      preencher
    />
  )
}
