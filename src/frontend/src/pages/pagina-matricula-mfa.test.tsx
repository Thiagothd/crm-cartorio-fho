import { MemoryRouter } from 'react-router-dom'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PaginaMatriculaMfa } from '@/pages/pagina-matricula-mfa'
import fonteDaPagina from '@/pages/pagina-matricula-mfa.tsx?raw'

const iniciarMatriculaPrivilegiada = vi.fn()
const iniciarAutenticadorViaDesafio = vi.fn()
const confirmarViaDesafio = vi.fn()
const aplicarNovaSessao = vi.fn()
const navegar = vi.fn()

vi.mock('@/lib/api/endpoints', () => ({
  mfaApi: {
    iniciarMatriculaPrivilegiada: (email: string, senha: string, ticket: string) =>
      iniciarMatriculaPrivilegiada(email, senha, ticket),
    iniciarAutenticadorViaDesafio: (desafio: string) => iniciarAutenticadorViaDesafio(desafio),
    confirmarViaDesafio: (desafio: string, codigo: string) => confirmarViaDesafio(desafio, codigo),
  },
}))

vi.mock('@/lib/auth/usar-autenticacao', () => ({
  usarAutenticacao: () => ({ aplicarNovaSessao }),
}))

vi.mock('react-router-dom', async () => {
  const real = await vi.importActual<typeof import('react-router-dom')>('react-router-dom')
  return { ...real, useNavigate: () => navegar }
})

vi.mock('qrcode', () => ({ default: { toCanvas: vi.fn().mockResolvedValue(undefined) } }))

const sessaoNova = {
  token: 'jwt-com-tokenversion-novo',
  atendente: { id: 'a1', nome: 'Titular', email: 'titular@cartorio.com.br', perfil: 'Titular' },
  tenant: { id: 't1', nome: 'Cartório de Teste' },
}

describe('PaginaMatriculaMfa', () => {
  beforeEach(() => {
    aplicarNovaSessao.mockReset()
    navegar.mockReset()
    iniciarMatriculaPrivilegiada.mockReset().mockResolvedValue({ desafio: 'desafio-de-matricula' })
    iniciarAutenticadorViaDesafio
      .mockReset()
      .mockResolvedValue({ otpauthUri: 'otpauth://totp/x', segredoBase32: 'ABCDEF' })
    confirmarViaDesafio
      .mockReset()
      .mockResolvedValue({ sessao: sessaoNova, codigosDeRecuperacao: ['AAAA-BBBB', 'CCCC-DDDD'] })
  })

  /**
   * A versão antiga contornava o TokenVersion novo com
   * `window.location.assign('/')` — um reload forçado. A sessão agora entra pelo
   * fluxo central, e a navegação é do router.
   */
  // `window.location` não é redefinível em jsdom, então a regressão do reload
  // é travada na fonte: se `window.location.assign('/')` voltar, isto quebra.
  it('não recarrega a página para contornar o TokenVersion novo', () => {
    expect(fonteDaPagina).not.toContain('window.location')
  })

  it('aplica a sessão nova e navega pelo router', async () => {
    const usuario = userEvent.setup()

    render(
      <MemoryRouter>
        <PaginaMatriculaMfa />
      </MemoryRouter>,
    )

    await usuario.type(screen.getByLabelText('E-mail'), 'titular@cartorio.com.br')
    await usuario.type(screen.getByLabelText('Senha'), 'senha-do-titular')
    await usuario.type(screen.getByLabelText(/Ticket/i), 'ticket-fora-de-banda')
    await usuario.click(screen.getByRole('button', { name: 'Continuar' }))

    await usuario.type(await screen.findByLabelText(/Código de 6 dígitos/i), '123456')
    await usuario.click(screen.getByRole('button', { name: 'Confirmar e concluir' }))

    await screen.findByText('Matrícula concluída.')
    expect(aplicarNovaSessao).toHaveBeenCalledWith(sessaoNova)

    await usuario.click(screen.getByRole('button', { name: 'Já guardei — entrar no CRM' }))
    expect(navegar).toHaveBeenCalledWith('/', { replace: true })
  })
})
