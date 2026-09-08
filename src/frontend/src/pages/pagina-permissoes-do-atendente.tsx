import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { toast } from 'sonner'
import { ArrowLeft } from 'lucide-react'
import { Avatar } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { usarAtendente, usarDefinirPermissoes, usarPermissoesDoAtendente } from '@/features/equipe/consultas'
import { usarAbaixoDe } from '@/lib/usar-abaixo-de'
import { CartaoDeModelo } from '@/features/equipe/permissoes/cartao-de-modelo'
import { BlocoDePermissoes } from '@/features/equipe/permissoes/bloco-de-permissoes'
import { ResumoDaMudanca } from '@/features/equipe/permissoes/resumo-da-mudanca'
import type { ChaveDePermissao } from '@/lib/api/tipos'

function ordemDosBlocos(blocos: string[]): string[] {
  const ORDEM = [
    'Atendimento',
    'Clientes',
    'Catálogo e estrutura',
    'Automação e IA',
    'Relatórios',
    'WhatsApp',
    'Aparência e serventia',
    'Equipe e segurança',
  ]
  return [...blocos].sort((a, b) => ORDEM.indexOf(a) - ORDEM.indexOf(b))
}

export function PaginaPermissoesDoAtendente() {
  const { id } = useParams<{ id: string }>()
  const navegar = useNavigate()
  const { data: atendente, isLoading: carregandoAtendente } = usarAtendente(id!)
  const { data, isLoading, isError, refetch } = usarPermissoesDoAtendente(id ?? null)
  const salvar = usarDefinirPermissoes()

  const [pendentes, setPendentes] = useState<Set<ChaveDePermissao> | null>(null)
  const [blocoAberto, setBlocoAberto] = useState<string | null>(null)
  // Abaixo de md os blocos vivem fechados por padrão (details), um por vez.
  // A partir de md ficam sempre abertos — só há espaço de sobra e "clicar
  // para ver a permissão" seria fricção que a tela de admin não precisa.
  const mobile = usarAbaixoDe(768)

  useEffect(() => {
    if (data) {
      setPendentes(new Set(data.chaves))
      setBlocoAberto(null)
    }
  }, [data])

  const catalogoPorChave = useMemo(
    () => new Map((data?.catalogo ?? []).map((item) => [item.chave, item])),
    [data],
  )

  const blocos = useMemo(() => {
    if (!data) return []
    const porBloco = new Map<string, typeof data.catalogo>()
    for (const item of data.catalogo) {
      const lista = porBloco.get(item.bloco) ?? []
      lista.push(item)
      porBloco.set(item.bloco, lista)
    }
    return ordemDosBlocos([...porBloco.keys()]).map((bloco) => ({ bloco, itens: porBloco.get(bloco)! }))
  }, [data])

  const salvas = useMemo(() => new Set(data?.chaves ?? []), [data])
  const mudancas = pendentes ? [...pendentes].filter((c) => !salvas.has(c)).length + [...salvas].filter((c) => !pendentes.has(c)).length : 0

  function alternar(chave: ChaveDePermissao) {
    setPendentes((atual) => {
      if (!atual) return atual
      const novo = new Set(atual)
      if (novo.has(chave)) novo.delete(chave)
      else novo.add(chave)
      return novo
    })
  }

  function aplicarModelo(chaves: ChaveDePermissao[]) {
    setPendentes(new Set(chaves))
  }

  function descartar() {
    setPendentes(new Set(salvas))
  }

  async function confirmar() {
    if (!id || !pendentes) return

    // O rótulo enviado é só a descrição do último preset aplicado — a
    // verdade é sempre o conjunto de chaves. Se o conjunto pendente bater
    // exatamente com um dos 6 modelos, aquele é o rótulo; senão, é
    // personalizado (nulo).
    const modeloCorrespondente = data?.modelos.find(
      (m) => m.chaves.length === pendentes.size && m.chaves.every((c) => pendentes.has(c)),
    )

    try {
      await salvar.mutateAsync({ id, modelo: modeloCorrespondente?.modelo ?? null, chaves: [...pendentes] })
      toast.success('Permissões atualizadas.')
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    }
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex items-center gap-3 border-b border-linha bg-branco px-3 py-3 md:px-6">
        <Button variant="ghost" size="icon-sm" onClick={() => navegar('/equipe')} aria-label="Voltar para Equipe">
          <ArrowLeft />
        </Button>
        {atendente ? (
          <>
            <Avatar nome={atendente.nome} tamanho="sm" />
            <div className="min-w-0">
              <p className="truncate text-sm font-semibold text-tinta">{atendente.nome}</p>
              <p className="truncate text-xs text-tinta-media">Permissões desta conta</p>
            </div>
          </>
        ) : carregandoAtendente ? (
          <Skeleton className="h-9 w-40" />
        ) : null}
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto p-3 md:p-6">
        <div className="mx-auto flex max-w-[1400px] flex-col gap-4">
          {isLoading ? (
            <div className="grid gap-3 md:grid-cols-3">
              {[0, 1, 2, 3, 4, 5].map((i) => (
                <Skeleton key={i} className="h-24 rounded-card" />
              ))}
            </div>
          ) : isError || !data || !pendentes ? (
            <EstadoDaInterface
              tipo="erro"
              titulo="Não foi possível carregar as permissões"
              acao={
                <Button variant="outline" onClick={() => void refetch()}>
                  Tentar de novo
                </Button>
              }
            />
          ) : (
            <>
              <section aria-label="Modelos prontos" className="grid grid-cols-2 gap-2.5 sm:grid-cols-3 lg:grid-cols-6">
                {data.modelos.map((modelo) => (
                  <CartaoDeModelo
                    key={modelo.modelo}
                    modelo={modelo}
                    ativo={
                      modelo.chaves.length === pendentes.size && modelo.chaves.every((c) => pendentes.has(c))
                    }
                    aoAplicar={() => aplicarModelo(modelo.chaves)}
                  />
                ))}
              </section>

              <div className="grid gap-4 lg:grid-cols-[1fr_1fr_260px]">
                <div className="flex flex-col gap-3 lg:col-span-2">
                  {blocos.map(({ bloco, itens }) => (
                    <BlocoDePermissoes
                      key={bloco}
                      bloco={bloco}
                      itens={itens}
                      catalogo={data.catalogo}
                      marcadas={pendentes}
                      aoAlternar={alternar}
                      aberto={!mobile || blocoAberto === bloco}
                      aoAlternarAberto={() => setBlocoAberto((atual) => (atual === bloco ? null : bloco))}
                    />
                  ))}
                </div>

                <aside className="hidden lg:block">
                  <div className="sticky top-4 rounded-card border border-linha bg-branco p-3.5 shadow-card">
                    <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-tinta-fraca">O que muda</p>
                    <ResumoDaMudanca catalogoPorChave={catalogoPorChave} salvas={salvas} pendentes={pendentes} />
                  </div>
                </aside>

                {/* Celular: o resumo fica visível ACIMA da barra fixa, não escondido atrás dela. */}
                {mudancas > 0 ? (
                  <div className="rounded-card border border-linha bg-branco p-3.5 shadow-card lg:hidden">
                    <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-tinta-fraca">O que muda</p>
                    <ResumoDaMudanca catalogoPorChave={catalogoPorChave} salvas={salvas} pendentes={pendentes} />
                  </div>
                ) : null}
              </div>
            </>
          )}
        </div>
      </div>

      {data && pendentes ? (
        // Item de flex NORMAL no final da coluna da página — não `fixed`. O
        // rodapé do celular (NavegacaoInferior) já reserva a própria altura
        // dentro do flex do Shell; uma barra fixa aqui desenharia por cima
        // dele em vez de ficar acima, na mesma faixa vertical.
        <div className="shrink-0 border-t border-linha bg-branco/95 px-3 py-2.5 md:px-6">
          <div className="mx-auto flex max-w-[1400px] items-center justify-between gap-3">
            <span className="text-xs font-medium text-tinta-media">
              {mudancas > 0 ? `${mudancas} mudança${mudancas > 1 ? 's' : ''}` : 'Sem mudanças'}
            </span>
            <div className="flex items-center gap-2">
              <Button variant="outline" size="sm" disabled={mudancas === 0 || salvar.isPending} onClick={descartar}>
                Descartar
              </Button>
              <Button size="sm" disabled={mudancas === 0 || salvar.isPending} onClick={() => void confirmar()}>
                Salvar
              </Button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  )
}
