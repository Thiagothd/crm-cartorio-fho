import { useState } from 'react'
import { ChevronsLeft, HandCoins, Search, SlidersHorizontal, X } from 'lucide-react'
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
import { DialogoNovaConversa } from './dialogo-nova-conversa'
import type { Etapa, FiltroDeConversas, Setor, SituacaoPrazo, StatusConversa } from '@/lib/api/tipos'

const TODOS = '__todos__'

export interface FiltrosVisiveis {
  status: StatusConversa
  setorId?: string
  etapaId?: string
  situacaoPrazo?: SituacaoPrazo
  apenasEncerradasHoje?: boolean
  apenasAguardandoResposta?: boolean
  apenasDevedores?: boolean
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
    apenasAguardandoResposta: filtros.apenasAguardandoResposta,
    apenasDevedores: filtros.apenasDevedores,
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
    Boolean(filtros.apenasEncerradasHoje) ||
    Boolean(filtros.apenasAguardandoResposta) ||
    Boolean(filtros.apenasDevedores)
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
  podeVerDevedores = false,
  aoMudar,
  aoRecolher,
  aoCriarConversa,
}: {
  filtros: FiltrosVisiveis
  setores: Setor[]
  etapas: Etapa[]
  podeVerDevedores?: boolean
  aoMudar: (filtros: FiltrosVisiveis) => void
  aoRecolher: () => void
  /**
   * "+ Novo atendimento" saiu do cabeçalho global na Fase 3 — o cabeçalho é
   * o mesmo em toda a aplicação, e essa ação só faz sentido aqui. Mora na
   * barra de filtros, não como botão isolado: é onde o atendente já está
   * olhando quando decide abrir um atendimento manual.
   */
  aoCriarConversa: (conversaId: string) => void
}) {
  function atualizar(parcial: Partial<FiltrosVisiveis>) {
    aoMudar({ ...filtros, ...parcial })
  }

  const temFiltroAvancado =
    Boolean(filtros.setorId) ||
    Boolean(filtros.etapaId) ||
    Boolean(filtros.situacaoPrazo) ||
    Boolean(filtros.apenasDevedores)
  const [filtrosAbertos, setFiltrosAbertos] = useState(false)

  return (
    <div className="flex shrink-0 flex-col gap-2.5 border-b border-linha p-3">
      {/* Linha 1 — busca em largura cheia. Pedido do Thiago em 2026-09-07:
          antes dividia a linha com "Novo atendimento", ficando espremida. */}
      <div className="relative w-full">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-tinta-fraca" />
        {/* Fora do preenchimento automatico do navegador. Achado em producao
            em 2026-10-28: o Chrome classificou o campo como "nome" pelo
            placeholder e o preencheu com o nome completo do proprio cartório parceiro — a
            lista ficou em "0 de 0" com 5 ativas, e parecia que as conversas
            tinham sumido. type="search" + autoComplete="off" + name neutro
            tiram o campo da heuristica de endereco/contato; data-1p-ignore e
            data-lpignore fazem o mesmo nos gerenciadores de senha. */}
        <Input
          type="search"
          name="busca-da-inbox"
          autoComplete="off"
          autoCorrect="off"
          spellCheck={false}
          data-1p-ignore
          data-lpignore="true"
          aria-label="Buscar atendimento por nome, telefone ou protocolo"
          value={filtros.busca}
          onChange={(e) => atualizar({ busca: e.target.value })}
          placeholder="Nome, telefone ou protocolo"
          // Fase 3.2: a coluna da lista virou cartao bg-branco (secao 16) — o
          // Input padrao tambem e bg-card (branco) e sumiria encaixado nela
          // sem este override local. O pr-9 abre espaco para o "limpar", e o
          // seletor esconde o X nativo do type="search" para nao aparecerem dois.
          className="w-full rounded-full bg-superficie pl-9 pr-9 focus-visible:bg-branco [&::-webkit-search-cancel-button]:appearance-none"
        />
        {filtros.busca ? (
          <button
            type="button"
            onClick={() => atualizar({ busca: '' })}
            aria-label="Limpar busca"
            title="Limpar busca"
            className="absolute right-2 top-1/2 flex size-6 -translate-y-1/2 items-center justify-center rounded-full text-tinta-fraca transition-colors hover:bg-branco hover:text-tinta"
          >
            <X className="size-3.5" />
          </button>
        ) : null}
      </div>

      {/* Linha 2 — "Novo atendimento" ancora a esquerda (e' a acao primaria
          desta barra); os controles de utilidade (filtro, recolher) vao para
          a direita, agrupados. Pedido do Thiago em 2026-09-07: antes os
          quatro elementos disputavam a mesma linha da busca e quebravam em
          ordem imprevisivel pelo flex-wrap. */}
      <div className="flex items-center justify-between gap-2">
        <DialogoNovaConversa aoCriar={aoCriarConversa} />

        <div className="flex items-center gap-1.5">
          {podeVerDevedores ? (
            <Button
              variant={filtros.apenasDevedores ? 'secondary' : 'outline'}
              size="icon"
              className={cn(
                'relative',
                filtros.apenasDevedores && 'border-ambar/50 bg-ambar/15 text-ambar hover:bg-ambar/20',
              )}
              onClick={() => atualizar({ apenasDevedores: !filtros.apenasDevedores })}
              aria-pressed={Boolean(filtros.apenasDevedores)}
              aria-label="Filtrar conversas de devedores"
              title={
                filtros.apenasDevedores
                  ? 'Mostrando apenas clientes com pendência financeira (clique para limpar)'
                  : 'Filtrar conversas de clientes com pendência financeira'
              }
            >
              <HandCoins />
              <span className="sr-only">Devedores</span>
            </Button>
          ) : null}

          {/* Fase 3.2: antes so' aparecia abaixo de md (os selects inline
              cobriam o desktop). Agora e' o unico caminho para os filtros
              avancados em qualquer largura — CamposDeFiltroAvancado inline
              foi removido, e o Dialog abaixo passa a ser a unica casa deles.
              Isso devolve ~80px a lista (secao 16). */}
          <Button
            variant={temFiltroAvancado ? 'secondary' : 'outline'}
            size="icon"
            className="relative"
            onClick={() => setFiltrosAbertos(true)}
            aria-label="Abrir filtros avançados"
            title="Filtros"
          >
            <SlidersHorizontal />
            {temFiltroAvancado ? (
              <span aria-hidden className="absolute -right-0.5 -top-0.5 size-2 rounded-full bg-tinta" />
            ) : null}
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
      </div>

      {/* Grupo segmentado — Fase 3.2 (docs/DESIGN.md, secao 16): mesma pilula
          de seletor-de-periodo.tsx. Continua sem bordô de propósito — a inbox
          já soma 3 ocorrências entre menu ativo, badge de não lidas e Enviar
          (DESIGN.md 2.1). `aria-pressed` deixa o estado audível para leitor
          de tela, já que cor sozinha nunca é sinal suficiente. */}
      <div
        role="group"
        aria-label="Escopo das conversas"
        className="flex items-center gap-1 rounded-full border border-linha bg-superficie p-1"
      >
        {escopos.map(({ valor, rotulo }) => {
          const ativo = filtros.escopo === valor && filtros.status === 'Ativa'

          return (
            <button
              key={valor}
              type="button"
              aria-pressed={ativo}
              onClick={() =>
                atualizar({
                  escopo: valor,
                  status: 'Ativa',
                  apenasEncerradasHoje: false,
                  apenasAguardandoResposta: false,
                })
              }
              className={cn(
                'flex min-h-10 flex-1 items-center justify-center rounded-full px-2 py-1 text-xs transition-colors md:min-h-0',
                ativo
                  ? 'bg-selecao font-semibold text-sobre-selecao shadow-card'
                  : 'font-medium text-tinta-media hover:bg-branco hover:text-tinta',
              )}
            >
              {rotulo}
            </button>
          )
        })}

        {/* Os quatro botões são UM grupo exclusivo: cada um é uma vista
            inteira, nunca uma camada sobre a anterior. "Encerradas" precisa
            zerar escopo e recortes junto — sem isso, quem tinha clicado em
            Minhas ou Fila antes seguia com esse recorte aplicado por baixo,
            vendo só parte das encerradas, e NENHUMA pílula acesa dizia por
            quê (o `ativo` das outras três exige status 'Ativa'). Achado em
            produção em 2026-09-09. */}
        <button
          type="button"
          aria-pressed={filtros.status === 'Encerrada'}
          onClick={() =>
            atualizar({
              status: 'Encerrada',
              escopo: 'todas',
              apenasEncerradasHoje: false,
              apenasAguardandoResposta: false,
            })
          }
          className={cn(
            'flex min-h-10 flex-1 items-center justify-center rounded-full px-2 py-1 text-xs transition-colors md:min-h-0',
            filtros.status === 'Encerrada'
              ? 'bg-selecao font-semibold text-sobre-selecao shadow-card'
              : 'font-medium text-tinta-media hover:bg-branco hover:text-tinta',
          )}
          title="Conversas encerradas continuam consultáveis — nada é apagado"
        >
          {filtros.apenasEncerradasHoje ? 'Encerradas hoje' : 'Encerradas'}
        </button>
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
            podeVerDevedores={podeVerDevedores}
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
  podeVerDevedores,
  aoMudar,
  temFiltroAvancado,
}: {
  filtros: FiltrosVisiveis
  etapas: Etapa[]
  setores: Setor[]
  podeVerDevedores: boolean
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
            onClick={() =>
              aoMudar({
                setorId: undefined,
                etapaId: undefined,
                situacaoPrazo: undefined,
                apenasDevedores: undefined,
              })
            }
          >
            <X />
            <span className="sr-only">Limpar filtros</span>
          </Button>
        ) : null}
      </div>

      {podeVerDevedores ? (
        <button
          type="button"
          onClick={() => aoMudar({ apenasDevedores: !filtros.apenasDevedores })}
          aria-pressed={Boolean(filtros.apenasDevedores)}
          className={cn(
            'mt-1 flex items-center justify-between rounded-md border px-3 py-2 text-left text-xs font-medium transition-colors',
            filtros.apenasDevedores
              ? 'border-ambar/50 bg-ambar/15 text-ambar'
              : 'border-linha bg-superficie text-tinta-media hover:border-linha-forte hover:text-tinta',
          )}
        >
          <span className="flex items-center gap-2">
            <HandCoins className="size-3.5 shrink-0" />
            Apenas clientes com pendência financeira (devedores)
          </span>
          <span className="font-dado text-[11px]">
            {filtros.apenasDevedores ? 'Ativo' : 'Filtrar'}
          </span>
        </button>
      ) : null}
    </div>
  )
}
