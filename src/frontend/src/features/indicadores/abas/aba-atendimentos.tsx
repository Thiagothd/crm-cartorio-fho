import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { Bot, Clock, Inbox, RefreshCw, TrendingUp, Users } from 'lucide-react'
import { Painel } from '@/components/ui/painel'
import { Button } from '@/components/ui/button'
import { Avatar } from '@/components/ui/avatar'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { FluxoDeAtendimentos } from '@/components/graficos/fluxo-de-atendimentos'
import { BarrasHorizontais } from '@/components/graficos/barras-horizontais'
import { BarraEmpilhada } from '@/components/graficos/barra-empilhada'
import { MiniHistograma } from '@/components/graficos/mini-histograma'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import { usarRelatorioDeAtendimentos } from '@/features/indicadores/consultas'
import {
  formatarDuracaoMinutos,
  formatarMomentoRelativo,
  formatarNumero,
  formatarPercentual,
  formatarTelefone,
} from '@/lib/formatacao'
import type { ClienteNoRanking, MesDoFluxo, PontoDoFluxo } from '@/lib/api/tipos'

function comoPontosDoFluxo(meses: MesDoFluxo[]): PontoDoFluxo[] {
  return meses.map((mes) => ({
    inicio: mes.mes,
    abertos: mes.abertos,
    encerrados: mes.encerrados,
    emAberto: mes.emAberto,
    primeiraRespostaMedianaMinutos: null,
  }))
}

