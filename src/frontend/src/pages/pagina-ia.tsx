import { useMemo, useState } from 'react'
import { RefreshCw } from 'lucide-react'
import { usarAnaliseDaIa } from '@/features/ia/consultas'
import {
  CHAVES_DE_PERIODO,
  resolverIntervalo,
  type ChaveDePeriodo,
  type IntervaloDePeriodo,
} from '@/features/indicadores/periodo'
import { SeletorDePeriodo } from '@/features/indicadores/seletor-de-periodo'
import { usarPreferenciaDeTexto } from '@/lib/usar-preferencia-local'
import { Badge } from '@/components/ui/badge'
import { Painel } from '@/components/ui/painel'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import { VolumeDaIa } from '@/components/graficos/volume-da-ia'
import { MiniHistograma } from '@/components/graficos/mini-histograma'
import { BarrasHorizontais, type ItemDeBarraHorizontal } from '@/components/graficos/barras-horizontais'
import { formatarDelta, formatarNumero, formatarPercentual, formatarSegundos } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { InsightDaIa, SituacaoAgoraDaIa } from '@/lib/api/tipos'
import { CircleAlert, Info } from 'lucide-react'

const ROTULO_DA_SITUACAO: Record<SituacaoAgoraDaIa['status'], string> = {
  Atendendo: 'Atendendo normalmente',
  Contingencia: 'Em contingência',
  Desligada: 'Desligada',
}

const BADGE_DA_SITUACAO: Record<SituacaoAgoraDaIa['status'], 'success' | 'warning' | 'outline'> = {
  Atendendo: 'success',
  Contingencia: 'warning',
  Desligada: 'outline',
}

