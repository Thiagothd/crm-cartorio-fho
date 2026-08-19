import { EyeOff, Forward, X } from 'lucide-react'
import { Button } from '@/components/ui/button'

/**
 * Barra do modo de seleção múltipla.
 *
 * Fica acima do fio e substitui as ações individuais enquanto está ativa: com
 * seleção ligada, clicar numa mensagem seleciona em vez de agir sobre ela, e ter
 * os dois comportamentos ao mesmo tempo seria clique ambíguo.
 *
 * Escape e o botão de fechar saem do modo — toda entrada precisa de saída óbvia.
 */
export function BarraDeSelecao({
  total,
  aoEncaminhar,
  aoOcultar,
  aoSair,
}: {
  total: number
  aoEncaminhar: () => void
  aoOcultar: () => void
  aoSair: () => void
}) {
  return (
    <div className="flex shrink-0 items-center justify-between gap-3 border-b border-linha-forte bg-branco px-3 py-2 sm:px-6">
      <p className="min-w-0 truncate text-sm text-tinta">
        <span className="font-dado">{total}</span>{' '}
        {total === 1 ? 'mensagem selecionada' : 'mensagens selecionadas'}
      </p>

      <div className="flex shrink-0 items-center gap-2">
        <Button size="sm" variant="outline" disabled={total === 0} onClick={aoEncaminhar}>
          <Forward />
          <span className="hidden sm:inline">Encaminhar</span>
          <span className="sr-only sm:hidden">Encaminhar</span>
        </Button>

        {/* "Ocultar", não "apagar": a mensagem sai da vista e continua no
            registro. Por isso não é botão destrutivo — não há destruição. */}
        <Button size="sm" variant="outline" disabled={total === 0} onClick={aoOcultar}>
          <EyeOff />
          <span className="hidden sm:inline">Ocultar</span>
          <span className="sr-only sm:hidden">Ocultar</span>
        </Button>

        <Button size="icon-sm" variant="ghost" onClick={aoSair} aria-label="Sair da seleção">
          <X />
        </Button>
      </div>
    </div>
  )
}
