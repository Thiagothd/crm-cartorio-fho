import { useQuery, useQueryClient } from '@tanstack/react-query'
import { etapasApi, setoresApi } from '@/lib/api/endpoints'

/**
 * As duas telas de Estrutura da operação leem a listagem COMPLETA (com
 * inativos) — é tela de cadastro, e esconder o que está desativado tiraria
 * justamente o que se vem aqui reativar.
 *
 * A chave é a mesma que a página e a lista usam: quem monta a faixa de
 * indicadores e quem desenha as linhas pedem o mesmo dado, e o React Query
 * resolve isso numa requisição só.
 */
export const chavesDaEstrutura = {
  etapas: ['etapas-gestao'] as const,
  setores: ['setores-gestao'] as const,
}

export function usarEtapasDeGestao() {
  return useQuery({
    queryKey: chavesDaEstrutura.etapas,
    queryFn: () => etapasApi.listar(true),
  })
}

export function usarSetoresDeGestao() {
  return useQuery({
    queryKey: chavesDaEstrutura.setores,
    queryFn: () => setoresApi.listar(true),
  })
}

/**
 * Invalida a listagem de gestão E a operacional (`['etapas']`/`['setores']`,
 * só ativos) — o filtro da inbox e o seletor da conversa leem a segunda, e
 * renomear ou desativar aqui muda o que eles mostram.
 */
export function usarInvalidarEstrutura() {
  const clienteDeConsultas = useQueryClient()

  return (alvo: 'etapas' | 'setores') => {
    void clienteDeConsultas.invalidateQueries({ queryKey: chavesDaEstrutura[alvo] })
    void clienteDeConsultas.invalidateQueries({ queryKey: [alvo] })
  }
}
