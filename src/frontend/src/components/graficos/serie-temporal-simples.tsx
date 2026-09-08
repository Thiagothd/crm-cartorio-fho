import { useState } from 'react'
import { formatarDataHora, formatarDiaCurto, formatarHoraDoDia } from '@/lib/formatacao'
import { cn } from '@/lib/utils'

export interface PontoDaSerieSimples {
  inicio: string
  valor: number
}

/**
 * Uma série numérica só, ao longo do tempo — custo estimado, total de tokens,
 * qualquer métrica de "um número por balde". Genérico de propósito: a Central
 * usa isto pelo menos duas vezes (custo e tokens), e as duas são a mesma forma
 * visual com escala/formatação diferentes — replicar o componente para cada
 * uma criaria dois lugares para divergir do mesmo desenho.
 */
/**
 * Classes completas e literais por cor — nunca `${cor}/70` via template
 * string: o scanner do Tailwind procura o texto exato da classe no código
 * fonte, e uma classe montada em runtime por interpolação não aparece
 * literalmente aqui, então nunca seria gerada no CSS final.
 */
const CORES: Record<'tinta' | 'verde' | 'ambar' | 'tendencia' | 'selo', { forte: string; fraca: string }> = {
  tinta: { forte: 'bg-tinta', fraca: 'bg-tinta/70' },
  verde: { forte: 'bg-verde', fraca: 'bg-verde/70' },
  ambar: { forte: 'bg-ambar', fraca: 'bg-ambar/70' },
  tendencia: { forte: 'bg-tendencia', fraca: 'bg-tendencia/70' },
  selo: { forte: 'bg-selo', fraca: 'bg-selo/70' },
}

export function SerieTemporalSimples({
  pontos,
  granularidade,
  cor = 'tinta',
  formatarValor,
  rotuloVazio,
}: {
  pontos: PontoDaSerieSimples[]
  granularidade: 'hora' | 'dia'
  cor?: keyof typeof CORES
  formatarValor: (valor: number) => string
  rotuloVazio: string
}) {
  const [indiceAtivo, setIndiceAtivo] = useState<number | null>(null)

  const maximo = Math.max(1, ...pontos.map((p) => p.valor))
  const semDados = pontos.every((p) => p.valor === 0)

  if (semDados) {
    return <div className="flex h-40 items-center justify-center text-sm text-tinta-fraca">{rotuloVazio}</div>
  }

  const n = pontos.length
  const passoDoRotulo = Math.max(1, Math.ceil(n / 7))

  function rotuloDoPonto(ponto: PontoDaSerieSimples) {
    const data = new Date(ponto.inicio)
    return granularidade === 'hora' ? formatarHoraDoDia(data.getHours()) : formatarDiaCurto(data)
  }

  return (
    <div className="flex gap-2.5">
      <div className="flex h-40 w-12 shrink-0 flex-col-reverse justify-between pb-6 text-right">
        <span className="font-dado text-[10px] leading-none text-tinta-fraca">{formatarValor(0)}</span>
        <span className="font-dado text-[10px] leading-none text-tinta-fraca">{formatarValor(maximo)}</span>
      </div>

      <div className="relative min-w-0 flex-1">
        <div aria-hidden className="absolute inset-x-0 top-0 flex h-40 flex-col-reverse justify-between pb-6">
          <div className="h-px w-full bg-linha-forte" />
          <div className="h-px w-full bg-linha" />
        </div>

        <div className="relative flex h-40 items-stretch gap-px">
          {pontos.map((ponto, i) => {
            const ativo = indiceAtivo === i
            const rotulado = i % passoDoRotulo === 0 || i === n - 1

            return (
              <div
                key={ponto.inicio}
                className="group relative min-w-0 flex-1"
                onPointerEnter={() => setIndiceAtivo(i)}
                onPointerDown={() => setIndiceAtivo(i)}
                onPointerLeave={() => setIndiceAtivo(null)}
              >
                <div
                  aria-hidden
                  className={cn(
                    'pointer-events-none absolute inset-x-0 bottom-6 top-0 rounded-sm transition-colors duration-150',
                    ativo && 'bg-superficie',
                  )}
                />

                <div className="absolute inset-x-0 bottom-6 top-0 flex flex-col-reverse items-center">
                  {ponto.valor > 0 ? (
                    <span
                      className={cn('w-full max-w-5 rounded-t-[3px] transition-colors', ativo ? CORES[cor].forte : CORES[cor].fraca)}
                      style={{
                        height: `${(ponto.valor / maximo) * 100}%`,
                        animation: `crm-crescer-barra-vertical 420ms cubic-bezier(0.16, 1, 0.3, 1) ${Math.min(i * 16, 400)}ms both`,
                      }}
                    />
                  ) : null}
                </div>

                {rotulado ? (
                  <span className="absolute bottom-0 left-1/2 -translate-x-1/2 whitespace-nowrap text-[10px] font-medium leading-6 text-tinta-fraca">
                    {rotuloDoPonto(ponto)}
                  </span>
                ) : null}

                {ativo ? (
                  <div className="pointer-events-none absolute bottom-full left-1/2 z-20 mb-2 w-max -translate-x-1/2 rounded-card border border-linha bg-branco px-3 py-2 text-xs shadow-card-hover">
                    <p className="font-bold text-tinta">{formatarDataHora(ponto.inicio)}</p>
                    <p className="font-dado text-tinta-media">{formatarValor(ponto.valor)}</p>
                  </div>
                ) : null}
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}
