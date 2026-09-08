import { screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { renderizarComProvedores } from '@/test/utils'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { usarVisaoGeral } from '@/features/indicadores/consultas'
import { usarResumoDoPainel, usarSetores, usarEtapas, usarInvalidarAtendimento } from '@/features/atendimento/consultas'
import { PaginaVisaoGeral } from './pagina-visao-geral'

vi.mock('@/lib/auth/usar-autenticacao', () => ({
  usarAutenticacao: vi.fn(),
}))

vi.mock('@/features/indicadores/consultas', () => ({
  usarVisaoGeral: vi.fn(),
}))

// O varredor de tempo real é testado isoladamente em `tempo-real.test.tsx`;
// aqui ele só precisa não quebrar (ele assina o hub, que exige um provedor que
// esta página, sozinha, não monta).
vi.mock('@/features/indicadores/tempo-real', () => ({
  usarVisaoGeralAoVivo: () => {},
}))

vi.mock('@/features/atendimento/consultas', () => ({
  usarResumoDoPainel: vi.fn(),
  usarSetores: vi.fn(),
  usarEtapas: vi.fn(),
  usarInvalidarAtendimento: vi.fn(),
}))

const autenticacaoMock = vi.mocked(usarAutenticacao)
const visaoGeralMock = vi.mocked(usarVisaoGeral)
const resumoMock = vi.mocked(usarResumoDoPainel)
const setoresMock = vi.mocked(usarSetores)
const etapasMock = vi.mocked(usarEtapas)
const invalidarMock = vi.mocked(usarInvalidarAtendimento)

function config(overrides: {
  visaoGeral?: Partial<ReturnType<typeof usarVisaoGeral>>
  resumo?: Partial<ReturnType<typeof usarResumoDoPainel>>
} = {}) {
  autenticacaoMock.mockReturnValue({
    atendente: { perfil: 'Administrador' },
    podeGerirCadastros: true,
    pode: () => true,
  } as unknown as ReturnType<typeof usarAutenticacao>)

  visaoGeralMock.mockReturnValue({
    data: undefined,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
    isFetching: false,
    ...overrides.visaoGeral,
  } as ReturnType<typeof usarVisaoGeral>)

  resumoMock.mockReturnValue({
    data: undefined,
    isLoading: false,
    isError: false,
    ...overrides.resumo,
  } as ReturnType<typeof usarResumoDoPainel>)

  // DialogoNovaConversa (montado sempre, fechado) chama estes dois sem
  // `enabled` — sem mock aqui a montagem dispararia fetch real.
  setoresMock.mockReturnValue({ data: [] } as unknown as ReturnType<typeof usarSetores>)
  etapasMock.mockReturnValue({ data: [] } as unknown as ReturnType<typeof usarEtapas>)
  invalidarMock.mockReturnValue(vi.fn())
}

describe('PaginaVisaoGeral — Ações rápidas', () => {
  it('as ações sobrevivem ao loading da Visão Geral', () => {
    config({ visaoGeral: { isLoading: true } })

    renderizarComProvedores(<PaginaVisaoGeral />)

    expect(screen.getByRole('button', { name: 'Abrir atendimento' })).toBeInTheDocument()
  })

  it('as ações sobrevivem ao erro da Visão Geral', () => {
    config({ visaoGeral: { isError: true } })

    renderizarComProvedores(<PaginaVisaoGeral />)

    expect(screen.getByText('Não foi possível carregar a Visão Geral.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Abrir atendimento' })).toBeInTheDocument()
  })

  it('o bloco de ações é o 2º filho da grade com animação de entrada', () => {
    config({ visaoGeral: { isLoading: true } })

    const { container } = renderizarComProvedores(<PaginaVisaoGeral />)

    const secao = container.querySelector('#acoes-rapidas-titulo')!.closest('section')!
    const grade = secao.parentElement!
    expect(grade).toHaveClass('grade-com-vida')
    expect(grade.children[1]).toBe(secao)
  })

  it('os contadores vêm do resumo mesmo com a query gorda em erro', () => {
    config({
      visaoGeral: { isError: true },
      resumo: {
        data: {
          totalAtivas: 20,
          naFila: 12,
          aguardandoResposta: 4,
          minhasConversas: 7,
          encerradasHoje: 3,
          emAlerta: 2,
          atrasadas: 1,
          porEtapa: [],
          porSetor: [],
        },
      },
    })

    renderizarComProvedores(<PaginaVisaoGeral />)

    expect(screen.getByRole('link', { name: 'Fila sem responsável: 12' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Aguardando resposta: 4' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Meus atendimentos: 7' })).toBeInTheDocument()
  })
})
