import { ChevronDown, ChevronUp } from 'lucide-react'
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
        'flex min-h-11 flex-col gap-0.5 border border-transparent border-l-linha-forte px-2.5 py-1 text-left transition-colors hover:bg-branco md:px-3',
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

/**
 * Alterna o painel entre completo e a barra fina.
 *
 * `aria-expanded` porque o botão controla a visibilidade de outro bloco — sem
 * isso, para quem usa leitor de tela o painel simplesmente some.
 */
function BotaoDeRecolher({
  recolhido,
  aoAlternar,
}: {
  recolhido: boolean
  aoAlternar: () => void
}) {
  const rotulo = recolhido ? 'Mostrar indicadores e funil' : 'Recolher indicadores e funil'

  return (
    <button
      type="button"
      onClick={aoAlternar}
      aria-expanded={!recolhido}
      aria-label={rotulo}
      title={rotulo}
      className="flex size-6 shrink-0 items-center justify-center rounded-md border border-transparent text-tinta-fraca transition-colors hover:border-linha-forte hover:bg-branco hover:text-tinta"
    >
      {recolhido ? <ChevronDown className="size-3.5" /> : <ChevronUp className="size-3.5" />}
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
  recolhido,
  aoAlternarRecolhido,
  className,
}: {
  resumo: ResumoDoPainel
  etapaSelecionada?: string
  indicadorSelecionado?: IndicadorDoPainel
  aoSelecionarIndicador: (indicador: IndicadorDoPainel) => void
  aoSelecionarEtapa: (etapaId?: string) => void
  recolhido: boolean
  aoAlternarRecolhido: () => void
  className?: string
}) {
  // Recolhido, o painel vira uma linha. O objetivo é altura: indicadores e funil
  // ocupam ~110px fixos no topo, e num notebook isso sai direto do fio de
  // mensagens — que é onde o atendimento realmente acontece.
  if (recolhido) {
    return (
      <div className={cn('flex shrink-0 items-center justify-between gap-4 border-b border-linha bg-superficie px-3 py-1.5 md:px-6', className)}>
        {/* Duas contagens, em tinta. "Atrasadas" ficou de fora de propósito: em
            bordô seria a quarta ocorrência da inbox — o limite é três
            (DESIGN.md, 2.1) —, e em qualquer outra cor contrariaria o
            mapeamento fixo da seção 5, onde atraso é sempre selo. Quem precisa
            do número expande o painel, que é justamente o que o botão faz. */}
        <p className="truncate text-xs text-tinta-media">
          <span className="font-dado text-tinta">{resumo.totalAtivas}</span> ativas
          <span className="mx-1.5 text-tinta-fraca">·</span>
          <span className="font-dado text-tinta">{resumo.naFila}</span> na fila
        </p>

        <BotaoDeRecolher recolhido aoAlternar={aoAlternarRecolhido} />
      </div>
    )
  }

  return (
    <div className={cn('flex shrink-0 flex-col gap-3 border-b border-linha bg-superficie px-3 py-3.5 md:px-6', className)}>
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

        <div className="order-last ml-auto">
          <BotaoDeRecolher recolhido={false} aoAlternar={aoAlternarRecolhido} />
        </div>

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
