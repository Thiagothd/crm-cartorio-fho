import { useQuery } from '@tanstack/react-query'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Skeleton } from '@/components/ui/skeleton'
import { whatsappApi } from '@/lib/api/endpoints'
import { formatarDataHora, formatarTelefone } from '@/lib/formatacao'
import type { MotivoDoDescarteDoCanal } from '@/lib/api/tipos'

const ROTULO_DO_MOTIVO: Record<MotivoDoDescarteDoCanal, string> = {
  ForaDaLista: 'fora da lista de liberados',
  Bloqueado: 'número bloqueado',
}

/** Contagem e últimas mensagens que o filtro de números descartou. */
export function MensagensBloqueadas() {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['whatsapp-descartes'],
    queryFn: () => whatsappApi.descartes(),
  })

  return (
    <div className="flex flex-col gap-3 rounded-md border border-linha bg-branco p-4">
      <div>
        <h2 className="font-titulo text-base text-tinta">Mensagens bloqueadas</h2>
        <p className="mt-1 text-sm text-tinta-media">
          Quem tentou falar com o canal e foi descartado pela regra de números acima.
        </p>
      </div>

      {isLoading ? (
        <Skeleton className="h-24 w-full" />
      ) : isError || !data ? (
        <EstadoDaInterface
          tipo="erro"
          titulo="Não foi possível carregar"
          descricao="Tente novamente em instantes."
        />
      ) : (
        <>
          <p className="text-sm text-tinta">
            <span className="font-titulo text-2xl text-tinta">{data.totalHoje}</span>{' '}
            <span className="text-tinta-media">
              {data.totalHoje === 1 ? 'mensagem bloqueada' : 'mensagens bloqueadas'} nas últimas 24h
            </span>
          </p>

          {data.recentes.length === 0 ? (
            <p className="rounded-md border border-linha bg-superficie px-3 py-3 text-xs text-tinta-media">
              Nenhuma mensagem foi bloqueada nos últimos 7 dias.
            </p>
          ) : (
            <ul className="flex flex-col divide-y divide-linha overflow-hidden rounded-md border border-linha">
              {data.recentes.map((descarte, indice) => (
                <li
                  key={`${descarte.telefone}-${descarte.criadoEm}-${indice}`}
                  className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm"
                >
                  <span className="font-dado text-tinta">{formatarTelefone(descarte.telefone)}</span>
                  <span className="text-xs text-tinta-media">
                    {ROTULO_DO_MOTIVO[descarte.motivo]} · {formatarDataHora(descarte.criadoEm)}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </div>
  )
}
