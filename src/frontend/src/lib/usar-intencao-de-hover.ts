import { useCallback, useEffect, useRef, useState } from 'react'

const ATRASO_PARA_ABRIR = 160
const ATRASO_PARA_FECHAR = 320

/**
 * Hover com intenção, para o rail de navegação: abre em 160ms, fecha com uma
 * folga de 320ms.
 *
 * Os 160ms para abrir não são lentidão, são a diferença entre "abriu porque eu
 * quis" e "abriu porque meu mouse passou perto": com 90ms (o valor anterior)
 * qualquer travessia do ponteiro pela borda esquerda escancarava o painel, e o
 * resultado era exatamente a sensação de coisa brusca e mecânica. 160ms
 * absorve a passagem acidental e ainda fica dentro da janela em que o
 * movimento é percebido como resposta, não como espera. Somado à transição de
 * largura do próprio painel, a abertura tem um começo e um fim visíveis em vez
 * de aparecer inteira de um quadro para o outro.
 *
 * Fechar é sempre mais lento que abrir (320ms), para quem move o mouse em
 * direção ao painel não ver ele sumir no meio do caminho. Cada evento cancela
 * o timer contrário, então entrar e sair rápido não acumula os dois.
 *
 * Foco de teclado TRAVA o painel aberto enquanto o foco estiver dentro —
 * sem isso, quem navega por Tab veria o painel fechar sozinho, porque o
 * ponteiro nunca esteve sobre ele. `aoDesfocarFora` deve ser chamado só
 * quando o foco de fato saiu do container (o componente confere
 * `currentTarget.contains(relatedTarget)` antes de chamar).
 */
export function usarIntencaoDeHover() {
  const [aberto, setAberto] = useState(false)
  const fixadoPeloFoco = useRef(false)
  const temporizador = useRef<number | null>(null)

  const limpar = useCallback(() => {
    if (temporizador.current !== null) {
      window.clearTimeout(temporizador.current)
      temporizador.current = null
    }
  }, [])

  const aoEntrar = useCallback(() => {
    limpar()
    temporizador.current = window.setTimeout(() => setAberto(true), ATRASO_PARA_ABRIR)
  }, [limpar])

  const aoSair = useCallback(() => {
    limpar()

    if (fixadoPeloFoco.current) {
      return
    }

    temporizador.current = window.setTimeout(() => setAberto(false), ATRASO_PARA_FECHAR)
  }, [limpar])

  const aoFocarDentro = useCallback(() => {
    limpar()
    fixadoPeloFoco.current = true
    setAberto(true)
  }, [limpar])

  const aoDesfocarFora = useCallback(() => {
    fixadoPeloFoco.current = false
    limpar()
    temporizador.current = window.setTimeout(() => setAberto(false), ATRASO_PARA_FECHAR)
  }, [limpar])

  const fecharAgora = useCallback(() => {
    limpar()
    fixadoPeloFoco.current = false
    setAberto(false)
  }, [limpar])

  const abrirAgora = useCallback(() => {
    limpar()
    setAberto(true)
  }, [limpar])

  useEffect(() => limpar, [limpar])

  return { aberto, aoEntrar, aoSair, aoFocarDentro, aoDesfocarFora, fecharAgora, abrirAgora }
}
