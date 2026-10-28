import { fireEvent, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { renderizarComProvedores } from '@/test/utils'
import { DialogoNovaConversa } from './dialogo-nova-conversa'
import { conversasApi } from '@/lib/api/endpoints'

vi.mock('@/lib/api/endpoints', () => ({
  clientesApi: {
    listar: vi.fn().mockResolvedValue({
      itens: [{ id: 'cli-1', nome: 'Cliente Teste', telefoneWhatsapp: '5538999990000' }],
      total: 1,
    }),
    criar: vi.fn(),
  },
  conversasApi: {
    criar: vi.fn().mockResolvedValue({ id: 'conv-1', protocolo: '2026-000001' }),
    enviarMensagem: vi.fn().mockResolvedValue({}),
  },
  setoresApi: { listar: vi.fn().mockResolvedValue([]) },
  etapasApi: { listar: vi.fn().mockResolvedValue([]) },
}))

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

  /**
   * Pedido do Thiago em 2026-10-28: o texto do diálogo é a primeira mensagem
   * DO ATENDENTE para o cliente — sai pelo envio do fio, nunca gravado como
   * mensagem do cliente.
   */
  it('a primeira mensagem sai como mensagem do atendente, depois de abrir', async () => {
    renderizarComProvedores(
      <DialogoNovaConversa aoCriar={vi.fn()} aberto aoMudarAberto={vi.fn()} />,
    )

    fireEvent.click(await screen.findByText('Cliente Teste'))
    fireEvent.change(screen.getByLabelText(/Primeira mensagem para o cliente/), {
      target: { value: 'Bom dia, aqui é do cartório' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Abrir atendimento' }))

    await waitFor(() =>
      expect(conversasApi.enviarMensagem).toHaveBeenCalledWith('conv-1', 'Bom dia, aqui é do cartório'),
    )
    expect(vi.mocked(conversasApi.criar).mock.calls[0][0]).not.toHaveProperty('primeiraMensagem')
  })
})
