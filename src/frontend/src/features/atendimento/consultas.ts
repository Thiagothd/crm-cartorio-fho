import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  conversasApi,
  atendentesApi,
  etapasApi,
  respostasRapidasApi,
  setoresApi,
  servicosApi,
} from '@/lib/api/endpoints'
import type { Conversa, FiltroDeConversas } from '@/lib/api/tipos'

export const chaves = {
  conversas: (filtro: FiltroDeConversas) => ['conversas', filtro] as const,
  conversa: (id: string) => ['conversa', id] as const,
  resumo: () => ['resumo-painel'] as const,
  setores: () => ['setores'] as const,
  etapas: () => ['etapas'] as const,
  servicos: () => ['servicos'] as const,
  atendentes: () => ['atendentes'] as const,
  respostasRapidas: () => ['respostas-rapidas'] as const,
}

// Cadastros usados o tempo todo pelos seletores da inbox. Mudam pouco, entao
// ficam frescos por bastante tempo.
const TEMPO_DE_CADASTRO = 5 * 60_000

export function usarSetores() {
  return useQuery({
    queryKey: chaves.setores(),
    queryFn: () => setoresApi.listar(),
    staleTime: TEMPO_DE_CADASTRO,
  })
}

export function usarEtapas() {
  return useQuery({
    queryKey: chaves.etapas(),
    queryFn: () => etapasApi.listar(),
    staleTime: TEMPO_DE_CADASTRO,
  })
}

export function usarServicos() {
  return useQuery({
    queryKey: chaves.servicos(),
    queryFn: () => servicosApi.listar(),
    staleTime: TEMPO_DE_CADASTRO,
  })
}

export function usarAtendentes() {
  return useQuery({
    queryKey: chaves.atendentes(),
    queryFn: () => atendentesApi.listar(),
    staleTime: TEMPO_DE_CADASTRO,
  })
}

/**
 * Só as ativas: o seletor "/" oferece o que pode ser enviado hoje. Resposta
 * desativada continua no cadastro, mas não aparece para quem está atendendo.
 */
export function usarRespostasRapidas() {
  return useQuery({
    queryKey: chaves.respostasRapidas(),
    queryFn: () => respostasRapidasApi.listar(),
    staleTime: TEMPO_DE_CADASTRO,
  })
}

export function usarConversas(filtro: FiltroDeConversas) {
  return useQuery({
    queryKey: chaves.conversas(filtro),
    queryFn: () => conversasApi.listar(filtro),
  })
}

export function usarConversa(id: string | null) {
  return useQuery({
    queryKey: chaves.conversa(id ?? ''),
    queryFn: () => conversasApi.obter(id!),
    enabled: Boolean(id),
  })
}

export function usarResumoDoPainel() {
  return useQuery({
    queryKey: chaves.resumo(),
    queryFn: () => conversasApi.resumo(),
  })
}

/**
 * Invalida tudo que uma acao sobre conversa pode ter mudado: a lista, o detalhe
 * e os contadores do painel. Centralizado para nao esquecer um deles.
 */
export function usarInvalidarAtendimento() {
  const cliente = useQueryClient()

  return (conversaId?: string) => {
    void cliente.invalidateQueries({ queryKey: ['conversas'] })
    void cliente.invalidateQueries({ queryKey: chaves.resumo() })

    if (conversaId) {
      void cliente.invalidateQueries({ queryKey: chaves.conversa(conversaId) })
    }
  }
}

type AcaoDeConversa = (id: string) => Promise<Conversa>

/** Mutation padrao das acoes de conversa: executa, invalida e devolve. */
export function usarAcaoDeConversa(acao: AcaoDeConversa) {
  const invalidar = usarInvalidarAtendimento()

  return useMutation({
    mutationFn: acao,
    onSuccess: (conversa) => invalidar(conversa.id),
  })
}
