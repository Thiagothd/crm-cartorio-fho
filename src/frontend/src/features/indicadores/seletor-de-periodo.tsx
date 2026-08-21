import type { ReactNode } from 'react'
import { CHAVES_DE_PERIODO, ROTULO_DO_PERIODO, paraCampoDeData, type ChaveDePeriodo, type IntervaloDePeriodo } from './periodo'
import { cn } from '@/lib/utils'

/**
 * Mesmo vocabulário visual do grupo segmentado de `filtros-da-inbox.tsx`:
 * estado ativo por borda e peso, nunca por matiz — cor de acento é orçamento
 * escasso (DESIGN.md 2.1), e um filtro de período não merece gastar dele.
 */
function BotaoDeGrupo({
  ativo,
  onClick,
  children,
}: {
  ativo: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      aria-pressed={ativo}
      onClick={onClick}
      className={cn(
        // 44px no celular (DESIGN.md 12.1/12.2), reduz a partir de md — o
        // mesmo alvo que IndicadorNumerico ja usa nesta mesma tela.
        'min-h-11 touch-manipulation rounded-full border px-3.5 text-xs font-semibold transition-all duration-200 md:min-h-0 md:py-1.5',
        ativo
          ? 'border-tinta bg-tinta text-branco shadow-card'
          : 'border-transparent text-tinta-media hover:bg-branco hover:text-tinta',
      )}
    >
      {children}
    </button>
  )
}

export function SeletorDePeriodo({
  periodo,
  aoMudarPeriodo,
  personalizado,
  aoMudarPersonalizado,
  escopo,
  aoMudarEscopo,
}: {
  periodo: ChaveDePeriodo
  aoMudarPeriodo: (chave: ChaveDePeriodo) => void
  personalizado: IntervaloDePeriodo
  aoMudarPersonalizado: (intervalo: IntervaloDePeriodo) => void
  /** Omitido em telas sem noção de "meus atendimentos" (ex.: Relatórios). */
  escopo?: 'todas' | 'minhas'
  aoMudarEscopo?: (escopo: 'todas' | 'minhas') => void
}) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <div role="group" aria-label="Período" className="flex flex-wrap items-center gap-1 rounded-full border border-linha bg-superficie p-1">
        {CHAVES_DE_PERIODO.map((chave) => (
          <BotaoDeGrupo key={chave} ativo={periodo === chave} onClick={() => aoMudarPeriodo(chave)}>
            {ROTULO_DO_PERIODO[chave]}
          </BotaoDeGrupo>
        ))}
      </div>

      {periodo === 'personalizado' ? (
        // `flex-wrap`: dois `input type="date"` medem ~118px no Chrome e ~150px
        // no Safari do iOS, e com o "até" a soma chega a 322px dos 336px de uma
        // tela de 360px — cabia sem folga nenhuma para fonte maior do sistema.
        <div className="flex flex-wrap items-center gap-1.5 text-xs text-tinta-media">
          <input
            type="date"
            aria-label="Início do período"
            value={paraCampoDeData(personalizado.de)}
            onChange={(evento) => {
              const valor = evento.target.valueAsDate
              if (valor) aoMudarPersonalizado({ ...personalizado, de: valor })
            }}
            className="h-11 rounded-lg border border-linha-forte bg-branco px-2.5 text-xs text-tinta md:h-8"
          />
          <span>até</span>
          <input
            type="date"
            aria-label="Fim do período"
            value={paraCampoDeData(personalizado.ate)}
            onChange={(evento) => {
              const valor = evento.target.valueAsDate
              if (valor) {
                // Fim do dia escolhido, senão "até 21/08" excluiria o próprio dia 21.
                valor.setHours(23, 59, 59, 999)
                aoMudarPersonalizado({ ...personalizado, ate: valor })
              }
            }}
            className="h-11 rounded-lg border border-linha-forte bg-branco px-2.5 text-xs text-tinta md:h-8"
          />
        </div>
      ) : null}

      {escopo && aoMudarEscopo ? (
        <div role="group" aria-label="Escopo" className="flex items-center gap-1 rounded-full border border-linha bg-superficie p-1">
          <BotaoDeGrupo ativo={escopo === 'todas'} onClick={() => aoMudarEscopo('todas')}>
            Toda a operação
          </BotaoDeGrupo>
          <BotaoDeGrupo ativo={escopo === 'minhas'} onClick={() => aoMudarEscopo('minhas')}>
            Meus atendimentos
          </BotaoDeGrupo>
        </div>
      ) : null}
    </div>
  )
}
