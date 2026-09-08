import { useState } from 'react'
import { GitBranch, Plus } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { etapasApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { LinhaDeEtapa } from './linha-de-etapa'
import { usarEtapasDeGestao, usarInvalidarEstrutura } from './consultas-de-estrutura'
import type { Etapa } from '@/lib/api/tipos'

export function ListaDeEtapas({
  aoEditar,
  aoCriar,
}: {
  aoEditar: (etapa: Etapa) => void
  aoCriar: () => void
}) {
  const { data, isLoading, isError, refetch } = usarEtapasDeGestao()
  const invalidar = usarInvalidarEstrutura()

  // Duas reordenações em voo brigariam pela mesma lista: a segunda sairia do
  // estado ANTERIOR à primeira e desfaria o movimento sem avisar ninguém.
  const [reordenando, setReordenando] = useState(false)

  async function mover(indice: number, direcao: -1 | 1) {
    if (!data || reordenando) return

    const destino = indice + direcao
    if (destino < 0 || destino >= data.length) return

    const ordenadas = [...data]
    const [movida] = ordenadas.splice(indice, 1)
    ordenadas.splice(destino, 0, movida)

    setReordenando(true)

    try {
      await etapasApi.reordenar(ordenadas.map((e) => e.id))
      invalidar('etapas')
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível reordenar.')
    } finally {
      setReordenando(false)
    }
  }

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
        titulo="Não foi possível carregar as etapas"
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
        icone={GitBranch}
        titulo="Nenhuma etapa cadastrada"
        descricao="Sem pelo menos uma etapa ativa, conversas novas nasceriam sem ponto nenhum no funil."
        acao={
          <Button size="sm" onClick={aoCriar}>
            <Plus />
            Criar a primeira
          </Button>
        }
      />
    )
  }

  return (
    // `<ol>` e não `<ul>`: a ordem aqui É o funil, e é isso que um leitor de
    // tela precisa ouvir junto com o nome da etapa.
    <ol aria-label="Etapas do funil" className="grade-com-vida flex flex-col gap-2.5">
      {data.map((etapa, indice) => (
        <LinhaDeEtapa
          key={etapa.id}
          etapa={etapa}
          primeira={indice === 0}
          ultima={indice === data.length - 1}
          aoMover={(direcao) => void mover(indice, direcao)}
          aoEditar={() => aoEditar(etapa)}
        />
      ))}
    </ol>
  )
}
