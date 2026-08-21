import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'

describe('EstadoDaInterface', () => {
  it('anuncia carregamento sem depender apenas da animação', () => {
    render(<EstadoDaInterface tipo="carregando" />)

    const estado = screen.getByRole('status')
    expect(estado).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByText('Carregando')).toBeInTheDocument()
  })

  it('anuncia erro e oferece uma recuperação explícita', () => {
    render(
      <EstadoDaInterface
        tipo="erro"
        titulo="Falha ao buscar clientes"
        acao={<button type="button">Tentar novamente</button>}
      />,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('Falha ao buscar clientes')
    expect(screen.getByRole('button', { name: 'Tentar novamente' })).toBeInTheDocument()
  })
})
