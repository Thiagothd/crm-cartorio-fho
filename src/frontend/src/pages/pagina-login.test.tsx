import { MemoryRouter } from 'react-router-dom'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PaginaLogin } from '@/pages/pagina-login'

const entrar = vi.fn()

vi.mock('@/lib/auth/usar-autenticacao', () => ({
  usarAutenticacao: () => ({
    atendente: null,
    carregando: false,
    entrar,
  }),
}))

function renderizar() {
  return render(
    <MemoryRouter>
      <PaginaLogin />
    </MemoryRouter>,
  )
}

describe('PaginaLogin', () => {
  beforeEach(() => {
    entrar.mockReset().mockResolvedValue(undefined)
  })

  it('apresenta a marca e o acesso sem o bloco de serventia', () => {
    renderizar()

    expect(screen.getAllByAltText('CRM Cartório')).not.toHaveLength(0)
    // Título VISÍVEL desde o redesenho de 2026-08-21 — antes era `sr-only`, e
    // uma tela cujo único h1 é invisível é mais fraca de ler e de navegar.
    expect(screen.getByRole('heading', { name: 'Entrar no CRM' })).not.toHaveClass('sr-only')
    expect(screen.queryByText(/acesso restrito/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/^Ambiente da serventia$/i)).not.toBeInTheDocument()
    expect(screen.queryByText('Cartório de Teste')).not.toBeInTheDocument()
  })

  it('coloca a marca e o formulário no bloco sobre a fotografia', () => {
    const { container } = renderizar()

    const tela = container.querySelector('.login-tela')
    const bloco = screen.getByRole('region', { name: 'Entrar no CRM' })

    expect(tela).toContainElement(bloco)
    expect(bloco).toContainElement(screen.getByAltText('CRM Cartório'))
    expect(screen.getByText(/continuar os atendimentos/i)).toBeInTheDocument()
    expect(screen.queryByText('Gestão de atendimento')).not.toBeInTheDocument()
    expect(screen.queryByText(/A rotina da serventia/i)).not.toBeInTheDocument()
  })

  it('permite conferir a senha e mantém o envio pelo formulário', async () => {
    const usuario = userEvent.setup()
    renderizar()

    const email = screen.getByLabelText('E-mail')
    const senha = screen.getByLabelText('Senha')

    expect(senha).toHaveAttribute('type', 'password')
    await usuario.click(screen.getByRole('button', { name: 'Mostrar senha' }))
    expect(senha).toHaveAttribute('type', 'text')

    await usuario.type(email, 'atendente@cartorio.com.br')
    await usuario.type(senha, 'senha-segura')
    await usuario.click(screen.getByRole('button', { name: 'Entrar' }))

    await waitFor(() => {
      expect(entrar).toHaveBeenCalledWith('atendente@cartorio.com.br', 'senha-segura')
    })
  })
})
