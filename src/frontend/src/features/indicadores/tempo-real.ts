import { useEffect, useRef } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { usarHubDeAtendimento } from '@/lib/realtime/usar-hub-de-atendimento'
import { chaves } from '@/features/atendimento/consultas'

/** Contadores: 3s. Payload pequeno, e é o número que precisa parecer instantâneo. */
const JANELA_DOS_CONTADORES = 3_000
/** Agregação do período: 20s. Teto de 3 refetches/min do endpoint gordo, e só se houve evento. */
const JANELA_DA_AGREGACAO = 20_000

/**
 * Põe a Visão Geral ao vivo sem transformar cartório movimentado em rajada de
 * requisição.
 *
 * O gatilho é o hub, não um relógio: em cartório parado, ZERO requisição —
 * `refetchInterval` bateria a cada N segundos para receber a mesma resposta.
 * A proteção contra a rajada é o throttle TRAILING: vinte mensagens em dez
 * segundos viram UMA invalidação de cada consulta, não vinte.
 *
 * Aba oculta não dispara: o refetch seria descartado ao voltar de qualquer
 * jeito (o `staleTime` já cuida disso) e só gastaria banda de quem deixou o
 * painel aberto numa aba de fundo. Ao voltar para a aba, o que ficou pendente
 * dispara na hora.
 */
export function usarVisaoGeralAoVivo(habilitado: boolean) {
  const clienteDeConsultas = useQueryClient()
  const pendentes = useRef({ contadores: false, agregacao: false })
  const agendados = useRef<{ contadores?: number; agregacao?: number }>({})

  useEffect(() => {
    function descarregar() {
      if (document.hidden) return

      if (pendentes.current.contadores) {
        pendentes.current.contadores = false
        void clienteDeConsultas.invalidateQueries({ queryKey: chaves.resumo() })
      }
      if (pendentes.current.agregacao) {
        pendentes.current.agregacao = false
        void clienteDeConsultas.invalidateQueries({ queryKey: ['indicadores'] })
      }
    }

    document.addEventListener('visibilitychange', descarregar)
    return () => document.removeEventListener('visibilitychange', descarregar)
  }, [clienteDeConsultas])

  function marcar(qual: 'contadores' | 'agregacao', janela: number, chave: readonly unknown[]) {
    pendentes.current[qual] = true
    if (agendados.current[qual]) return

    agendados.current[qual] = window.setTimeout(() => {
      agendados.current[qual] = undefined
      if (document.hidden || !pendentes.current[qual]) return
      pendentes.current[qual] = false
      void clienteDeConsultas.invalidateQueries({ queryKey: chave })
    }, janela)
  }

  function aoMudarAlgo() {
    marcar('contadores', JANELA_DOS_CONTADORES, chaves.resumo())
    marcar('agregacao', JANELA_DA_AGREGACAO, ['indicadores'])
  }

  usarHubDeAtendimento(
    {
      onMensagemRecebida: aoMudarAlgo,
      onConversaAtribuida: aoMudarAlgo,
      onConversaLiberada: aoMudarAlgo,
      onConversaTransferida: aoMudarAlgo,
      onConversaAtualizada: aoMudarAlgo,
      onConversaEncerrada: aoMudarAlgo,
      onConversaReaberta: aoMudarAlgo,
    },
    habilitado,
  )

  useEffect(
    () => () => {
      window.clearTimeout(agendados.current.contadores)
      window.clearTimeout(agendados.current.agregacao)
    },
    [],
  )
}
