import { useQuery } from '@tanstack/react-query'
import { iaApi } from '@/lib/api/endpoints'

export const chavesDaIa = {
  situacao: (de?: string, ate?: string) => ['ia', 'situacao', de ?? 'hoje', ate ?? 'hoje'] as const,
  analise: (de?: string, ate?: string) => ['ia', 'analise', de ?? '7dias', ate ?? '7dias'] as const,
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

/**
 * A tela do gestor (`/automacao/ia`). Diferente de `usarSituacaoDaIa`, é uma
 * agregação de período — sem `refetchInterval`: o usuário troca o período
 * pelo `SeletorDePeriodo` (mesmo padrão da Visão Geral), não por polling.
 */
export function usarAnaliseDaIa(de: string, ate: string) {
  return useQuery({
    queryKey: chavesDaIa.analise(de, ate),
    queryFn: () => iaApi.analise({ de, ate }),
    staleTime: 30_000,
  })
}
