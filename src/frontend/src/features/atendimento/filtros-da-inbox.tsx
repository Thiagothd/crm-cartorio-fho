import { Search, X } from 'lucide-react'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { cn } from '@/lib/utils'
import type { Etapa, FiltroDeConversas, Setor, SituacaoPrazo, StatusConversa } from '@/lib/api/tipos'

const TODOS = '__todos__'

export interface FiltrosVisiveis {
  status: StatusConversa
  setorId?: string
  etapaId?: string
  situacaoPrazo?: SituacaoPrazo
  apenasEncerradasHoje?: boolean
  escopo: 'todas' | 'minhas' | 'fila'
  busca: string
}

export function filtrosParaConsulta(filtros: FiltrosVisiveis): FiltroDeConversas {
  return {
    status: filtros.status,
    setorId: filtros.setorId,
    etapaId: filtros.etapaId,
    situacaoPrazo: filtros.situacaoPrazo,
    apenasMinhas: filtros.escopo === 'minhas',
    apenasNaFila: filtros.escopo === 'fila',
    apenasEncerradasHoje: filtros.apenasEncerradasHoje,
    busca: filtros.busca.trim() || undefined,
    tamanho: 60,
  }
}

const escopos: { valor: FiltrosVisiveis['escopo']; rotulo: string }[] = [
  { valor: 'todas', rotulo: 'Todas' },
  { valor: 'minhas', rotulo: 'Minhas' },
  { valor: 'fila', rotulo: 'Fila' },
]

export function FiltrosDaInbox({
  filtros,
  setores,
  etapas,
  aoMudar,
}: {
  filtros: FiltrosVisiveis
  setores: Setor[]
  etapas: Etapa[]
  aoMudar: (filtros: FiltrosVisiveis) => void
}) {
  function atualizar(parcial: Partial<FiltrosVisiveis>) {
    aoMudar({ ...filtros, ...parcial })
  }

  const temFiltroAvancado =
    Boolean(filtros.setorId) || Boolean(filtros.etapaId) || Boolean(filtros.situacaoPrazo)

  return (
    <div className="flex flex-col gap-2.5 border-b border-linha p-3">
      <div className="relative">
        <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-tinta-media" />
        <Input
          value={filtros.busca}
          onChange={(e) => atualizar({ busca: e.target.value })}
          placeholder="Nome, telefone ou protocolo"
          className="pl-8"
        />
      </div>

      <div className="flex items-center gap-1 rounded-md bg-muted p-0.5">
        {escopos.map(({ valor, rotulo }) => (
          <button
          key={valor}
          type="button"
            onClick={() => atualizar({ escopo: valor, apenasEncerradasHoje: false })}
            className={cn(
              'flex-1 rounded-md px-2 py-1 text-xs font-medium transition-colors',
              filtros.escopo === valor
                ? 'bg-card text-tinta'
                : 'text-tinta-media hover:text-tinta',
            )}
          >
            {rotulo}
          </button>
        ))}

        <button
          type="button"
          onClick={() =>
            atualizar({
              status: filtros.status === 'Ativa' ? 'Encerrada' : 'Ativa',
              apenasEncerradasHoje: false,
            })
          }
          className={cn(
            'flex-1 rounded-md px-2 py-1 text-xs font-medium transition-colors',
            filtros.status === 'Encerrada' && !filtros.apenasEncerradasHoje
              ? 'bg-card text-tinta'
              : 'text-tinta-media hover:text-tinta',
          )}
          title="Conversas encerradas continuam consultáveis — nada é apagado"
        >
          {filtros.apenasEncerradasHoje ? 'Encerradas hoje' : 'Encerradas'}
        </button>
      </div>

      <div className="grid grid-cols-2 gap-2">
        <Select
          value={filtros.etapaId ?? TODOS}
          onValueChange={(v) => atualizar({ etapaId: v === TODOS ? undefined : v })}
        >
          <SelectTrigger className="h-8 text-xs">
            <SelectValue placeholder="Etapa" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={TODOS}>Todas as etapas</SelectItem>
            {etapas.map((etapa) => (
              <SelectItem key={etapa.id} value={etapa.id}>
                {etapa.nome}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select
          value={filtros.setorId ?? TODOS}
          onValueChange={(v) => atualizar({ setorId: v === TODOS ? undefined : v })}
        >
          <SelectTrigger className="h-8 text-xs">
            <SelectValue placeholder="Setor" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={TODOS}>Todos os setores</SelectItem>
            {setores.map((setor) => (
              <SelectItem key={setor.id} value={setor.id}>
                {setor.nome}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="flex items-center gap-2">
        <Select
          value={filtros.situacaoPrazo ?? TODOS}
          onValueChange={(v) =>
            atualizar({ situacaoPrazo: v === TODOS ? undefined : (v as SituacaoPrazo) })
          }
        >
          <SelectTrigger className="h-8 flex-1 text-xs">
            <SelectValue placeholder="Prazo" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={TODOS}>Qualquer prazo</SelectItem>
            <SelectItem value="Atrasada">Atrasadas</SelectItem>
            <SelectItem value="EmAlerta">Em alerta</SelectItem>
            <SelectItem value="EmDia">No prazo</SelectItem>
            <SelectItem value="SemPrazo">Sem prazo definido</SelectItem>
          </SelectContent>
        </Select>

        {temFiltroAvancado ? (
          <Button
            variant="ghost"
            size="icon-sm"
            title="Limpar filtros"
            onClick={() => atualizar({ setorId: undefined, etapaId: undefined, situacaoPrazo: undefined })}
          >
            <X />
          </Button>
        ) : null}
      </div>
    </div>
  )
}
