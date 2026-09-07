import { act } from 'react'
import type { ReactNode } from 'react'
import { renderHook } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { usarVisaoGeralAoVivo } from './tempo-real'

/**
 * `usarHubDeAtendimento` é a única dependência externa desta lógica — mockado
 * para capturar os handlers que `tempo-real.ts` registra, sem precisar de uma
 * conexão SignalR de verdade nem do `ProvedorDoHubDeAtendimento`.
 */
let eventosCapturados: { onMensagemRecebida?: () => void } | null = null

vi.mock('@/lib/realtime/usar-hub-de-atendimento', () => ({
  usarHubDeAtendimento: (eventos: { onMensagemRecebida?: () => void }) => {
    eventosCapturados = eventos
  },
}))

describe('usarVisaoGeralAoVivo', () => {
  let clienteDeConsultas: QueryClient
  let invalidar: ReturnType<typeof vi.spyOn>

  beforeEach(() => {
    vi.useFakeTimers()
    eventosCapturados = null
    clienteDeConsultas = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    invalidar = vi.spyOn(clienteDeConsultas, 'invalidateQueries')
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  function envolvedor({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={clienteDeConsultas}>{children}</QueryClientProvider>
  }

  function dispararMensagens(quantidade: number) {
    for (let i = 0; i < quantidade; i++) {
      act(() => {
        eventosCapturados?.onMensagemRecebida?.()
      })
    }
  }

  it('vinte eventos em rajada viram UMA invalidação de cada consulta, não vinte', () => {
    renderHook(() => usarVisaoGeralAoVivo(true), { wrapper: envolvedor })

    dispararMensagens(20)

    act(() => {
      vi.advanceTimersByTime(3_000)
    })
    expect(invalidar).toHaveBeenCalledWith({ queryKey: ['resumo-painel'] })
    expect(invalidar).toHaveBeenCalledTimes(1)

    act(() => {
      vi.advanceTimersByTime(17_000)
    })
    expect(invalidar).toHaveBeenCalledWith({ queryKey: ['indicadores'] })
    expect(invalidar).toHaveBeenCalledTimes(2)
  })

  it('cartório parado não gera nenhuma requisição', () => {
    renderHook(() => usarVisaoGeralAoVivo(true), { wrapper: envolvedor })

    act(() => {
      vi.advanceTimersByTime(60_000)
    })

    expect(invalidar).not.toHaveBeenCalled()
  })

  it('aba oculta não descarrega a invalidação pendente', () => {
    renderHook(() => usarVisaoGeralAoVivo(true), { wrapper: envolvedor })

    Object.defineProperty(document, 'hidden', { value: true, configurable: true })
    dispararMensagens(1)

    act(() => {
      vi.advanceTimersByTime(3_000)
    })
    expect(invalidar).not.toHaveBeenCalled()

    // Volta a ficar visível: o disparo do evento descarrega o que estava pendente.
    Object.defineProperty(document, 'hidden', { value: false, configurable: true })
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'))
    })
    expect(invalidar).toHaveBeenCalledWith({ queryKey: ['resumo-painel'] })
  })
})
