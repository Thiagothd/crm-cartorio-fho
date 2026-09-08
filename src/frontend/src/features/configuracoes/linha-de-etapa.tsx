import { ArrowDown, ArrowUp, Flag, Pencil } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { coresDaEtapa } from '@/features/atendimento/cores-de-etapa'
import { cn } from '@/lib/utils'
import type { Etapa } from '@/lib/api/tipos'

/**
 * Uma etapa como linha-cartão — e em coluna única, de propósito: aqui a ordem
 * vertical É a ordem do funil. Numa grade que reflui, "para cima" deixaria de
 * querer dizer "antes no atendimento", que é a única coisa que esta tela
 * precisa deixar óbvia.
 *
 * O marcador da esquerda é a mini-pílula preenchida da seção 2.2 do DESIGN.md
 * — o mesmo trio fundo/tinta/borda que a inbox usa —, carregando a posição no
 * funil. Nenhuma etapa usa bordô (2.1), então preencher é seguro.
 */
export function LinhaDeEtapa({
  etapa,
  primeira,
  ultima,
  aoMover,
  aoEditar,
}: {
  etapa: Etapa
  primeira: boolean
  ultima: boolean
  aoMover: (direcao: -1 | 1) => void
  aoEditar: () => void
}) {
  const cores = coresDaEtapa(etapa.cor)

  return (
    <li
      className={cn(
        'group flex items-center gap-3 rounded-card border border-linha bg-branco p-3 shadow-card transition-all duration-200 hover:-translate-y-0.5 hover:shadow-card-hover sm:gap-3.5 sm:p-3.5',
        !etapa.ativo && 'opacity-60',
      )}
    >
      <span
        aria-hidden
        className="flex size-9 shrink-0 items-center justify-center rounded-full border font-dado text-xs font-semibold"
        style={{ backgroundColor: cores.fundo, color: cores.texto, borderColor: cores.borda }}
      >
        {etapa.ordem}
      </span>

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="truncate font-medium text-tinta">{etapa.nome}</span>
          {etapa.ehFinal ? (
            <Badge variant="success">
              <Flag className="size-3" />
              Conclusão
            </Badge>
          ) : null}
          {!etapa.ativo ? <Badge variant="secondary">Inativa</Badge> : null}
        </div>

        {etapa.descricao ? (
          <p className="truncate text-xs text-tinta-media">{etapa.descricao}</p>
        ) : null}

        <p className="mt-1 text-[11px] text-tinta-fraca sm:hidden">
          {etapa.conversasAtivas} {etapa.conversasAtivas === 1 ? 'conversa ativa' : 'conversas ativas'}
        </p>
      </div>

      <div className="hidden shrink-0 text-right sm:block">
        <span className="block font-dado text-sm font-semibold leading-none text-tinta">
          {etapa.conversasAtivas}
        </span>
        <span className="mt-1 block text-[11px] text-tinta-fraca">
          {etapa.conversasAtivas === 1 ? 'ativa' : 'ativas'}
        </span>
      </div>

      {/* Sempre visíveis, diferente de Equipe: reordenar é a ação PRINCIPAL
          desta tela, não uma ação secundária de linha. Esconder no hover
          esconderia o que se veio fazer aqui. */}
      <div className="flex shrink-0 items-center gap-0.5">
        <Button
          variant="ghost"
          size="icon-sm"
          disabled={primeira}
          onClick={() => aoMover(-1)}
          aria-label={`Mover ${etapa.nome} para antes`}
          title="Mover para antes"
        >
          <ArrowUp />
        </Button>
        <Button
          variant="ghost"
          size="icon-sm"
          disabled={ultima}
          onClick={() => aoMover(1)}
          aria-label={`Mover ${etapa.nome} para depois`}
          title="Mover para depois"
        >
          <ArrowDown />
        </Button>
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={aoEditar}
          aria-label={`Editar ${etapa.nome}`}
          title="Editar"
        >
          <Pencil />
        </Button>
      </div>
    </li>
  )
}
