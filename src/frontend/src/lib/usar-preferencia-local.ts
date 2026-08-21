import { useCallback, useState } from 'react'

/**
 * Preferência de interface que sobrevive ao recarregamento, guardada no
 * localStorage.
 *
 * É para PREFERÊNCIA DE TELA — painel recolhido, coluna escondida — e não para
 * dado de negócio. Nada aqui é do cartório: se o navegador limpar, o pior que
 * acontece é a tela voltar ao padrão. Dado de verdade mora no banco, por tenant.
 *
 * A leitura acontece no inicializador do useState, e não num efeito, para o
 * primeiro render já sair com o valor certo — caso contrário a tela pisca do
 * padrão para o escolhido a cada carregamento.
 *
 * Só grava o que o usuário ESCOLHEU. A versão anterior gravava num efeito, o que
 * carimbava o padrão no localStorage na primeira montagem: a partir daí o padrão
 * do código virava letra morta, porque toda tela já encontrava um valor guardado
 * que ninguém tinha escolhido. Mudar o padrão não chegava em ninguém.
 */
export function usarPreferenciaLocal(chave: string, padrao: boolean) {
  const [valor, setValor] = useState<boolean>(() => {
    try {
      const guardado = localStorage.getItem(chave)
      return guardado === null ? padrao : guardado === 'true'
    } catch {
      // localStorage indisponível (modo privado, política do navegador). A
      // preferência não é essencial; a tela abre no padrão e segue.
      return padrao
    }
  })

  const alternar = useCallback(() => {
    const proximo = !valor
    setValor(proximo)

    try {
      localStorage.setItem(chave, String(proximo))
    } catch {
      // Idem: não gravar a preferência não pode quebrar a tela.
    }
  }, [chave, valor])

  return [valor, alternar] as const
}

/**
 * Mesma disciplina de `usarPreferenciaLocal`, para preferência de texto —
 * período e escopo da Visão Geral, por exemplo. `valoresValidos` existe para
 * um valor gravado por uma versão antiga da tela (ou editado à mão no
 * `localStorage`) não travar a tela num estado que a UI atual não sabe
 * desenhar; cai no padrão em silêncio, igual a preferência ausente.
 */
export function usarPreferenciaDeTexto<T extends string>(
  chave: string,
  padrao: T,
  valoresValidos: readonly T[],
) {
  const [valor, setValorInterno] = useState<T>(() => {
    try {
      const guardado = localStorage.getItem(chave)
      return guardado && (valoresValidos as readonly string[]).includes(guardado) ? (guardado as T) : padrao
    } catch {
      return padrao
    }
  })

  const definir = useCallback(
    (proximo: T) => {
      setValorInterno(proximo)

      try {
        localStorage.setItem(chave, proximo)
      } catch {
        // Não gravar a preferência não pode quebrar a tela.
      }
    },
    [chave],
  )

  return [valor, definir] as const
}
