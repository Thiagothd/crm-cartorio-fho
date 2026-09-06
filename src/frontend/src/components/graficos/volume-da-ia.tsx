import { useState } from 'react'
import { formatarDataHora, formatarDiaCurto, formatarHoraDoDia, formatarNumero } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { PontoDeVolumeDeIa } from '@/lib/api/tipos'

/**
 * Volume de perguntas ao longo do tempo, empilhado por resultado — responde
 * "a IA está dando conta?" numa olhada só: barra alta e verde é bom sinal,
 * barra com ambar/selo crescendo é sinal de atenção.
 *
 * Empilhado, não em três séries lado a lado: as três categorias somam o
 * volume TOTAL do balde (entregue + não soube + falha = tentativas daquele
 * período), então a altura total da barra já é uma informação own própria
 * (volume), e a composição de cores dentro dela é a segunda camada de leitura.
 *
 * Mesma linguagem visual de `mini-histograma.tsx`: eixo Y com só duas marcas
 * (0 e o pico), barra cresce da base, hover realça a coluna inteira.
 */
export function VolumeDaIa({
  pontos,
  granularidade,
}: {
  pontos: PontoDeVolumeDeIa[]
  granularidade: 'hora' | 'dia'
}) {
  const [indiceAtivo, setIndiceAtivo] = useState<number | null>(null)

  const totais = pontos.map((p) => p.entregues + p.naoSoubeResponder + p.falhas)
  const maximo = Math.max(1, ...totais)
  const semDados = totais.every((t) => t === 0)

  if (semDados) {
    return (
      <div className="flex h-56 items-center justify-center text-sm text-tinta-fraca">
        Nenhuma pergunta feita à IA neste período.
      </div>
    )
  }

  const n = pontos.length
  const passoDoRotulo = Math.max(1, Math.ceil(n / 7))

  function rotuloDoPonto(ponto: PontoDeVolumeDeIa) {
    const data = new Date(ponto.inicio)
    return granularidade === 'hora' ? formatarHoraDoDia(data.getHours()) : formatarDiaCurto(data)
  }

  return (
    <div className="flex flex-col gap-4">
      <Legenda />

      <div className="flex gap-2.5">
        <div className="flex h-56 w-7 shrink-0 flex-col-reverse justify-between pb-6 text-right">
          <span className="font-dado text-[10px] leading-none text-tinta-fraca">0</span>
          <span className="font-dado text-[10px] leading-none text-tinta-fraca">{formatarNumero(maximo)}</span>
        </div>

        <div className="relative min-w-0 flex-1">
          <div aria-hidden className="absolute inset-x-0 top-0 flex h-56 flex-col-reverse justify-between pb-6">
            <div className="h-px w-full bg-linha-forte" />
            <div className="h-px w-full bg-linha" />
          </div>

          <div className="relative flex h-56 items-stretch gap-px">
            {pontos.map((ponto, i) => {
              const total = totais[i]
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
                    {total > 0 ? (
                      <div
                        className="flex w-full max-w-5 flex-col-reverse overflow-hidden rounded-t-[3px]"
                        style={{
                          height: `${(total / maximo) * 100}%`,
                          animation: `crm-crescer-barra-vertical 420ms cubic-bezier(0.16, 1, 0.3, 1) ${Math.min(i * 16, 400)}ms both`,
                        }}
                      >
                        {ponto.entregues > 0 ? (
                          <span
                            className={cn('w-full bg-verde/80 transition-colors', ativo && 'bg-verde')}
                            style={{ height: `${(ponto.entregues / total) * 100}%` }}
                          />
                        ) : null}
                        {ponto.naoSoubeResponder > 0 ? (
                          <span
                            className={cn('w-full bg-ambar/80 transition-colors', ativo && 'bg-ambar')}
                            style={{ height: `${(ponto.naoSoubeResponder / total) * 100}%` }}
                          />
                        ) : null}
                        {ponto.falhas > 0 ? (
                          <span
                            className={cn('w-full bg-selo/80 transition-colors', ativo && 'bg-selo')}
                            style={{ height: `${(ponto.falhas / total) * 100}%` }}
                          />
                        ) : null}
                      </div>
                    ) : null}
                  </div>

                  {rotulado ? (
                    <span className="absolute bottom-0 left-1/2 -translate-x-1/2 whitespace-nowrap text-[10px] font-medium leading-6 text-tinta-fraca">
                      {rotuloDoPonto(ponto)}
                    </span>
                  ) : null}

                  {ativo ? (
                    <div className="pointer-events-none absolute bottom-full left-1/2 z-20 mb-2 w-max min-w-40 -translate-x-1/2 rounded-card border border-linha bg-branco px-3 py-2 text-xs shadow-card-hover">
                      <p className="font-bold text-tinta">{formatarDataHora(ponto.inicio)}</p>
                      <LinhaDaLegenda cor="bg-verde" rotulo="Entregues" valor={ponto.entregues} />
                      <LinhaDaLegenda cor="bg-ambar" rotulo="Não soube" valor={ponto.naoSoubeResponder} />
                      <LinhaDaLegenda cor="bg-selo" rotulo="Falhas" valor={ponto.falhas} />
                    </div>
                  ) : null}
                </div>
              )
            })}
          </div>
        </div>
      </div>
    </div>
  )
}

function Legenda() {
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-[13px] font-semibold text-tinta-media">
      <LinhaDaLegenda cor="bg-verde" rotulo="Entregues" />
      <LinhaDaLegenda cor="bg-ambar" rotulo="Não soube" />
      <LinhaDaLegenda cor="bg-selo" rotulo="Falhas" />
    </div>
  )
}

function LinhaDaLegenda({ cor, rotulo, valor }: { cor: string; rotulo: string; valor?: number }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <span aria-hidden className={cn('inline-block size-2 shrink-0 rounded-full', cor)} />
      {rotulo}
      {valor !== undefined ? (
        <span className="font-dado font-bold text-tinta">{formatarNumero(valor)}</span>
      ) : null}
    </span>
  )
}
