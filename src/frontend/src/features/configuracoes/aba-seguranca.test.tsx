import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AbaSeguranca } from './aba-seguranca'

const situacao = vi.fn()
const iniciar = vi.fn()
const confirmar = vi.fn()
const esquecerDispositivosConfiaveis = vi.fn()
const aplicarNovaSessao = vi.fn()

vi.mock('@/lib/api/endpoints', () => ({
  mfaApi: {
    situacao: () => situacao(),
    iniciar: (senha: string, codigoAtual: string | null) => iniciar(senha, codigoAtual),
    confirmar: (codigo: string) => confirmar(codigo),
    esquecerDispositivosConfiaveis: () => esquecerDispositivosConfiaveis(),
  },
}))

vi.mock('@/lib/auth/usar-autenticacao', () => ({
  usarAutenticacao: () => ({ aplicarNovaSessao }),
}))

// O QR Code desenha em <canvas>, que jsdom não implementa.
vi.mock('qrcode', () => ({ default: { toCanvas: vi.fn().mockResolvedValue(undefined) } }))

const sessaoNova = {
  token: 'jwt-com-tokenversion-novo',
  atendente: { id: 'a1', nome: 'Ana', email: 'ana@cartorio.com.br', perfil: 'Administrador' },
  tenant: { id: 't1', nome: 'Cartório de Teste' },
}

function renderizar() {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={cliente}>
      <AbaSeguranca />
    </QueryClientProvider>,
  )
}

describe('AbaSeguranca', () => {
  beforeEach(() => {
    aplicarNovaSessao.mockReset()
    iniciar.mockReset().mockResolvedValue({ otpauthUri: 'otpauth://totp/x', segredoBase32: 'ABCDEF' })
    confirmar
      .mockReset()
      .mockResolvedValue({ sessao: sessaoNova, codigosDeRecuperacao: ['AAAA-BBBB', 'CCCC-DDDD'] })
    esquecerDispositivosConfiaveis.mockReset().mockResolvedValue(undefined)
    situacao.mockReset().mockResolvedValue({
      ativo: false,
      obrigatorio: false,
      codigosDeRecuperacaoRestantes: null,
      confirmadoEm: null,
      dispositivosConfiaveisAtivos: 0,
    })
  })

  /**
   * O bug da tela branca: confirmar o MFA sobe o `TokenVersion` no servidor, e o
   * JWT que este navegador ainda carrega morre nesse instante. A sessão nova vem
   * na própria resposta — se ela não for aplicada pelo fluxo central ANTES de a
   * tela seguir, a próxima chamada autenticada cai em 401.
   */
  it('aplica a sessão nova devolvida pela confirmação antes de mostrar os códigos', async () => {
    const usuario = userEvent.setup()
    renderizar()

    await usuario.click(await screen.findByRole('button', { name: 'Ativar' }))
    await usuario.type(screen.getByLabelText('Senha atual'), 'senha-atual')
    await usuario.click(screen.getByRole('button', { name: 'Continuar' }))

    await usuario.type(await screen.findByLabelText(/Código de 6 dígitos/i), '123456')
    await usuario.click(screen.getByRole('button', { name: 'Confirmar e ativar' }))

    await screen.findByText('Verificação em duas etapas ativada.')

    expect(aplicarNovaSessao).toHaveBeenCalledWith(sessaoNova)
    // Nada de reload: a sessão entra pelo mesmo caminho do login.
    expect(aplicarNovaSessao).toHaveBeenCalledTimes(1)
    expect(screen.getByText('AAAA-BBBB')).toBeInTheDocument()
  })

  it('a troca de autenticador usa o mesmo caminho e também aplica a sessão nova', async () => {
    situacao.mockResolvedValue({
      ativo: true,
      obrigatorio: false,
      codigosDeRecuperacaoRestantes: 8,
      confirmadoEm: '2026-08-30T12:00:00.000Z',
      dispositivosConfiaveisAtivos: 0,
    })

    const usuario = userEvent.setup()
    renderizar()

    await usuario.click(await screen.findByRole('button', { name: 'Trocar autenticador' }))
    await usuario.type(screen.getByLabelText('Senha atual'), 'senha-atual')
    await usuario.type(screen.getByLabelText('Código do autenticador atual'), '111111')
    await usuario.click(screen.getByRole('button', { name: 'Continuar' }))

    await usuario.type(await screen.findByLabelText(/Código de 6 dígitos/i), '222222')
    await usuario.click(screen.getByRole('button', { name: 'Confirmar e ativar' }))

    await screen.findByText('Verificação em duas etapas ativada.')
    expect(aplicarNovaSessao).toHaveBeenCalledWith(sessaoNova)
  })

  it('não oferece esquecer dispositivos quando não há nenhum confiável', async () => {
    situacao.mockResolvedValue({
      ativo: true,
      obrigatorio: false,
      codigosDeRecuperacaoRestantes: 10,
      confirmadoEm: '2026-08-30T12:00:00.000Z',
      dispositivosConfiaveisAtivos: 0,
    })

    renderizar()
    await screen.findByText('Verificação em duas etapas ativa')
    expect(screen.queryByRole('button', { name: /Esquecer/i })).not.toBeInTheDocument()
  })

  it('revoga todos os dispositivos confiáveis quando existem', async () => {
    situacao.mockResolvedValue({
      ativo: true,
      obrigatorio: false,
      codigosDeRecuperacaoRestantes: 10,
      confirmadoEm: '2026-08-30T12:00:00.000Z',
      dispositivosConfiaveisAtivos: 3,
    })

    const usuario = userEvent.setup()
    renderizar()

    expect(await screen.findByText(/3 dispositivos confiáveis/i)).toBeInTheDocument()

    const confirmarDoNavegador = vi.spyOn(window, 'confirm').mockReturnValue(true)
    await usuario.click(screen.getByRole('button', { name: 'Esquecer dispositivos' }))
    confirmarDoNavegador.mockRestore()
    await waitFor(() => {
      expect(esquecerDispositivosConfiaveis).toHaveBeenCalledTimes(1)
    })
  })
})
