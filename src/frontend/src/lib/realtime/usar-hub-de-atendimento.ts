import { useEffect, useRef } from 'react'
import { usarContextoDoHub, type EventosDoAtendimento } from './contexto-do-hub-de-atendimento'

export type { EventosDoAtendimento }

/**
 * Liga o consumidor ao hub de atendimento — a conexão em si vive uma vez só,
 * no `ProvedorDoHubDeAtendimento` (ver `contexto-do-hub-de-atendimento.tsx`).
 * Esta função só assina e desassina; a assinatura pública não mudou, então
 * quem já chamava `usarHubDeAtendimento(eventos, habilitado)` — a inbox — não
 * precisa mudar nada.
 *
 * E o que faz a trava de conversa aparecer na tela: quando alguem assume, o
 * evento chega em todos os paineis e o botao "Assumir" some antes de o segundo
 * atendente clicar. O UPDATE condicional no backend continua sendo a garantia
 * real — isto aqui evita que a corrida chegue a acontecer.
 */
export function usarHubDeAtendimento(eventos: EventosDoAtendimento, habilitado: boolean) {
  const { inscrever, conexaoRef } = usarContextoDoHub()

  // Os handlers vao numa ref para que trocar callback nao precise reinscrever.
  const eventosRef = useRef(eventos)
  eventosRef.current = eventos

  useEffect(() => {
    if (!habilitado) {
      return
    }

    const desinscrever = inscrever({
      onConversaAtribuida: (c) => eventosRef.current.onConversaAtribuida?.(c),
      onConversaLiberada: (c) => eventosRef.current.onConversaLiberada?.(c),
      onConversaTransferida: (c) => eventosRef.current.onConversaTransferida?.(c),
      onConversaAtualizada: (c) => eventosRef.current.onConversaAtualizada?.(c),
      onConversaEncerrada: (c) => eventosRef.current.onConversaEncerrada?.(c),
      onConversaReaberta: (c) => eventosRef.current.onConversaReaberta?.(c),
      onMensagemRecebida: (d) => eventosRef.current.onMensagemRecebida?.(d),
    })

    return desinscrever
  }, [habilitado, inscrever])

  return conexaoRef
}
