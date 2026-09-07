import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { renderizarComProvedores } from '@/test/utils'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { usarBuscaGlobal } from '@/components/layout/contexto-de-busca-global'
import { AcoesRapidas, type ContadoresDeAcoes } from './acoes-rapidas'

vi.mock('@/lib/auth/usar-autenticacao', () => ({
  usarAutenticacao: vi.fn(),
}))

vi.mock('@/components/layout/contexto-de-busca-global', () => ({
  usarBuscaGlobal: vi.fn(),
}))

const autenticacaoMock = vi.mocked(usarAutenticacao)
const buscaGlobalMock = vi.mocked(usarBuscaGlobal)

const CONTADORES_VAZIOS: ContadoresDeAcoes = { fila: null, aguardando: null, minhas: null }

/**
 * `gestao` cobre Respostas rápidas + Indicadores (`automacao.respostas.ver`,
 * `relatorios.ver`); `administrador` acrescenta o Canal WhatsApp
 * (`whatsapp.ver`) — mesma progressão de antes, em chave de permissão.
 */
function perfil(gestao: boolean, administrador: boolean) {
  const chaves = [
    ...(gestao ? ['automacao.respostas.ver', 'relatorios.ver'] : []),
    ...(administrador ? ['whatsapp.ver'] : []),
  ]
  return { pode: (chave: string) => chaves.includes(chave) } as ReturnType<typeof usarAutenticacao>
}

function renderizar(props: Partial<Parameters<typeof AcoesRapidas>[0]> = {}) {
  return renderizarComProvedores(
    <AcoesRapidas
      contadores={CONTADORES_VAZIOS}
      aoAbrirNovoAtendimento={vi.fn()}
      aoAbrirNovoCliente={vi.fn()}
      {...props}
    />,
  )
}

