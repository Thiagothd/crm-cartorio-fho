import { useContext } from 'react'
import { ContextoDeAutenticacao } from './contexto-de-autenticacao'

export function usarAutenticacao() {
  const contexto = useContext(ContextoDeAutenticacao)

  if (!contexto) {
    throw new Error('usarAutenticacao precisa estar dentro de ProvedorDeAutenticacao.')
  }

  return contexto
}
