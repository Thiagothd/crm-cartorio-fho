import { useQuery } from '@tanstack/react-query'
import { iaApi } from '@/lib/api/endpoints'

export const chavesDaIa = {
  situacao: (de?: string, ate?: string) => ['ia', 'situacao', de ?? 'hoje', ate ?? 'hoje'] as const,
}

/**
 * `refetchInterval` de 30s: é um painel operacional (estado de disponibilidade
 * dos dois provedores), não uma agregação de período — mesmo padrão de
 * `painel-de-conexao.tsx` (canal WhatsApp). `de`/`ate` omitidos deixam o
 * backend cair no recorte de "hoje", que é a visão padrão desta tela.
 */
export function usarSituacaoDaIa(de?: string, ate?: string) {
  return useQuery({
    queryKey: chavesDaIa.situacao(de, ate),
    queryFn: () => iaApi.situacao({ de, ate }),
    staleTime: 20_000,
    refetchInterval: 30_000,
  })
}
