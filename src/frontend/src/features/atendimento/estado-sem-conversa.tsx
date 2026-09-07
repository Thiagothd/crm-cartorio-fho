import { MessagesSquare } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { formatarNumero } from '@/lib/formatacao'
import { DialogoNovaConversa } from './dialogo-nova-conversa'
import type { ResumoDoPainel } from '@/lib/api/tipos'

/**
 * O que o atendente vê ao abrir /atendimentos sem nenhuma conversa
 * selecionada — a primeira coisa que a tela mostra no dia. É o único lugar do
 * balcão (Fase 3.2, docs/DESIGN.md seção 16) onde elevação com respiro é
 * legítima: não compete por altura com o fio, porque os dois nunca coexistem
 * na tela (ver o ramo em pagina-inbox.tsx que escolhe entre este componente e
 * a conversa aberta).
 *
 * Os três números vêm do MESMO `resumo` que alimentava a faixa de
 * indicadores — não é um resumo próprio, então nunca diverge do que a lista
 * mostra.
 */
export function EstadoSemConversa({
  resumo,
  primeiraDaFilaId,
  aoAbrirPrimeiraDaFila,
  aoCriarConversa,
}: {
  resumo: ResumoDoPainel | undefined
  primeiraDaFilaId: string | null
  aoAbrirPrimeiraDaFila: (id: string) => void
  aoCriarConversa: (conversaId: string) => void
}) {
  return (
    <div className="fundo-conversa-selo flex min-h-0 flex-1 flex-col items-center justify-center gap-5 bg-papel p-6">
      <div className="anima-descer w-full max-w-md rounded-card border border-linha bg-branco p-7 shadow-card">
        <div className="mx-auto flex size-12 items-center justify-center rounded-card border border-linha bg-superficie">
          <MessagesSquare className="size-5 text-tinta-media" aria-hidden />
        </div>

        <h2 className="mt-4 text-center text-[19px] font-bold tracking-tight text-tinta">
          Escolha uma conversa
        </h2>
        <p className="mt-1.5 text-center text-sm text-tinta-media">
          Selecione um atendimento à esquerda. O histórico, os anexos e o prazo abrem junto.
        </p>

        {resumo ? (
          <div className="grade-com-vida mt-6 grid grid-cols-3 gap-2">
            <NumeroDoResumo rotulo="Ativas" valor={resumo.totalAtivas} />
            <NumeroDoResumo rotulo="Na fila" valor={resumo.naFila} />
            <NumeroDoResumo rotulo="Atrasadas" valor={resumo.atrasadas} cor="selo" />
          </div>
        ) : null}

        <div className="mt-6 flex flex-col gap-2 sm:flex-row">
          {primeiraDaFilaId ? (
            <Button
              size="sm"
              className="flex-1"
              onClick={() => aoAbrirPrimeiraDaFila(primeiraDaFilaId)}
            >
              Abrir a primeira da fila
            </Button>
          ) : null}
          <DialogoNovaConversa aoCriar={aoCriarConversa} />
        </div>
      </div>
    </div>
  )
}

function NumeroDoResumo({
  rotulo,
  valor,
  cor,
}: {
  rotulo: string
  valor: number
  cor?: 'selo'
}) {
  return (
    <div className="rounded-card border border-linha bg-superficie px-3 py-2.5 text-center">
      <span
        className={cor === 'selo' && valor > 0 ? 'font-dado text-[22px] font-bold leading-none tracking-tight text-selo' : 'font-dado text-[22px] font-bold leading-none tracking-tight text-tinta'}
      >
        {formatarNumero(valor)}
      </span>
      <p className="mt-1 text-[10px] font-semibold uppercase tracking-wider text-tinta-fraca">{rotulo}</p>
    </div>
  )
}
