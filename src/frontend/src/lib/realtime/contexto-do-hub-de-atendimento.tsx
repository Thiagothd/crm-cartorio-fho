import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useRef,
  type ReactNode,
  type RefObject,
} from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { baseDaApi, lerToken } from '@/lib/api/cliente-http'
import type { Conversa, Mensagem } from '@/lib/api/tipos'

export interface EventosDoAtendimento {
  onConversaAtribuida?: (conversa: Conversa) => void
  onConversaLiberada?: (conversa: Conversa) => void
  onConversaTransferida?: (conversa: Conversa) => void
  onConversaAtualizada?: (conversa: Conversa) => void
  onConversaEncerrada?: (conversa: Conversa) => void
  onConversaReaberta?: (conversa: Conversa) => void
  onMensagemRecebida?: (dados: { mensagem: Mensagem; conversa: Conversa }) => void
}

interface ContextoDoHub {
  inscrever: (eventos: EventosDoAtendimento) => () => void
  conexaoRef: RefObject<HubConnection | null>
}

const ContextoDoHubDeAtendimento = createContext<ContextoDoHub | null>(null)

/**
 * Uma única conexão SignalR para a aplicação inteira.
 *
 * Antes do shell global, só a inbox chamava `usarHubDeAtendimento` e abria a
 * própria conexão. Um sino de alertas no cabeçalho, fora da inbox, chamando o
 * mesmo hook abriria uma SEGUNDA conexão ao mesmo hub — o servidor aceita, mas
 * dobra o tráfego e a memória do hub sem motivo. Aqui, quem quer eventos se
 * INSCREVE num despachante único; a conexão em si mora aqui, uma vez.
 */
export function ProvedorDoHubDeAtendimento({
  habilitado,
  children,
}: {
  habilitado: boolean
  children: ReactNode
}) {
  const assinantesRef = useRef(new Set<EventosDoAtendimento>())
  const conexaoRef = useRef<HubConnection | null>(null)

  useEffect(() => {
    if (!habilitado) {
      return
    }

    const conexao = new HubConnectionBuilder()
      .withUrl(`${baseDaApi}/hubs/atendimento`, {
        accessTokenFactory: () => lerToken() ?? '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    conexaoRef.current = conexao

    function disparar<K extends keyof EventosDoAtendimento>(
      evento: K,
      ...args: Parameters<NonNullable<EventosDoAtendimento[K]>>
    ) {
      for (const assinante of assinantesRef.current) {
        const manipulador = assinante[evento] as
          | ((...args: Parameters<NonNullable<EventosDoAtendimento[K]>>) => void)
          | undefined

        manipulador?.(...args)
      }
    }

    conexao.on('ConversaAtribuida', (c: Conversa) => disparar('onConversaAtribuida', c))
    conexao.on('ConversaLiberada', (c: Conversa) => disparar('onConversaLiberada', c))
    conexao.on('ConversaTransferida', (c: Conversa) => disparar('onConversaTransferida', c))
    conexao.on('ConversaAtualizada', (c: Conversa) => disparar('onConversaAtualizada', c))
    conexao.on('ConversaEncerrada', (c: Conversa) => disparar('onConversaEncerrada', c))
    conexao.on('ConversaReaberta', (c: Conversa) => disparar('onConversaReaberta', c))
    conexao.on('MensagemRecebida', (dados: { mensagem: Mensagem; conversa: Conversa }) =>
      disparar('onMensagemRecebida', dados),
    )

    conexao.start().catch(() => {
      // Sem tempo real o painel ainda funciona por refetch — nao vale derrubar
      // a tela por causa disso.
    })

    return () => {
      if (conexao.state !== HubConnectionState.Disconnected) {
        void conexao.stop()
      }
      conexaoRef.current = null
    }
  }, [habilitado])

  const valor = useMemo<ContextoDoHub>(
    () => ({
      inscrever: (eventos) => {
        assinantesRef.current.add(eventos)
        return () => {
          assinantesRef.current.delete(eventos)
        }
      },
      conexaoRef,
    }),
    [],
  )

  return (
    <ContextoDoHubDeAtendimento.Provider value={valor}>{children}</ContextoDoHubDeAtendimento.Provider>
  )
}

export function usarContextoDoHub() {
  const contexto = useContext(ContextoDoHubDeAtendimento)

  if (!contexto) {
    throw new Error('usarContextoDoHub precisa estar dentro de ProvedorDoHubDeAtendimento.')
  }

  return contexto
}
