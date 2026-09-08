import { useCallback, useEffect, useState } from 'react'
import { RefreshCw, Search } from 'lucide-react'
import { ErroDaCentralDeIa, historico, type FiltroDeObservabilidade, type PaginaDeHistorico } from '@/lib/api/central-ia'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Skeleton } from '@/components/ui/skeleton'
import { formatarDataHora, formatarNumero } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import { Paginacao } from './paginacao'

const TAMANHO_DA_PAGINA = 20

const BADGE_DO_RESULTADO: Record<string, 'success' | 'warning' | 'outline'> = {
  Entregue: 'success',
  NaoSoubeResponder: 'warning',
  Falha: 'outline',
}

const ROTULO_DO_RESULTADO: Record<string, string> = {
  Entregue: 'Entregue',
  NaoSoubeResponder: 'Não soube',
  Falha: 'Falha',
}

/**
 * Investigação por tentativa (C.6) — paginada, e buscável por RodadaId: traz
 * as duas tentativas de uma mesma pergunta lado a lado quando houve fallback.
 * Nunca mostra ConversaId, MensagemId ou texto livre — ver o contrato
 * `LinhaDoHistoricoResponse` e o cenário de reflexão em
 * `verificar-observabilidade-da-ia.cs`.
 */
export function TabelaDeHistorico({
  filtro,
  aoPerderSessao,
}: {
  filtro: FiltroDeObservabilidade
  aoPerderSessao: () => void
}) {
  const [buscaDeRodada, setBuscaDeRodada] = useState('')
  const [pagina, setPagina] = useState(1)

  const [dados, setDados] = useState<PaginaDeHistorico | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    setPagina(1)
  }, [filtro.de, filtro.ate, filtro.tenantId, filtro.provedor, filtro.modelo, filtro.resultado, filtro.tipoDeFallback, filtro.tipoDeFalha, filtro.codigoHttp])

  // Um RodadaId incompleto/invalido nunca deve virar requisicao — so tenta
  // quando o texto e' um UUID valido.
  const rodadaIdValida = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(buscaDeRodada.trim())
    ? buscaDeRodada.trim()
    : undefined

  const buscar = useCallback(async () => {
    setCarregando(true)
    setErro(null)

    try {
      const resposta = await historico(filtro, { rodadaId: rodadaIdValida, pagina, tamanhoDaPagina: TAMANHO_DA_PAGINA })
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
  }, [filtro, rodadaIdValida, pagina, aoPerderSessao])

  useEffect(() => {
    void buscar()
  }, [buscar])

  return (
    <div className="flex flex-col gap-3">
      <div className="relative w-full max-w-sm">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-tinta-fraca" aria-hidden />
        <Input
          placeholder="Buscar por RodadaId (UUID)..."
          value={buscaDeRodada}
          onChange={(evento) => {
            setBuscaDeRodada(evento.target.value)
            setPagina(1)
          }}
          className="pl-9 font-dado"
        />
      </div>

      {buscaDeRodada.trim() && !rodadaIdValida ? (
        <p className="text-xs text-tinta-fraca">RodadaId precisa ser um UUID completo.</p>
      ) : null}

      {carregando && !dados ? (
        <Skeleton className="h-64 w-full" />
      ) : erro ? (
        <EstadoDaInterface
          tipo="erro"
          descricao={erro}
          acao={
            <Button size="sm" onClick={() => void buscar()}>
              <RefreshCw />
              Tentar de novo
            </Button>
          }
        />
      ) : !dados || dados.itens.length === 0 ? (
        <p className="py-6 text-center text-sm text-tinta-media">Nenhuma tentativa encontrada.</p>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[900px] border-collapse text-sm">
              <thead>
                <tr className="border-b border-linha text-left text-[11px] font-semibold uppercase tracking-wide text-tinta-fraca">
                  <th className="py-2 pr-3">Quando</th>
                  <th className="py-2 pr-3">Tenant</th>
                  <th className="py-2 pr-3">Provedor</th>
                  <th className="py-2 pr-3">Resultado</th>
                  <th className="py-2 pr-3">Fallback</th>
                  <th className="py-2 pr-3">Falha</th>
                  <th className="py-2 pr-3">HTTP</th>
                  <th className="py-2 pr-3">Latência</th>
                  <th className="py-2 pr-3">Tokens</th>
                  <th className="py-2 pr-3">Custo</th>
                  <th className="py-2 pr-3">RodadaId</th>
                </tr>
              </thead>
              <tbody>
                {dados.itens.map((item) => (
                  <tr
                    key={item.id}
                    className={cn(
                      'border-b border-linha last:border-0',
                      item.rodadaId && item.rodadaId === rodadaIdValida && 'bg-superficie',
                    )}
                  >
                    <td className="py-2 pr-3 whitespace-nowrap font-dado text-xs">{formatarDataHora(item.iniciadaEmUtc)}</td>
                    <td className="py-2 pr-3">{item.tenantNome}</td>
                    <td className="py-2 pr-3">{item.provedor}</td>
                    <td className="py-2 pr-3">
                      <Badge variant={BADGE_DO_RESULTADO[item.resultado] ?? 'outline'}>{ROTULO_DO_RESULTADO[item.resultado] ?? item.resultado}</Badge>
                    </td>
                    <td className="py-2 pr-3 text-xs text-tinta-fraca">{item.tipoDeFallback === 'NaoEhFallback' ? '—' : item.tipoDeFallback}</td>
                    <td className="py-2 pr-3 text-xs text-tinta-fraca">{item.tipoDeFalha === 'Nenhuma' ? '—' : item.tipoDeFalha}</td>
                    <td className="py-2 pr-3 font-dado">{item.codigoHttp ?? '—'}</td>
                    <td className="py-2 pr-3 font-dado">{item.latenciaMs}ms</td>
                    <td className="py-2 pr-3 font-dado text-xs">
                      {formatarNumero(item.tokensEntrada)}→{formatarNumero(item.tokensSaida)}
                    </td>
                    <td className="py-2 pr-3 font-dado text-xs">{item.custoEstimadoUsd === null ? '—' : `US$ ${item.custoEstimadoUsd.toFixed(4)}`}</td>
                    <td className="py-2 pr-3">
                      {item.rodadaId ? (
                        <button
                          type="button"
                          onClick={() => setBuscaDeRodada(item.rodadaId!)}
                          className="font-dado text-[11px] text-tinta-fraca underline decoration-dotted hover:text-tinta"
                          title="Ver as duas tentativas desta pergunta"
                        >
                          {item.rodadaId.slice(0, 8)}…
                        </button>
                      ) : (
                        <span className="text-[11px] text-tinta-fraca">—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Paginacao pagina={dados.pagina} tamanhoDaPagina={dados.tamanhoDaPagina} total={dados.total} aoMudarPagina={setPagina} />
        </>
      )}
    </div>
  )
}
