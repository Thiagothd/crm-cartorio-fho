import { useQuery } from '@tanstack/react-query'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Painel } from '@/components/ui/painel'
import { Skeleton } from '@/components/ui/skeleton'
import { whatsappApi } from '@/lib/api/endpoints'
import { formatarDataHora, formatarTelefone } from '@/lib/formatacao'
import type { MotivoDaConexaoDoCanal } from '@/lib/api/tipos'

const ROTULO_DO_MOTIVO: Record<MotivoDaConexaoDoCanal, string> = {
  Pareamento: 'Pareamento',
  Troca: 'Troca de número',
  Desconexao: 'Desconexão',
  QuedaDoGateway: 'Caiu sozinha',
}

/** Últimas sessões do canal — quando conectou, quando caiu, e quem trocou. */
export function HistoricoDeConexoes() {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['whatsapp-conexoes'],
    queryFn: () => whatsappApi.conexoes(),
  })

  return (
    <Painel
      titulo="Histórico de conexões"
      descricao="Quando cada número conectou, quando caiu, e por quê."
    >
      {isLoading ? (
        <Skeleton className="h-32 w-full" />
      ) : isError || !data ? (
        <EstadoDaInterface
          tipo="erro"
          titulo="Não foi possível carregar"
          descricao="Tente novamente em instantes."
        />
      ) : data.length === 0 ? (
        <p className="rounded-md border border-linha bg-superficie px-3 py-3 text-xs text-tinta-media">
          Nenhuma conexão registrada ainda.
        </p>
      ) : (
        <div className="overflow-x-auto overflow-y-hidden rounded-md border border-linha">
          <table className="w-full text-sm">
            <thead className="bg-superficie text-xs text-tinta-media">
              <tr>
                <th className="px-3 py-2 text-left font-medium">Número</th>
                <th className="px-3 py-2 text-left font-medium">Conectado em</th>
                <th className="px-3 py-2 text-left font-medium">Desconectado em</th>
                <th className="hidden px-3 py-2 text-left font-medium md:table-cell">Motivo</th>
              </tr>
            </thead>
            <tbody>
              {data.map((conexao, indice) => (
                <tr key={`${conexao.conectadoEm}-${indice}`} className="border-t border-linha">
                  <td className="px-3 py-2">
                    <span className="font-dado text-tinta">
                      {conexao.numero ? formatarTelefone(conexao.numero) : '—'}
                    </span>
                    {conexao.perfilNome ? (
                      <span className="ml-2 text-xs text-tinta-media">{conexao.perfilNome}</span>
                    ) : null}
                  </td>
                  <td className="px-3 py-2 font-dado text-xs text-tinta-media">
                    {formatarDataHora(conexao.conectadoEm)}
                  </td>
                  <td className="px-3 py-2 font-dado text-xs text-tinta-media">
                    {conexao.desconectadoEm ? formatarDataHora(conexao.desconectadoEm) : 'em curso'}
                  </td>
                  <td className="hidden px-3 py-2 text-xs text-tinta-media md:table-cell">
                    {ROTULO_DO_MOTIVO[conexao.motivo]}
                    {conexao.atendenteNome ? ` · ${conexao.atendenteNome}` : ''}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Painel>
  )
}
