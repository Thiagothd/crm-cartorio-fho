import { CalendarOff, History, Pencil } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { descreverVigencia, estaForaDeVigencia } from './vigencia-de-conhecimento'
import type { ConhecimentoDeIa } from '@/lib/api/tipos'

/**
 * Cartão, não linha de tabela — mesma razão registrada em 17.4 para Mensagens:
 * o dado principal aqui é um parágrafo de até 2000 caracteres, e parágrafo em
 * célula ou vira rolagem horizontal ou vira uma linha cortada. Na tabela
 * antiga o conteúdo nem coluna tinha: só aparecia no celular, em duas linhas.
 */
export function CartaoDeConhecimento({
  conhecimento,
  aoEditar,
  aoVerHistorico,
}: {
  conhecimento: ConhecimentoDeIa
  aoEditar: () => void
  aoVerHistorico: () => void
}) {
  const fora = estaForaDeVigencia(conhecimento)
  const temJanela = conhecimento.vigenteDesde !== null || conhecimento.vigenteAte !== null

  return (
    <article
      aria-label={conhecimento.titulo}
      className={cn(
        'group flex h-full flex-col gap-2 rounded-card border border-linha bg-branco p-3.5 shadow-card transition-all duration-200 hover:-translate-y-0.5 hover:shadow-card-hover',
        !conhecimento.ativo && 'opacity-70',
      )}
    >
      <div className="flex items-start gap-2">
        <div className="min-w-0 flex-1">
          <h3 className="text-[13px] font-semibold leading-tight text-tinta">
            {conhecimento.titulo}
          </h3>
          {!conhecimento.ativo || fora ? (
            <div className="mt-1 flex flex-wrap items-center gap-1.5">
              {!conhecimento.ativo ? <Badge variant="secondary">Desativado</Badge> : null}
              {fora ? (
                <Badge variant="warning">
                  <CalendarOff className="size-3" />
                  Fora de vigência
                </Badge>
              ) : null}
            </div>
          ) : null}
        </div>

        {/* Esconder do mouse nunca esconde do teclado: `group-focus-within`
            traz o par de volta na navegação por Tab. */}
        <div className="flex shrink-0 items-center gap-0.5 transition-opacity duration-150 md:opacity-0 md:group-hover:opacity-100 md:group-focus-within:opacity-100">
          <Button
            variant="ghost"
            size="icon-sm"
            onClick={aoVerHistorico}
            aria-label={`Ver histórico de ${conhecimento.titulo}`}
            title="Histórico de versões"
          >
            <History />
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            onClick={aoEditar}
            aria-label={`Editar ${conhecimento.titulo}`}
            title="Editar"
          >
            <Pencil />
          </Button>
        </div>
      </div>

      <p className="line-clamp-4 whitespace-pre-wrap border-t border-linha pt-2 text-[11px] leading-relaxed text-tinta-media">
        {conhecimento.conteudo}
      </p>

      <div className="mt-auto flex flex-wrap items-center gap-1 pt-1">
        <Ficha>{conhecimento.tema}</Ficha>
        {conhecimento.tipoServicoNome ? <Ficha>{conhecimento.tipoServicoNome}</Ficha> : null}
        {temJanela ? (
          <Ficha>{descreverVigencia(conhecimento.vigenteDesde, conhecimento.vigenteAte)}</Ficha>
        ) : null}
        <span className="ml-auto font-dado text-[10px] text-tinta-fraca">
          v{conhecimento.versao}
        </span>
      </div>
    </article>
  )
}

function Ficha({ children }: { children: React.ReactNode }) {
  return (
    <span className="rounded-md border border-linha bg-superficie px-1.5 py-0.5 text-[10px] text-tinta-media">
      {children}
    </span>
  )
}
