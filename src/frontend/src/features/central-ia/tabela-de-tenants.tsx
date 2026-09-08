import { useCallback, useEffect, useState } from 'react'
import { ArrowDown, ArrowUp, RefreshCw, Search } from 'lucide-react'
import {
  ErroDaCentralDeIa,
  tenants,
  type FiltroDeObservabilidade,
  type OrdenarTenantsPor,
  type PaginaDeTenants,
} from '@/lib/api/central-ia'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Skeleton } from '@/components/ui/skeleton'
import { formatarNumero, formatarReais } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import { Paginacao } from './paginacao'

const TAMANHO_DA_PAGINA = 10

const COLUNAS: { chave: OrdenarTenantsPor; rotulo: string }[] = [
  { chave: 'Tentativas', rotulo: 'Tentativas' },
  { chave: 'Falhas', rotulo: 'Falhas' },
  { chave: 'FallbackExecutado', rotulo: 'Fallback executado' },
  { chave: 'Custo', rotulo: 'Custo estimado' },
  { chave: 'Latencia', rotulo: 'Latência média' },
]

/**
 * Drill-down por tenant (C.5) — ordenável, buscável por nome, paginado
 * server-side. Clicar num tenant aplica o filtro global (`aoSelecionarTenant`)
 * — todos os outros blocos da tela passam a mostrar só ele.
 */
export function TabelaDeTenants({
  filtro,
  tenantSelecionado,
  aoSelecionarTenant,
  aoPerderSessao,
}: {
  filtro: FiltroDeObservabilidade
  tenantSelecionado: string | null
  aoSelecionarTenant: (tenantId: string | null, nome: string | null) => void
  aoPerderSessao: () => void
}) {
  const [busca, setBusca] = useState('')
  const [ordenarPor, setOrdenarPor] = useState<OrdenarTenantsPor>('Tentativas')
  const [decrescente, setDecrescente] = useState(true)
  const [pagina, setPagina] = useState(1)

  const [dados, setDados] = useState<PaginaDeTenants | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  // Filtro global (período, provedor, resultado...) muda a base de dados —
  // volta pra pagina 1 pra nao pedir uma pagina que pode nem existir mais.
  useEffect(() => {
    setPagina(1)
  }, [filtro.de, filtro.ate, filtro.tenantId, filtro.provedor, filtro.modelo, filtro.resultado, filtro.tipoDeFallback, filtro.tipoDeFalha, filtro.codigoHttp])

  const buscar = useCallback(async () => {
    setCarregando(true)
    setErro(null)

    try {
      const resposta = await tenants(filtro, { busca: busca || undefined, ordenarPor, decrescente, pagina, tamanhoDaPagina: TAMANHO_DA_PAGINA })
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
  }, [filtro, busca, ordenarPor, decrescente, pagina, aoPerderSessao])

  useEffect(() => {
    void buscar()
  }, [buscar])

  function aoClicarColuna(chave: OrdenarTenantsPor) {
    if (chave === ordenarPor) {
      setDecrescente((atual) => !atual)
    } else {
      setOrdenarPor(chave)
      setDecrescente(true)
    }
    setPagina(1)
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <div className="relative w-full max-w-xs">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-tinta-fraca" aria-hidden />
          <Input
            placeholder="Buscar tenant por nome..."
            value={busca}
            onChange={(evento) => {
              setBusca(evento.target.value)
              setPagina(1)
            }}
            className="pl-9"
          />
        </div>
        {tenantSelecionado ? (
          <Button variant="outline" size="sm" onClick={() => aoSelecionarTenant(null, null)}>
            Limpar filtro de tenant
          </Button>
        ) : null}
      </div>

      {carregando && !dados ? (
        <Skeleton className="h-48 w-full" />
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
        <p className="py-6 text-center text-sm text-tinta-media">Nenhum tenant com chamada de IA no período/filtro atual.</p>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] border-collapse text-sm">
              <thead>
                <tr className="border-b border-linha text-left text-[11px] font-semibold uppercase tracking-wide text-tinta-fraca">
                  <th className="py-2 pr-3">Tenant</th>
                  {COLUNAS.map((coluna) => (
                    <th key={coluna.chave} className="py-2 pr-3">
                      <button
                        type="button"
                        onClick={() => aoClicarColuna(coluna.chave)}
                        className="inline-flex items-center gap-1 hover:text-tinta"
                      >
                        {coluna.rotulo}
                        {ordenarPor === coluna.chave ? (
                          decrescente ? <ArrowDown className="size-3" /> : <ArrowUp className="size-3" />
                        ) : null}
                      </button>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {dados.itens.map((item) => (
                  <tr
                    key={item.tenantId}
                    onClick={() => aoSelecionarTenant(item.tenantId, item.nome)}
                    className={cn(
                      'cursor-pointer border-b border-linha last:border-0 hover:bg-superficie',
                      tenantSelecionado === item.tenantId && 'bg-superficie',
                    )}
                  >
                    <td className="py-2 pr-3 font-medium text-tinta">{item.nome}</td>
                    <td className="py-2 pr-3 font-dado">{formatarNumero(item.tentativas)}</td>
                    <td className={cn('py-2 pr-3 font-dado', item.falhas > 0 && 'text-ambar')}>{formatarNumero(item.falhas)}</td>
                    <td className="py-2 pr-3 font-dado">{formatarNumero(item.fallbackExecutado)}</td>
                    <td className="py-2 pr-3 font-dado">
                      {formatarReais(item.custoEstimadoReais)}
                      <span className="ml-1 text-[10px] text-tinta-fraca">US$ {item.custoEstimadoUsd.toFixed(4)}</span>
                    </td>
                    <td className="py-2 pr-3 font-dado">{item.latenciaMediaMs === null ? '—' : `${Math.round(item.latenciaMediaMs)}ms`}</td>
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
