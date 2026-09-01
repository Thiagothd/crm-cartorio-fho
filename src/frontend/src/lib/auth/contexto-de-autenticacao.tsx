import { createContext, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { autenticacaoApi } from '@/lib/api/endpoints'
import { gravarToken, lerToken, limparToken } from '@/lib/api/cliente-http'
import { limparCacheDeAnexos } from '@/features/atendimento/cache-de-anexos'
import type { Atendente, DesafioMfa, Sessao, Tenant } from '@/lib/api/tipos'

/**
 * SEC-03 — a etapa 1 pode terminar de dois jeitos: sessão completa (conta sem
 * MFA), ou um desafio que a tela de login precisa resolver antes de existir
 * sessão. `entrar` nunca lança para o caso de MFA — é um resultado esperado,
 * não um erro.
 */
export type ResultadoDoLogin = { tipo: 'sucesso' } | { tipo: 'mfa'; desafio: DesafioMfa }

export interface EstadoDeAutenticacao {
  carregando: boolean
  atendente: Atendente | null
  tenant: Tenant | null
  entrar: (email: string, senha: string) => Promise<ResultadoDoLogin>
  /** Etapa 2 — conclui um login que abriu desafio de segundo fator. */
  concluirLoginComMfa: (desafio: string, codigo: string) => Promise<void>
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

  const aplicarSessao = useCallback((sessao: Sessao) => {
    gravarToken(sessao.token)
    setAtendente(sessao.atendente)
    setTenant(sessao.tenant)
  }, [])

  const entrar = useCallback(
    async (email: string, senha: string): Promise<ResultadoDoLogin> => {
      const resposta = await autenticacaoApi.login(email, senha)

      if (resposta.requerMfa) {
        // desafio, nao sessao — a etapa 2 (concluirLoginComMfa) e que decide.
        return { tipo: 'mfa', desafio: resposta.desafio! }
      }

      aplicarSessao(resposta.sessao!)
      return { tipo: 'sucesso' }
    },
    [aplicarSessao],
  )

  const concluirLoginComMfa = useCallback(
    async (desafio: string, codigo: string) => {
      const sessao = await autenticacaoApi.loginMfa(desafio, codigo)
      aplicarSessao(sessao)
    },
    [aplicarSessao],
  )

  // SEC-01: logout local nao e revogacao individual de token. Isto so apaga o
  // token deste navegador — o JWT em si continua criptograficamente valido
  // ate expirar (ate 8h), e continuaria funcionando em qualquer outra aba ou
  // dispositivo onde ainda esteja salvo. TokenVersion (backend) deliberadamente
  // NAO sobe aqui: subir a versao no logout derrubaria TODOS os
  // computadores/dispositivos do atendente, nao so esta sessao. Revogar so o
  // token individual exigiria SessionId/jti persistido por dispositivo — fora
  // do escopo deste PR (ver Modules/Usuarios/README.md, secao SEC-01).
  const sair = useCallback(() => {
    limparToken()

    // Anexo de cartorio e documento de cliente. Deixar os blobs vivos depois do
    // logout anularia a razao de o endpoint ser autenticado — a proxima pessoa
    // no computador de balcao veria o RG do atendimento anterior.
    limparCacheDeAnexos()

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
      concluirLoginComMfa,
      sair,
      podeGerirCadastros: perfil === 'Supervisor' || perfil === 'Administrador',
      podeGerirAtendentes: perfil === 'Administrador',
      podeOperarConversaDeTerceiro: perfil === 'Supervisor' || perfil === 'Administrador',
    }
  }, [atendente, tenant, carregando, entrar, concluirLoginComMfa, sair])

  return <ContextoDeAutenticacao.Provider value={valor}>{children}</ContextoDeAutenticacao.Provider>
}
