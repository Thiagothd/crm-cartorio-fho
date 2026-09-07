import { useEffect, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { ChevronRight, PanelLeftClose, PanelLeftOpen } from 'lucide-react'
import { navegacaoVisivel, type EntradaDeNavegacao, type ItemDeNavegacao } from '@/app/navegacao'
import { cn } from '@/lib/utils'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { usarAbaixoDe } from '@/lib/usar-abaixo-de'
import { usarPreferenciaLocal } from '@/lib/usar-preferencia-local'
import { usarIntencaoDeHover } from '@/lib/usar-intencao-de-hover'

function ehAtivo(pathname: string, rota: string) {
  return pathname === rota || pathname.startsWith(`${rota}/`)
}

/*
 * NÃO existe tooltip neste arquivo, e a ausência é deliberada — removida em
 * 2026-08-21 por ser defeituosa por construção, não por estar mal configurada.
 *
 * O que acontecia: o rail abre no hover em 160ms, e o tooltip só valeria com o
 * rail FECHADO. Ou seja, a única janela em que ele podia aparecer eram esses
 * 160ms antes do painel abrir e mostrar o rótulo de verdade. Resultado: com o
 * atraso normal de 500ms ele nunca aparecia, e com o `skipDelayDuration` de
 * 300ms do TooltipProvider (que torna o segundo tooltip instantâneo) ele
 * piscava e sumia.
 *
 * Pior: ele FICAVA PRESO na tela. O componente alternava entre controlado
 * (`open={false}`, com o rail aberto) e não controlado (`open={undefined}`, com
 * o rail fechado). Voltar para não controlado restaura o estado interno do
 * Radix, que ainda era "aberto" — então o balão reaparecia sobre o rail fechado
 * e só saía com um clique em outro lugar.
 *
 * Não há perda de acessibilidade: o nome acessível de cada item vem do `<span>`
 * do rótulo, que continua no DOM (`opacity-0`, nunca `display:none`, justamente
 * para leitor de tela continuar lendo), e os botões só-ícone têm `aria-label`.
 * O tooltip nunca foi o nome acessível — era só descrição.
 */

/**
 * Uma linha do rail. A célula do ícone tem largura fixa de 48px — exatamente a
 * largura útil do rail fechado (64px menos os 8px de padding de cada lado).
 * Com isso o ícone fica centrado no rail fechado SEM precisar de
 * `justify-center`, e continua na mesmíssima coordenada quando o painel abre:
 * o rótulo entra ao lado por opacidade, nada se desloca.
 *
 * (`justify-center` seria a solução óbvia e está errada aqui: ele centraliza o
 * CONJUNTO ícone+rótulo, então o ícone saltaria de posição no instante em que
 * o rótulo deixasse de ocupar espaço.)
 *
 * `className` do `NavLink` é uma STRING, nunca a função `({isActive}) => ...`:
 * o `pathname` decide o estado ativo aqui fora, uma vez, e desce como booleano.
 * (A regra nasceu de um bug com `TooltipTrigger asChild`, que estringificava a
 * função ao mesclar classes. O tooltip saiu, a forma fica — é mais direta e
 * mantém o cálculo do estado ativo num lugar só.)
 */
function ItemDoRail({ item, expandido, pathname }: { item: ItemDeNavegacao; expandido: boolean; pathname: string }) {
  const Icone = item.icone
  const ativo = ehAtivo(pathname, item.rota)

  return (
    <NavLink
      to={item.rota}
      end={item.rota === '/'}
      onPointerEnter={() => void item.carregar()}
      onFocus={() => void item.carregar()}
      className={cn(
        // `border-l-2` em TODOS os estados, variando só a cor. Antes o estado
        // base tinha `border` (1px) e só o ativo-fechado subia para 2px na
        // esquerda: cada item deslocava 1px ao ficar ativo ou ao painel abrir,
        // e numa coluna de dez itens isso lê como tremida.
        //
        // NUNCA `transition-all` aqui: a largura deste item é ditada pela
        // largura do `aside`, que está animando. Com `all`, o item anima a
        // PRÓPRIA largura em 200ms perseguindo um alvo que muda a cada quadro
        // — fica sempre atrás da borda do painel, e o menu inteiro dá a
        // sensação de arrastado. Só cor e sombra transicionam.
        'flex h-10 items-center gap-2.5 rounded-lg border-l-2 border-transparent text-sm font-medium',
        'transition-[background-color,border-color,color,box-shadow] duration-200',
        ativo && expandido
          ? // Painel aberto: folha clara, cantos macios.
            'bg-branco text-selo shadow-card'
          : ativo
            ? // Rail fechado: só a lombada bordô, fundo continua transparente
              // sobre o `tinta` do rail — NUNCA `bg-branco` aqui, ou o ícone
              // branco (linha abaixo) fica branco sobre branco, invisível.
              'border-l-selo text-branco'
            : 'text-linha hover:bg-branco/[0.07] hover:text-branco',
      )}
    >
      <span className="flex h-10 w-12 shrink-0 items-center justify-center">
        <span
          className={cn(
            'flex size-8 items-center justify-center rounded-lg transition-colors duration-200',
            // So no rail fechado: a linha ja ganha fundo branco quando
            // expandida (folha clara), um chip aqui por cima ficaria
            // redundante. Fechado, o chip da presenca ao icone alem da
            // lombada — sem repetir `bg-branco` (regra do fix acima).
            ativo && !expandido && 'bg-selo/20',
          )}
        >
          <Icone
            className={cn('size-4 shrink-0', ativo && expandido ? 'text-selo' : ativo ? 'text-branco' : undefined)}
          />
        </span>
      </span>
      {/* Opacidade, nao `sr-only`: `sr-only` some do layout de um quadro para
            o outro e o rotulo POPA na tela no fim da animacao de largura. Com
            opacidade ele esmaece junto com a abertura — e `opacity: 0` continua
            legivel para leitor de tela, entao este span E' o nome acessivel do
            link, o que e' exatamente por que ele nunca pode virar `sr-only`
            condicional nem sair do DOM. O atraso de 60ms na entrada deixa a
            largura comecar a abrir antes do texto aparecer, que e o que faz o
            movimento parecer natural em vez de simultaneo e mecanico. */}
      <span
        className={cn(
          'min-w-0 truncate pr-2 transition-opacity',
          expandido ? 'opacity-100 duration-200 delay-[60ms]' : 'opacity-0 duration-100',
        )}
      >
        {item.rotulo}
      </span>
    </NavLink>
  )
}

function SubmenuDoRail({
  entrada,
  expandido,
  aberto,
  aoAlternar,
  pathname,
}: {
  entrada: Extract<EntradaDeNavegacao, { tipo: 'submenu' }>
  expandido: boolean
  aberto: boolean
  aoAlternar: () => void
  pathname: string
}) {
  const Icone = entrada.icone
  const temFilhoAtivo = entrada.itens.some((item) => ehAtivo(pathname, item.rota))

  return (
    <div>
      <button
        type="button"
        onClick={aoAlternar}
        aria-expanded={aberto}
        className={cn(
          // Mesma disciplina do ItemDoRail: `border-l-2` sempre, só a cor muda.
          'flex h-10 w-full items-center gap-2.5 rounded-lg border-l-2 border-transparent text-sm transition-colors duration-200 hover:bg-branco/[0.07] hover:text-branco',
          temFilhoAtivo ? 'border-l-linha text-branco' : 'text-linha',
        )}
      >
        <span className="flex h-10 w-12 shrink-0 items-center justify-center">
          <span className="flex size-8 items-center justify-center rounded-lg">
            <Icone className="size-4 shrink-0" />
          </span>
        </span>
        {/* Opacidade, nao `sr-only` — mesma razao do ItemDoRail: o botao
              precisa de nome acessivel mesmo com o rail fechado, e o rotulo
              deve esmaecer junto com a abertura em vez de popar no fim dela. */}
        <span
          className={cn(
            'min-w-0 flex-1 truncate text-left transition-opacity',
            expandido ? 'opacity-100 duration-200 delay-[60ms]' : 'opacity-0 duration-100',
          )}
        >
          {entrada.rotulo}
        </span>
        <ChevronRight
          className={cn(
            'mr-2 size-3.5 shrink-0 transition-[transform,opacity] duration-200',
            aberto && 'rotate-90',
            expandido ? 'opacity-100 delay-[60ms]' : 'opacity-0',
          )}
        />
      </button>

      {expandido ? (
        <div
          className="grid overflow-hidden transition-[grid-template-rows] duration-[var(--duracao-padrao)] ease-out"
          style={{ gridTemplateRows: aberto ? '1fr' : '0fr' }}
        >
          <div className="min-h-0">
            {/* `ml-6` = 24px, que é exatamente o centro da célula de ícone
                (48px de largura) — o traço do submenu cai debaixo dos ícones
                dos pais, não desalinhado ao lado deles. */}
            <div className="ml-6 flex flex-col gap-0.5 border-l border-linha-forte/30 py-0.5 pl-2">
              {entrada.itens.map((item) => (
                <ItemDoRail key={item.rota} item={item} expandido pathname={pathname} />
              ))}
            </div>
          </div>
        </div>
      ) : null}
    </div>
  )
}

/**
 * Navigation rail com abertura por hover-com-intenção. Fechado, é uma coluna
 * de 64px em fluxo, só ícones — os itens não recalculam a largura de nada.
 * Aberto (hover, foco, ou fixado), o painel de 256px é OVERLAY sobre o
 * conteúdo: não empurra nada, então não anima largura de coluna nenhuma —
 * exceção deliberada a DESIGN.md §10.3/10.7 (aquelas colunas ficam em fluxo;
 * este painel não).
 */
export function RailDeNavegacao() {
  const { pode } = usarAutenticacao()
  const location = useLocation()

  const [fixado, alternarFixado] = usarPreferenciaLocal('crm-cartorio.menu-fixado', false)
  const estreito = usarAbaixoDe(1280)
  const fixadoEfetivo = fixado && !estreito

  const { aberto, aoEntrar, aoSair, aoFocarDentro, aoDesfocarFora, abrirAgora, fecharAgora } = usarIntencaoDeHover()
  const expandido = fixadoEfetivo || aberto

  const grupos = navegacaoVisivel(pode)
  const [submenuAberto, setSubmenuAberto] = useState<string | null>(null)

  useEffect(() => {
    for (const grupo of grupos) {
      for (const entrada of grupo.entradas) {
        if (entrada.tipo === 'submenu' && entrada.itens.some((item) => ehAtivo(location.pathname, item.rota))) {
          setSubmenuAberto(entrada.rotulo)
        }
      }
    }
    // Só quando a rota muda — reabrir manualmente um submenu não deve ser
    // desfeito por essa varredura.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.pathname])

  // Escolher um destino RECOLHE o painel na hora. Sem isto ele ficava aberto
  // sobre a página nova até o ponteiro sair da área do rail — e como o ponteiro
  // continua exatamente onde clicou, "sair" podia significar não sair nunca,
  // até um clique em qualquer outro lugar da tela.
  //
  // `fecharAgora` cancela o temporizador pendente e zera a trava de foco, então
  // não sobra nada agendado para reabrir. Fixado não é afetado: `expandido` é
  // `fixadoEfetivo || aberto`, e só o `aberto` é zerado aqui.
  //
  // Fica no efeito de rota, não num `onClick` dos itens, porque a navegação
  // também acontece por submenu, por atalho de teclado e pelo histórico do
  // navegador — o `onClick` cobriria só um dos caminhos.
  useEffect(() => {
    fecharAgora()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.pathname])

  return (
    <div
      className={cn(
        'relative hidden shrink-0 transition-[width] duration-[220ms] ease-out md:block',
        fixadoEfetivo ? 'w-64' : 'w-16',
      )}
    >
      {/* Fecha ao tocar fora, só em ponteiro grosso (touch) — `pointer-coarse:`.
          Em mouse, sair da área já fecha por `aoSair`; deixar isto capturando
          clique em QUALQUER ponteiro roubaria clique do conteúdo principal
          durante o atraso de fechamento (alguém que move o mouse do rail
          direto para um botão próximo perderia o clique). Em tablet e
          notebook touchscreen sem mouse (768–1279px), `pointerenter`/
          `pointerleave` sozinhos não garantem fechar, e o painel ficaria preso
          cobrindo o conteúdo até o usuário descobrir como sair. */}
      {expandido && !fixadoEfetivo ? (
        <button
          type="button"
          tabIndex={-1}
          aria-hidden
          onPointerDown={fecharAgora}
          className="pointer-events-none fixed inset-0 z-30 cursor-default pointer-coarse:pointer-events-auto"
        />
      ) : null}

      {/* SEMPRE `absolute`, com a largura em transição — não mais alternando
          entre estático e absoluto conforme o estado. `position` não é
          animável, então a versão anterior trocava de layout de um quadro para
          o outro e o painel simplesmente APARECIA inteiro: era daí que vinha a
          sensação de coisa brusca e mecânica. Absoluto fixo deixa `width` ser a
          única coisa que muda, e `width` transiciona. O container externo
          continua reservando 64px (ou 256px fixado) no fluxo, então o conteúdo
          da página nunca reflui durante a animação. */}
      <aside
        aria-label="Menu principal"
        role="navigation"
        onPointerEnter={aoEntrar}
        onPointerLeave={aoSair}
        onFocusCapture={aoFocarDentro}
        onBlurCapture={(evento) => {
          if (!evento.currentTarget.contains(evento.relatedTarget as Node)) {
            aoDesfocarFora()
          }
        }}
        className={cn(
          'absolute inset-y-0 left-0 z-40 flex flex-col overflow-hidden border-r border-linha-forte bg-tinta pb-[env(safe-area-inset-bottom)] pt-[env(safe-area-inset-top)]',
          'transition-[width,box-shadow] duration-[220ms] ease-out',
          '[--cor-foco:var(--cor-linha)]', // o outline padrao (tinta-media) nao contrasta sobre bg-tinta
          expandido ? 'w-64' : 'w-16',
          // Sombra só quando é overlay de verdade: fixado, o painel ocupa a
          // própria coluna no fluxo e não tem nada por baixo para sombrear.
          expandido && !fixadoEfetivo && 'shadow-[8px_0_32px_-12px_rgba(11,11,15,.45)]',
        )}
      >
        {/* Sem bloco de marca aqui: a logo subiu para o cabeçalho branco
            (`cabecalho-global.tsx`) e o nome da serventia vive no menu de
            perfil (DESIGN.md §13). O rail ficou navegação pura, e por isso os
            itens começam no topo. */}
        {/* `overflow-x-hidden` é obrigatório, não zelo: com `overflow-y-auto`
            sozinho, o CSS promove o outro eixo de `visible` para `auto`, e os
            rótulos (que continuam ocupando largura mesmo em `opacity-0`)
            transbordam os 64px do rail fechado. O resultado era uma BARRA DE
            ROLAGEM HORIZONTAL desenhada no pé do rail — a "barra cinza embaixo",
            que ainda por cima aparece estilizada como pílula clara pelo tema de
            scrollbar do projeto e parecia um elemento da interface. */}
        {/* A barra VERTICAL é escondida, não desligada: `overflow-y-auto` fica,
            então roda, trackpad e teclado continuam rolando o menu — some só o
            desenho. O tema global (`index.css`) pinta o polegar com
            `--cor-linha-forte`, e sobre o `bg-tinta` do rail isso vira o maior
            contraste da interface: uma pílula clara colada nos ícones. Mesmo par
            de classes de `features/indicadores/abas-de-relatorio.tsx`. */}
        <nav className="flex flex-1 flex-col gap-0.5 overflow-y-auto overflow-x-hidden p-2 pt-3 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
          {grupos.map((grupo, indiceDoGrupo) => (
            <div key={grupo.titulo ?? `grupo-${indiceDoGrupo}`}>
              {/* Título e divisor ocupam A MESMA caixa de 16px, trocando por
                  opacidade — não são dois elementos alternados. Alternar faria
                  a altura mudar entre os estados e TODOS os ícones abaixo
                  subiriam/desceriam no instante da abertura, que é o oposto de
                  "o ícone não se move 1px". */}
              {grupo.titulo ? (
                <div className="relative mb-1 mt-3 h-4 first:mt-0">
                  <span
                    className={cn(
                      'absolute inset-x-3 top-0 truncate text-[10px] font-semibold uppercase leading-4 tracking-wider text-linha/70 transition-opacity',
                      expandido ? 'opacity-100 delay-[60ms] duration-200' : 'opacity-0 duration-100',
                    )}
                  >
                    {grupo.titulo}
                  </span>
                  <span
                    aria-hidden
                    className={cn(
                      'absolute inset-x-2 top-1/2 h-px bg-linha-forte/30 transition-opacity',
                      expandido ? 'opacity-0 duration-100' : 'opacity-100 duration-200',
                    )}
                  />
                </div>
              ) : null}

              <div className="flex flex-col gap-0.5">
                {grupo.entradas.map((entrada) =>
                  entrada.tipo === 'item' ? (
                    <ItemDoRail key={entrada.rota} item={entrada} expandido={expandido} pathname={location.pathname} />
                  ) : (
                    <SubmenuDoRail
                      key={entrada.rotulo}
                      entrada={entrada}
                      expandido={expandido}
                      aberto={submenuAberto === entrada.rotulo}
                      pathname={location.pathname}
                      aoAlternar={() => {
                        if (!expandido) abrirAgora()
                        setSubmenuAberto((atual) => (atual === entrada.rotulo ? null : entrada.rotulo))
                      }}
                    />
                  ),
                )}
              </div>
            </div>
          ))}
        </nav>

        {/* Perfil e Sair moraram aqui na versão antiga do shell — agora vivem no
            cabeçalho global (desktop) e no rodapé da folha "Mais" (celular).
            Duas superfícies de logout na mesma tela seria redundância, não
            conveniência. O rail fica só com navegação e o controle de fixar. */}
        {/* UM botão só, não dois trocados por `expandido`: alternar dois
            elementos na mesma posição faz o React desmontar e remontar o
            botão a cada abertura do rail. Aqui o rótulo só esmaece, como nos
            itens, e o `aria-label` cobre o estado fechado. */}
        <div className="border-t border-linha-forte/35 p-2">
          <button
            type="button"
            onClick={alternarFixado}
            aria-pressed={fixado}
            aria-label={fixado ? 'Recolher menu' : 'Fixar o menu aberto'}
            className="flex h-10 w-full items-center rounded-lg border-l-2 border-transparent text-xs text-linha transition-colors duration-200 hover:bg-branco/[0.07] hover:text-branco"
          >
            {/* Mesmo chip de 32px dos itens de navegação, para o ícone cair na
                MESMA coordenada que os de cima — antes ele era um ícone solto
                na célula e assentava alguns pixels fora do eixo dos outros.
                `size-5` (20px) contra os 16px dos itens: é um controle de
                interface, não um destino, e um pouco maior é o que o distingue
                sem competir com a navegação. */}
            <span className="flex h-10 w-12 shrink-0 items-center justify-center">
              <span className="flex size-8 items-center justify-center rounded-lg">
                {fixado ? <PanelLeftClose className="size-5 shrink-0" /> : <PanelLeftOpen className="size-5 shrink-0" />}
              </span>
            </span>
            <span
              className={cn(
                'min-w-0 truncate pr-2 transition-opacity',
                expandido ? 'opacity-100 duration-200 delay-[60ms]' : 'opacity-0 duration-100',
              )}
            >
              {fixado ? 'Recolher menu' : 'Fixar menu aberto'}
            </span>
          </button>
        </div>
      </aside>
    </div>
  )
}
