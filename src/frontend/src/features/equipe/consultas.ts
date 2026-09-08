import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { atendentesApi } from '@/lib/api/endpoints'
import type { CargoNaServentia, ModeloDePermissao, PerfilDeAtendente } from '@/lib/api/tipos'

export const chavesDaEquipe = {
  lista: (incluirInativos: boolean) => ['equipe', 'lista', incluirInativos] as const,
  atendente: (id: string) => ['equipe', 'atendente', id] as const,
  permissoes: (id: string) => ['equipe', 'permissoes', id] as const,
}

export function usarAtendentes(incluirInativos: boolean) {
  return useQuery({
    queryKey: chavesDaEquipe.lista(incluirInativos),
    queryFn: () => atendentesApi.listar(incluirInativos),
  })
}

export function usarAtendente(id: string) {
  return useQuery({
    queryKey: chavesDaEquipe.atendente(id),
    queryFn: () => atendentesApi.obter(id),
  })
}

export function usarPermissoesDoAtendente(id: string | null) {
  return useQuery({
    queryKey: chavesDaEquipe.permissoes(id ?? ''),
    queryFn: () => atendentesApi.obterPermissoes(id!),
    enabled: id !== null,
  })
}

/**
 * Invalida as DUAS listagens de atendente — `equipe` (esta tela, sempre com
 * inativos) e `atendentes` (o seletor de "transferir para..." do balcão,
 * `ListarOpcoesAsync`, só ativos). Uma mudança aqui (cadastro, edição,
 * desativação) pode afetar as duas.
 */
function usarInvalidarEquipe() {
  const clienteDeConsultas = useQueryClient()
  return () => {
    void clienteDeConsultas.invalidateQueries({ queryKey: ['equipe'] })
    void clienteDeConsultas.invalidateQueries({ queryKey: ['atendentes'] })
  }
}

export function usarCriarAtendente() {
  const invalidar = usarInvalidarEquipe()
  return useMutation({
    mutationFn: (dados: {
      nome: string
      email: string
      senha: string
      perfil: PerfilDeAtendente
      cargo: CargoNaServentia
      setorPadraoId?: string | null
    }) => atendentesApi.criar(dados),
    onSuccess: invalidar,
  })
}

export function usarAtualizarAtendente() {
  const invalidar = usarInvalidarEquipe()
  return useMutation({
    mutationFn: ({
      id,
      ...dados
    }: {
      id: string
      nome: string
      email: string
      perfil: PerfilDeAtendente
      cargo: CargoNaServentia
      setorPadraoId?: string | null
      ativo: boolean
    }) => atendentesApi.atualizar(id, dados),
    onSuccess: invalidar,
  })
}

export function usarRedefinirSenha() {
  return useMutation({
    mutationFn: ({ id, novaSenha }: { id: string; novaSenha: string }) =>
      atendentesApi.redefinirSenha(id, novaSenha),
  })
}

export function usarExcluirAtendente() {
  const invalidar = usarInvalidarEquipe()
  return useMutation({
    mutationFn: (id: string) => atendentesApi.excluir(id),
    onSuccess: invalidar,
  })
}

export function usarDefinirPermissoes() {
  const clienteDeConsultas = useQueryClient()
  return useMutation({
    mutationFn: ({
      id,
      modelo,
      chaves,
    }: {
      id: string
      modelo: ModeloDePermissao | null
      chaves: string[]
    }) => atendentesApi.definirPermissoes(id, { modelo, chaves }),
    onSuccess: (_dados, variaveis) => {
      void clienteDeConsultas.invalidateQueries({ queryKey: chavesDaEquipe.permissoes(variaveis.id) })
      void clienteDeConsultas.invalidateQueries({ queryKey: ['equipe'] })
    },
  })
}
