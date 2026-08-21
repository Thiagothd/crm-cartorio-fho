import { Link } from 'react-router-dom'
import { Building2, IdCard, MessagesSquare, RefreshCw, UserPlus, Users } from 'lucide-react'
import { Painel } from '@/components/ui/painel'
import { Button } from '@/components/ui/button'
import { Avatar } from '@/components/ui/avatar'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import { usarRelatorioDeClientes } from '@/features/indicadores/consultas'
import {
  formatarMesCurto,
  formatarMesExtenso,
  formatarMomentoRelativo,
  formatarNumero,
  formatarPercentual,
  formatarTelefone,
} from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { ClienteNoRanking, PontoMensal } from '@/lib/api/tipos'

export function AbaClientes({ de, ate, ativa }: { de: string; ate: string; ativa: boolean }) {
  const { data, isLoading, isError, refetch, isFetching } = usarRelatorioDeClientes(de, ate, ativa)

  if (isLoading) {
    return (
      <div className="flex flex-col gap-4">
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-28 w-full" />
        </div>
        <Skeleton className="h-72 w-full" />
        <Skeleton className="h-80 w-full" />
      </div>
    )
  }

  if (isError || !data) {
    return (
      <EstadoDaInterface
        tipo="erro"
        descricao="Não foi possível carregar o relatório de clientes."
        acao={
          <Button size="sm" onClick={() => void refetch()}>
            <RefreshCw />
            Tentar de novo
          </Button>
        }
      />
    )
  }

  const semDocumento = data.total > 0 ? (100 * data.semDocumento) / data.total : 0

  return (
    <div className="grade-com-vida flex flex-col gap-4">
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <CartaoDeIndicador
          rotulo="Clientes cadastrados"
          valor={formatarNumero(data.total)}
          icone={Users}
          detalhe="base inteira, não muda com o período"
        />
        <CartaoDeIndicador
          rotulo="Novos no período"
          valor={formatarNumero(data.novosNoPeriodo)}
          icone={UserPlus}
          cor="verde"
          detalhe={
            data.total > 0 ? `${formatarPercentual((100 * data.novosNoPeriodo) / data.total)} da base` : null
          }
        />
        <CartaoDeIndicador
          rotulo="Com atendimento aberto"
          valor={formatarNumero(data.comAtendimentoAtivo)}
          icone={MessagesSquare}
          cor="tendencia"
          detalhe="agora, independente do período"
        />
        <CartaoDeIndicador
          rotulo="Sem documento"
          valor={formatarNumero(data.semDocumento)}
          icone={IdCard}
          // Ambar quando passa de um terco da base: cadastro incompleto atrasa
          // lavratura, entao e' pendencia operacional — mas nao e' prazo
          // estourado, e por isso nunca bordo (DESIGN.md secao 5).
          cor={semDocumento >= 33 ? 'ambar' : 'tinta'}
          detalhe={data.total > 0 ? `${formatarPercentual(semDocumento)} da base sem CPF/CNPJ` : null}
        />
      </div>

      <div className="grid gap-4 md:grid-cols-3">
        <Painel
          titulo="Crescimento da base"
          descricao="Clientes cadastrados por mês, últimos 12 meses"
          className="md:col-span-2"
          acao={isFetching ? <RefreshCw className="size-3.5 animate-spin text-tinta-fraca" aria-hidden /> : null}
        >
          <NovosPorMes pontos={data.novosPorMes} />
        </Painel>

        <Painel titulo="Tipo de pessoa">
          <TipoDePessoa fisica={data.pessoaFisica} juridica={data.pessoaJuridica} />
        </Painel>
      </div>

      <Painel titulo="Clientes que mais procuraram" descricao="No período selecionado">
        <ListaDeClientes clientes={data.maisAtendidos} />
      </Painel>
    </div>
  )
}

/**
 * Barras verticais mensais. Mesma mecânica de `MiniHistograma`: HTML/flex com
 * altura definida no container, e a barra cresce da base.
 */
