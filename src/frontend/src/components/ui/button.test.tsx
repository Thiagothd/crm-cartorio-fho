import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Button } from '@/components/ui/button'

describe('Button', () => {
  it('mantém nome acessível e executa a ação', async () => {
    const usuario = userEvent.setup()
    const aoClicar = vi.fn()

    render(<Button onClick={aoClicar}>Salvar atendimento</Button>)
    await usuario.click(screen.getByRole('button', { name: 'Salvar atendimento' }))

    expect(aoClicar).toHaveBeenCalledOnce()
  })

  it('não executa a ação quando está desabilitado', async () => {
    const usuario = userEvent.setup()
    const aoClicar = vi.fn()

    render(
      <Button disabled onClick={aoClicar}>
        Salvar atendimento
      </Button>,
    )
    const botao = screen.getByRole('button', { name: 'Salvar atendimento' })
    await usuario.click(botao)

    expect(botao).toBeDisabled()
    expect(aoClicar).not.toHaveBeenCalled()
  })
})
