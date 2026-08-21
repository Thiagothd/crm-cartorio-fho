import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { RefreshCw } from 'lucide-react'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { usarPreferenciaDeTexto } from '@/lib/usar-preferencia-local'
import { usarVisaoGeral } from '@/features/indicadores/consultas'
import {
  CHAVES_DE_PERIODO,
  resolverIntervalo,
  type ChaveDePeriodo,
  type IntervaloDePeriodo,
} from '@/features/indicadores/periodo'
import { SeletorDePeriodo } from '@/features/indicadores/seletor-de-periodo'
import { IndicadorNumerico } from '@/features/indicadores/indicador-numerico'
import { RequerAtencao } from '@/features/indicadores/requer-atencao'
import { CargaDaEquipe } from '@/features/indicadores/carga-da-equipe'
import { SaudeDaOperacaoCard } from '@/features/indicadores/saude-da-operacao'
import { FluxoDeAtendimentos } from '@/components/graficos/fluxo-de-atendimentos'
import { BarrasHorizontais } from '@/components/graficos/barras-horizontais'
import { BarraEmpilhada } from '@/components/graficos/barra-empilhada'
import { Painel } from '@/components/ui/painel'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Button } from '@/components/ui/button'
import { formatarDuracaoMinutos } from '@/lib/formatacao'
import { cn } from '@/lib/utils'

const ESCOPOS = ['todas', 'minhas'] as const

const dataDeHojeExtenso = new Intl.DateTimeFormat('pt-BR', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
})

