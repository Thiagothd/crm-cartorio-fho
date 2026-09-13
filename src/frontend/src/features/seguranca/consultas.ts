import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { segurancaApi } from '@/lib/api/endpoints'
import type { FiltroDeAcessos, FiltroDeEventos } from '@/lib/api/tipos'

export const chavesDeSeguranca = {
  acessos: (filtro: FiltroDeAcessos) =>
    ['seguranca', 'acessos', filtro.de, filtro.ate, filtro.atendenteId, filtro.conversaId, filtro.pagina] as const,
  eventos: (filtro: FiltroDeEventos) => ['seguranca', 'eventos', filtro.de, filtro.ate, filtro.pagina] as const,
  protecao: () => ['seguranca', 'protecao'] as const,
}

/**
 * `keepPreviousData`: trocar de página ou de período mantém a tabela anterior
 * na tela em vez de piscar para o skeleton — mesmo raciocínio de
 * `features/indicadores/consultas.ts`.
 */
const OPCOES_DA_TRILHA = {
  staleTime: 30_000,
  placeholderData: keepPreviousData,
} as const

export function usarRegistrosDeAcesso(filtro: FiltroDeAcessos) {
  return useQuery({
    queryKey: chavesDeSeguranca.acessos(filtro),
    queryFn: () => segurancaApi.acessos(filtro),
    ...OPCOES_DA_TRILHA,
  })
}

export function usarEventosDeSeguranca(filtro: FiltroDeEventos) {
  return useQuery({
    queryKey: chavesDeSeguranca.eventos(filtro),
    queryFn: () => segurancaApi.eventos(filtro),
    ...OPCOES_DA_TRILHA,
  })
}

/** `staleTime` mais curto: é o resumo "o que já está protegido" — vale a pena refletir mudança rápido. */
export function usarProtecaoAtiva() {
  return useQuery({
    queryKey: chavesDeSeguranca.protecao(),
    queryFn: () => segurancaApi.protecao(),
    staleTime: 15_000,
  })
}
