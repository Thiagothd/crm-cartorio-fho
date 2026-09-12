import { useEffect, useState } from 'react'
import { Painel } from '@/components/ui/painel'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Paginacao } from '@/features/central-ia/paginacao'
import { formatarDataHora } from '@/lib/formatacao'
import { usarRegistrosDeAcesso } from './consultas'
import { ROTULO_DO_ACESSO } from './rotulos'

const TAMANHO_DA_PAGINA = 20

/**
 * "Quem abriu o quê" — a trilha que sustenta a promessa de que ler conversa,
 * anexo ou ficha de cliente nunca acontece sem deixar rastro. Nunca mostra
 * conteúdo: só protocolo, nome do cliente e o tipo de acesso (ver
 * `RegistroDeAcesso`, que já vem sem `Conteudo`/`Observacao`).
 */
export function TabelaDeAcessos({ de, ate }: { de: string; ate: string }) {
  const [pagina, setPagina] = useState(1)

  useEffect(() => {
    setPagina(1)
  }, [de, ate])

  const { data, isLoading, isError } = usarRegistrosDeAcesso({ de, ate, pagina, tamanho: TAMANHO_DA_PAGINA })

  return (
    <Painel titulo="Quem abriu o quê" descricao="Toda leitura de conversa, anexo ou ficha de cliente pela tela.">
      {isLoading ? (
        <Skeleton className="h-48 w-full" />
      ) : isError || !data ? (
        <EstadoDaInterface tipo="erro" titulo="Não foi possível carregar" descricao="Tente novamente em instantes." />
      ) : data.itens.length === 0 ? (
        <p className="rounded-md border border-linha bg-superficie px-3 py-3 text-xs text-tinta-media">
          Nenhum acesso registrado neste período.
        </p>
      ) : (
        <div className="flex flex-col gap-3">
          <div className="overflow-x-auto overflow-y-hidden rounded-md border border-linha">
            <table className="w-full text-sm">
              <thead className="bg-superficie text-xs text-tinta-media">
                <tr>
                  <th className="px-3 py-2 text-left font-medium">Quando</th>
                  <th className="px-3 py-2 text-left font-medium">Quem</th>
                  <th className="px-3 py-2 text-left font-medium">O quê</th>
                  <th className="hidden px-3 py-2 text-left font-medium md:table-cell">Cliente / conversa</th>
                  <th className="hidden px-3 py-2 text-left font-medium lg:table-cell">IP</th>
                </tr>
              </thead>
              <tbody>
                {data.itens.map((registro) => (
                  <tr key={registro.id} className="border-t border-linha">
                    <td className="whitespace-nowrap px-3 py-2 font-dado text-xs text-tinta-media">
                      {formatarDataHora(registro.ocorridoEm)}
                    </td>
                    <td className="px-3 py-2 text-tinta">
                      {registro.atendenteNome ?? registro.atorExterno ?? '—'}
                      {registro.autorizacaoDeAcessoTecnicoId ? (
                        <span className="ml-2 rounded-full border border-linha px-2 py-0.5 text-[10px] font-medium text-tinta-media">
                          via acesso técnico
                        </span>
                      ) : null}
                    </td>
                    <td className="px-3 py-2 text-tinta-media">{ROTULO_DO_ACESSO[registro.tipo]}</td>
                    <td className="hidden px-3 py-2 text-xs text-tinta-media md:table-cell">
                      {registro.clienteNome ?? '—'}
                      {registro.conversaProtocolo ? ` · ${registro.conversaProtocolo}` : ''}
                    </td>
                    <td className="hidden px-3 py-2 font-dado text-xs text-tinta-media lg:table-cell">
                      {registro.ipDeOrigem ?? '—'}
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
