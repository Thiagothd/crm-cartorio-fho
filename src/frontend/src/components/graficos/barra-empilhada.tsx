import { corSolidaDaEtapa } from '@/features/atendimento/cores-de-etapa'
import { cn } from '@/lib/utils'
import type { ContagemPorChave } from '@/lib/api/tipos'

/**
 * Distribuição por etapa: "onde o funil está entupido?". Usa as mesmas cores
 * de etapa da inbox (`cores-de-etapa.ts`), então a leitura é a mesma em toda a
 * aplicação.
 *
 * A legenda é de PÍLULAS clicáveis, não texto com bolinha solta: a pílula tem
 * borda e fundo, então parece o que é — um filtro que se clica e leva para a
 * inbox — enquanto o texto solto da versão anterior não sugeria ação nenhuma.
 * Cada segmento também é interativo, e passar o mouse na pílula acende o
 * segmento correspondente (e vice-versa), o que amarra as duas metades da peça.
 */
export function BarraEmpilhada({
  segmentos,
  aoSelecionarSegmento,
}: {
  segmentos: ContagemPorChave[]
  aoSelecionarSegmento?: (id: string | null) => void
}) {
  const visiveis = segmentos.filter((s) => s.total > 0)
  const total = visiveis.reduce((soma, s) => soma + s.total, 0)

  if (total === 0) {
    return <p className="text-sm text-tinta-fraca">Nenhuma conversa ativa.</p>
  }

  return (
    <div className="flex flex-col gap-3.5">
      {/* `gap-0.5` entre segmentos: a separação por vão respira melhor que
          cores encostadas, e dispensa borda (que somaria linha a cada etapa). */}
      <div className="flex h-3 gap-0.5 overflow-hidden rounded-full">
        {visiveis.map((segmento) => {
          const chave = segmento.id ?? 'sem-etapa'

          return (
            <div
              key={chave}
              data-etapa={chave}
              title={`${segmento.nome}: ${segmento.total}`}
              className="h-full min-w-1 rounded-full transition-opacity duration-200 hover:opacity-80"
              style={{
                width: `${(segmento.total / total) * 100}%`,
                backgroundColor: corSolidaDaEtapa(segmento.cor) ?? 'var(--cor-linha-forte)',
                animation: 'crm-crescer-barra 480ms cubic-bezier(0.16, 1, 0.3, 1) both',
                transformOrigin: 'left',
              }}
            />
          )
        })}
      </div>

      <ul className="flex flex-wrap gap-1.5">
        {visiveis.map((segmento) => {
          const clicavel = Boolean(aoSelecionarSegmento && segmento.id)

          return (
            <li key={segmento.id ?? 'sem-etapa'}>
              <button
                type="button"
                disabled={!clicavel}
                onClick={() => aoSelecionarSegmento?.(segmento.id)}
                className={cn(
                  // `min-h-11` no celular: estas pílulas NAVEGAM (levam para a
                  // inbox filtrada por etapa) e tinham ~25px de altura, lado a
                  // lado — alvos pequenos e vizinhos são o pior caso no dedo.
                  'inline-flex items-center gap-1.5 rounded-full border border-linha bg-superficie px-3 text-[11px] font-medium text-tinta-media transition-all duration-200',
                  clicavel
                    ? 'min-h-11 touch-manipulation hover:-translate-y-px hover:border-linha-forte hover:bg-branco hover:text-tinta hover:shadow-card sm:min-h-0 sm:py-1'
                    : 'cursor-default py-1',
                )}
              >
                <span
                  aria-hidden
                  className="inline-block size-2 shrink-0 rounded-full"
                  style={{ backgroundColor: corSolidaDaEtapa(segmento.cor) ?? 'var(--cor-linha-forte)' }}
                />
                {segmento.nome}
                <span className="font-dado text-[11px] font-bold text-tinta">{segmento.total}</span>
              </button>
            </li>
          )
        })}
      </ul>
    </div>
  )
}
