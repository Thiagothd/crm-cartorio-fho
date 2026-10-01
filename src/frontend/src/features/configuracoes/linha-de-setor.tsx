import { Pencil } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import type { Setor } from '@/lib/api/tipos'
import { BotaoDoRoboNoSetor } from './botao-do-robo-no-setor'

/**
 * Mesma linha-cartão de `LinhaDeEtapa`, sem as setas: setor não tem endpoint
 * de reordenação — a ordem se ajusta pelo campo do cadastro. O marcador da
 * esquerda fica neutro, e isso também é regra: cor de etapa não aparece em
 * listagem que não é de etapa (DESIGN.md 2.2).
 */
export function LinhaDeSetor({
  setor,
  aoEditar,
}: {
  setor: Setor
  aoEditar: () => void
}) {
  return (
    <li
      className={cn(
        'group flex items-center gap-3 rounded-card border border-linha bg-branco p-3 shadow-card transition-all duration-200 hover:-translate-y-0.5 hover:shadow-card-hover sm:gap-3.5 sm:p-3.5',
        !setor.ativo && 'opacity-60',
      )}
    >
      <span
        aria-hidden
        className="flex size-9 shrink-0 items-center justify-center rounded-full border border-linha bg-superficie font-dado text-xs font-semibold text-tinta-media"
      >
        {setor.ordem}
      </span>

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="truncate font-medium text-tinta">{setor.nome}</span>
          {!setor.ativo ? <Badge variant="secondary">Inativo</Badge> : null}

        </div>

        {setor.descricao ? (
          <p className="truncate text-xs text-tinta-media">{setor.descricao}</p>
        ) : null}

        <p className="mt-1 text-[11px] text-tinta-fraca sm:hidden">
          {setor.conversasAtivas} {setor.conversasAtivas === 1 ? 'conversa ativa' : 'conversas ativas'}
        </p>
      </div>

      <div className="hidden shrink-0 text-right sm:block">
        <span className="block font-dado text-sm font-semibold leading-none text-tinta">
          {setor.conversasAtivas}
        </span>
        <span className="mt-1 block text-[11px] text-tinta-fraca">
          {setor.conversasAtivas === 1 ? 'ativa' : 'ativas'}
        </span>
      </div>

      {/* O robô do setor à vista e a um clique — exceção que se esquece ligada,
          por isso âmbar quando não está no normal (DESIGN.md, seção 5). */}
      <BotaoDoRoboNoSetor setor={setor} />

      <Button
        variant="ghost"
        size="icon-sm"
        className="shrink-0"
        onClick={aoEditar}
        aria-label={`Editar ${setor.nome}`}
        title="Editar"
      >
        <Pencil />
      </Button>
    </li>
  )
}
