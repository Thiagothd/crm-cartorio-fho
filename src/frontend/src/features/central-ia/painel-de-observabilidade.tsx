import { useCallback, useEffect, useMemo, useState } from 'react'
import { RefreshCw, X } from 'lucide-react'
import {
  ErroDaCentralDeIa,
  observabilidade,
  type FiltroDeObservabilidade,
  type ObservabilidadeDaIa,
  type ProvedorDeIa,
  type ResultadoDaChamadaDeIa,
  type TipoDeFalhaDeIa,
  type TipoDeFallbackDeIa,
} from '@/lib/api/central-ia'
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
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import { VolumeDaIa } from '@/components/graficos/volume-da-ia'
import { VolumePorProvedor } from '@/components/graficos/volume-por-provedor'
import { HistogramaDeLatenciaGrafico } from '@/components/graficos/histograma-de-latencia'
import { SerieTemporalSimples } from '@/components/graficos/serie-temporal-simples'
import { BarrasHorizontais, type ItemDeBarraHorizontal } from '@/components/graficos/barras-horizontais'
import { CartaoDeSaudeDoProvedor } from './cartao-de-saude-do-provedor'
import { TabelaDeTenants } from './tabela-de-tenants'
import { TabelaDeHistorico } from './tabela-de-historico'
import { formatarNumero, formatarPercentual, formatarReais } from '@/lib/formatacao'

const TODOS = '__todos__'

