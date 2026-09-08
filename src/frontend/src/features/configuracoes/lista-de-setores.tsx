import { Building2, Plus } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { LinhaDeSetor } from './linha-de-setor'
import { usarSetoresDeGestao } from './consultas-de-estrutura'
import type { Setor } from '@/lib/api/tipos'

export function ListaDeSetores({
  aoEditar,
  aoCriar,
}: {
  aoEditar: (setor: Setor) => void
  aoCriar: () => void
}) {
  const { data, isLoading, isError, refetch } = usarSetoresDeGestao()

  if (isLoading) {
    return (
      <div className="flex flex-col gap-2.5">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} className="h-[68px] rounded-card" />
        ))}
      </div>
    )
  }

  if (isError) {
    return (
      <EstadoDaInterface
        tipo="erro"
        titulo="Não foi possível carregar os setores"
        acao={
          <Button variant="outline" onClick={() => void refetch()}>
            Tentar de novo
          </Button>
        }
      />
    )
  }

  if (!data?.length) {
    return (
      <EstadoDaInterface
        tipo="vazio"
        icone={Building2}
        titulo="Nenhum setor cadastrado"
        descricao="O menu que o cliente recebe no primeiro contato é montado com os setores ativos — sem nenhum, não há para onde rotear."
        acao={
          <Button size="sm" onClick={aoCriar}>
            <Plus />
            Criar o primeiro
          </Button>
        }
      />
    )
  }

  return (
    // A ordem também é dado aqui: define a posição no menu que o cliente
    // recebe no primeiro contato.
    <ol aria-label="Setores da serventia" className="grade-com-vida flex flex-col gap-2.5">
      {data.map((setor) => (
        <LinhaDeSetor key={setor.id} setor={setor} aoEditar={() => aoEditar(setor)} />
      ))}
    </ol>
  )
}
