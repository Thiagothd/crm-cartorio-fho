import { RefreshCw } from 'lucide-react'
import { usarSituacaoDaIa } from '@/features/ia/consultas'
import { Badge } from '@/components/ui/badge'
import { Painel } from '@/components/ui/painel'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import { BarrasHorizontais } from '@/components/graficos/barras-horizontais'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Button } from '@/components/ui/button'
import { formatarNumero, formatarPercentual, formatarReais, descreverDistanciaDePrazo } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { EstadoDoProvedorDeIa, StatusDoProvedorDeIa } from '@/lib/api/tipos'

const ROTULO_DO_STATUS: Record<StatusDoProvedorDeIa, string> = {
  Disponivel: 'Disponível',
  Contingencia: 'Em contingência',
  SemChave: 'Sem chave configurada',
  TetoDiarioAtingido: 'Teto diário atingido',
}

const BADGE_DO_STATUS: Record<StatusDoProvedorDeIa, 'success' | 'warning' | 'outline'> = {
  Disponivel: 'success',
  Contingencia: 'warning',
  SemChave: 'outline',
  TetoDiarioAtingido: 'warning',
}

/**
 * Cartão de estado de um provedor — não usa `Badge variant="destructive"`
 * (bordô) de propósito: um provedor em contingência é o sistema se defendendo
 * como projetado, não uma ruptura. Bordô fica reservado para o que é falha de
 * verdade (DESIGN.md 2.1 — mesmo raciocínio do card "Saúde da operação").
 */
function CartaoDoProvedor({ papel, provedor }: { papel: string; provedor: EstadoDoProvedorDeIa }) {
  return (
    <div className="flex flex-col gap-2 rounded-card border border-linha bg-branco p-4 shadow-card">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-[11px] font-bold uppercase tracking-wider text-tinta-fraca">{papel}</p>
          <p className="truncate text-[15px] font-bold text-tinta">{provedor.nome}</p>
          <p className="text-xs text-tinta-media">Modelo {provedor.modelo}</p>
        </div>
        <Badge variant={BADGE_DO_STATUS[provedor.status]} className="shrink-0">
          {ROTULO_DO_STATUS[provedor.status]}
        </Badge>
      </div>

      {provedor.motivo ? <p className="text-xs text-tinta-fraca">{provedor.motivo}</p> : null}
      {provedor.voltaATentarEm ? (
        <p className="text-xs text-tinta-fraca">
          Nova tentativa: {descreverDistanciaDePrazo(provedor.voltaATentarEm)}
        </p>
      ) : null}
    </div>
  )
}

function BarraDeTeto({ usado, limite }: { usado: number; limite: number }) {
  const percentual = Math.min(100, (usado / Math.max(1, limite)) * 100)

  return (
    <div className="h-1.5 overflow-hidden rounded-full bg-superficie">
      <div
        className={cn(
          'h-full rounded-full transition-[width] duration-[var(--duracao-padrao)]',
          usado >= limite ? 'bg-selo' : 'bg-verde',
        )}
        style={{ width: `${percentual}%` }}
      />
    </div>
  )
}

