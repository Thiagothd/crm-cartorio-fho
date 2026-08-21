import type { LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'

/**
 * Cartão de indicador rápido — a faixa de números que abre cada aba de
 * Relatórios.
 *
 * Difere de `IndicadorNumerico` (usado na Visão Geral) de propósito: lá os
 * números moram AGRUPADOS dentro de um cartão só, porque pertencem ao mesmo
 * recorte ("Agora", "No período"). Aqui cada medida é um cartão próprio, com
 * ícone e legenda, porque são leituras independentes numa grade — e um bloco
 * único com seis números viraria parede.
 *
 * `cor` pinta só o número, nunca o fundo: são até quatro cartões por linha, e
 * pintar o fundo de cada um estouraria o orçamento de acento (DESIGN.md 2.1)
 * sem relação com a gravidade do que está sendo dito.
 */
export function CartaoDeIndicador({
  rotulo,
  valor,
  detalhe,
  icone: Icone,
  cor = 'tinta',
  className,
}: {
  rotulo: string
  valor: string
  /** Linha de contexto sob o número: amostra, comparação, unidade. */
  detalhe?: string | null
  icone?: LucideIcon
  cor?: 'tinta' | 'verde' | 'ambar' | 'selo' | 'tendencia'
  className?: string
}) {
  const corDoValor = {
    tinta: 'text-tinta',
    verde: 'text-verde',
    ambar: 'text-ambar',
    selo: 'text-selo',
    tendencia: 'text-tendencia',
  }[cor]

  return (
    <div
      className={cn(
        'group flex flex-col gap-1 rounded-card border border-linha bg-branco p-4 shadow-card transition-all duration-200 hover:-translate-y-0.5 hover:shadow-card-hover',
        className,
      )}
    >
      <div className="flex items-center justify-between gap-2">
        <span className="truncate text-[11px] font-semibold uppercase tracking-wider text-tinta-fraca">
          {rotulo}
        </span>
        {Icone ? (
          <span
            aria-hidden
            className="flex size-7 shrink-0 items-center justify-center rounded-lg bg-superficie text-tinta-media transition-colors duration-200 group-hover:text-tinta"
          >
            <Icone className="size-3.5" />
          </span>
        ) : null}
      </div>

      <span className={cn('font-dado text-[26px] font-bold leading-none tracking-tight', corDoValor)}>{valor}</span>

      {/* Altura reservada mesmo sem detalhe: numa grade de quatro cartões, um
          sem a linha de contexto ficaria mais baixo que os vizinhos. */}
      <span className="min-h-4 text-[11px] font-medium text-tinta-media">{detalhe ?? ''}</span>
    </div>
  )
}
