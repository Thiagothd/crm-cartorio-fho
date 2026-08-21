import { useMemo } from 'react'
import { CalendarDays, CalendarRange, RefreshCw, Sun } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { Painel } from '@/components/ui/painel'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { FluxoDeAtendimentos } from '@/components/graficos/fluxo-de-atendimentos'
import { usarPainel } from '@/features/indicadores/consultas'
import { formatarDuracaoMinutos, formatarNumero } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { MesDoFluxo, PontoDoFluxo, ResumoDeJanela } from '@/lib/api/tipos'

const ICONE_DA_JANELA: Record<string, LucideIcon> = {
  hoje: Sun,
  '7dias': CalendarDays,
  mes: CalendarRange,
}

/**
 * A série mensal cabe no MESMO gráfico da Visão Geral — só a granularidade muda.
 * O adaptador vive aqui e não no componente do gráfico para que o gráfico
 * continue falando de uma forma só (`PontoDoFluxo`), sem conhecer os dois
 * contratos do backend.
 *
 * `primeiraRespostaMedianaMinutos: null` é honesto: a série mensal não carrega
 * essa medida, e o gráfico já sabe esconder a linha da dica quando a
 * granularidade é mensal.
 */
function comoPontosDoFluxo(meses: MesDoFluxo[]): PontoDoFluxo[] {
  return meses.map((mes) => ({
    inicio: mes.mes,
    abertos: mes.abertos,
    encerrados: mes.encerrados,
    emAberto: mes.emAberto,
    primeiraRespostaMedianaMinutos: null,
  }))
}

export function AbaDashboard({ ativa }: { ativa: boolean }) {
  const { data, isLoading, isError, refetch, isFetching } = usarPainel(ativa)

  const pontos = useMemo(() => (data ? comoPontosDoFluxo(data.dozeMeses) : []), [data])

  if (isLoading) {
    return (
      <div className="flex flex-col gap-4">
        <Skeleton className="h-[26rem] w-full" />
        <div className="grid gap-4 md:grid-cols-3">
          <Skeleton className="h-40 w-full" />
          <Skeleton className="h-40 w-full" />
          <Skeleton className="h-40 w-full" />
        </div>
      </div>
    )
  }

  if (isError || !data) {
    return (
      <EstadoDaInterface
        tipo="erro"
        descricao="Não foi possível carregar o painel."
        acao={
          <Button size="sm" onClick={() => void refetch()}>
            <RefreshCw />
            Tentar de novo
          </Button>
        }
      />
    )
  }

  return (
    <div className="grade-com-vida flex flex-col gap-4">
      {/* Linha 1 — o gráfico grande, largura inteira. */}
      <Painel
        titulo="Últimos 12 meses"
        descricao="Quanto entrou, quanto foi concluído e o tamanho da fila ao fim de cada mês"
        acao={isFetching ? <RefreshCw className="size-3.5 animate-spin text-tinta-fraca" aria-hidden /> : null}
      >
        <FluxoDeAtendimentos pontos={pontos} granularidade="mes" />
      </Painel>

      {/* Linha 2 — as três janelas, dividindo a linha. */}
      <div className="grid gap-4 md:grid-cols-3">
        {data.janelas.map((janela) => (
          <CartaoDeJanela key={janela.chave} janela={janela} />
        ))}
      </div>
    </div>
  )
}

/**
 * Uma janela fixa (Hoje / 7 dias / Este mês). Os três cartões mostram
 * exatamente as mesmas quatro medidas, na mesma ordem, de propósito: é o que
 * permite comparar as escalas de tempo passando o olho na horizontal.
 */
function CartaoDeJanela({ janela }: { janela: ResumoDeJanela }) {
  const Icone = ICONE_DA_JANELA[janela.chave] ?? CalendarDays
  const saldo = janela.abertos - janela.encerrados
  const tempo = formatarDuracaoMinutos(janela.primeiraRespostaMedianaMinutos)

  return (
    <Painel className="flex flex-col gap-3">
      <div className="flex items-center gap-2.5">
        <span
          aria-hidden
          className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-superficie text-tinta-media"
        >
          <Icone className="size-4" />
        </span>
        <h3 className="text-[13px] font-bold tracking-tight text-tinta">{janela.rotulo}</h3>
      </div>

      <div className="flex items-end gap-5">
        <Medida rotulo="Abertos" valor={formatarNumero(janela.abertos)} />
        <Medida rotulo="Encerrados" valor={formatarNumero(janela.encerrados)} cor="text-verde" />
      </div>

      <div className="flex flex-col gap-1.5 border-t border-linha pt-2.5">
        <Linha
          rotulo="Saldo"
          valor={`${saldo > 0 ? '+' : ''}${formatarNumero(saldo)}`}
          // Saldo positivo = entrou mais do que saiu = fila crescendo. Ambar,
          // nao bordo: e' sinal de atencao, nao de prazo estourado — a mesma
          // distincao que DESIGN.md secao 5 faz.
          cor={saldo > 0 ? 'text-ambar' : saldo < 0 ? 'text-verde' : 'text-tinta'}
        />
        <Linha
          rotulo="1ª resposta"
          // "sem medição" e nao "0 min": nao houve resposta para medir.
          valor={tempo ?? 'sem medição'}
          cor={tempo ? 'text-tinta' : 'text-tinta-fraca'}
          detalhe={
            janela.amostraPrimeiraResposta > 0
              ? `mediana de ${formatarNumero(janela.amostraPrimeiraResposta)}`
              : null
          }
        />
      </div>
    </Painel>
  )
}

function Medida({ rotulo, valor, cor = 'text-tinta' }: { rotulo: string; valor: string; cor?: string }) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className={cn('font-dado text-[28px] font-bold leading-none tracking-tight', cor)}>{valor}</span>
      <span className="text-[10px] font-semibold uppercase tracking-wider text-tinta-fraca">{rotulo}</span>
    </div>
  )
}

function Linha({
  rotulo,
  valor,
  cor,
  detalhe,
}: {
  rotulo: string
  valor: string
  cor: string
  detalhe?: string | null
}) {
  return (
    <div className="flex items-baseline justify-between gap-3 text-xs">
      <span className="text-tinta-media">{rotulo}</span>
      <span className="flex items-baseline gap-1.5">
        {detalhe ? <span className="text-[10px] text-tinta-fraca">{detalhe}</span> : null}
        <span className={cn('font-dado font-bold', cor)}>{valor}</span>
      </span>
    </div>
  )
}
