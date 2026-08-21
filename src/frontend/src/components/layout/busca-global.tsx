import { useDeferredValue, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import * as DialogPrimitive from '@radix-ui/react-dialog'
import { Loader2, MessagesSquare, Search, SearchX, Users } from 'lucide-react'
import { conversasApi, clientesApi } from '@/lib/api/endpoints'
import { formatarTelefone } from '@/lib/formatacao'
import { cn } from '@/lib/utils'

interface ResultadoDaBusca {
  chave: string
  rotulo: string
  sublinha: string
  ir: () => void
}

/**
 * Busca real, não decorativa: os mesmos endpoints que a inbox e a tela de
 * clientes já usam (`conversasApi.listar`/`clientesApi.listar`, ambos abertos
 * a qualquer autenticado) — zero backend novo. É o caso de uso mais frequente
 * de balcão: o cliente liga e diz o protocolo.
 */
export function BuscaGlobal() {
  const [aberta, setAberta] = useState(false)
  const [busca, setBusca] = useState('')
  const buscaAdiada = useDeferredValue(busca)
  const [indiceAtivo, setIndiceAtivo] = useState(0)
  const navegar = useNavigate()
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    function aoTeclarNaJanela(evento: KeyboardEvent) {
      if ((evento.metaKey || evento.ctrlKey) && evento.key.toLowerCase() === 'k') {
        evento.preventDefault()
        setAberta((atual) => !atual)
      }
    }

    window.addEventListener('keydown', aoTeclarNaJanela)
    return () => window.removeEventListener('keydown', aoTeclarNaJanela)
  }, [])

  useEffect(() => {
    if (!aberta) {
      setBusca('')
      setIndiceAtivo(0)
    }
  }, [aberta])

  const termo = buscaAdiada.trim()
  const habilitado = aberta && termo.length >= 2

  const { data: conversas, isFetching: buscandoConversas } = useQuery({
    queryKey: ['busca-global-conversas', termo],
    queryFn: () => conversasApi.listar({ busca: termo, tamanho: 6 }),
    enabled: habilitado,
  })

  const { data: clientes, isFetching: buscandoClientes } = useQuery({
    queryKey: ['busca-global-clientes', termo],
    queryFn: () => clientesApi.listar({ busca: termo, tamanho: 6 }),
    enabled: habilitado,
  })

  const carregando = buscandoConversas || buscandoClientes

  const resultados = useMemo<{ atendimentos: ResultadoDaBusca[]; clientes: ResultadoDaBusca[] }>(() => {
    const atendimentos = (conversas?.itens ?? []).map((c) => ({
      chave: `conversa-${c.id}`,
      rotulo: `${c.protocolo} · ${c.clienteNome}`,
      sublinha: formatarTelefone(c.clienteTelefone),
      ir: () => navegar(`/atendimentos/${c.id}`),
    }))

    const listaDeClientes = (clientes?.itens ?? []).map((cl) => ({
      chave: `cliente-${cl.id}`,
      rotulo: cl.nome,
      sublinha: formatarTelefone(cl.telefoneWhatsapp),
      ir: () => navegar(`/clientes?busca=${encodeURIComponent(cl.nome)}`),
    }))

    return { atendimentos, clientes: listaDeClientes }
  }, [conversas, clientes, navegar])

  const lista = [...resultados.atendimentos, ...resultados.clientes]

  function fechar() {
    setAberta(false)
  }

  function selecionar(indice: number) {
    const item = lista[indice]

    if (item) {
      item.ir()
      fechar()
    }
  }

  return (
    <>
      {/* `w-56` em vez de `w-48`, e `whitespace-nowrap` no rótulo: em 12rem o
          texto + o atalho não caíam na mesma linha, o rótulo quebrava em duas e
          o `kbd` era esmagado — a "busca bugada" que aparecia no cabeçalho.
          `min-w-0 truncate` no rótulo e `shrink-0` no atalho garantem que, se
          ainda faltar espaço, quem corta é o texto (que é decorativo) e nunca o
          atalho (que é informação).

          `size-11` no celular = 44px de alvo de toque (DESIGN.md 12.2), caindo
          para 36px a partir de `md`. Era `h-9 w-9` em toda largura, ou seja 36px
          no dedo — abaixo do mínimo que o próprio documento exige, num dos dois
          botões que todo mundo usa. */}
      <button
        type="button"
        onClick={() => setAberta(true)}
        className="flex size-11 shrink-0 touch-manipulation items-center justify-center rounded-lg border border-linha text-tinta-media transition-colors hover:border-linha-forte hover:bg-superficie hover:text-tinta md:h-9 md:w-56 md:justify-between md:gap-2 md:px-2.5"
      >
        <span className="flex min-w-0 items-center gap-2">
          <Search className="size-4 shrink-0" aria-hidden />
          <span className="hidden truncate whitespace-nowrap text-xs md:inline">Buscar protocolo, cliente…</span>
        </span>
        <kbd className="hidden shrink-0 whitespace-nowrap rounded border border-linha-forte px-1.5 py-0.5 font-dado text-[10px] leading-none text-tinta-fraca md:inline-flex md:items-center">
          Ctrl K
        </kbd>
        <span className="sr-only">Buscar atendimentos e clientes</span>
      </button>

      <DialogPrimitive.Root open={aberta} onOpenChange={setAberta}>
        <DialogPrimitive.Portal>
          <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-overlay" />
          <DialogPrimitive.Content
            className="fixed left-1/2 top-[18%] z-50 w-[calc(100%-1.5rem)] max-w-lg -translate-x-1/2 rounded-card border border-linha-forte bg-branco shadow-card-hover focus:outline-none"
            onOpenAutoFocus={(evento) => {
              evento.preventDefault()
              inputRef.current?.focus()
            }}
          >
            <DialogPrimitive.Title className="sr-only">Buscar atendimentos e clientes</DialogPrimitive.Title>

            <div className="flex items-center gap-2 border-b border-linha px-3 py-2.5">
            <Search className="size-4 shrink-0 text-tinta-fraca" aria-hidden />
            <input
              ref={inputRef}
              value={busca}
              onChange={(evento) => {
                setBusca(evento.target.value)
                setIndiceAtivo(0)
              }}
              onKeyDown={(evento) => {
                if (evento.key === 'ArrowDown') {
                  evento.preventDefault()
                  setIndiceAtivo((i) => Math.min(lista.length - 1, i + 1))
                } else if (evento.key === 'ArrowUp') {
                  evento.preventDefault()
                  setIndiceAtivo((i) => Math.max(0, i - 1))
                } else if (evento.key === 'Enter') {
                  evento.preventDefault()
                  selecionar(indiceAtivo)
                }
              }}
              placeholder="Protocolo, nome do cliente, telefone…"
              className="h-8 flex-1 border-none bg-transparent text-sm text-tinta placeholder:text-tinta-fraca focus:outline-none"
              aria-activedescendant={lista[indiceAtivo]?.chave}
              role="combobox"
              aria-expanded={lista.length > 0}
              aria-controls="lista-de-busca-global"
            />
          </div>

            <div id="lista-de-busca-global" role="listbox" className="max-h-80 overflow-y-auto p-1.5">
              {termo.length < 2 ? (
                <p className="p-4 text-center text-sm text-tinta-fraca">Digite ao menos 2 caracteres.</p>
              ) : carregando ? (
                <p className="flex items-center justify-center gap-2 p-4 text-sm text-tinta-media">
                  <Loader2 className="size-4 animate-spin" aria-hidden />
                  Buscando…
                </p>
              ) : lista.length === 0 ? (
                <p className="flex flex-col items-center gap-2 p-6 text-center text-sm text-tinta-fraca">
                  <SearchX className="size-5" aria-hidden />
                  Nenhum resultado para "{termo}".
                </p>
              ) : (
                <>
                  {resultados.atendimentos.length > 0 ? (
                    <GrupoDeResultados
                      titulo="Atendimentos"
                      icone={MessagesSquare}
                      itens={resultados.atendimentos}
                      inicioDoIndice={0}
                      indiceAtivo={indiceAtivo}
                      aoSelecionar={selecionar}
                    />
                  ) : null}
                  {resultados.clientes.length > 0 ? (
                    <GrupoDeResultados
                      titulo="Clientes"
                      icone={Users}
                      itens={resultados.clientes}
                      inicioDoIndice={resultados.atendimentos.length}
                      indiceAtivo={indiceAtivo}
                      aoSelecionar={selecionar}
                    />
                  ) : null}
                </>
              )}
            </div>
          </DialogPrimitive.Content>
        </DialogPrimitive.Portal>
      </DialogPrimitive.Root>
    </>
  )
}

