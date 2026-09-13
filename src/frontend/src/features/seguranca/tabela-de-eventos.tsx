import { useEffect, useState } from 'react'
import { Painel } from '@/components/ui/painel'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Paginacao } from '@/features/central-ia/paginacao'
import { formatarDataHora } from '@/lib/formatacao'
import { usarEventosDeSeguranca } from './consultas'
import { ROTULO_DO_EVENTO } from './rotulos'

const TAMANHO_DA_PAGINA = 20

/**
 * Os fatos do SEC-03 (MFA, reset, break-glass, permissões alteradas, login) —
 * mesma tabela `EventosDeSeguranca` que já existia sem tela própria até esta
 * entrega. Nunca mostra segredo, código ou ticket — só o fato.
 */
export function TabelaDeEventos({ de, ate }: { de: string; ate: string }) {
  const [pagina, setPagina] = useState(1)

  useEffect(() => {
    setPagina(1)
  }, [de, ate])

  const { data, isLoading, isError } = usarEventosDeSeguranca({ de, ate, pagina, tamanho: TAMANHO_DA_PAGINA })

  return (
    <Painel titulo="Eventos de segurança" descricao="Verificação em duas etapas, reset, permissões alteradas e login.">
      {isLoading ? (
        <Skeleton className="h-48 w-full" />
      ) : isError || !data ? (
        <EstadoDaInterface tipo="erro" titulo="Não foi possível carregar" descricao="Tente novamente em instantes." />
      ) : data.itens.length === 0 ? (
        <p className="rounded-md border border-linha bg-superficie px-3 py-3 text-xs text-tinta-media">
          Nenhum evento registrado neste período.
        </p>
      ) : (
        <div className="flex flex-col gap-3">
          <div className="overflow-x-auto overflow-y-hidden rounded-md border border-linha">
            <table className="w-full text-sm">
              <thead className="bg-superficie text-xs text-tinta-media">
                <tr>
                  <th className="px-3 py-2 text-left font-medium">Quando</th>
                  <th className="px-3 py-2 text-left font-medium">O quê</th>
                  <th className="px-3 py-2 text-left font-medium">Conta</th>
                  <th className="hidden px-3 py-2 text-left font-medium md:table-cell">Motivo</th>
                  <th className="hidden px-3 py-2 text-left font-medium lg:table-cell">IP</th>
                </tr>
              </thead>
              <tbody>
                {data.itens.map((evento) => (
                  <tr key={evento.id} className="border-t border-linha">
                    <td className="whitespace-nowrap px-3 py-2 font-dado text-xs text-tinta-media">
                      {formatarDataHora(evento.ocorridoEm)}
                    </td>
                    <td className="px-3 py-2 text-tinta-media">{ROTULO_DO_EVENTO[evento.tipo]}</td>
                    <td className="px-3 py-2 text-tinta">
                      {evento.atendenteNome ?? '—'}
                      {evento.atorAtendenteNome ? ` · por ${evento.atorAtendenteNome}` : ''}
                      {evento.atorExterno ? ` · por ${evento.atorExterno}` : ''}
                    </td>
                    <td className="hidden px-3 py-2 text-xs text-tinta-media md:table-cell">
                      {evento.motivo ?? '—'}
                    </td>
                    <td className="hidden px-3 py-2 font-dado text-xs text-tinta-media lg:table-cell">
                      {evento.ipDeOrigem ?? '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Paginacao
            pagina={data.pagina}
            tamanhoDaPagina={data.tamanho}
            total={data.total}
            aoMudarPagina={setPagina}
          />
        </div>
      )}
    </Painel>
  )
}