describe('AcoesRapidas', () => {
  it('esconde as pílulas de gestão para o perfil Atendente', () => {
    autenticacaoMock.mockReturnValue(perfil(false, false))
    buscaGlobalMock.mockReturnValue(null)

    renderizar()

    expect(screen.queryByRole('link', { name: 'Respostas rápidas' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Indicadores' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Canal WhatsApp' })).not.toBeInTheDocument()
    expect(screen.queryByText('Gestão')).not.toBeInTheDocument()
  })

  it('mostra Respostas rápidas e Indicadores para Supervisor, mas não Canal WhatsApp', () => {
    autenticacaoMock.mockReturnValue(perfil(true, false))
    buscaGlobalMock.mockReturnValue(null)

    renderizar()

    expect(screen.getByRole('link', { name: 'Respostas rápidas' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Indicadores' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Canal WhatsApp' })).not.toBeInTheDocument()
  })

  it('mostra as três pílulas de gestão para Administrador', () => {
    autenticacaoMock.mockReturnValue(perfil(true, true))
    buscaGlobalMock.mockReturnValue(null)

    renderizar()

    expect(screen.getByRole('link', { name: 'Respostas rápidas' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Indicadores' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Canal WhatsApp' })).toBeInTheDocument()
  })

  it('a grade principal tem sempre 1 destaque + 4 cards, em qualquer perfil', () => {
    autenticacaoMock.mockReturnValue(perfil(true, true))
    buscaGlobalMock.mockReturnValue({ aberta: false, abrir: vi.fn(), definirAberta: vi.fn() })

    const { container: comAdmin } = renderizar()
    const gradeAdmin = comAdmin.querySelector('.grid.grid-cols-2')!
    expect(gradeAdmin.children).toHaveLength(5)

    autenticacaoMock.mockReturnValue(perfil(false, false))
    const { container: comAtendente } = renderizar()
    const gradeAtendente = comAtendente.querySelector('.grid.grid-cols-2')!
    expect(gradeAtendente.children).toHaveLength(5)
  })

  it('contador undefined mostra esqueleto e aria-busy, sem travar a navegação', () => {
    autenticacaoMock.mockReturnValue(perfil(false, false))
    buscaGlobalMock.mockReturnValue(null)

    renderizar({ contadores: { fila: undefined, aguardando: null, minhas: null } })

    const link = screen.getByRole('link', { name: 'Fila sem responsável' })
    expect(link).toHaveAttribute('aria-busy', 'true')
    expect(link).toHaveAttribute('href', '/atendimentos?escopo=fila')
  })

  it('contador zero continua no card, sem acento de cor', () => {
    autenticacaoMock.mockReturnValue(perfil(false, false))
    buscaGlobalMock.mockReturnValue(null)

    renderizar({ contadores: { fila: 0, aguardando: null, minhas: null } })

    const link = screen.getByRole('link', { name: 'Fila sem responsável: 0' })
    const numero = link.querySelector('.font-dado')!
    expect(numero).toHaveTextContent('0')
    expect(numero).not.toHaveClass('text-ambar')
    expect(numero).not.toHaveClass('text-selo')
  })

  it('contador maior que zero ganha o tom configurado', () => {
    autenticacaoMock.mockReturnValue(perfil(false, false))
    buscaGlobalMock.mockReturnValue(null)

    renderizar({ contadores: { fila: null, aguardando: 4, minhas: null } })

    const link = screen.getByRole('link', { name: 'Aguardando resposta: 4' })
    expect(link.querySelector('.font-dado')).toHaveClass('text-ambar')
  })

  it('contador indisponível (null) mostra travessão e continua navegando', () => {
    autenticacaoMock.mockReturnValue(perfil(false, false))
    buscaGlobalMock.mockReturnValue(null)

    renderizar({ contadores: { fila: null, aguardando: null, minhas: null } })

    const link = screen.getByRole('link', { name: 'Fila sem responsável' })
    expect(link).toHaveTextContent('—')
  })

  it('os cards de trabalho levam para os destinos certos', () => {
    autenticacaoMock.mockReturnValue(perfil(false, false))
    buscaGlobalMock.mockReturnValue(null)

    renderizar()

    expect(screen.getByRole('link', { name: 'Fila sem responsável' })).toHaveAttribute(
      'href',
      '/atendimentos?escopo=fila',
    )
    expect(screen.getByRole('link', { name: 'Aguardando resposta' })).toHaveAttribute(
      'href',
      '/atendimentos?aguardando=1',
    )
    expect(screen.getByRole('link', { name: 'Meus atendimentos' })).toHaveAttribute(
      'href',
      '/atendimentos?escopo=minhas',
    )
  })

  it('sem provedor de busca montado, o card "Buscar" não aparece', () => {
    autenticacaoMock.mockReturnValue(perfil(false, false))
    buscaGlobalMock.mockReturnValue(null)

    renderizar()

    expect(screen.queryByText('Buscar')).not.toBeInTheDocument()
  })

  it('com o provedor de busca, o card "Buscar" abre a busca e não navega', () => {
    const abrir = vi.fn()
    autenticacaoMock.mockReturnValue(perfil(false, false))
    buscaGlobalMock.mockReturnValue({ aberta: false, abrir, definirAberta: vi.fn() })

    renderizar()

    const botao = screen.getByRole('button', { name: 'Buscar' })
    fireEvent.click(botao)

    expect(abrir).toHaveBeenCalledTimes(1)
  })

  it('o cartão de destaque chama os callbacks de criação', () => {
    const aoAbrirNovoAtendimento = vi.fn()
    const aoAbrirNovoCliente = vi.fn()
    autenticacaoMock.mockReturnValue(perfil(false, false))
    buscaGlobalMock.mockReturnValue(null)

    renderizar({ aoAbrirNovoAtendimento, aoAbrirNovoCliente })

    fireEvent.click(screen.getByRole('button', { name: 'Abrir atendimento' }))
    expect(aoAbrirNovoAtendimento).toHaveBeenCalledTimes(1)

    fireEvent.click(screen.getByRole('button', { name: 'Novo cliente' }))
    expect(aoAbrirNovoCliente).toHaveBeenCalledTimes(1)
  })
})
