import { useEffect, useRef } from 'react'
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

/**
 * Liga o painel ao hub de atendimento.
 *
 * E o que faz a trava de conversa aparecer na tela: quando alguem assume, o
 * evento chega em todos os paineis e o botao "Assumir" some antes de o segundo
 * atendente clicar. O UPDATE condicional no backend continua sendo a garantia
 * real — isto aqui evita que a corrida chegue a acontecer.
 */
export function usarHubDeAtendimento(eventos: EventosDoAtendimento, habilitado: boolean) {
  // Os handlers vao numa ref para que trocar callback nao derrube a conexao.
  const eventosRef = useRef(eventos)
  eventosRef.current = eventos

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

    conexao.on('ConversaAtribuida', (c: Conversa) => eventosRef.current.onConversaAtribuida?.(c))
    conexao.on('ConversaLiberada', (c: Conversa) => eventosRef.current.onConversaLiberada?.(c))
    conexao.on('ConversaTransferida', (c: Conversa) => eventosRef.current.onConversaTransferida?.(c))
    conexao.on('ConversaAtualizada', (c: Conversa) => eventosRef.current.onConversaAtualizada?.(c))
    conexao.on('ConversaEncerrada', (c: Conversa) => eventosRef.current.onConversaEncerrada?.(c))
    conexao.on('ConversaReaberta', (c: Conversa) => eventosRef.current.onConversaReaberta?.(c))
    conexao.on('MensagemRecebida', (dados: { mensagem: Mensagem; conversa: Conversa }) =>
      eventosRef.current.onMensagemRecebida?.(dados),
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

  return conexaoRef
}
