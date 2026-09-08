import { useMemo, useState } from 'react'
import { Search } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { usarAtendentes } from './consultas'
import { LinhaDeAtendente } from './linha-de-atendente'
import type { Atendente } from '@/lib/api/tipos'

type Filtro = 'todos' | 'com-acesso' | 'desativados' | 'administram'

const PILULAS: { chave: Filtro; rotulo: string }[] = [
  { chave: 'todos', rotulo: 'Todos' },
  { chave: 'com-acesso', rotulo: 'Com acesso' },
  { chave: 'desativados', rotulo: 'Desativados' },
  { chave: 'administram', rotulo: 'Administram' },
]

function aplicarFiltro(atendentes: Atendente[], filtro: Filtro): Atendente[] {
  switch (filtro) {
    case 'com-acesso':
      return atendentes.filter((a) => a.ativo)
    case 'desativados':
      return atendentes.filter((a) => !a.ativo)
    case 'administram':
      return atendentes.filter((a) => a.permissoes.includes('equipe.permissoes'))
    default:
      return atendentes
  }
}

function normalizar(texto: string) {
  return texto
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
}

export function ListaDeAtendentes({
  aoEditar,
  aoRedefinirSenha,
  aoAbrirMfa,
  aoExcluir,
}: {
  aoEditar: (atendente: Atendente) => void
  aoRedefinirSenha: (atendente: Atendente) => void
  aoAbrirMfa: (atendente: Atendente) => void
  aoExcluir: (atendente: Atendente) => void
}) {
  const [busca, setBusca] = useState('')
  const [filtro, setFiltro] = useState<Filtro>('todos')
  const { data, isLoading, isError, refetch } = usarAtendentes(true)

  const filtrados = useMemo(() => {
    const base = aplicarFiltro(data ?? [], filtro)
    const alvo = normalizar(busca.trim())
    if (!alvo) return base

    return base.filter(
      (a) => normalizar(a.nome).includes(alvo) || normalizar(a.email).includes(alvo),
    )
  }, [data, filtro, busca])

  if (isLoading) {
    return (
      <div className="flex flex-col gap-2.5">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} className="h-[92px] rounded-card" />
        ))}
      </div>
    )
  }

  if (isError) {
    return (
      <EstadoDaInterface
        tipo="erro"
        titulo="Não foi possível carregar a equipe"
        acao={
          <Button variant="outline" onClick={() => void refetch()}>
            Tentar de novo
          </Button>
        }
      />
    )
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-2.5 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative w-full sm:max-w-xs">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-tinta-fraca" />
          <Input
            value={busca}
            onChange={(e) => setBusca(e.target.value)}
            placeholder="Nome ou e-mail"
            className="w-full rounded-full bg-superficie pl-9 focus-visible:bg-branco"
          />
        </div>

        <div role="group" aria-label="Filtro" className="flex items-center gap-1 self-start rounded-full border border-linha bg-superficie p-1 sm:self-auto">
          {PILULAS.map((p) => (
            <button
              key={p.chave}
              type="button"
              aria-pressed={filtro === p.chave}
              onClick={() => setFiltro(p.chave)}
              className={
                filtro === p.chave
                  ? 'min-h-8 rounded-full bg-tinta px-3 text-xs font-semibold text-branco shadow-card transition-all duration-200'
                  : 'min-h-8 rounded-full px-3 text-xs font-semibold text-tinta-media transition-all duration-200 hover:bg-branco hover:text-tinta'
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
          titulo={busca ? 'Nenhum atendente encontrado' : 'Nenhum atendente neste filtro'}
          acao={
            busca ? (
              <Button variant="outline" onClick={() => setBusca('')}>
                Limpar busca
              </Button>
            ) : undefined
          }
        />
      ) : (
        <div className="grade-com-vida flex flex-col gap-2.5">
          {filtrados.map((atendente) => (
            <LinhaDeAtendente
              key={atendente.id}
              atendente={atendente}
              aoEditar={() => aoEditar(atendente)}
              aoRedefinirSenha={() => aoRedefinirSenha(atendente)}
              aoAbrirMfa={() => aoAbrirMfa(atendente)}
              aoExcluir={() => aoExcluir(atendente)}
            />
          ))}
        </div>
      )}
    </div>
  )
}
