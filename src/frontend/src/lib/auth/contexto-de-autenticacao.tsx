import { createContext, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { autenticacaoApi } from '@/lib/api/endpoints'
import { gravarToken, lerToken, limparToken } from '@/lib/api/cliente-http'
import type { Atendente, Tenant } from '@/lib/api/tipos'

export interface EstadoDeAutenticacao {
  carregando: boolean
  atendente: Atendente | null
  tenant: Tenant | null
  entrar: (email: string, senha: string) => Promise<void>
  sair: () => void
  podeGerirCadastros: boolean
  podeGerirAtendentes: boolean
  podeOperarConversaDeTerceiro: boolean
}

export const ContextoDeAutenticacao = createContext<EstadoDeAutenticacao | null>(null)

export function ProvedorDeAutenticacao({ children }: { children: ReactNode }) {
  const [carregando, setCarregando] = useState(true)
  const [atendente, setAtendente] = useState<Atendente | null>(null)
  const [tenant, setTenant] = useState<Tenant | null>(null)

  // Ao abrir o app, um token guardado ainda precisa ser validado no servidor:
  // ele pode ter expirado ou o atendente pode ter sido desativado desde ontem.
  useEffect(() => {
    if (!lerToken()) {
      setCarregando(false)
      return
    }

    let ativo = true

    autenticacaoApi
      .sessao()
      .then((sessao) => {
        if (!ativo) return
        setAtendente(sessao.atendente)
        setTenant(sessao.tenant)
      })
      .catch(() => {
        limparToken()
      })
      .finally(() => {
        if (ativo) setCarregando(false)
      })

    return () => {
      ativo = false
    }
  }, [])

  const entrar = useCallback(async (email: string, senha: string) => {
    const sessao = await autenticacaoApi.login(email, senha)
    gravarToken(sessao.token)
    setAtendente(sessao.atendente)
    setTenant(sessao.tenant)
  }, [])

  const sair = useCallback(() => {
    limparToken()
    setAtendente(null)
    setTenant(null)
  }, [])

  const valor = useMemo<EstadoDeAutenticacao>(() => {
    const perfil = atendente?.perfil

    return {
      carregando,
      atendente,
      tenant,
      entrar,
      sair,
      podeGerirCadastros: perfil === 'Supervisor' || perfil === 'Administrador',
      podeGerirAtendentes: perfil === 'Administrador',
      podeOperarConversaDeTerceiro: perfil === 'Supervisor' || perfil === 'Administrador',
    }
  }, [atendente, tenant, carregando, entrar, sair])

  return <ContextoDeAutenticacao.Provider value={valor}>{children}</ContextoDeAutenticacao.Provider>
}
