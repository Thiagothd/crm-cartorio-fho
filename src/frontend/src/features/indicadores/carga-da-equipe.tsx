import { Clock, TriangleAlert, Users } from 'lucide-react'
import { Avatar } from '@/components/ui/avatar'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { formatarNumero, formatarPercentual } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { CargaDoAtendente } from '@/lib/api/tipos'

/**
 * Quem está com o quê AGORA — substituiu "Atividade recente" em 2026-08-21.
 *
 * O painel anterior era um log: vinte linhas de "Etapa alterada para X",
 * retrospectivo e sem nada a fazer com a informação. Este responde uma pergunta
 * de gestão que a tela não respondia — "o trabalho está distribuído ou tem
 * alguém afogado?" — e cada linha é acionável, porque abre a lista já filtrada
 * naquele atendente.
 *
 * ORDEM ALFABÉTICA, nunca por volume: é distribuição de trabalho, não ranking de
 * desempenho. Ordenar por quantidade transformaria o painel num quadro de
 * comparação entre colegas, que não é o propósito e não é o que um cartório
 * precisa ver na tela inicial. Quem tem carga zero aparece igual — some da lista
 * seria esconder folga disponível, que é justamente o que se procura ao olhar
 * distribuição.
 *
 * A linha NÃO é clicável, e isso é deliberado: a inbox filtra por "minhas" ou
 * "fila", não por um atendente arbitrário (`FiltrosVisiveis` não tem esse campo,
 * nem `FiltroDeConversas` no backend). Um link que caísse numa lista sem filtro
 * seria pior que nenhum link. Filtrar a inbox por atendente é uma melhoria
 * legítima, mas mexe em `ConversaService.ListarAsync` — a consulta mais delicada
 * do sistema — e não cabe de carona numa mudança de painel.
 */
