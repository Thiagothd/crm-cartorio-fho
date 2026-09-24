import type { ReactNode } from 'react'
import { CalendarRange } from 'lucide-react'
import { CHAVES_DE_PERIODO, ROTULO_DO_PERIODO, paraCampoDeData, type ChaveDePeriodo, type IntervaloDePeriodo } from './periodo'
import { cn } from '@/lib/utils'

/** As quatro janelas fixas — "Personalizado" tem tratamento próprio no celular, ver abaixo. */
const CHAVES_FIXAS = CHAVES_DE_PERIODO.filter((chave) => chave !== 'personalizado')

/**
 * Mesmo vocabulário visual do grupo segmentado de `filtros-da-inbox.tsx`:
 * estado ativo por borda e peso, nunca por matiz — cor de acento é orçamento
 * escasso (DESIGN.md 2.1), e um filtro de período não merece gastar dele.
 */
function BotaoDeGrupo({
  ativo,
  onClick,
  className,
  children,
}: {
  ativo: boolean
  onClick: () => void
  className?: string
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
          ? 'border-selecao bg-selecao text-sobre-selecao shadow-card'
          : 'border-transparent text-tinta-media hover:bg-branco hover:text-tinta',
        className,
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
  const personalizadoAtivo = periodo === 'personalizado'

  return (
    <div className="flex flex-wrap items-center gap-2 sm:gap-3">
      {/* Só as 4 janelas fixas aqui — "Personalizado" some deste grupo no
          celular (ver abaixo). Elas cabem numa linha só em qualquer largura;
          quem não cabia era o quinto item. */}
      <div role="group" aria-label="Período" className="flex items-center gap-1 rounded-full border border-linha bg-superficie p-1">
        {CHAVES_FIXAS.map((chave) => (
          <BotaoDeGrupo key={chave} ativo={periodo === chave} onClick={() => aoMudarPeriodo(chave)}>
            {ROTULO_DO_PERIODO[chave]}
          </BotaoDeGrupo>
        ))}

        {/* A partir de `sm` "Personalizado" volta para dentro do grupo — há
            largura de sobra e cinco pílulas numa linha só ficam bem. */}
        <BotaoDeGrupo
          ativo={personalizadoAtivo}
          onClick={() => aoMudarPeriodo('personalizado')}
          className="hidden sm:inline-flex"
        >
          {ROTULO_DO_PERIODO.personalizado}
        </BotaoDeGrupo>
      </div>

      {/* No celular "Personalizado" é um botão PRÓPRIO, fora da pílula do
          grupo. Antes ele vivia dentro do mesmo `role="group"` com
          `flex-wrap`: em 360px cabiam só as 4 primeiras pílulas, a quinta
          quebrava para uma segunda linha sozinha e ficava presa dentro do
          MESMO contorno oval do grupo — lida como elemento quebrado, não como
          item de menu. Como botão avulso ele tem contorno próprio
          (`border-linha bg-superficie`, o mesmo tom do grupo ao lado) em vez
          de herdar o fundo transparente que `BotaoDeGrupo` usa quando
          INATIVO — aquele estilo pressupõe o fundo da pílula-mãe por baixo, e
          sozinho ficaria sem contorno nenhum. */}
      <button
        type="button"
        aria-pressed={personalizadoAtivo}
        onClick={() => aoMudarPeriodo('personalizado')}
        className={cn(
          'flex min-h-11 touch-manipulation items-center gap-1.5 rounded-full border px-3.5 text-xs font-semibold transition-all duration-200 sm:hidden',
          personalizadoAtivo
            ? 'border-selecao bg-selecao text-sobre-selecao shadow-card'
            : 'border-linha bg-superficie text-tinta-media hover:border-linha-forte hover:bg-branco hover:text-tinta',
        )}
      >
        <CalendarRange className="size-3.5 shrink-0" aria-hidden />
        {ROTULO_DO_PERIODO.personalizado}
      </button>

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
