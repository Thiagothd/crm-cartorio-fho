import { ArrowRight, Flag, GitBranch } from 'lucide-react'
import { formatarDataHora } from '@/lib/formatacao'
import type { EventoDeHistorico } from '@/lib/api/tipos'

/**
 * Trilha de etapa e setor da conversa. Vive num painel lateral porque e consulta
 * ocasional — o fluxo normal do atendente e o fio de mensagens.
 *
 * A casca — largura, borda, cabecalho, botao de fechar e o comportamento de
 * tela cheia no celular — mora em `painel-lateral-da-conversa.tsx`, dividida com
 * o painel de midias. Aqui fica so o miolo.
 */
export function LinhaDoTempo({ eventos }: { eventos: EventoDeHistorico[] }) {
  return (
    <div className="flex-1 overflow-y-auto px-4 py-3">
      {eventos.length === 0 ? (
        <p className="text-xs text-tinta-media">Sem movimentações registradas.</p>
      ) : (
        <ol className="flex flex-col gap-3">
          {eventos.map((evento) => (
            <li key={evento.id} className="relative flex gap-2.5 pl-1">
              <div className="mt-0.5 flex size-6 shrink-0 items-center justify-center rounded-full bg-card text-tinta-media ring-1 ring-border">
                {evento.tipoMudanca === 'Abertura' ? (
                  <Flag className="size-3" />
                ) : (
                  <GitBranch className="size-3" />
                )}
              </div>

              <div className="min-w-0 flex-1">
                {evento.etapaNova ? (
                  <p className="flex flex-wrap items-center gap-1 text-xs">
                    {evento.etapaAnterior ? (
                      <>
                        <span className="text-tinta-media">{evento.etapaAnterior}</span>
                        <ArrowRight className="size-3 text-tinta-media" />
                      </>
                    ) : null}
                    <span className="font-medium">{evento.etapaNova}</span>
                  </p>
                ) : null}

                {evento.setorNovo ? (
                  <p className="flex flex-wrap items-center gap-1 text-xs">
                    {evento.setorAnterior ? (
                      <>
                        <span className="text-tinta-media">{evento.setorAnterior}</span>
                        <ArrowRight className="size-3 text-tinta-media" />
                      </>
                    ) : null}
                    <span className="font-medium">{evento.setorNovo}</span>
                  </p>
                ) : null}

                {evento.observacao ? (
                  <p className="mt-0.5 text-[11px] text-tinta-media">{evento.observacao}</p>
                ) : null}

                <p className="mt-0.5 text-[10px] text-tinta-media">
                  {formatarDataHora(evento.ocorridoEm)}
                  {evento.atendenteNome ? ` · ${evento.atendenteNome}` : ''}
                </p>
              </div>
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}