export function CargaDaEquipe({ atendentes }: { atendentes: CargaDoAtendente[] }) {
  if (atendentes.length === 0) {
    return <EstadoDaInterface tipo="vazio" icone={Users} titulo="Nenhum atendente ativo" />
  }

  // Escala relativa ao mais carregado, não ao total: com 3 e 14 ativas, o
  // objetivo e' ver que um tem cinco vezes o do outro. Piso de 1 evita divisao
  // por zero quando a operacao inteira esta parada.
  const maximo = Math.max(1, ...atendentes.map((a) => a.ativas))
  const totalAtivas = atendentes.reduce((soma, a) => soma + a.ativas, 0)
  const totalEmDia = atendentes.reduce((soma, a) => soma + (a.emAlerta + a.atrasadas === 0 ? a.ativas : 0), 0)

  return (
    <div className="flex flex-col gap-3">
      {/* Faixa de resumo: a soma que o olho não faz sozinho percorrendo a lista,
          e o contexto que transforma "4 ativas" em "4 de 11". */}
      <div className="flex items-baseline gap-4 border-b border-linha pb-2.5">
        <span>
          <span className="font-dado text-xl font-bold leading-none tracking-tight text-tinta">
            {formatarNumero(totalAtivas)}
          </span>
          <span className="ml-1.5 text-[11px] font-medium uppercase tracking-wider text-tinta-fraca">
            ativas na equipe
          </span>
        </span>
        <span className="ml-auto text-[11px] font-medium text-tinta-media">
          {formatarNumero(totalEmDia)} em dia
        </span>
      </div>

      <ul className="flex flex-col gap-0.5">
        {atendentes.map((atendente, i) => {
          const proporcao = (atendente.ativas / maximo) * 100
          const fatia = totalAtivas === 0 ? 0 : (atendente.ativas / totalAtivas) * 100
          const temPendencia = atendente.emAlerta > 0 || atendente.atrasadas > 0
          const semCarga = atendente.ativas === 0

          return (
            <li key={atendente.atendenteId}>
              <div className="group relative flex items-center gap-3 overflow-hidden rounded-lg px-2.5 py-2 transition-colors duration-200 hover:bg-superficie/70">
                {/* Trilha proporcional atrás da linha — a comparação entre
                    atendentes se lê de relance, sem precisar somar os números.
                    Escalonada por índice: a lista se preenche de cima para
                    baixo ao abrir, em vez de aparecer pronta. */}
                <span
                  aria-hidden
                  className="absolute inset-y-0 left-0 origin-left rounded-lg bg-tinta/[0.07] transition-colors duration-200 group-hover:bg-tinta/[0.13]"
                  style={{
                    width: `${Math.max(2, proporcao)}%`,
                    animation: `crm-crescer-barra 460ms cubic-bezier(0.16, 1, 0.3, 1) ${Math.min(i * 45, 320)}ms both`,
                  }}
                />

                <Avatar
                  nome={atendente.atendenteNome}
                  tamanho="sm"
                  className="relative z-10 transition-transform duration-200 group-hover:scale-105"
                />

                <span className="relative z-10 min-w-0 flex-1">
                  <span className="block truncate text-[13px] font-semibold text-tinta">
                    {atendente.atendenteNome}
                  </span>
                  {/* Segunda linha revelada no hover. Reserva de altura fixa
                      (`h-3.5`), não montagem condicional: sem a reserva, a linha
                      cresceria ao passar o mouse e empurraria as de baixo — o
                      cursor sairia de onde estava e o hover piscaria em loop. */}
                  <span className="block h-3.5 overflow-hidden">
                    <span
                      className={cn(
                        'block text-[10px] font-medium text-tinta-fraca transition-all duration-200',
                        'translate-y-1 opacity-0 group-hover:translate-y-0 group-hover:opacity-100',
                      )}
                    >
                      {semCarga
                        ? 'sem atendimento no momento'
                        : `${formatarPercentual(fatia)} da fila da equipe${
                            temPendencia
                              ? ` · ${atendente.emAlerta + atendente.atrasadas} pedindo atenção`
                              : ' · tudo em dia'
                          }`}
                    </span>
                  </span>
                </span>

                <span className="relative z-10 flex shrink-0 items-center gap-2.5">
                  {/* Alerta e atraso como ícone + número, nunca como cor de fundo:
                      são N atendentes por tela, e pintar cada um estouraria o teto
                      de 3 bordôs (DESIGN.md 2.1) sem relação com a gravidade. */}
                  {temPendencia ? (
                    <span className="flex items-center gap-2 font-dado text-[11px] text-tinta-media">
                      {atendente.emAlerta > 0 ? (
                        <span
                          className="inline-flex items-center gap-1"
                          title={`${atendente.emAlerta} próximos do prazo`}
                        >
                          <Clock className="size-3" aria-hidden />
                          {atendente.emAlerta}
                        </span>
                      ) : null}
                      {atendente.atrasadas > 0 ? (
                        <span className="inline-flex items-center gap-1" title={`${atendente.atrasadas} atrasados`}>
                          <TriangleAlert className="size-3" aria-hidden />
                          {atendente.atrasadas}
                        </span>
                      ) : null}
                    </span>
                  ) : null}

                  <span className="min-w-8 text-right">
                    <span
                      className={cn(
                        'font-dado text-base font-bold',
                        semCarga ? 'text-tinta-fraca' : 'text-tinta',
                      )}
                    >
                      {formatarNumero(atendente.ativas)}
                    </span>
                    {/* "ativas" sai no celular: custava ~30px do nome, que
                        ficava com 87px e cortava em ~12 caracteres. O rótulo
                        do resumo no topo já diz que a unidade é "ativas". */}
                    <span className="ml-1 hidden text-[10px] font-medium uppercase tracking-wider text-tinta-fraca sm:inline">
                      ativas
                    </span>
                  </span>
                </span>
              </div>
            </li>
          )
        })}
      </ul>
    </div>
  )
}
