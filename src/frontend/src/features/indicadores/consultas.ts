import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { indicadoresApi } from '@/lib/api/endpoints'

export const chavesDeIndicadores = {
  visaoGeral: (de: string, ate: string, escopo: string) => ['indicadores', 'visao-geral', de, ate, escopo] as const,
  relatorio: (de: string, ate: string) => ['indicadores', 'relatorio', de, ate] as const,
  alertas: () => ['indicadores', 'alertas'] as const,
  painel: () => ['indicadores', 'painel'] as const,
  atendimentos: (de: string, ate: string) => ['indicadores', 'atendimentos', de, ate] as const,
  atendentes: (de: string, ate: string) => ['indicadores', 'atendentes', de, ate] as const,
  clientes: (de: string, ate: string) => ['indicadores', 'clientes', de, ate] as const,
}

/**
 * `keepPreviousData`: trocar de período (ou de escopo) mantém os números
 * anteriores na tela enquanto os novos vêm, em vez de derrubar tudo para o
 * skeleton. A percepção é de resposta imediata — quem clicou em "30 dias" vê
 * a tela inteira continuar de pé, só o discreto "Atualizando…" indica o
 * trabalho em curso. Sem isto, cada clique de filtro pisca a tela toda.
 *
 * `staleTime` de 2 minutos, não 30s: o dado é agregação de período, não fio de
 * conversa. Combinado com a `queryKey` estável (ver `resolverIntervalo` em
 * `periodo.ts`), voltar para a tela dentro da janela mostra na hora, sem
 * requisição nenhuma. Os eventos SignalR continuam invalidando quando algo
 * muda de verdade — o `staleTime` não atrasa fato novo, só evita refetch
 * gratuito por navegação.
 */
const OPCOES_DE_PERIODO = {
  staleTime: 120_000,
  gcTime: 600_000,
  placeholderData: keepPreviousData,
} as const

export function usarVisaoGeral(de: string, ate: string, escopo: 'todas' | 'minhas') {
  return useQuery({
    queryKey: chavesDeIndicadores.visaoGeral(de, ate, escopo),
    queryFn: () => indicadoresApi.visaoGeral({ de, ate, escopo }),
    ...OPCOES_DE_PERIODO,
  })
}

export function usarRelatorio(de: string, ate: string) {
  return useQuery({
    queryKey: chavesDeIndicadores.relatorio(de, ate),
    queryFn: () => indicadoresApi.relatorio({ de, ate }),
    ...OPCOES_DE_PERIODO,
  })
}

// -------------------------------- Abas da tela de Indicadores e Relatórios
// Um hook por aba, cada um com `enabled`: a aba que não está na tela não
// dispara requisição, e a que já foi visitada continua em cache ao voltar.

export function usarPainel(habilitado: boolean) {
  return useQuery({
    queryKey: chavesDeIndicadores.painel(),
    queryFn: () => indicadoresApi.painel(),
    enabled: habilitado,
    ...OPCOES_DE_PERIODO,
  })
}

export function usarRelatorioDeAtendimentos(de: string, ate: string, habilitado: boolean) {
  return useQuery({
    queryKey: chavesDeIndicadores.atendimentos(de, ate),
    queryFn: () => indicadoresApi.relatorioDeAtendimentos({ de, ate }),
    enabled: habilitado,
    ...OPCOES_DE_PERIODO,
  })
}

export function usarRelatorioDeAtendentes(de: string, ate: string, habilitado: boolean) {
  return useQuery({
    queryKey: chavesDeIndicadores.atendentes(de, ate),
    queryFn: () => indicadoresApi.relatorioDeAtendentes({ de, ate }),
    enabled: habilitado,
    ...OPCOES_DE_PERIODO,
  })
}

export function usarRelatorioDeClientes(de: string, ate: string, habilitado: boolean) {
  return useQuery({
    queryKey: chavesDeIndicadores.clientes(de, ate),
    queryFn: () => indicadoresApi.relatorioDeClientes({ de, ate }),
    enabled: habilitado,
    ...OPCOES_DE_PERIODO,
  })
}

/**
 * Piso de 120s: a varredura de prazo no servidor roda a cada 5 minutos
 * (`Automacao:IntervaloEmMinutos`), então checar mais rápido que isso só
 * repetiria a mesma resposta. Os eventos SignalR (ver
 * `usar-hub-de-atendimento.ts`) continuam invalidando na hora quando algo
 * muda de verdade.
 */
export function usarAlertasOperacionais(habilitado: boolean) {
  return useQuery({
    queryKey: chavesDeIndicadores.alertas(),
    queryFn: () => indicadoresApi.alertas(),
    // 30 s, e nao 2 min: o aviso de WhatsApp caido no cabecalho precisa aparecer
    // logo. O servidor guarda o estado do canal por 15 s, entao nao pesa.
    staleTime: 20_000,
    refetchInterval: 30_000,
    enabled: habilitado,
  })
}
