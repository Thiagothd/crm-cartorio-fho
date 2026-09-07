import { createContext, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { autenticacaoApi } from '@/lib/api/endpoints'
import { gravarToken, lerToken, limparToken } from '@/lib/api/cliente-http'
import { limparCacheDeAnexos } from '@/features/atendimento/cache-de-anexos'
import type { Atendente, ChaveDePermissao, DesafioMfa, Sessao, Tenant } from '@/lib/api/tipos'

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
  concluirLoginComMfa: (desafio: string, codigo: string, confiarNesteDispositivo?: boolean) => Promise<void>
  /**
   * Aplica uma sessão nova recebida FORA do fluxo de login normal — hoje:
   * confirmação de ativação/troca de autenticador de MFA e matrícula
   * privilegiada por ticket. Todas essas confirmações sobem
   * `Atendente.TokenVersion` na mesma escrita que ativam o MFA (SEC-01), o
   * que invalida o JWT antigo na hora — sem aplicar a sessão nova devolvida
   * na própria resposta, a próxima chamada autenticada cai em 401 e a tela
   * fica branca. Nunca duplica a gravação de token/atendente/tenant: usa a
   * MESMA rotina central que `entrar`/`concluirLoginComMfa` já usam.
   */
  aplicarNovaSessao: (sessao: Sessao) => void
  sair: () => void
  /**
   * A checagem granular — mesmo vocabulário do backend
   * (`Modules/Usuarios/Domain/CatalogoDePermissoes.cs`). Fail-closed: sem
   * sessão, nega tudo.
   */
  pode: (chave: ChaveDePermissao) => boolean
  /**
   * @deprecated Legado — mantido só para os testes existentes não
   * precisarem migrar todos de uma vez. Prefira `pode(chave)`. Equivalem a
   * `pode('equipe.editar')`/`pode('equipe.cadastrar')`/`pode('atendimento.mexer_no_de_terceiro')`
   * hoje, mas não são a MESMA checagem que o backend faz endpoint a
   * endpoint — cada tela deveria migrar para a chave certa dela.
   */
  podeGerirCadastros: boolean
  /** @deprecated Legado — ver `podeGerirCadastros`. */
  podeGerirAtendentes: boolean
  /** @deprecated Legado — ver `podeGerirCadastros`. */
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

  // Permissoes granulares: o backend resolve o conjunto a cada request (SEC-01
  // já paga esse round-trip), mas o TOKEN não é revogado quando alguém só
  // muda o que outro atendente acessa (de propósito — ver
  // Modules/Usuarios/README.md, "Permissões granulares"). Sem isto, o MENU
  // ficaria com o conjunto antigo até o JWT expirar (até 8h) ou até a
  // próxima ação que force um refetch. Refazer a sessão quando a aba volta a
  // ficar visível é o mesmo compromisso que o backend já assume: o menu pode
  // ficar desatualizado por um instante, o backend nunca fica — cada
  // endpoint confere de novo, sempre.
  useEffect(() => {
    function aoFicarVisivel() {
      if (document.visibilityState !== 'visible' || !lerToken()) return

      autenticacaoApi
        .sessao()
        .then((sessao) => {
          setAtendente(sessao.atendente)
          setTenant(sessao.tenant)
        })
        .catch(() => {
          // Sessão realmente inválida (token revogado, conta desativada):
          // deixa a próxima chamada autenticada normal descobrir isso e
          // redirecionar — não é papel deste refresh silencioso derrubar o
          // usuário no meio de outra aba.
        })
    }

    document.addEventListener('visibilitychange', aoFicarVisivel)
    return () => document.removeEventListener('visibilitychange', aoFicarVisivel)
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
    async (desafio: string, codigo: string, confiarNesteDispositivo = false) => {
      const sessao = await autenticacaoApi.loginMfa(desafio, codigo, confiarNesteDispositivo)
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
    const permissoes = atendente?.permissoes

    return {
      carregando,
      atendente,
      tenant,
      entrar,
      concluirLoginComMfa,
      aplicarNovaSessao: aplicarSessao,
      sair,
      pode: (chave) => permissoes?.includes(chave) ?? false,
      // Legado: continuam derivadas da Classe da conta, não do conjunto
      // granular — é o que preserva o comportamento de hoje para as telas
      // que ainda não migraram (nenhuma conta perde nem ganha acesso só por
      // este arquivo mudar).
      podeGerirCadastros: perfil === 'Supervisor' || perfil === 'Administrador',
      podeGerirAtendentes: perfil === 'Administrador',
      podeOperarConversaDeTerceiro: perfil === 'Supervisor' || perfil === 'Administrador',
    }
  }, [atendente, tenant, carregando, entrar, concluirLoginComMfa, aplicarSessao, sair])

  return <ContextoDeAutenticacao.Provider value={valor}>{children}</ContextoDeAutenticacao.Provider>
}
