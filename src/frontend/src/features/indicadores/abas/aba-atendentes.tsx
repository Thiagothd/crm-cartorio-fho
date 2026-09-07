import type { ReactNode } from 'react'
import { CheckCircle2, Clock, RefreshCw, TriangleAlert, UserCheck, UserRound, UserX } from 'lucide-react'
import { Painel } from '@/components/ui/painel'
import { Button } from '@/components/ui/button'
import { Avatar } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import { usarRelatorioDeAtendentes } from '@/features/indicadores/consultas'
import { formatarDuracaoMinutos, formatarMomentoRelativo, formatarNumero } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { AtendenteNoRelatorio } from '@/lib/api/tipos'

export function AbaAtendentes({ de, ate, ativa }: { de: string; ate: string; ativa: boolean }) {
  const { data, isLoading, isError, refetch, isFetching } = usarRelatorioDeAtendentes(de, ate, ativa)

  if (isLoading) {
    return (
      <div className="flex flex-col gap-4">
        <div className="grid gap-4 @xl:grid-cols-2 @5xl:grid-cols-4">
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-28 w-full" />
        </div>
        <Skeleton className="h-96 w-full" />
      </div>
    )
  }

  if (isError || !data) {
    return (
      <EstadoDaInterface
        tipo="erro"
        descricao="Não foi possível carregar o relatório de atendentes."
        acao={
          <Button size="sm" onClick={() => void refetch()}>
            <RefreshCw />
            Tentar de novo
          </Button>
        }
      />
    )
  }

  const totalEncerrados = data.encerramentos.porAtendente + data.encerramentos.peloCliente

  return (
    <div className="grade-com-vida flex flex-col gap-4">
      {/* Container query (`@xl`/`@5xl`), não breakpoint de tela: quem
          aperta esta grade e a largura que sobra do container `@container`
          de pagina-indicadores.tsx, apos o padding e o `max-w-6xl` — o rail
          empurrando o conteudo muda isso sem a janela mudar de tamanho.
          `@xl`(576) e conversao exata de `sm:`(640) menos o padding; `@5xl`
          (1024) fica abaixo de `@6xl` para nao regredir a 2 colunas num
          monitor de 1440px com o rail aberto (1184-48=1136px de conteudo). */}
      <div className="grid gap-4 @xl:grid-cols-2 @5xl:grid-cols-4">
        <CartaoDeIndicador
          rotulo="Atendentes ativos"
          valor={formatarNumero(data.totalDeAtendentes)}
          icone={UserRound}
          detalhe={`${formatarNumero(data.comConversaAtiva)} com conversa em aberto`}
        />
        <CartaoDeIndicador
          rotulo="Na fila sem responsável"
          valor={formatarNumero(data.naFilaSemResponsavel)}
          icone={UserX}
          cor={data.naFilaSemResponsavel > 0 ? 'ambar' : 'tinta'}
          detalhe="não muda com o período"
        />
        <CartaoDeIndicador
          rotulo="Encerrados por atendente"
          valor={formatarNumero(data.encerramentos.porAtendente)}
          icone={UserCheck}
          cor="verde"
          detalhe={
            totalEncerrados > 0
              ? `de ${formatarNumero(totalEncerrados)} encerramentos no período`
              : 'nenhum encerramento no período'
          }
        />
        <CartaoDeIndicador
          rotulo="Encerrados pelo cliente"
          valor={formatarNumero(data.encerramentos.peloCliente)}
          icone={CheckCircle2}
          detalhe="cliente pediu para encerrar"
        />
      </div>

      <Painel
        titulo="Equipe"
        descricao="Ordenada por nome — é distribuição de trabalho, não ranking de desempenho"
        acao={isFetching ? <RefreshCw className="size-3.5 animate-spin text-tinta-fraca" aria-hidden /> : null}
      >
        <TabelaDeAtendentes atendentes={data.atendentes} />
      </Painel>
    </div>
  )
}

/**
 * ORDEM ALFABÉTICA, garantida pelo backend, e a decisão é de produto: ordenar
 * por volume ou por tempo de resposta transformaria a tela num quadro de
 * comparação entre colegas. O que se procura aqui é distribuição — quem está
 * afogado, quem tem folga — não classificação.
 *
 * No celular a tabela vira lista de cartões: sete colunas em 360px cortariam
 * todas, e rolagem horizontal em tabela de leitura é pior que empilhar.
 */
