import { useState } from 'react'
import { ChevronsLeft, Search, SlidersHorizontal, X } from 'lucide-react'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
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

/**
 * Ha algum filtro em vigor, alem do padrao "ativas, todas, sem busca"?
 *
 * Vive aqui, e nao na pagina, porque quem define o padrao e este arquivo: o dia
 * que FILTROS_INICIAIS mudar, a resposta muda junto sem ninguem lembrar de dois
 * lugares. O trilho recolhido usa isto para avisar que a lista esta filtrada —
 * com os filtros escondidos, lista curta sem explicacao parece defeito.
 */
export function temFiltroAplicado(filtros: FiltrosVisiveis) {
  return (
    filtros.status !== 'Ativa' ||
    filtros.escopo !== 'todas' ||
    filtros.busca.trim().length > 0 ||
    Boolean(filtros.setorId) ||
    Boolean(filtros.etapaId) ||
    Boolean(filtros.situacaoPrazo) ||
    Boolean(filtros.apenasEncerradasHoje)
  )
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
  aoRecolher,
}: {
  filtros: FiltrosVisiveis
  setores: Setor[]
  etapas: Etapa[]
  aoMudar: (filtros: FiltrosVisiveis) => void
  aoRecolher: () => void
}) {
  function atualizar(parcial: Partial<FiltrosVisiveis>) {
    aoMudar({ ...filtros, ...parcial })
  }

  const temFiltroAvancado =
    Boolean(filtros.setorId) || Boolean(filtros.etapaId) || Boolean(filtros.situacaoPrazo)
  const [filtrosAbertos, setFiltrosAbertos] = useState(false)

  return (
    <div className="flex shrink-0 flex-col gap-2.5 border-b border-linha p-3">
      {/* O botao de recolher divide a LINHA da busca, em vez de ganhar uma
          linha propria: linha nova aqui sairia da altura da lista, que e
          exatamente o que esta mudanca veio devolver. */}
      <div className="flex items-center gap-2">
        <div className="relative min-w-0 flex-1">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-tinta-media" />
          <Input
            value={filtros.busca}
            onChange={(e) => atualizar({ busca: e.target.value })}
            placeholder="Nome, telefone ou protocolo"
            className="pl-8"
          />
        </div>

        <Button
          variant={temFiltroAvancado ? 'secondary' : 'outline'}
          size="icon"
          className="md:hidden"
          onClick={() => setFiltrosAbertos(true)}
          aria-label="Abrir filtros avançados"
          title="Filtros"
        >
          <SlidersHorizontal />
          <span className="sr-only">Filtros</span>
        </Button>

        <button
          type="button"
          onClick={aoRecolher}
          aria-expanded
          aria-label="Recolher a lista de conversas"
          title="Recolher a lista de conversas"
          className="hidden size-9 shrink-0 items-center justify-center rounded-md border border-linha text-tinta-media transition-colors hover:border-linha-forte hover:bg-superficie hover:text-tinta md:flex"
        >
          <ChevronsLeft className="size-4" />
        </button>
      </div>

      {/* Grupo segmentado. O estado ativo antes era bg-card sobre bg-muted —
          branco sobre superfície, dois tons vizinhos na paleta de papel, e o
          botão seiecionado ficava indistinguível.

          A distinção agora vem de BORDA + PESO, não de matiz: é o mesmo
          tratamento que tabs.tsx e o Indicador do painel já usam. Bordô ficou
          fora de propósito — a inbox já tem três ocorrências (menu ativo, Nova
          conversa, Enviar) e DESIGN.md 2.1 manda usar tinta ou linha-forte a
          partir daí. `aria-pressed` deixa o estado audível para leitor de tela,
          já que cor sozinha nunca é sinal suficiente. */}
      <div
        role="group"
        aria-label="Escopo das conversas"
        className="flex items-center gap-1 rounded-md border border-linha bg-muted p-0.5"
      >
        {escopos.map(({ valor, rotulo }) => {
          const ativo = filtros.escopo === valor && filtros.status === 'Ativa'

          return (
            <button
              key={valor}
              type="button"
              aria-pressed={ativo}
              onClick={() =>
                atualizar({ escopo: valor, status: 'Ativa', apenasEncerradasHoje: false })
              }
              className={cn(
                'flex min-h-10 flex-1 items-center justify-center rounded-md border px-2 py-1 text-xs transition-colors md:min-h-0',
                ativo
                  ? 'border-linha-forte bg-branco font-semibold text-tinta'
                  : 'border-transparent font-medium text-tinta-media hover:bg-branco/60 hover:text-tinta',
              )}
            >
              {rotulo}
            </button>
          )
        })}

        <button
          type="button"
          aria-pressed={filtros.status === 'Encerrada'}
          onClick={() =>
            atualizar({
              status: filtros.status === 'Ativa' ? 'Encerrada' : 'Ativa',
              apenasEncerradasHoje: false,
            })
          }
          className={cn(
            'flex min-h-10 flex-1 items-center justify-center rounded-md border px-2 py-1 text-xs transition-colors md:min-h-0',
            filtros.status === 'Encerrada'
              ? 'border-linha-forte bg-branco font-semibold text-tinta'
              : 'border-transparent font-medium text-tinta-media hover:bg-branco/60 hover:text-tinta',
          )}
          title="Conversas encerradas continuam consultáveis — nada é apagado"
        >
          {filtros.apenasEncerradasHoje ? 'Encerradas hoje' : 'Encerradas'}
        </button>
      </div>

      <div className="hidden md:block">
        <CamposDeFiltroAvancado
          filtros={filtros}
          etapas={etapas}
          setores={setores}
          aoMudar={atualizar}
          temFiltroAvancado={temFiltroAvancado}
        />
      </div>

      <Dialog open={filtrosAbertos} onOpenChange={setFiltrosAbertos}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Filtrar atendimentos</DialogTitle>
            <DialogDescription>
              Refine a lista sem perder a busca e o escopo que já estão selecionados.
            </DialogDescription>
          </DialogHeader>
          <CamposDeFiltroAvancado
            filtros={filtros}
            etapas={etapas}
            setores={setores}
            aoMudar={atualizar}
            temFiltroAvancado={temFiltroAvancado}
          />
          <Button variant="outline" onClick={() => setFiltrosAbertos(false)}>
            Concluir
          </Button>
        </DialogContent>
      </Dialog>
    </div>
  )
}

