import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

interface EstadoDaBuscaGlobal {
  aberta: boolean
  abrir: () => void
  definirAberta: (aberta: boolean) => void
}

const Contexto = createContext<EstadoDaBuscaGlobal | null>(null)

/**
 * O estado da paleta de busca vive AQUI, no Shell — não mais dentro de
 * `BuscaGlobal`. Motivo: mais de um lugar precisa abri-la (o botão do
 * cabeçalho, o Ctrl+K, e o cartão "Buscar" das ações rápidas da Visão Geral),
 * e estado local num componente do cabeçalho não é alcançável por nenhum dos
 * dois últimos.
 *
 * O atalho de teclado subiu junto: ele sempre foi comportamento global, e
 * morava no cabeçalho só por acidente de onde o componente foi escrito.
 */
export function ProvedorDaBuscaGlobal({ children }: { children: ReactNode }) {
  const [aberta, definirAberta] = useState(false)

  useEffect(() => {
    function aoTeclarNaJanela(evento: KeyboardEvent) {
      if ((evento.metaKey || evento.ctrlKey) && evento.key.toLowerCase() === 'k') {
        evento.preventDefault()
        definirAberta((atual) => !atual)
      }
    }

    window.addEventListener('keydown', aoTeclarNaJanela)
    return () => window.removeEventListener('keydown', aoTeclarNaJanela)
  }, [])

  const abrir = useCallback(() => definirAberta(true), [])
  const valor = useMemo(() => ({ aberta, abrir, definirAberta }), [aberta, abrir])

  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>
}

/** `null` fora do Shell — quem consome oferece a ação só quando ela existe. */
export function usarBuscaGlobal(): EstadoDaBuscaGlobal | null {
  return useContext(Contexto)
}
