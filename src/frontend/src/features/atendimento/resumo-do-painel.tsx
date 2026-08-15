import { cn } from '@/lib/utils'
import { coresDaEtapa } from './cores-de-etapa'
import type { ResumoDoPainel } from '@/lib/api/tipos'

/**
 * Indicadores do painel. Sem ícone e sem cartão colorido: número em Spectral,
 * rótulo em caixa alta pequena — a leitura de um cabeçalho de livro.
 * docs/DESIGN.md, secoes 1 e 4.
 */
function Indicador({
  rotulo,
  valor,
  cor,
  ativo,
  aoSelecionar,
}: {
  rotulo: string
  valor: number
  cor?: 'ambar' | 'selo'
  ativo: boolean
  aoSelecionar: () => void
}) {
  return (
    <button
      type="button"
      onClick={aoSelecionar}
      aria-pressed={ativo}
      className={cn(
        'flex min-h-11 flex-col gap-0.5 border border-transparent border-l-linha-forte px-3 py-1 text-left transition-colors hover:bg-branco',
        ativo && 'border-linha-forte bg-branco',
      )}
      title={`Filtrar por ${rotulo.toLocaleLowerCase('pt-BR')}`}
    >
      <span
        className={cn(
          'font-titulo text-2xl leading-none',
          cor === 'selo' && valor > 0 && 'text-selo',
          cor === 'ambar' && valor > 0 && 'text-ambar',
          !cor && 'text-tinta',
          cor && valor === 0 && 'text-tinta',
        )}
      >
        {valor}
      </span>
      <span className="text-[10px] uppercase tracking-wider text-tinta-fraca">{rotulo}</span>
    </button>
  )
}

export type IndicadorDoPainel =
  | 'ativas'
  | 'fila'
  | 'minhas'
  | 'alerta'
  | 'atrasadas'
  | 'encerradas-hoje'

export function ResumoDoPainelAtendimento({
  resumo,
  etapaSelecionada,
  indicadorSelecionado,
  aoSelecionarIndicador,
  aoSelecionarEtapa,
}: {
  resumo: ResumoDoPainel
  etapaSelecionada?: string
  indicadorSelecionado?: IndicadorDoPainel
  aoSelecionarIndicador: (indicador: IndicadorDoPainel) => void
  aoSelecionarEtapa: (etapaId?: string) => void
}) {
  return (
    <div className="flex flex-col gap-3 border-b border-linha bg-superficie px-6 py-3.5">
      <div className="flex flex-wrap gap-y-2">
        <Indicador rotulo="Ativas" valor={resumo.totalAtivas} ativo={indicadorSelecionado === 'ativas'} aoSelecionar={() => aoSelecionarIndicador('ativas')} />
        <Indicador rotulo="Na fila" valor={resumo.naFila} ativo={indicadorSelecionado === 'fila'} aoSelecionar={() => aoSelecionarIndicador('fila')} />
        <Indicador rotulo="Minhas" valor={resumo.minhasConversas} ativo={indicadorSelecionado === 'minhas'} aoSelecionar={() => aoSelecionarIndicador('minhas')} />
        <Indicador rotulo="Em alerta" valor={resumo.emAlerta} cor="ambar" ativo={indicadorSelecionado === 'alerta'} aoSelecionar={() => aoSelecionarIndicador('alerta')} />
        <Indicador rotulo="Atrasadas" valor={resumo.atrasadas} cor="selo" ativo={indicadorSelecionado === 'atrasadas'} aoSelecionar={() => aoSelecionarIndicador('atrasadas')} />
        <Indicador rotulo="Encerradas hoje" valor={resumo.encerradasHoje} ativo={indicadorSelecionado === 'encerradas-hoje'} aoSelecionar={() => aoSelecionarIndicador('encerradas-hoje')} />
      </div>

      {/* O funil como filtro: clicar na etapa filtra a inbox por ela. */}
      <div className="flex flex-wrap items-center gap-1.5">
        <span className="mr-1 text-[11px] uppercase tracking-wider text-tinta-fraca">Funil</span>

        {resumo.porEtapa.map((etapa) => {
          const ativa = etapaSelecionada === etapa.id
          const cores = coresDaEtapa(etapa.cor)

          return (
            <button
              key={etapa.id ?? 'sem-etapa'}
              type="button"
              disabled={!etapa.id}
              onClick={() => aoSelecionarEtapa(ativa ? undefined : (etapa.id ?? undefined))}
              className={cn(
                'inline-flex items-center gap-1.5 rounded-md border px-2 py-1 text-xs font-medium transition-colors',
                // Selecionada: a borda vira tinta. A cor da etapa fica no fundo
                // e no texto nos dois estados, senao a pilula "apagaria" ao ser
                // filtrada e o funil perderia a leitura de relance.
                ativa && 'border-tinta',
                !etapa.id && 'cursor-default opacity-70',
              )}
              style={{
                backgroundColor: cores.fundo,
                color: cores.texto,
                borderColor: ativa ? undefined : cores.borda,
              }}
            >
              {etapa.nome}
              <span className="font-dado text-[11px] opacity-70">{etapa.total}</span>
            </button>
          )
        })}
      </div>
    </div>
  )
}