function NovosPorMes({ pontos }: { pontos: PontoMensal[] }) {
  const total = pontos.reduce((soma, p) => soma + p.total, 0)

  if (total === 0) {
    return (
      <div className="flex h-40 items-center justify-center text-sm text-tinta-fraca">
        Nenhum cliente cadastrado nos últimos 12 meses.
      </div>
    )
  }

  const maximo = Math.max(1, ...pontos.map((p) => p.total))

  return (
    // `gap-0.5` no celular: com `gap-1.5`, os 11 vãos comiam 66px dos ~296px e
    // cada coluna sobrava com 19px.
    <div className="flex h-40 items-stretch gap-0.5 sm:gap-1.5">
      {pontos.map((ponto, i) => (
        <div
          key={ponto.mes}
          className="group relative flex min-w-0 flex-1 flex-col justify-end"
          title={`${formatarMesExtenso(ponto.mes)} — ${formatarNumero(ponto.total)} novo(s)`}
        >
          <span
            aria-hidden
            className="pointer-events-none absolute inset-x-0 bottom-5 top-0 rounded-sm transition-colors duration-150 group-hover:bg-superficie"
          />

          {/* No celular o número fica SEMPRE visível; a partir de `sm` só no
              hover. Não é inconsistência: em tela de toque não existe hover, e
              esconder o valor atrás dele deixava o gráfico sem número E sem
              rótulo legível. São uma ou duas casas, cabem nos 19px da coluna. */}
          <span className="pointer-events-none absolute inset-x-0 bottom-5 z-10 pb-1 text-center font-dado text-[10px] font-bold text-tinta transition-opacity duration-150 sm:opacity-0 sm:group-hover:opacity-100">
            {formatarNumero(ponto.total)}
          </span>

          <div className="relative flex flex-1 items-end pb-5">
            {ponto.total > 0 ? (
              <span
                className="w-full origin-bottom rounded-t-[4px] bg-gradient-to-t from-tendencia to-tendencia/60 transition-[filter] duration-150 group-hover:brightness-110"
                style={{
                  height: `${(ponto.total / maximo) * 100}%`,
                  animation: `crm-crescer-barra-vertical 520ms cubic-bezier(0.34, 1.4, 0.64, 1) ${i * 30}ms both`,
                }}
              />
            ) : null}
          </div>

          {/* Rótulo a cada 3 meses no celular (mais o último, que é o mês
              corrente e nunca pode sumir): "ago/26" pede ~33px e a coluna tem
              19px, então desenhar os 12 truncava todos para "ag…". A partir de
              `sm` há largura para os doze. */}
          <span
            className={cn(
              'absolute inset-x-0 bottom-0 truncate text-center text-[9px] font-medium leading-5 text-tinta-fraca',
              i % 3 !== 0 && i !== pontos.length - 1 && 'hidden sm:block',
            )}
          >
            {formatarMesCurto(ponto.mes)}
          </span>
        </div>
      ))}
    </div>
  )
}

function TipoDePessoa({ fisica, juridica }: { fisica: number; juridica: number }) {
  const total = fisica + juridica

  if (total === 0) {
    return <p className="text-sm text-tinta-fraca">Nenhum cliente cadastrado.</p>
  }

  const linhas = [
    { rotulo: 'Pessoa física', valor: fisica, icone: Users, classe: 'bg-tinta' },
    { rotulo: 'Pessoa jurídica', valor: juridica, icone: Building2, classe: 'bg-tendencia' },
  ]

  return (
    <div className="flex flex-col gap-3.5">
      <div className="flex h-3 gap-0.5 overflow-hidden rounded-full">
        {linhas
          .filter((l) => l.valor > 0)
          .map((linha) => (
            <div
              key={linha.rotulo}
              className={cn('h-full min-w-1 rounded-full', linha.classe)}
              style={{
                width: `${(linha.valor / total) * 100}%`,
                animation: 'crm-crescer-barra 480ms cubic-bezier(0.16, 1, 0.3, 1) both',
                transformOrigin: 'left',
              }}
            />
          ))}
      </div>

      <ul className="flex flex-col gap-2">
        {linhas.map((linha) => (
          <li key={linha.rotulo} className="flex items-center justify-between gap-3">
            <span className="flex items-center gap-2 text-[13px] font-medium text-tinta">
              <span
                aria-hidden
                className="flex size-7 shrink-0 items-center justify-center rounded-lg bg-superficie text-tinta-media"
              >
                <linha.icone className="size-3.5" />
              </span>
              {linha.rotulo}
            </span>
            <span className="shrink-0 text-right">
              <span className="font-dado text-sm font-bold text-tinta">{formatarNumero(linha.valor)}</span>
              <span className="ml-1.5 text-[10px] font-medium text-tinta-fraca">
                {formatarPercentual((100 * linha.valor) / total)}
              </span>
            </span>
          </li>
        ))}
      </ul>
    </div>
  )
}

/** Igual à lista da aba Atendimentos — mesmo cliente, mesma cara. */
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
