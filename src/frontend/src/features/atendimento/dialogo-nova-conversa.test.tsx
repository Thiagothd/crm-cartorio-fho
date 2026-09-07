import { screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { renderizarComProvedores } from '@/test/utils'
import { DialogoNovaConversa } from './dialogo-nova-conversa'

/**
 * Regressão da mudança para "opcionalmente controlado" (ver Visão Geral, que
 * passa a montar este diálogo fechado e o abre por um cartão externo). O uso
 * histórico — a inbox, sem `aberto`/`aoMudarAberto` — não pode mudar.
 */
describe('DialogoNovaConversa', () => {
  it('sem `aberto`, continua desenhando o próprio botão gatilho (uso da inbox)', () => {
    renderizarComProvedores(<DialogoNovaConversa aoCriar={vi.fn()} />)

    expect(screen.getByRole('button', { name: /Novo atendimento/ })).toBeInTheDocument()
  })

  it('com `aberto={false}`, não desenha gatilho nenhum', () => {
    renderizarComProvedores(
      <DialogoNovaConversa aoCriar={vi.fn()} aberto={false} aoMudarAberto={vi.fn()} />,
    )

    expect(screen.queryByRole('button', { name: /Novo atendimento/ })).not.toBeInTheDocument()
  })

  it('com `aberto={true}`, o diálogo já aparece aberto', () => {
    renderizarComProvedores(
      <DialogoNovaConversa aoCriar={vi.fn()} aberto aoMudarAberto={vi.fn()} />,
    )

    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(screen.getByText('Novo atendimento')).toBeInTheDocument()
  })
})