export function PaginaIa() {
  const { data, isLoading, isError, refetch, isFetching } = usarSituacaoDaIa()

  return (
    <div className="flex-1 overflow-y-auto p-3 md:p-6">
      <div className="mx-auto flex max-w-6xl flex-col gap-4">
        <p className="text-sm text-tinta-media">
          A Resposta Inteligente responde só com o que está cadastrado no catálogo de serviços e nas respostas
          rápidas — nunca inventa prazo, valor ou documento. Gemini é o provedor principal (gratuito dentro da
          franquia); o Haiku assume automaticamente quando necessário, sem o cliente perceber.
        </p>

        {isLoading ? (
          <div className="flex flex-col gap-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <Skeleton className="h-28 w-full" />
              <Skeleton className="h-28 w-full" />
            </div>
            <Skeleton className="h-44 w-full" />
            <div className="grid gap-4 md:grid-cols-2">
              <Skeleton className="h-52 w-full" />
              <Skeleton className="h-52 w-full" />
            </div>
          </div>
        ) : isError || !data ? (
          <EstadoDaInterface
            tipo="erro"
            descricao="Não foi possível carregar a situação da IA."
            acao={
              <Button size="sm" onClick={() => void refetch()}>
                <RefreshCw />
                Tentar de novo
              </Button>
            }
          />
        ) : !data.habilitada ? (
          <EstadoDaInterface
            tipo="vazio"
            titulo="Resposta Inteligente desligada"
            descricao="Ative RespostaInteligente:Habilitada na configuração do servidor para usar este painel."
          />
        ) : (
          <div className="grade-com-vida flex flex-col gap-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <CartaoDoProvedor papel="IA principal" provedor={data.gemini} />
              <CartaoDoProvedor papel="IA de contingência" provedor={data.anthropic} />
            </div>

            <Painel
              titulo="Hoje"
              acao={isFetching ? <RefreshCw className="size-3.5 animate-spin text-tinta-fraca" aria-hidden /> : null}
            >
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
                <CartaoDeIndicador rotulo="Chamadas de IA" valor={formatarNumero(data.hoje.totalDeChamadas)} />
                <CartaoDeIndicador
                  rotulo="Pelo Gemini"
                  valor={formatarNumero(data.hoje.atendidasPeloGemini)}
                  cor="verde"
                />
                <CartaoDeIndicador
                  rotulo="Caídas no Haiku"
                  valor={formatarNumero(data.hoje.caidasNoHaiku)}
                  cor={data.hoje.caidasNoHaiku > 0 ? 'ambar' : 'tinta'}
                />
                <CartaoDeIndicador rotulo="% gratuito" valor={formatarPercentual(data.hoje.percentualGratuito)} />
                <CartaoDeIndicador
                  rotulo="Custo do Haiku"
                  valor={formatarReais(data.hoje.custoEstimadoHaikuReais)}
                  detalhe="estimativa"
                />
                <CartaoDeIndicador rotulo="Tokens (Gemini)" valor={formatarNumero(data.hoje.tokensDoGemini)} />
              </div>

              <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:gap-6">
                <div className="flex-1">
                  <div className="mb-1 flex items-center justify-between text-xs text-tinta-media">
                    <span>Teto geral (qualquer provedor)</span>
                    <span className="font-medium text-tinta">
                      {data.usadoHojeGeral} de {data.limiteDiario}
                    </span>
                  </div>
                  <BarraDeTeto usado={data.usadoHojeGeral} limite={data.limiteDiario} />
                </div>
                <div className="flex-1">
                  <div className="mb-1 flex items-center justify-between text-xs text-tinta-media">
                    <span>Teto do Haiku (custo)</span>
                    <span className="font-medium text-tinta">
                      {data.usadoHojeHaiku} de {data.limiteDiarioHaiku}
                    </span>
                  </div>
                  <BarraDeTeto usado={data.usadoHojeHaiku} limite={data.limiteDiarioHaiku} />
                </div>
              </div>
            </Painel>

            <div className="grid gap-4 md:grid-cols-2">
              <Painel titulo="Franquia do Gemini" descricao="Estimativa local — não é uma leitura oficial do Google.">
                {data.franquia.limitesCadastrados ? (
                  <div className="flex flex-col gap-3">
                    {(
                      [
                        ['Por dia (RPD)', data.franquia.requisicoesHoje, data.franquia.limiteRpd],
                        ['Por minuto (RPM)', data.franquia.requisicoesNaJanela, data.franquia.limiteRpm],
                        ['Tokens por minuto (TPM)', data.franquia.tokensNaJanela, data.franquia.limiteTpm],
                      ] as const
                    ).map(([rotulo, usado, limite]) =>
                      limite ? (
                        <div key={rotulo}>
                          <div className="mb-1 flex items-center justify-between text-xs text-tinta-media">
                            <span>{rotulo}</span>
                            <span className="font-medium text-tinta">
                              {formatarNumero(usado)} de {formatarNumero(limite)}
                            </span>
                          </div>
                          <BarraDeTeto usado={usado} limite={limite} />
                        </div>
                      ) : null,
                    )}
                    <p className="text-xs text-tinta-fraca">
                      Margem de segurança configurada: {data.franquia.percentualDeSeguranca}%.
                    </p>
                  </div>
                ) : (
                  <div className="flex flex-col gap-2">
                    <p className="text-sm text-tinta-media">
                      Franquia não cadastrada — o sistema opera por reação: um 429 real do Gemini abre a
                      contingência, sem estimar percentual de uma cota desconhecida.
                    </p>
                    <p className="text-xs text-tinta-fraca">
                      Consumo observado hoje: {formatarNumero(data.franquia.requisicoesHoje)} requisições.
                      {data.franquia.sugestaoDeLimiteRpd
                        ? ` Sugestão de teto diário, a partir do último 429 de cota observado: ${formatarNumero(data.franquia.sugestaoDeLimiteRpd)}.`
                        : ''}
                    </p>
                  </div>
                )}
              </Painel>

              <Painel titulo="Motivos de fallback para o Haiku">
                {data.motivosDeFallback.fallbackExecutado === 0 && data.motivosDeFallback.fallbackPreventivo === 0 ? (
                  <p className="text-sm text-tinta-media">Nenhum fallback no período — o Gemini respondeu tudo.</p>
                ) : data.motivosDeFallback.fallbackExecutado === 0 ? (
                  // O Gemini nunca foi discado neste período (desligado, disjuntor
                  // aberto ou margem de cota atingida) — não há falha DELE para
                  // detalhar, só o fato de o Haiku ter respondido sozinho.
                  <p className="text-sm text-tinta-media">
                    O Gemini não foi acionado neste período (desligado, em contingência ou dentro da margem de
                    segurança da cota) — o Haiku respondeu sozinho{' '}
                    {formatarNumero(data.motivosDeFallback.fallbackPreventivo)}{' '}
                    {data.motivosDeFallback.fallbackPreventivo === 1 ? 'vez' : 'vezes'}.
                  </p>
                ) : (
                  <BarrasHorizontais
                    itens={[
                      { id: 'quota', rotulo: 'Quota esgotada', valor: data.motivosDeFallback.quota },
                      { id: 'rate-limit', rotulo: 'Rate limit', valor: data.motivosDeFallback.rateLimit },
                      {
                        id: 'indisponibilidade',
                        rotulo: 'Indisponibilidade',
                        valor: data.motivosDeFallback.indisponibilidade,
                      },
                      { id: 'configuracao', rotulo: 'Erro de configuração', valor: data.motivosDeFallback.configuracao },
                      { id: 'outros', rotulo: 'Outros', valor: data.motivosDeFallback.outros },
                    ].filter((item) => item.valor > 0)}
                  />
                )}
              </Painel>
            </div>

            <Painel titulo="Economia estimada" descricao="Comparado a responder 100% pelo Haiku no mesmo período.">
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                <CartaoDeIndicador rotulo="Custo real (Haiku)" valor={formatarReais(data.economia.custoRealHaikuReais)} />
                <CartaoDeIndicador
                  rotulo="Se fosse 100% Haiku"
                  valor={formatarReais(data.economia.custoSeFosseTudoHaikuReais)}
                />
                <CartaoDeIndicador
                  rotulo="Economia estimada"
                  valor={formatarReais(data.economia.economiaEstimadaReais)}
                  cor="verde"
                />
                <CartaoDeIndicador
                  rotulo="% de economia"
                  valor={formatarPercentual(data.economia.percentualDeEconomia)}
                  cor="verde"
                />
              </div>
            </Painel>
          </div>
        )}
      </div>
    </div>
  )
}
