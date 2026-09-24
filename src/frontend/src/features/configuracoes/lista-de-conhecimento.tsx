import { useMemo, useState } from 'react'
import { Search } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { CartaoDeConhecimento } from './cartao-de-conhecimento'
import { normalizarTexto } from './normalizar-texto'
import { estaEmUsoPelaIa, estaForaDeVigencia, hojeIso } from './vigencia-de-conhecimento'
import type { ConhecimentoDeIa } from '@/lib/api/tipos'

type Filtro = 'todos' | 'em-uso' | 'fora-de-vigencia' | 'desativados'

const PILULAS: { chave: Filtro; rotulo: string }[] = [
  { chave: 'todos', rotulo: 'Todos' },
  { chave: 'em-uso', rotulo: 'Em uso' },
  { chave: 'fora-de-vigencia', rotulo: 'Fora de vigência' },
  { chave: 'desativados', rotulo: 'Desativados' },
]

function aplicarFiltro(conhecimentos: ConhecimentoDeIa[], filtro: Filtro): ConhecimentoDeIa[] {
  const hoje = hojeIso()

  switch (filtro) {
    case 'em-uso':
      return conhecimentos.filter((c) => estaEmUsoPelaIa(c, hoje))
    case 'fora-de-vigencia':
      return conhecimentos.filter((c) => estaForaDeVigencia(c, hoje))
    case 'desativados':
      return conhecimentos.filter((c) => !c.ativo)
    default:
      return conhecimentos
  }
}

/**
 * Busca e filtro no mesmo arranjo de `ListaDeAtendentes` e das abas de
 * Mensagens — a tela de administração inteira usa o mesmo par para o dono não
 * reaprender o controle a cada destino.
 *
 * A busca varre também o CONTEÚDO, não só o título: quem abre esta tela
 * costuma lembrar do que escreveu ("onde falei em cartório de notas"), não de
 * como nomeou o cadastro.
 */
export function ListaDeConhecimento({
  conhecimentos,
  aoEditar,
  aoVerHistorico,
}: {
  conhecimentos: ConhecimentoDeIa[]
  aoEditar: (conhecimento: ConhecimentoDeIa) => void
  aoVerHistorico: (conhecimento: ConhecimentoDeIa) => void
}) {
  const [busca, setBusca] = useState('')
  const [filtro, setFiltro] = useState<Filtro>('todos')

  const filtrados = useMemo(() => {
    const base = aplicarFiltro(conhecimentos, filtro)
    const alvo = normalizarTexto(busca.trim())
    if (!alvo) return base

    return base.filter((c) =>
      [c.titulo, c.tema, c.conteudo, c.tipoServicoNome ?? ''].some((campo) =>
        normalizarTexto(campo).includes(alvo),
      ),
    )
  }, [conhecimentos, filtro, busca])

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-2.5 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative w-full sm:max-w-xs">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-tinta-fraca" />
          <Input
            value={busca}
            onChange={(e) => setBusca(e.target.value)}
            placeholder="Título, tema ou texto"
            className="w-full rounded-full bg-superficie pl-9 focus-visible:bg-branco"
          />
        </div>

        <div
          role="group"
          aria-label="Filtro"
          className="flex items-center gap-1 self-start overflow-x-auto rounded-full border border-linha bg-superficie p-1 sm:self-auto"
        >
          {PILULAS.map((p) => (
            <button
              key={p.chave}
              type="button"
              aria-pressed={filtro === p.chave}
              onClick={() => setFiltro(p.chave)}
              className={
                filtro === p.chave
                  ? 'min-h-8 shrink-0 rounded-full bg-selecao px-3 text-xs font-semibold text-sobre-selecao shadow-card transition-all duration-200'
                  : 'min-h-8 shrink-0 rounded-full px-3 text-xs font-semibold text-tinta-media transition-all duration-200 hover:bg-branco hover:text-tinta'
              }
            >
              {p.rotulo}
            </button>
          ))}
        </div>
      </div>

      {filtrados.length === 0 ? (
        <EstadoDaInterface
          tipo="sem-resultados"
          titulo={busca ? 'Nenhum conhecimento encontrado' : 'Nenhum conhecimento neste filtro'}
          acao={
            busca ? (
              <Button variant="outline" onClick={() => setBusca('')}>
                Limpar busca
              </Button>
            ) : undefined
          }
        />
      ) : (
        <div className="grade-com-vida grid gap-2.5 @3xl:grid-cols-2 @6xl:grid-cols-3">
          {filtrados.map((conhecimento) => (
            <CartaoDeConhecimento
              key={conhecimento.id}
              conhecimento={conhecimento}
              aoEditar={() => aoEditar(conhecimento)}
              aoVerHistorico={() => aoVerHistorico(conhecimento)}
            />
          ))}
        </div>
      )}
    </div>
  )
}