export function PaginaVisaoGeral() {
  const navegar = useNavigate()
  const { atendente, podeGerirCadastros } = usarAutenticacao()

  const escopoPadrao = atendente?.perfil === 'Atendente' ? 'minhas' : 'todas'
  const [chaveDePeriodo, definirChaveDePeriodo] = usarPreferenciaDeTexto<ChaveDePeriodo>(
    'crm-cartorio.visao-geral.periodo',
    '7dias',
    CHAVES_DE_PERIODO,
  )
  const [escopo, definirEscopo] = usarPreferenciaDeTexto<(typeof ESCOPOS)[number]>(
    'crm-cartorio.visao-geral.escopo',
    escopoPadrao,
    ESCOPOS,
  )
  const [personalizado, definirPersonalizado] = useState<IntervaloDePeriodo>(() => resolverIntervalo('7dias'))

  const intervalo = useMemo(
    () => resolverIntervalo(chaveDePeriodo, personalizado),
    [chaveDePeriodo, personalizado],
  )

  const de = intervalo.de.toISOString()
  const ate = intervalo.ate.toISOString()

  const { data, isLoading, isError, refetch, isFetching } = usarVisaoGeral(de, ate, escopo)

  function irParaInbox(query: string) {
    navegar(`/atendimentos${query}`)
  }

  return (
    <div className="flex-1 overflow-y-auto p-3 md:p-6">
      <div className="mx-auto flex max-w-6xl flex-col gap-4">
        <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
          <p className="text-sm capitalize text-tinta-media">{dataDeHojeExtenso.format(new Date())}</p>
          <SeletorDePeriodo
            periodo={chaveDePeriodo}
            aoMudarPeriodo={definirChaveDePeriodo}
            personalizado={personalizado}
            aoMudarPersonalizado={definirPersonalizado}
            escopo={escopo}
            aoMudarEscopo={definirEscopo}
          />
        </div>

        {isLoading ? (
          <div className="flex flex-col gap-4">
            <Skeleton className="h-24 w-full" />
            <Skeleton className="h-64 w-full" />
            <div className="grid gap-4 md:grid-cols-2">
              <Skeleton className="h-48 w-full" />
              <Skeleton className="h-48 w-full" />
            </div>
          </div>
        ) : isError || !data ? (
          <EstadoDaInterface
            tipo="erro"
            descricao="Não foi possível carregar a Visão Geral."
            acao={
              <Button size="sm" onClick={() => void refetch()}>
                <RefreshCw />
                Tentar de novo
              </Button>
            }
          />
        ) : (
          // `grade-com-vida`: os 4 blocos da tela entram subindo e esmaecendo em
          // cascata (index.css) — a Visao Geral tem "vida" ao carregar, nao um
          // recorte estatico. Recalcula do zero a cada `key` (embutida no
          // primeiro filho via `agora-${escopo}`), entao trocar de escopo ou
          // período repete a entrada.
          <div className="flex flex-col gap-4 grade-com-vida">
            {/* AGORA nunca muda com o período — misturar as duas coisas numa
                faixa só faria parecer que o filtro afeta tudo, e não afeta. */}
            <div className="grid gap-4 md:grid-cols-2">
              <div key={`agora-${escopo}`} className="rounded-card border border-linha bg-branco p-2 shadow-card">
                <p className="px-2.5 pb-2 pt-1 text-[11px] font-bold uppercase tracking-wider text-tinta-fraca">
                  Agora
                </p>
                {/* 2×2 no celular, fila única a partir de `sm`. Em 318px os
                    quatro em linha somavam 313px de conteúdo mínimo (98% de
                    ocupação): "NA FILA" e "EM ALERTA" quebravam em duas linhas,
                    e um valor de três dígitos jogava o quarto item sozinho para
                    uma segunda fila. */}
                <div className="grid grid-cols-2 gap-1 sm:flex sm:flex-wrap">
                  <IndicadorNumerico
                    rotulo="Ativas"
                    valor={data.agora.ativas}
                    aoSelecionar={() => irParaInbox('')}
                  />
                  <IndicadorNumerico
                    rotulo="Na fila"
                    valor={data.agora.naFila}
                    aoSelecionar={() => irParaInbox('?escopo=fila')}
                  />
                  <IndicadorNumerico
                    rotulo="Em alerta"
                    valor={data.agora.emAlerta}
                    cor="ambar"
                    aoSelecionar={() => irParaInbox('?situacao=EmAlerta')}
                  />
                  <IndicadorNumerico
                    rotulo="Atrasados"
                    valor={data.agora.atrasadas}
                    cor="selo"
                    aoSelecionar={() => irParaInbox('?situacao=Atrasada')}
                  />
                </div>
              </div>

              <div key={`periodo-${de}-${ate}-${escopo}`} className="rounded-card border border-linha bg-branco p-2 shadow-card">
                <p className="px-2.5 pb-2 pt-1 text-[11px] font-bold uppercase tracking-wider text-tinta-fraca">
                  No período
                </p>
                <div className="grid grid-cols-2 gap-1 sm:flex sm:flex-wrap">
                  <IndicadorNumerico rotulo="Abertos" valor={data.noPeriodo.abertos} />
                  <IndicadorNumerico
                    rotulo="Encerrados"
                    valor={data.noPeriodo.encerrados}
                    aoSelecionar={() => irParaInbox('?status=Encerrada')}
                  />
                  {/* Linha inteira no celular (`col-span-2`): a 28px, "1h 20min"
                      mede ~125px e não caberia nos ~107px que sobravam numa fila
                      de três — quebrava em "1h" / "20min", que lê como dois
                      números diferentes. `whitespace-nowrap` garante isso mesmo
                      com valor mais longo. */}
                  <div
                    className="col-span-2 flex min-h-11 flex-col justify-center gap-1 rounded-lg px-3 py-2.5 sm:col-span-1 sm:flex-1 md:px-3.5"
                    title={
                      data.noPeriodo.primeiraRespostaMediaMinutos !== null
                        ? `Média: ${formatarDuracaoMinutos(data.noPeriodo.primeiraRespostaMediaMinutos)} · amostra de ${data.noPeriodo.amostraPrimeiraResposta}`
                        : undefined
                    }
                  >
                    <span className="whitespace-nowrap text-2xl font-bold leading-none tracking-tight text-tinta sm:text-[28px]">
                      {formatarDuracaoMinutos(data.noPeriodo.primeiraRespostaMedianaMinutos) ?? '—'}
                    </span>
                    <span className="text-[11px] font-medium uppercase tracking-wider text-tinta-fraca">
                      1ª resposta humana · mediana
                    </span>
                  </div>
                </div>
              </div>
            </div>

            <Painel
              titulo="Fluxo de atendimentos"
              acao={isFetching ? <RefreshCw className="size-3.5 animate-spin text-tinta-fraca" aria-hidden /> : null}
            >
              <FluxoDeAtendimentos pontos={data.fluxoDeAtendimentos} granularidade={data.periodo.granularidade} />
            </Painel>

            {/* Sem carga da equipe (atendente comum), "Requer atenção" fica com
                a largura inteira em vez de dividir com um espaço vazio ou com um
                aviso de "sem permissão". */}
            <div className={cn('grid gap-4', data.cargaDaEquipe && 'md:grid-cols-2')}>
              <Painel titulo="Requer atenção">
                <RequerAtencao grupos={data.requerAtencao} />
              </Painel>
              {data.cargaDaEquipe ? (
                <Painel titulo="Carga da equipe agora">
                  <CargaDaEquipe atendentes={data.cargaDaEquipe} />
                </Painel>
              ) : null}
            </div>

            <div className="grid gap-4 md:grid-cols-3">
              <Painel titulo="Serviços mais procurados">
                <BarrasHorizontais
                  itens={data.servicosMaisProcurados.map((s) => ({
                    id: s.tipoServicoId ?? 'sem-servico',
                    rotulo: s.nome,
                    valor: s.total,
                    percentual: s.percentual,
                  }))}
                />
              </Painel>
              <Painel titulo="Distribuição por etapa">
                <BarraEmpilhada
                  segmentos={data.distribuicaoPorEtapa}
                  aoSelecionarSegmento={(id) => irParaInbox(id ? `?etapa=${id}` : '')}
                />
              </Painel>
              <Painel titulo={podeGerirCadastros ? 'Saúde da operação' : 'Minha operação'}>
                <SaudeDaOperacaoCard saude={data.saude} minhaDistribuicao={data.minhaDistribuicaoPorEtapa} />
              </Painel>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