export function PainelDeObservabilidade({ aoPerderSessao }: { aoPerderSessao: () => void }) {
  const [chaveDePeriodo, definirChaveDePeriodo] = usarPreferenciaDeTexto<ChaveDePeriodo>(
    'crm-cartorio.central-ia.periodo',
    '7dias',
    CHAVES_DE_PERIODO,
  )
  const [personalizado, definirPersonalizado] = useState<IntervaloDePeriodo>(() => resolverIntervalo('7dias'))

  const [provedor, setProvedor] = useState<ProvedorDeIa | undefined>()
  const [resultado, setResultado] = useState<ResultadoDaChamadaDeIa | undefined>()
  const [tipoDeFallback, setTipoDeFallback] = useState<TipoDeFallbackDeIa | undefined>()
  const [tipoDeFalha, setTipoDeFalha] = useState<TipoDeFalhaDeIa | undefined>()
  const [codigoHttpTexto, setCodigoHttpTexto] = useState('')
  const [tenantId, setTenantId] = useState<string | null>(null)
  const [tenantNome, setTenantNome] = useState<string | null>(null)

  const intervalo = useMemo(() => resolverIntervalo(chaveDePeriodo, personalizado), [chaveDePeriodo, personalizado])
  const granularidade: 'hora' | 'dia' = intervalo.ate.getTime() - intervalo.de.getTime() <= 25 * 3_600_000 ? 'hora' : 'dia'

  const codigoHttp = /^\d{3}$/.test(codigoHttpTexto.trim()) ? Number(codigoHttpTexto.trim()) : undefined

  const filtro: FiltroDeObservabilidade = useMemo(
    () => ({
      de: intervalo.de.toISOString(),
      ate: intervalo.ate.toISOString(),
      tenantId: tenantId ?? undefined,
      provedor,
      resultado,
      tipoDeFallback,
      tipoDeFalha,
      codigoHttp,
    }),
    [intervalo, tenantId, provedor, resultado, tipoDeFallback, tipoDeFalha, codigoHttp],
  )

  const [dados, setDados] = useState<ObservabilidadeDaIa | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  const buscar = useCallback(async () => {
    setCarregando(true)
    setErro(null)

    try {
      const resposta = await observabilidade(filtro)
      setDados(resposta)
    } catch (falha) {
      if (falha instanceof ErroDaCentralDeIa && falha.naoAutenticado) {
        aoPerderSessao()
        return
      }
      setErro(falha instanceof ErroDaCentralDeIa ? falha.message : 'Não foi possível conectar ao servidor.')
    } finally {
      setCarregando(false)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filtro, aoPerderSessao])

  useEffect(() => {
    void buscar()
  }, [buscar])

  function aoSelecionarTenant(id: string | null, nome: string | null) {
    setTenantId(id)
    setTenantNome(nome)
  }

  const filtrosAtivos = Boolean(provedor || resultado || tipoDeFallback || tipoDeFalha || codigoHttp || tenantId)

  function limparFiltros() {
    setProvedor(undefined)
    setResultado(undefined)
    setTipoDeFallback(undefined)
    setTipoDeFalha(undefined)
    setCodigoHttpTexto('')
    setTenantId(null)
    setTenantNome(null)
  }

  const errosPorCategoria: ItemDeBarraHorizontal[] =
    dados?.errosPorCategoria.map((e, i) => ({
      id: `${e.provedor}-${e.tipoDeFalha}-${i}`,
      rotulo: `${e.provedor} — ${e.tipoDeFalha}`,
      valor: e.total,
    })) ?? []

  const fallbacksPorTipo: ItemDeBarraHorizontal[] =
    dados?.fallbacksPorTipo.map((f) => ({ id: f.tipo, rotulo: f.tipo, valor: f.total })) ?? []

  const distribuicaoPorModelo: ItemDeBarraHorizontal[] =
    dados?.distribuicaoPorModelo.map((m, i) => ({
      id: `${m.provedor}-${m.modelo}-${i}`,
      rotulo: `${m.provedor} — ${m.modelo}`,
      valor: m.total,
    })) ?? []

  const custoAoLongoDoTempo = (dados?.linhaDoTempo ?? []).map((p) => ({ inicio: p.inicio, valor: p.custoEstimadoUsd }))
  const tokensAoLongoDoTempo = (dados?.linhaDoTempo ?? []).map((p) => ({ inicio: p.inicio, valor: p.tokensEntrada + p.tokensSaida }))

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-3 rounded-card border border-linha bg-branco p-3 shadow-card sm:p-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <SeletorDePeriodo
            periodo={chaveDePeriodo}
            aoMudarPeriodo={definirChaveDePeriodo}
            personalizado={personalizado}
            aoMudarPersonalizado={definirPersonalizado}
          />
          {filtrosAtivos ? (
            <Button variant="ghost" size="sm" onClick={limparFiltros}>
              <X />
              Limpar filtros
            </Button>
          ) : null}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {tenantNome ? (
            <Badge variant="outline" className="gap-1.5">
              Tenant: {tenantNome}
              <button type="button" onClick={() => aoSelecionarTenant(null, null)} aria-label="Remover filtro de tenant">
                <X className="size-3" />
              </button>
            </Badge>
          ) : null}

          <SeletorSimples
            rotulo="Provedor"
            valor={provedor ?? TODOS}
            aoMudar={(v) => setProvedor(v === TODOS ? undefined : (v as ProvedorDeIa))}
            opcoes={[
              { valor: TODOS, rotulo: 'Todos os provedores' },
              { valor: 'Gemini', rotulo: 'Gemini' },
              { valor: 'Anthropic', rotulo: 'Anthropic' },
            ]}
          />

          <SeletorSimples
            rotulo="Resultado"
            valor={resultado ?? TODOS}
            aoMudar={(v) => setResultado(v === TODOS ? undefined : (v as ResultadoDaChamadaDeIa))}
            opcoes={[
              { valor: TODOS, rotulo: 'Qualquer resultado' },
              { valor: 'Entregue', rotulo: 'Entregue' },
              { valor: 'NaoSoubeResponder', rotulo: 'Não soube responder' },
              { valor: 'Falha', rotulo: 'Falha' },
            ]}
          />

          <SeletorSimples
            rotulo="Fallback"
            valor={tipoDeFallback ?? TODOS}
            aoMudar={(v) => setTipoDeFallback(v === TODOS ? undefined : (v as TipoDeFallbackDeIa))}
            opcoes={[
              { valor: TODOS, rotulo: 'Qualquer fallback' },
              { valor: 'NaoEhFallback', rotulo: 'Não é fallback' },
              { valor: 'Executado', rotulo: 'Executado' },
              { valor: 'PreventivoDesligado', rotulo: 'Preventivo — desligado' },
              { valor: 'PreventivoDisjuntor', rotulo: 'Preventivo — disjuntor' },
              { valor: 'PreventivoMargemDeCota', rotulo: 'Preventivo — margem de cota' },
              { valor: 'Indeterminado', rotulo: 'Indeterminado (linha antiga)' },
            ]}
          />

          <SeletorSimples
            rotulo="Tipo de falha"
            valor={tipoDeFalha ?? TODOS}
            aoMudar={(v) => setTipoDeFalha(v === TODOS ? undefined : (v as TipoDeFalhaDeIa))}
            opcoes={[
              { valor: TODOS, rotulo: 'Qualquer falha' },
              { valor: 'RateLimit', rotulo: 'Rate limit (429)' },
              { valor: 'QuotaEsgotada', rotulo: 'Quota esgotada' },
              { valor: 'Indisponibilidade', rotulo: 'Indisponibilidade' },
              { valor: 'Timeout', rotulo: 'Timeout' },
              { valor: 'Rede', rotulo: 'Rede' },
              { valor: 'Configuracao', rotulo: 'Configuração' },
              { valor: 'RespostaInvalida', rotulo: 'Resposta inválida' },
            ]}
          />

          <Input
            placeholder="Código HTTP"
            value={codigoHttpTexto}
            onChange={(evento) => setCodigoHttpTexto(evento.target.value.replace(/\D/g, '').slice(0, 3))}
            className="h-9 w-32 text-xs"
          />
        </div>
      </div>

      {carregando && !dados ? (
        <div className="flex flex-col gap-4">
          <Skeleton className="h-40 w-full" />
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Skeleton className="h-28 w-full" />
            <Skeleton className="h-28 w-full" />
            <Skeleton className="h-28 w-full" />
            <Skeleton className="h-28 w-full" />
          </div>
          <Skeleton className="h-64 w-full" />
        </div>
      ) : erro || !dados ? (
        <EstadoDaInterface
          tipo="erro"
          descricao={erro ?? 'Não foi possível carregar a observabilidade da IA.'}
          acao={
            <Button size="sm" onClick={() => void buscar()}>
              <RefreshCw />
              Tentar de novo
            </Button>
          }
        />
      ) : (
        <>
          <Painel titulo="Saúde agora" descricao="Sempre 'hoje' no fuso da serventia — independente do período filtrado acima.">
            <div className="grid gap-4 sm:grid-cols-2">
              <CartaoDeSaudeDoProvedor saude={dados.saudeAgora.gemini} />
              <CartaoDeSaudeDoProvedor saude={dados.saudeAgora.anthropic} />
            </div>
          </Painel>

          <div className="grid grid-cols-2 gap-3 sm:gap-4 lg:grid-cols-4">
            <CartaoDeIndicador
              rotulo="Tentativas"
              valor={formatarNumero(dados.kpis.tentativas)}
              detalhe={`${formatarNumero(dados.kpis.rodadas)} rodadas · ${formatarNumero(dados.kpis.tenantsAtivos)} tenants`}
            />
            <CartaoDeIndicador
              rotulo="Taxa de sucesso"
              valor={formatarPercentual(dados.kpis.taxaDeSucesso)}
              cor={dados.kpis.taxaDeSucesso >= 95 ? 'verde' : 'ambar'}
              detalhe={`Falha: ${formatarPercentual(dados.kpis.taxaDeFalha)}`}
            />
            <CartaoDeIndicador
              rotulo="Fallback executado"
              valor={formatarNumero(dados.kpis.fallbackExecutado)}
              detalhe={`Preventivo: ${formatarNumero(
                dados.kpis.fallbackPreventivoDesligado + dados.kpis.fallbackPreventivoDisjuntor + dados.kpis.fallbackPreventivoMargemDeCota,
              )}`}
            />
            <CartaoDeIndicador
              rotulo="Custo estimado"
              valor={formatarReais(dados.kpis.custoEstimadoReaisTotal)}
              detalhe={`US$ ${dados.kpis.custoEstimadoUsdTotal.toFixed(4)}${dados.kpis.custoMedioUsdPorRodada !== null ? ` · US$ ${dados.kpis.custoMedioUsdPorRodada.toFixed(4)}/rodada` : ''}`}
            />
            <CartaoDeIndicador
              rotulo="Latência (p50)"
              valor={dados.kpis.p50LatenciaMs === null ? '—' : `${Math.round(dados.kpis.p50LatenciaMs)}ms`}
              detalhe={
                dados.kpis.p95LatenciaMs === null
                  ? `±${dados.kpis.larguraDoBaldeDeLatenciaMs / 2}ms`
                  : `p95: ${Math.round(dados.kpis.p95LatenciaMs)}ms · p99: ${Math.round(dados.kpis.p99LatenciaMs ?? 0)}ms (±${dados.kpis.larguraDoBaldeDeLatenciaMs / 2}ms)`
              }
            />
            <CartaoDeIndicador
              rotulo="Tokens"
              valor={formatarNumero(dados.kpis.tokensEntrada + dados.kpis.tokensSaida)}
              detalhe={`${formatarNumero(dados.kpis.tokensEntrada)} entrada · ${formatarNumero(dados.kpis.tokensSaida)} saída`}
            />
            <CartaoDeIndicador
              rotulo="Rate limit (429)"
              valor={formatarNumero(dados.kpis.total429)}
              cor={dados.kpis.total429 > 0 ? 'ambar' : 'tinta'}
            />
            <CartaoDeIndicador
              rotulo="Timeouts"
              valor={formatarNumero(dados.kpis.totalTimeouts)}
              cor={dados.kpis.totalTimeouts > 0 ? 'ambar' : 'tinta'}
            />
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <Painel titulo="Volume por provedor" descricao="Quem está atendendo, Gemini ou Haiku.">
              <VolumePorProvedor pontos={dados.linhaDoTempo} granularidade={granularidade} />
            </Painel>
            <Painel titulo="Resultado ao longo do tempo" descricao="Entregue, não soube responder ou falhou.">
              <VolumeDaIa pontos={dados.linhaDoTempo} granularidade={granularidade} />
            </Painel>
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <Painel titulo="Custo estimado ao longo do tempo" descricao="Cálculo local sobre tokens — nunca fatura real.">
              <SerieTemporalSimples
                pontos={custoAoLongoDoTempo}
                granularidade={granularidade}
                cor="selo"
                formatarValor={(v) => `US$ ${v.toFixed(2)}`}
                rotuloVazio="Nenhum custo estimado neste período."
              />
            </Painel>
            <Painel titulo="Tokens ao longo do tempo" descricao="Entrada + saída, somados.">
              <SerieTemporalSimples
                pontos={tokensAoLongoDoTempo}
                granularidade={granularidade}
                cor="tendencia"
                formatarValor={formatarNumero}
                rotuloVazio="Nenhum token consumido neste período."
              />
            </Painel>
          </div>

          <Painel titulo="Histograma de latência" descricao={`Balde de ${dados.kpis.larguraDoBaldeDeLatenciaMs}ms, só tentativas com sucesso.`}>
            <HistogramaDeLatenciaGrafico histogramas={dados.histogramaDeLatencia} />
          </Painel>

          <div className="grid gap-4 md:grid-cols-3">
            <Painel titulo="Erros por categoria">
              {errosPorCategoria.length === 0 ? (
                <p className="text-sm text-tinta-media">Nenhuma falha neste período.</p>
              ) : (
                <BarrasHorizontais itens={errosPorCategoria} />
              )}
            </Painel>
            <Painel titulo="Fallbacks por tipo">
              {fallbacksPorTipo.length === 0 ? (
                <p className="text-sm text-tinta-media">Nenhum fallback neste período.</p>
              ) : (
                <BarrasHorizontais itens={fallbacksPorTipo} />
              )}
            </Painel>
            <Painel titulo="Distribuição por modelo">
              {distribuicaoPorModelo.length === 0 ? (
                <p className="text-sm text-tinta-media">Nenhuma tentativa neste período.</p>
              ) : (
                <BarrasHorizontais itens={distribuicaoPorModelo} />
              )}
            </Painel>
          </div>

          <Painel titulo="Tenants" descricao="Clique num tenant para aplicar o filtro em toda a tela.">
            <TabelaDeTenants
              filtro={filtro}
              tenantSelecionado={tenantId}
              aoSelecionarTenant={aoSelecionarTenant}
              aoPerderSessao={aoPerderSessao}
            />
          </Painel>

          <Painel titulo="Histórico / investigação" descricao="Uma linha por tentativa. Buscar por RodadaId traz as duas tentativas de uma mesma pergunta.">
            <TabelaDeHistorico filtro={filtro} aoPerderSessao={aoPerderSessao} />
          </Painel>
        </>
      )}
    </div>
  )
}

function SeletorSimples<T extends string>({
  rotulo,
  valor,
  aoMudar,
  opcoes,
}: {
  rotulo: string
  valor: T
  aoMudar: (valor: T) => void
  opcoes: { valor: T; rotulo: string }[]
}) {
  return (
    <Select value={valor} onValueChange={(v) => aoMudar(v as T)}>
      <SelectTrigger className="h-9 w-auto min-w-36 text-xs" aria-label={rotulo}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {opcoes.map((opcao) => (
          <SelectItem key={opcao.valor} value={opcao.valor}>
            {opcao.rotulo}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