function CamposDeFiltroAvancado({
  filtros,
  etapas,
  setores,
  aoMudar,
  temFiltroAvancado,
}: {
  filtros: FiltrosVisiveis
  etapas: Etapa[]
  setores: Setor[]
  aoMudar: (parcial: Partial<FiltrosVisiveis>) => void
  temFiltroAvancado: boolean
}) {
  return (
    <div className="flex flex-col gap-2">
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        <Select
          value={filtros.etapaId ?? TODOS}
          onValueChange={(v) => aoMudar({ etapaId: v === TODOS ? undefined : v })}
        >
          <SelectTrigger className="text-xs md:text-xs">
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
          onValueChange={(v) => aoMudar({ setorId: v === TODOS ? undefined : v })}
        >
          <SelectTrigger className="text-xs md:text-xs">
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
            aoMudar({ situacaoPrazo: v === TODOS ? undefined : (v as SituacaoPrazo) })
          }
        >
          <SelectTrigger className="flex-1 text-xs md:text-xs">
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
            size="icon"
            title="Limpar filtros"
            onClick={() => aoMudar({ setorId: undefined, etapaId: undefined, situacaoPrazo: undefined })}
          >
            <X />
            <span className="sr-only">Limpar filtros</span>
          </Button>
        ) : null}
      </div>
    </div>
  )
}
