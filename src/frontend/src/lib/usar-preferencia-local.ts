import { useCallback, useEffect, useState } from 'react'

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

  useEffect(() => {
    try {
      localStorage.setItem(chave, String(valor))
    } catch {
      // Idem: não gravar a preferência não pode quebrar a tela.
    }
  }, [chave, valor])

  const alternar = useCallback(() => setValor((atual) => !atual), [])

  return [valor, alternar] as const
}
