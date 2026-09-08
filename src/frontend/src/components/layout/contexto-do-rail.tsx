import { createContext, useContext, useEffect, useMemo, type ReactNode } from 'react'
import { usarPreferenciaLocal } from '@/lib/usar-preferencia-local'
import { usarIntencaoDeHover } from '@/lib/usar-intencao-de-hover'

/** Espelham as classes `w-16`/`w-64` do rail — ver `rail-de-navegacao.tsx`. */
export const LARGURA_DO_RAIL_RECOLHIDO = 64
export const LARGURA_DO_RAIL_EXPANDIDO = 256

const CHAVE_DO_MENU_FIXADO = 'crm-cartorio.menu-fixado'

export type EstadoDoRail = {
  /**
   * Estado EFETIVO — `fixado` OU aberto por hover/foco. É o que qualquer
   * consumidor externo (a inbox, o `Shell`) precisa saber: "o rail está
   * ocupando 256px agora?", não "por quê".
   */
  expandido: boolean
  /** Preferência persistida — só o clique no botão do rodapé muda isto. */
  fixado: boolean
  /** Alterna e grava `fixado`. É o que o botão do rodapé chama. */
  alternarFixado: () => void
  /**
   * Abre transitoriamente (o mesmo estado que o hover usa), sem persistir —
   * para o submenu: clicar num grupo com o rail recolhido precisa desenhar
   * os filhos na hora, e fixar o rail seria mais do que o clique pediu.
   */
  expandir: () => void
  /** Fecha o transitório na hora. Usado ao navegar e pelo backdrop de toque. */
  fecharAgora: () => void
  /** Handlers de hover/foco — o rail anexa estes ao `<aside>`. */
  aoEntrar: () => void
  aoSair: () => void
  aoFocarDentro: () => void
  aoDesfocarFora: () => void
  /** 64 ou 256, em pixels — para quem precisa medir a largura útil do conteúdo. */
  largura: number
}

const ESTADO_PADRAO: EstadoDoRail = {
  expandido: false,
  fixado: false,
  alternarFixado: () => {},
  expandir: () => {},
  fecharAgora: () => {},
  aoEntrar: () => {},
  aoSair: () => {},
  aoFocarDentro: () => {},
  aoDesfocarFora: () => {},
  largura: LARGURA_DO_RAIL_RECOLHIDO,
}

const ContextoDoRail = createContext<EstadoDoRail>(ESTADO_PADRAO)

/**
 * Consumido pelo rail (para saber o que desenhar) e por qualquer tela que
 * precise saber quanto de largura o rail está ocupando (a inbox, para
 * recolher a lista de conversas quando o menu aberto não deixa fio
 * suficiente — ver `usar-conteudo-abaixo-de.ts`).
 *
 * Fora do `ProvedorDoRail` (um componente de tela testado isolado, por
 * exemplo) devolve o estado padrão recolhido, sem lançar.
 */
export function usarRail() {
  return useContext(ContextoDoRail)
}

/**
 * Dono do estado do rail. Monta na raiz do `Shell`, acima de tudo que precisa
 * da largura — o rail em si, e qualquer tela.
 *
 * Sem elemento DOM próprio: é só `<ContextoDoRail.Provider>` — `shell.test.tsx`
 * depende de `container.firstElementChild` ser a moldura do Shell.
 *
 * Duas fontes compõem `expandido`: `fixado` (clique, persistido) e o hover
 * com intenção (`usarIntencaoDeHover`, transitório). As duas empurram o
 * conteúdo igual — não há mais um modo "hover é overlay, fixado é coluna":
 * a largura publicada pelo `Shell` (`--largura-do-rail`) segue `expandido`
 * inteiro, então sair com o mouse devolve a largura ao conteúdo do mesmo jeito
 * que fechar manualmente devolveria.
 */
export function ProvedorDoRail({ children }: { children: ReactNode }) {
  const [fixado, alternarFixado] = usarPreferenciaLocal(CHAVE_DO_MENU_FIXADO, false)
  const { aberto, aoEntrar, aoSair, aoFocarDentro, aoDesfocarFora, fecharAgora, abrirAgora } =
    usarIntencaoDeHover()

  const expandido = fixado || aberto

  // Ctrl/Cmd+B fixa/desafixa — atalho para quem prefere teclado ao hover.
  useEffect(() => {
    function aoTeclarNaJanela(evento: KeyboardEvent) {
      if ((evento.metaKey || evento.ctrlKey) && evento.key.toLowerCase() === 'b') {
        evento.preventDefault()
        alternarFixado()
      }
    }

    window.addEventListener('keydown', aoTeclarNaJanela)
    return () => window.removeEventListener('keydown', aoTeclarNaJanela)
  }, [alternarFixado])

  const valor = useMemo<EstadoDoRail>(
    () => ({
      expandido,
      fixado,
      alternarFixado,
      expandir: abrirAgora,
      fecharAgora,
      aoEntrar,
      aoSair,
      aoFocarDentro,
      aoDesfocarFora,
      largura: expandido ? LARGURA_DO_RAIL_EXPANDIDO : LARGURA_DO_RAIL_RECOLHIDO,
    }),
    [expandido, fixado, alternarFixado, abrirAgora, fecharAgora, aoEntrar, aoSair, aoFocarDentro, aoDesfocarFora],
  )

  return <ContextoDoRail.Provider value={valor}>{children}</ContextoDoRail.Provider>
}