export function AbaAtendimentos({ de, ate, ativa }: { de: string; ate: string; ativa: boolean }) {
  const { data, isLoading, isError, refetch, isFetching } = usarRelatorioDeAtendimentos(de, ate, ativa)

  const pontos = useMemo(() => (data ? comoPontosDoFluxo(data.totalMensal) : []), [data])

  if (isLoading) {
    return (
      <div className="flex flex-col gap-4">
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-28 w-full" />
        </div>
        <Skeleton className="h-[26rem] w-full" />
        <Skeleton className="h-80 w-full" />
      </div>
    )
  }

  if (isError || !data) {
    return (
      <EstadoDaInterface
        tipo="erro"
        descricao="Não foi possível carregar o relatório de atendimentos."
        acao={
          <Button size="sm" onClick={() => void refetch()}>
            <RefreshCw />
            Tentar de novo
          </Button>
        }
      />
    )
  }

  const tempo = formatarDuracaoMinutos(data.primeiraRespostaMedianaMinutos)

  return (
    <div className="grade-com-vida flex flex-col gap-4">
      {/* Visão geral da aba: os números do período, um cartão cada. */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <CartaoDeIndicador
          rotulo="Abertos no período"
          valor={formatarNumero(data.abertos)}
          icone={TrendingUp}
          detalhe={`${formatarNumero(data.clientesAtendidosNoPeriodo)} clientes distintos`}
        />
        <CartaoDeIndicador
          rotulo="Encerrados no período"
          valor={formatarNumero(data.encerrados)}
          icone={Inbox}
          cor="verde"
          detalhe={
            data.abertos > 0
              ? `${formatarPercentual((100 * data.encerrados) / data.abertos)} do que entrou`
              : null
          }
        />
        <CartaoDeIndicador
          rotulo="Em aberto agora"
          valor={formatarNumero(data.emAbertoAgora)}
          icone={Users}
          cor="tendencia"
          detalhe="não muda com o período"
        />
        <CartaoDeIndicador
          rotulo="1ª resposta humana"
          valor={tempo ?? '—'}
          icone={Clock}
          detalhe={
            data.amostraPrimeiraResposta > 0
              ? `mediana de ${formatarNumero(data.amostraPrimeiraResposta)} conversas`
              : 'sem resposta medida no período'
          }
        />
      </div>

      {/* Total mensal — largura inteira, mesma leitura do Dashboard mas nesta
          aba serve de contexto para a lista de clientes abaixo. */}
      <Painel
        titulo="Total mensal"
        descricao="Últimos 12 meses — entradas, conclusões e fila ao fim de cada mês"
        acao={isFetching ? <RefreshCw className="size-3.5 animate-spin text-tinta-fraca" aria-hidden /> : null}
      >
        <FluxoDeAtendimentos pontos={pontos} granularidade="mes" />
      </Painel>

      <Painel
        titulo="Clientes que mais procuraram"
        descricao={`No período · ${formatarNumero(data.clientesAtendidosNoPeriodo)} clientes distintos atendidos`}
      >
        <ListaDeClientes clientes={data.clientesMaisAtendidos} />
      </Painel>

      <div className="grid gap-4 md:grid-cols-2">
        <Painel titulo="Distribuição por setor">
          <BarrasHorizontais
            itens={data.distribuicaoPorSetor.map((s) => ({
              id: s.id ?? 'sem-setor',
              rotulo: s.nome,
              valor: s.total,
            }))}
          />
        </Painel>
        <Painel titulo="Distribuição por etapa">
          <BarraEmpilhada segmentos={data.distribuicaoPorEtapa} />
        </Painel>
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Painel titulo="Horários de maior demanda" descricao="Atendimentos abertos por hora, fuso do cartório">
          <MiniHistograma valoresPorHora={data.horariosDeMaiorDemanda} />
        </Painel>

        <Painel titulo="Absorção da automação" descricao="Quanto a automação e a IA tiraram da mesa no período">
          <AbsorcaoDaAutomacao dados={data.absorcaoDaAutomacao} />
        </Painel>
      </div>
    </div>
  )
}

/**
 * Lista de clientes por volume. Reusa `Avatar` e a linguagem visual da inbox,
 * pela mesma razão de "Requer atenção": o mesmo cliente deve ter a mesma cara em
 * toda a aplicação.
 */
function ListaDeClientes({ clientes }: { clientes: ClienteNoRanking[] }) {
  if (clientes.length === 0) {
    return <p className="text-sm text-tinta-fraca">Nenhum atendimento no período.</p>
  }

  const maximo = Math.max(1, ...clientes.map((c) => c.total))

  return (
    <ul className="flex flex-col divide-y divide-linha/70">
      {clientes.map((cliente, i) => (
        <li key={cliente.clienteId}>
          <Link
            to={`/clientes?busca=${encodeURIComponent(cliente.nome)}`}
            className="group relative flex items-center gap-3 overflow-hidden rounded-lg px-2 py-2.5 transition-colors duration-150 hover:bg-superficie"
          >
            {/* Trilha proporcional, atrás da linha — comparação de relance sem
                precisar ler todos os números. */}
            <span
              aria-hidden
              className="absolute inset-y-0 left-0 origin-left rounded-lg bg-tinta/[0.05] transition-colors duration-200 group-hover:bg-tinta/[0.1]"
              style={{
                width: `${Math.max(2, (cliente.total / maximo) * 100)}%`,
                animation: `crm-crescer-barra 460ms cubic-bezier(0.16, 1, 0.3, 1) ${Math.min(i * 40, 320)}ms both`,
              }}
            />

            <span
              aria-hidden
              className="relative z-10 w-5 shrink-0 text-center font-dado text-[11px] font-bold text-tinta-fraca"
            >
              {i + 1}
            </span>

            <Avatar nome={cliente.nome} fotoUrl={cliente.fotoPerfilUrl} tamanho="sm" className="relative z-10" />

            <span className="relative z-10 min-w-0 flex-1">
              <span className="block truncate text-[13px] font-semibold text-tinta">{cliente.nome}</span>
              <span className="block truncate font-dado text-[11px] text-tinta-media">
                {formatarTelefone(cliente.telefoneWhatsapp)}
              </span>
            </span>

            <span className="relative z-10 hidden shrink-0 text-right text-[11px] text-tinta-fraca sm:block">
              último em {formatarMomentoRelativo(cliente.ultimoAtendimentoEm)}
            </span>

            <span className="relative z-10 shrink-0 text-right">
              <span className="font-dado text-sm font-bold text-tinta">{formatarNumero(cliente.total)}</span>
              <span className="ml-1.5 text-[10px] font-medium text-tinta-fraca">
                {formatarPercentual(cliente.percentual)}
              </span>
            </span>
          </Link>
        </li>
      ))}
    </ul>
  )
}

function AbsorcaoDaAutomacao({
  dados,
}: {
  dados: { humanas: number; automaticas: number; ia: number; percentualAutomatizado: number }
}) {
  const total = dados.humanas + dados.automaticas + dados.ia

  if (total === 0) {
    return <p className="text-sm text-tinta-fraca">Nenhuma mensagem no período.</p>
  }

  const fatias = [
    { rotulo: 'De gente', valor: dados.humanas, classe: 'bg-tinta' },
    { rotulo: 'Automáticas', valor: dados.automaticas, classe: 'bg-tendencia' },
    { rotulo: 'Da IA', valor: dados.ia, classe: 'bg-verde' },
  ]

  return (
    <div className="flex flex-col gap-3.5">
      <div className="flex items-baseline gap-2">
        <span className="font-dado text-[26px] font-bold leading-none tracking-tight text-tinta">
          {formatarPercentual(dados.percentualAutomatizado)}
        </span>
        <span className="text-xs font-medium text-tinta-media">das mensagens não foram digitadas por gente</span>
      </div>

      <div className="flex h-3 gap-0.5 overflow-hidden rounded-full">
        {fatias
          .filter((f) => f.valor > 0)
          .map((fatia) => (
            <div
              key={fatia.rotulo}
              title={`${fatia.rotulo}: ${formatarNumero(fatia.valor)}`}
              className={`h-full min-w-1 rounded-full transition-opacity duration-200 hover:opacity-80 ${fatia.classe}`}
              style={{
                width: `${(fatia.valor / total) * 100}%`,
                animation: 'crm-crescer-barra 480ms cubic-bezier(0.16, 1, 0.3, 1) both',
                transformOrigin: 'left',
              }}
            />
          ))}
      </div>

      <ul className="flex flex-wrap gap-1.5">
        {fatias.map((fatia) => (
          <li key={fatia.rotulo}>
            <span className="inline-flex items-center gap-1.5 rounded-full border border-linha bg-superficie py-1 pl-2 pr-2.5 text-[11px] font-medium text-tinta-media">
              <span aria-hidden className={`inline-block size-2 shrink-0 rounded-full ${fatia.classe}`} />
              {fatia.rotulo}
              <span className="font-dado text-[11px] font-bold text-tinta">{formatarNumero(fatia.valor)}</span>
            </span>
          </li>
        ))}
      </ul>

      <p className="flex items-start gap-2 text-[11px] leading-relaxed text-tinta-fraca">
        <Bot className="mt-0.5 size-3.5 shrink-0" aria-hidden />
        Mensagem automática vem de regra cadastrada; a da IA é resposta gerada. As duas contam como absorção porque
        nenhuma consumiu tempo de atendente.
      </p>
    </div>
  )
}