function TabelaDeAtendentes({ atendentes }: { atendentes: AtendenteNoRelatorio[] }) {
  if (atendentes.length === 0) {
    return <EstadoDaInterface tipo="vazio" icone={UserRound} titulo="Nenhum atendente ativo" />
  }

  const maximo = Math.max(1, ...atendentes.map((a) => a.ativas))

  return (
    <>
      {/* Desktop — tabela de verdade, com cabeçalho. */}
      <div className="hidden overflow-x-auto md:block">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-linha text-left">
              <Cabecalho className="pl-2">Atendente</Cabecalho>
              <Cabecalho alinharDireita>Ativas</Cabecalho>
              <Cabecalho alinharDireita>Alerta</Cabecalho>
              <Cabecalho alinharDireita>Atrasadas</Cabecalho>
              <Cabecalho alinharDireita>Encerrados</Cabecalho>
              <Cabecalho alinharDireita>1ª resposta</Cabecalho>
              <Cabecalho alinharDireita className="pr-2">
                Último acesso
              </Cabecalho>
            </tr>
          </thead>
          <tbody>
            {atendentes.map((atendente, i) => (
              <tr
                key={atendente.atendenteId}
                className="group relative border-b border-linha/60 transition-colors last:border-0 hover:bg-superficie/70"
              >
                <td className="relative py-2.5 pl-2">
                  {/* Trilha proporcional dentro da primeira celula: barra em
                      `<tr>` nao funciona (a linha nao e' bloco posicionavel de
                      forma confiavel entre navegadores). */}
                  <span
                    aria-hidden
                    className="pointer-events-none absolute inset-y-0 left-0 origin-left rounded-r-lg bg-tinta/[0.05]"
                    style={{
                      width: `${Math.max(2, (atendente.ativas / maximo) * 100)}%`,
                      animation: `crm-crescer-barra 460ms cubic-bezier(0.16, 1, 0.3, 1) ${Math.min(i * 40, 320)}ms both`,
                    }}
                  />
                  <span className="relative z-10 flex items-center gap-2.5">
                    <Avatar nome={atendente.nome} tamanho="sm" />
                    <span className="min-w-0">
                      <span className="block truncate text-[13px] font-semibold text-tinta">{atendente.nome}</span>
                      <span className="block truncate text-[11px] text-tinta-media">
                        {atendente.cargoDescrito}
                        <span className="mx-1 text-tinta-fraca">·</span>
                        {atendente.perfil}
                      </span>
                    </span>
                  </span>
                </td>
                <Celula>
                  <span className="font-dado text-sm font-bold text-tinta">{formatarNumero(atendente.ativas)}</span>
                </Celula>
                <Celula>
                  <Contagem valor={atendente.emAlerta} icone={Clock} />
                </Celula>
                <Celula>
                  <Contagem valor={atendente.atrasadas} icone={TriangleAlert} />
                </Celula>
                <Celula>
                  <span className="font-dado text-xs text-tinta">{formatarNumero(atendente.encerradosNoPeriodo)}</span>
                </Celula>
                <Celula>
                  <TempoDeResposta atendente={atendente} />
                </Celula>
                <Celula className="pr-2">
                  <span className="whitespace-nowrap text-[11px] text-tinta-fraca">
                    {atendente.ultimoAcessoEm ? formatarMomentoRelativo(atendente.ultimoAcessoEm) : 'nunca entrou'}
                  </span>
                </Celula>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Celular — cartões empilhados. */}
      <ul className="flex flex-col gap-2 md:hidden">
        {atendentes.map((atendente) => (
          <li
            key={atendente.atendenteId}
            className="rounded-card border border-linha bg-superficie/50 p-3"
          >
            <div className="flex items-center gap-2.5">
              <Avatar nome={atendente.nome} tamanho="sm" />
              <div className="min-w-0 flex-1">
                <p className="truncate text-[13px] font-semibold text-tinta">{atendente.nome}</p>
                <p className="truncate text-[11px] text-tinta-media">{atendente.cargoDescrito}</p>
              </div>
              <span className="shrink-0 text-right">
                <span className="font-dado text-base font-bold text-tinta">{formatarNumero(atendente.ativas)}</span>
                <span className="ml-1 text-[10px] font-medium uppercase tracking-wider text-tinta-fraca">ativas</span>
              </span>
            </div>

            <div className="mt-2.5 flex flex-wrap items-center gap-x-4 gap-y-1 border-t border-linha pt-2.5 text-[11px] text-tinta-media">
              <span className="inline-flex items-center gap-1">
                <Clock className="size-3" aria-hidden />
                {formatarNumero(atendente.emAlerta)} em alerta
              </span>
              <span className="inline-flex items-center gap-1">
                <TriangleAlert className="size-3" aria-hidden />
                {formatarNumero(atendente.atrasadas)} atrasadas
              </span>
              <span>{formatarNumero(atendente.encerradosNoPeriodo)} encerrados</span>
              <span className="ml-auto">
                <TempoDeResposta atendente={atendente} />
              </span>
            </div>
          </li>
        ))}
      </ul>
    </>
  )
}

function Cabecalho({
  children,
  alinharDireita = false,
  className,
}: {
  children: ReactNode
  alinharDireita?: boolean
  className?: string
}) {
  return (
    <th
      scope="col"
      className={cn(
        'pb-2 text-[10px] font-semibold uppercase tracking-wider text-tinta-fraca',
        alinharDireita && 'text-right',
        className,
      )}
    >
      {children}
    </th>
  )
}

function Celula({ children, className }: { children: ReactNode; className?: string }) {
  return <td className={cn('py-2.5 text-right align-middle', className)}>{children}</td>
}

/** Zero aparece como travessão, não como "0": lista de sete colunas com zeros
 *  repetidos vira ruído, e o que importa é destacar quem TEM pendência. */
function Contagem({ valor, icone: Icone }: { valor: number; icone: typeof Clock }) {
  if (valor === 0) {
    return <span className="text-tinta-fraca">—</span>
  }

  return (
    <span className="inline-flex items-center gap-1 font-dado text-xs text-tinta">
      <Icone className="size-3 shrink-0 text-tinta-media" aria-hidden />
      {formatarNumero(valor)}
    </span>
  )
}

function TempoDeResposta({ atendente }: { atendente: AtendenteNoRelatorio }) {
  const tempo = formatarDuracaoMinutos(atendente.primeiraRespostaMedianaMinutos)

  if (!tempo) {
    return <span className="text-[11px] text-tinta-fraca">sem medição</span>
  }

  return (
    <Badge variant="outline" className="font-dado">
      {tempo}
      {/* A amostra viaja junto: uma mediana de 1 conversa e uma de 40 não
          merecem a mesma confiança, e esconder o tamanho da amostra é o jeito
          mais fácil de fazer um número parecer mais firme do que é. */}
      <span className="ml-1 text-[9px] font-normal text-tinta-fraca">
        n={formatarNumero(atendente.primeirasRespostasNoPeriodo)}
      </span>
    </Badge>
  )
}
