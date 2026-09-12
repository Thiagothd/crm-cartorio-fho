import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { acessoTecnicoApi } from '@/lib/api/endpoints'
import type { FiltroDeAcessoTecnico, SolicitarAcessoTecnico } from '@/lib/api/tipos'

const CHAVE = ['seguranca', 'acesso-tecnico'] as const

function usarInvalidarAcessoTecnico() {
  const clienteDeConsultas = useQueryClient()
  return () => void clienteDeConsultas.invalidateQueries({ queryKey: CHAVE })
}

export function usarAcessoTecnicoAtivo() {
  return useQuery({
    queryKey: [...CHAVE, 'ativa'],
    queryFn: () => acessoTecnicoApi.ativa(),
    refetchInterval: 60_000,
  })
}

export function usarSolicitacoesDeAcessoTecnico(filtro: FiltroDeAcessoTecnico) {
  return useQuery({
    queryKey: [...CHAVE, 'lista', filtro.de, filtro.ate, filtro.pagina],
    queryFn: () => acessoTecnicoApi.listar(filtro),
  })
}

export function usarSolicitarAcessoTecnico() {
  const invalidar = usarInvalidarAcessoTecnico()
  return useMutation({
    mutationFn: (dados: SolicitarAcessoTecnico) => acessoTecnicoApi.solicitar(dados),
    onSuccess: invalidar,
  })
}

export function usarAprovarAcessoTecnico() {
  const invalidar = usarInvalidarAcessoTecnico()
  return useMutation({
    mutationFn: ({ id, codigoTotp }: { id: string; codigoTotp: string }) => acessoTecnicoApi.aprovar(id, codigoTotp),
    onSuccess: invalidar,
  })
}

export function usarNegarAcessoTecnico() {
  const invalidar = usarInvalidarAcessoTecnico()
  return useMutation({
    mutationFn: (id: string) => acessoTecnicoApi.negar(id),
    onSuccess: invalidar,
  })
}

export function usarRevogarAcessoTecnico() {
  const invalidar = usarInvalidarAcessoTecnico()
  return useMutation({
    mutationFn: (id: string) => acessoTecnicoApi.revogar(id),
    onSuccess: invalidar,
  })
}