export function PaginaIa() {
  const [chaveDePeriodo, definirChaveDePeriodo] = usarPreferenciaDeTexto<ChaveDePeriodo>(
    'crm-cartorio.ia.periodo',
    '7dias',
    CHAVES_DE_PERIODO,
  )
  const [personalizado, definirPersonalizado] = useState<IntervaloDePeriodo>(() => resolverIntervalo('7dias'))

  const intervalo = useMemo(() => resolverIntervalo(chaveDePeriodo, personalizado), [chaveDePeriodo, personalizado])
  const de = intervalo.de.toISOString()
  const ate = intervalo.ate.toISOString()

  const { data, isLoading, isError, refetch, isFetching } = usarAnaliseDaIa(de, ate)

  const assuntos: ItemDeBarraHorizontal[] =
    data?.assuntosMaisPerguntados.map((a) => ({
      id: a.tipoServicoId ?? 'nao-identificado',
      rotulo: a.nome,
      valor: a.total,
      percentual: a.percentual,
    })) ?? []

  const lacunas: ItemDeBarraHorizontal[] =
    data?.lacunasDeCatalogo.map((l, i) => ({
      id: `${l.tipoServicoId ?? 'geral'}-${l.categoria}-${i}`,
      rotulo: `${l.servicoNome} — ${l.categoria}`,
      valor: l.total,
    })) ?? []

  return (
    <div className="flex-1 overflow-y-auto p-3 md:p-6">
      <div className="mx-auto flex max-w-6xl flex-col gap-4">
        <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
          <p className="max-w-2xl text-sm text-tinta-media">
            A Resposta Inteligente atende no WhatsApp usando só o que está cadastrado no catálogo de serviços e nas
            respostas rápidas — nunca inventa prazo, valor ou documento.
          </p>
          <SeletorDePeriodo
            periodo={chaveDePeriodo}
            aoMudarPeriodo={definirChaveDePeriodo}
            personalizado={personalizado}
            aoMudarPersonalizado={definirPersonalizado}
          />
        </div>

        {isLoading ? (
          <div className="flex flex-col gap-4">
            <Skeleton className="h-16 w-full" />
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <Skeleton className="h-28 w-full" />
              <Skeleton className="h-28 w-full" />
              <Skeleton className="h-28 w-full" />
              <Skeleton className="h-28 w-full" />
            </div>
            <div className="grid gap-4 md:grid-cols-2">
              <Skeleton className="h-64 w-full" />
              <Skeleton className="h-64 w-full" />
            </div>
          </div>
        ) : isError || !data ? (
          <EstadoDaInterface
            tipo="erro"
            descricao="Não foi possível carregar a análise da IA."
            acao={
              <Button size="sm" onClick={() => void refetch()}>
                <RefreshCw />
                Tentar de novo
              </Button>
            }
          />
        ) : data.situacaoAgora.status === 'Desligada' && data.resultadoDasTentativas.entregues === 0 ? (
          <EstadoDaInterface
            tipo="vazio"
            titulo="Resposta Inteligente desligada"
            descricao="Ative a Resposta Inteligente na configuração do sistema para a IA passar a atender no WhatsApp."
          />
        ) : (
          <div className="grade-com-vida flex flex-col gap-4">
            {/* 1. Situação agora — uma frase e um selo, nunca nome de provedor. */}
            <Painel>
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="flex items-center gap-3">
                  <Badge variant={BADGE_DA_SITUACAO[data.situacaoAgora.status]}>
                    {ROTULO_DA_SITUACAO[data.situacaoAgora.status]}
                  </Badge>
                  {data.situacaoAgora.detalhe ? (
                    <p className="text-sm text-tinta-media">{data.situacaoAgora.detalhe}</p>
                  ) : null}
                </div>
                {isFetching ? <RefreshCw className="size-3.5 animate-spin text-tinta-fraca" aria-hidden /> : null}
              </div>
            </Painel>

            {/* 2. Valor entregue no período — 4 cards, com comparação ao período anterior. */}
            <div className="grid grid-cols-2 gap-3 sm:gap-4 lg:grid-cols-4">
              <CartaoDeIndicador
                rotulo="Perguntas atendidas"
                valor={data.valorEntregue.perguntasAtendidas === null ? '—' : formatarNumero(data.valorEntregue.perguntasAtendidas)}
                detalhe={
                  data.valorEntregue.perguntasAtendidas === null
                    ? `Medido a partir de ${data.rodadasMedidasDesde ? new Date(data.rodadasMedidasDesde).toLocaleDateString('pt-BR') : '—'}`
                    : formatarDelta(data.valorEntregue.perguntasAtendidasDeltaPercentual)
                      ? `${formatarDelta(data.valorEntregue.perguntasAtendidasDeltaPercentual)} vs. período anterior`
                      : 'Sem período anterior para comparar'
                }
              />
              <CartaoDeIndicador
                rotulo="Taxa de resolução"
                valor={formatarPercentual(data.valorEntregue.taxaDeResolucao)}
                cor={data.valorEntregue.taxaDeResolucao >= 80 ? 'verde' : 'tinta'}
                detalhe={
                  data.valorEntregue.taxaDeResolucaoDeltaPontos === null
                    ? 'Sem período anterior para comparar'
                    : `${data.valorEntregue.taxaDeResolucaoDeltaPontos > 0 ? '+' : ''}${data.valorEntregue.taxaDeResolucaoDeltaPontos.toFixed(1).replace('.', ',')} pontos vs. período anterior`
                }
              />
              <CartaoDeIndicador
                rotulo="A IA não soube"
                valor={formatarNumero(data.valorEntregue.naoSoubeResponder)}
                cor={data.valorEntregue.naoSoubeResponder > 0 ? 'ambar' : 'tinta'}
                detalhe={
                  formatarDelta(data.valorEntregue.naoSoubeResponderDeltaPercentual)
                    ? `${formatarDelta(data.valorEntregue.naoSoubeResponderDeltaPercentual)} vs. período anterior`
                    : 'Sem período anterior para comparar'
                }
              />
              <CartaoDeIndicador
                rotulo="Tempo típico de resposta"
                valor={formatarSegundos(data.valorEntregue.tempoTipicoDeRespostaSegundos) ?? '—'}
                detalhe={
                  data.valorEntregue.tempoTipicoDeRespostaSegundos === null
                    ? 'Nenhuma resposta medida no período'
                    : formatarDelta(data.valorEntregue.tempoTipicoDeRespostaDeltaPercentual)
                      ? `${formatarDelta(data.valorEntregue.tempoTipicoDeRespostaDeltaPercentual)} vs. período anterior`
                      : 'Mede só o tempo de resposta do provedor'
                }
              />
            </div>

            {/* 3. Utilização — volume ao longo do tempo e horários de maior uso. */}
            <div className="grid gap-4 md:grid-cols-2">
              <Painel titulo="Volume de perguntas" descricao="Cada barra é o total de perguntas feitas naquele período.">
                <VolumeDaIa pontos={data.volumeAoLongoDoTempo} granularidade={data.periodo.granularidade} />
              </Painel>

              <Painel titulo="Horários de maior uso" descricao="Quando o cliente mais recorre à IA — útil para planejar plantão.">
                <MiniHistograma valoresPorHora={data.horariosDeMaiorUso} />
              </Painel>
            </div>

            {/* 4. Sobre o que perguntam. */}
            <Painel titulo="Sobre o que perguntam" descricao="Assuntos mais procurados no período.">
              <BarrasHorizontais itens={assuntos} />
            </Painel>

            {/* 5. Oportunidades de melhoria — lacunas de catálogo, acionáveis. */}
            <Painel
              titulo="Oportunidades de melhoria"
              descricao="Perguntas que a IA não soube responder, agrupadas por assunto e motivo — o que vale cadastrar primeiro."
            >
              {lacunas.length === 0 ? (
                <p className="text-sm text-tinta-media">
                  Nenhuma lacuna identificada neste período — a IA respondeu com o que já está cadastrado.
                </p>
              ) : (
                <BarrasHorizontais itens={lacunas} />
              )}
            </Painel>

            {/* 6. Insights — no máximo 4, derivados dos mesmos números acima. */}
            {data.insights.length > 0 ? (
              <Painel titulo="Insights do período">
                <ul className="flex flex-col gap-3">
                  {data.insights.map((insight, i) => (
                    <CartaoDeInsight key={i} insight={insight} />
                  ))}
                </ul>
              </Painel>
            ) : null}
          </div>
        )}
      </div>
    </div>
  )
}

function CartaoDeInsight({ insight }: { insight: InsightDaIa }) {
  const Icone = insight.severidade === 'atencao' ? CircleAlert : Info

  return (
    <li
      className={cn(
        'flex items-start gap-2.5 rounded-lg border px-3.5 py-3 text-sm',
        insight.severidade === 'atencao' ? 'border-ambar/30 bg-ambar/[0.06]' : 'border-linha bg-superficie',
      )}
    >
      <Icone
        aria-hidden
        className={cn('mt-0.5 size-4 shrink-0', insight.severidade === 'atencao' ? 'text-ambar' : 'text-tinta-media')}
      />
      <p className="text-tinta">{insight.texto}</p>
    </li>
  )
}
