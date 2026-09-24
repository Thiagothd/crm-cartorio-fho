import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CHAVE_DO_TEMA, definirTema, iniciarTema } from './tema'
import { SeletorDeTema } from '@/components/ui/seletor-de-tema'

let parar: (() => void) | undefined
let escuroNoSistema = false
const ouvintes = new Set<() => void>()

beforeEach(() => {
  localStorage.clear()
  escuroNoSistema = false
  ouvintes.clear()
  window.dispatchEvent(new StorageEvent('storage', { key: null }))
  vi.spyOn(window, 'matchMedia').mockImplementation((media) => ({
    media,
    get matches() { return escuroNoSistema },
    addEventListener: (_: string, ouvinte: () => void) => ouvintes.add(ouvinte),
    removeEventListener: (_: string, ouvinte: () => void) => ouvintes.delete(ouvinte),
  }) as unknown as MediaQueryList)
  document.head.innerHTML = '<meta name="theme-color" content="#F7F6F3">'
  parar = iniciarTema()
})

afterEach(() => {
  parar?.()
  vi.restoreAllMocks()
})

describe('tema da interface', () => {
  it('preserva o claro como padrao e ignora preferencias desconhecidas', () => {
    expect(document.documentElement.dataset.tema).toBe('claro')
    localStorage.setItem(CHAVE_DO_TEMA, 'invalido')
    window.dispatchEvent(new StorageEvent('storage', { key: CHAVE_DO_TEMA }))
    expect(document.documentElement.dataset.tema).toBe('claro')
  })

  it('sincroniza seletores, documento e preferencia sem remontar a tela', async () => {
    const usuario = userEvent.setup()
    render(<><SeletorDeTema expandido /><SeletorDeTema /></>)
    await usuario.click(screen.getByRole('radio', { name: 'Escuro' }))
    expect(screen.getByRole('button', { name: 'Tema: Escuro' })).toBeInTheDocument()
    expect(localStorage.getItem(CHAVE_DO_TEMA)).toBe('escuro')
    expect(document.documentElement.dataset.tema).toBe('escuro')
    expect(document.documentElement.style.colorScheme).toBe('dark')
    expect(document.querySelector('meta[name="theme-color"]')).toHaveAttribute('content', '#101113')
  })

  it('acompanha o sistema somente quando essa opcao esta selecionada', () => {
    definirTema('sistema')
    escuroNoSistema = true
    ouvintes.forEach((ouvinte) => ouvinte())
    expect(document.documentElement.dataset.tema).toBe('escuro')
    definirTema('claro')
    ouvintes.forEach((ouvinte) => ouvinte())
    expect(document.documentElement.dataset.tema).toBe('claro')
  })

  it('recebe mudancas e limpeza de preferencia em outra aba', () => {
    render(<SeletorDeTema expandido />)
    act(() => {
      localStorage.setItem(CHAVE_DO_TEMA, 'escuro')
      window.dispatchEvent(new StorageEvent('storage', { key: CHAVE_DO_TEMA }))
    })
    expect(screen.getByRole('radio', { name: 'Escuro' })).toBeChecked()
    act(() => {
      localStorage.clear()
      window.dispatchEvent(new StorageEvent('storage', { key: null }))
    })
    expect(screen.getByRole('radio', { name: 'Claro' })).toBeChecked()
  })

  it('permite trocar o tema quando o armazenamento esta bloqueado', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('bloqueado') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('bloqueado') })
    definirTema('escuro')
    expect(document.documentElement.dataset.tema).toBe('escuro')
    definirTema('claro')
    expect(document.documentElement.dataset.tema).toBe('claro')
  })

  it('permite selecionar com teclado', async () => {
    const usuario = userEvent.setup()
    render(<SeletorDeTema expandido />)
    await usuario.tab()
    await usuario.keyboard('{ArrowRight}')
    expect(screen.getByRole('radio', { name: 'Escuro' })).toBeChecked()
    expect(document.documentElement.dataset.tema).toBe('escuro')
  })
})
