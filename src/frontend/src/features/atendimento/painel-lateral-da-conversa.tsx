import { useEffect, type ReactNode } from 'react'
import { X } from 'lucide-react'

/**
 * A casca dos painéis que abrem ao lado do fio — histórico e mídias.
 *
 * Existe para que os dois sejam irmãos de verdade, e não duas cópias que
 * divergem no terceiro mês. Toda a posicionalidade mora aqui; cada painel
 * entrega só o miolo.
 *
 * DOIS POSICIONAMENTOS, UM COMPONENTE. No desktop é uma coluna em fluxo, à
 * direita do fio, como o histórico sempre foi. Abaixo de `md` vira tela cheia:
 * num celular não existe "ao lado", e espremer uma coluna de 256px num aparelho
 * de 360px devolveria 100px de conversa. É só CSS — o componente é o mesmo, não
 * há remontagem ao girar o aparelho.
 */
export function PainelLateralDaConversa({
  titulo,
  descricao,
  aoFechar,
  escapeAtivo = true,
  children,
}: {
  titulo: string
  descricao: string
  aoFechar: () => void
  /**
   * Cede o Escape a quem está por cima. Com o visualizador de imagem aberto
   * sobre a galeria, um Escape que fizesse as duas coisas fecharia a foto E o
   * painel de uma vez — e o atendente voltaria dois passos tendo pedido um.
   */
  escapeAtivo?: boolean
  children: ReactNode
}) {
  // Escape fecha. Toda entrada precisa de saída óbvia, e no celular o painel
  // cobre o botão que o abriu.
  useEffect(() => {
    if (!escapeAtivo) {
      return
    }

    function aoTeclar(evento: KeyboardEvent) {
      if (evento.key === 'Escape') {
        aoFechar()
      }
    }

    window.addEventListener('keydown', aoTeclar)
    return () => window.removeEventListener('keydown', aoTeclar)
  }, [aoFechar, escapeAtivo])

  return (
    <aside
      aria-label={titulo}
      // Fase 3.2 (secao 16): anima-esmaecer e' seguro aqui porque o painel
      // realmente monta/desmonta a cada abrir/fechar (ao contrario do wrapper
      // do fio, que so' monta uma vez por sessao de conversa).
      className="anima-esmaecer fixed inset-0 z-30 flex flex-col overflow-hidden border-linha bg-superficie md:static md:z-auto md:w-64 md:shrink-0 md:border-l xl:w-72"
    >
      <div className="flex shrink-0 items-start justify-between gap-2 border-b border-linha px-4 py-3">
        <div className="min-w-0">
          <p className="text-sm font-semibold">{titulo}</p>
          <p className="text-xs text-tinta-media">{descricao}</p>
        </div>

        <button
          type="button"
          onClick={aoFechar}
          aria-label={`Fechar ${titulo}`}
          title="Fechar"
          className="flex size-9 shrink-0 items-center justify-center rounded-md border border-transparent text-tinta-media transition-colors hover:border-linha hover:bg-branco hover:text-tinta"
        >
          <X className="size-4" />
        </button>
      </div>

      <div className="flex min-h-0 flex-1 flex-col overflow-hidden">{children}</div>
    </aside>
  )
}
