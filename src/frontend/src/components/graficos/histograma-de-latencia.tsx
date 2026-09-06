import { useState } from 'react'
import { formatarNumero } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { HistogramaDeLatencia } from '@/lib/api/central-ia'

const COR_POR_PROVEDOR: Record<string, { forte: string; fraca: string; rotulo: string }> = {
  Gemini: { forte: 'bg-verde', fraca: 'bg-verde/70', rotulo: 'Gemini' },
  Anthropic: { forte: 'bg-tendencia', fraca: 'bg-tendencia/70', rotulo: 'Anthropic' },
}

/**
 * Distribuição de latência por provedor, em baldes de `larguraDoBaldeMs`
 * (250ms — ver `ObservabilidadeDaIaService`). Um painel por provedor: os dois
 * lado a lado numa mesma escala de tentativas confundiriam mais do que
 * ajudariam, já que Gemini e Anthropic têm volumes tipicamente bem diferentes.
 */
export function HistogramaDeLatenciaGrafico({ histogramas }: { histogramas: HistogramaDeLatencia[] }) {
  if (histogramas.length === 0) {
    return (
      <div className="flex h-40 items-center justify-center text-sm text-tinta-fraca">
        Nenhuma tentativa com sucesso neste período.
      </div>
    )
  }

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      {histogramas.map((h) => (
        <UmHistograma key={h.provedor} histograma={h} />
      ))}
    </div>
  )
}

function UmHistograma({ histograma }: { histograma: HistogramaDeLatencia }) {
  const [indiceAtivo, setIndiceAtivo] = useState<number | null>(null)
  const cor = COR_POR_PROVEDOR[histograma.provedor] ?? COR_POR_PROVEDOR.Anthropic

  const maximo = Math.max(1, ...histograma.baldes.map((b) => b.total))
  const passoDoRotulo = Math.max(1, Math.ceil(histograma.baldes.length / 5))

  return (
    <div className="flex flex-col gap-2">
      <p className="text-xs font-semibold text-tinta-media">{cor.rotulo}</p>

      <div className="flex h-32 items-stretch gap-0.5">
        {histograma.baldes.map((balde, i) => {
          const ativo = indiceAtivo === i
          const rotulado = i % passoDoRotulo === 0 || i === histograma.baldes.length - 1

          return (
            <div
              key={balde.inicioMs}
              className="group relative flex min-w-0 flex-1 flex-col justify-end"
              onPointerEnter={() => setIndiceAtivo(i)}
              onPointerDown={() => setIndiceAtivo(i)}
              onPointerLeave={() => setIndiceAtivo(null)}
            >
              <div
                className={cn('w-full rounded-t-[2px] transition-colors', ativo ? cor.forte : cor.fraca)}
                style={{ height: `${(balde.total / maximo) * 100}%`, minHeight: balde.total > 0 ? '2px' : 0 }}
              />
              {rotulado ? (
                <span className="mt-1 block text-center text-[9px] leading-none text-tinta-fraca">
                  {balde.inicioMs}ms
                </span>
              ) : null}

              {ativo ? (
                <div className="pointer-events-none absolute bottom-full left-1/2 z-20 mb-2 w-max -translate-x-1/2 rounded-card border border-linha bg-branco px-2.5 py-1.5 text-[11px] shadow-card-hover">
                  <p className="font-bold text-tinta">
                    {balde.inicioMs}–{balde.inicioMs + histograma.larguraDoBaldeMs}ms
                  </p>
                  <p className="text-tinta-media">{formatarNumero(balde.total)} tentativas</p>
                </div>
              ) : null}
            </div>
          )
        })}
      </div>
    </div>
  )
}