function GrupoDeResultados({
  titulo,
  icone: Icone,
  itens,
  inicioDoIndice,
  indiceAtivo,
  aoSelecionar,
}: {
  titulo: string
  icone: typeof MessagesSquare
  itens: ResultadoDaBusca[]
  inicioDoIndice: number
  indiceAtivo: number
  aoSelecionar: (indice: number) => void
}) {
  return (
    <div className="mb-1">
      <p className="px-2 py-1 text-[10px] uppercase tracking-wider text-tinta-fraca">{titulo}</p>
      {itens.map((item, i) => {
        const indice = inicioDoIndice + i
        const ativo = indice === indiceAtivo

        return (
          <button
            key={item.chave}
            id={item.chave}
            type="button"
            role="option"
            aria-selected={ativo}
            onClick={() => aoSelecionar(indice)}
            className={cn(
              'flex w-full items-center gap-2.5 rounded-lg px-2 py-2 text-left text-sm transition-colors',
              ativo ? 'bg-superficie text-tinta' : 'text-tinta hover:bg-superficie',
            )}
          >
            <Icone className="size-4 shrink-0 text-tinta-media" aria-hidden />
            <span className="min-w-0 flex-1">
              <span className="block truncate">{item.rotulo}</span>
              <span className="block truncate text-xs text-tinta-media">{item.sublinha}</span>
            </span>
          </button>
        )
      })}
    </div>
  )
}
