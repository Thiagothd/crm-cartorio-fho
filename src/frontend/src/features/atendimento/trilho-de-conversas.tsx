import { ChevronsRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Avatar } from '@/components/ui/avatar'
import { corSolidaDaEtapa } from './cores-de-etapa'
import type { Conversa } from '@/lib/api/tipos'

/**
 * A lista de conversas recolhida.
 *
 * Recolher devolve 300px de largura para o fio e para a caixa de resposta, mas
 * NAO pode custar a troca de conversa — por isso o trilho guarda os avatares, e
 * nao vira uma faixa vazia. E o mesmo raciocinio do menu principal recolhido
 * (DESIGN.md, 10.3), que mantem os icones e joga os rotulos para o `title`.
 *
 * Nao tem estado proprio: quem manda continua sendo a pagina. Este componente so
 * desenha o que ja existe de outro jeito.
 */
export function TrilhoDeConversas({
  conversas,
  total,
  selecionadaId,
  temFiltro,
  aoSelecionar,
  aoAbrir,
}: {
  conversas: Conversa[]
  total: number
  selecionadaId: string | null
  temFiltro: boolean
  aoSelecionar: (id: string) => void
  aoAbrir: () => void
}) {
  return (
    <>
      <div className="flex shrink-0 flex-col items-center gap-1.5 border-b border-linha p-2">
        <button
          type="button"
          onClick={aoAbrir}
          aria-expanded={false}
          aria-label="Abrir a lista de conversas"
          title="Abrir a lista de conversas"
          className="flex size-9 items-center justify-center rounded-md border border-linha text-tinta-media transition-colors hover:border-linha-forte hover:bg-superficie hover:text-tinta"
        >
          <ChevronsRight className="size-4" />
        </button>

        {/* Com os filtros escondidos, uma lista curta parece defeito. O ponto
            avisa que ela esta curta de proposito. Em tinta-media, nao em ambar:
            filtro aplicado nao e pendencia nem alerta (DESIGN.md, secao 5). */}
        {temFiltro ? (
          <span
            className="size-1.5 rounded-full bg-tinta-media"
            title="Ha filtros aplicados — abra a lista para ver quais"
            aria-label="Ha filtros aplicados"
          />
        ) : null}
      </div>

      <div className="flex flex-1 flex-col items-center gap-1 overflow-y-auto py-2">
        {conversas.map((conversa) => {
          const selecionada = conversa.id === selecionadaId

          return (
            <button
              key={conversa.id}
              type="button"
              onClick={() => aoSelecionar(conversa.id)}
              aria-current={selecionada ? 'true' : undefined}
              title={`${conversa.clienteNome} — ${conversa.protocolo}`}
              className={cn(
                'relative flex w-full justify-center rounded-md border border-transparent py-1 transition-colors',
                selecionada
                  ? // Mesma marca da lista aberta: barra em tinta a esquerda.
                    'border-l-2 border-l-tinta bg-superficie'
                  : 'hover:bg-superficie/60',
              )}
            >
              <Avatar
                nome={conversa.clienteNome}
                tamanho="md"
                corDeFundo={corSolidaDaEtapa(conversa.etapaCor)}
              />

              {/* Substitui a contagem em bordo que a linha da lista mostra e que
                  some ao recolher — nao e uma quarta ocorrencia de bordo na
                  tela, e a mesma trocada de lugar (DESIGN.md, 2.1). */}
              {conversa.mensagensNaoLidas > 0 ? (
                <span
                  className="absolute right-1.5 top-0.5 size-2 rounded-full border border-branco bg-selo"
                  aria-label={`${conversa.mensagensNaoLidas} nao lidas`}
                />
              ) : null}
            </button>
          )
        })}
      </div>

      <div
        className="shrink-0 border-t border-linha px-1 py-2 text-center font-dado text-[11px] text-tinta-media"
        title={`${conversas.length} de ${total} conversas`}
      >
        {conversas.length}
      </div>
    </>
  )
}
