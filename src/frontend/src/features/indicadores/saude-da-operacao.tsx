import type { LucideIcon } from 'lucide-react'
import { Bot, MessageSquare, Zap } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { BarraEmpilhada } from '@/components/graficos/barra-empilhada'
import { formatarNumero } from '@/lib/formatacao'
import type { ContagemPorChave, SaudeDaOperacao } from '@/lib/api/tipos'

function LinhaDeSaude({
  icone: Icone,
  rotulo,
  texto,
  variante,
}: {
  icone: LucideIcon
  rotulo: string
  texto: string
  variante: 'success' | 'warning' | 'outline'
}) {
  // Empilhado no celular, lado a lado a partir de `sm`. Somados, "Mensagens
  // automáticas" (155px) + "12 ativas · 3 desativadas" (150px) + ícone + badge
  // pediam ~385px onde há 284px: sem `truncate` nem alternativa, os DOIS lados
  // quebravam por palavra e cada linha do painel virava 2-3 linhas de altura,
  // com o `justify-between` desalinhando rótulo e badge.
  return (
    <li className="flex flex-col gap-1 rounded-lg px-1.5 py-2 transition-colors hover:bg-superficie sm:flex-row sm:items-center sm:justify-between sm:gap-3">
      <span className="flex min-w-0 items-center gap-2.5 text-sm font-medium text-tinta">
        <span
          aria-hidden
          className="flex size-7 shrink-0 items-center justify-center rounded-lg bg-superficie text-tinta-media"
        >
          <Icone className="size-4" />
        </span>
        <span className="min-w-0 truncate">{rotulo}</span>
      </span>
      {/* No celular alinha com o texto do rótulo (`pl-[38px]` = ícone + gap),
          para a segunda linha não parecer solta do item a que pertence. */}
      <span className="flex shrink-0 items-center gap-2 pl-[38px] sm:pl-0">
        <span className="text-xs text-tinta-media">{texto}</span>
        <Badge variant={variante}>
          {variante === 'success' ? 'OK' : variante === 'warning' ? 'Atenção' : '—'}
        </Badge>
      </span>
    </li>
  )
}

/**
 * WhatsApp exige `PodeGerirAtendentes`; IA e Automações exigem
 * `PodeGerirCadastros` — os mesmos gates do resto do sistema, resolvidos no
 * BACKEND (`IndicadoresService.MontarSaudeAsync`). Aqui só se decide o que
 * fazer quando NENHUM campo chegou: em vez de mostrar "sem permissão", o
 * espaço vira "Meus atendimentos por etapa" — o que de fato serve a quem não
 * gerencia a operação.
 */
export function SaudeDaOperacaoCard({
  saude,
  minhaDistribuicao,
}: {
  saude: SaudeDaOperacao
  minhaDistribuicao: ContagemPorChave[]
}) {
  if (!saude.whatsApp && !saude.ia && !saude.automacao) {
    return (
      <div>
        <p className="mb-2 text-xs uppercase tracking-wider text-tinta-fraca">Meus atendimentos por etapa</p>
        <BarraEmpilhada segmentos={minhaDistribuicao} />
      </div>
    )
  }

  return (
    <ul className="flex flex-col divide-y divide-linha">
      {saude.whatsApp ? (
        <LinhaDeSaude
          icone={MessageSquare}
          rotulo="WhatsApp"
          // 'warning' (ambar), nunca 'destructive' (selo): desconexao e' saude
          // operacional, nao atraso de prazo legal — a distincao que
          // DESIGN.md secao 5 ja faz. Bordo fica reservado para "Atrasada",
          // e o sino ja cobre esta mesma informacao sem gastar o orcamento
          // (ver sino-de-alertas.tsx).
          variante={saude.whatsApp.conectado ? 'success' : saude.whatsApp.habilitado ? 'warning' : 'outline'}
          texto={
            saude.whatsApp.conectado
              ? 'Conectado'
              : saude.whatsApp.habilitado
                ? 'Desconectado'
                : 'Desligado'
          }
        />
      ) : null}

      {saude.ia ? (
        <LinhaDeSaude
          icone={Bot}
          rotulo="Resposta Inteligente"
          variante={
            !saude.ia.habilitada || !saude.ia.estaConfigurada
              ? 'outline'
              : saude.ia.usadoHoje >= saude.ia.limiteDiario
                ? 'warning'
                : 'success'
          }
          texto={
            !saude.ia.habilitada
              ? 'Desligada'
              : saude.ia.estaConfigurada
                ? `${formatarNumero(saude.ia.usadoHoje)}/${formatarNumero(saude.ia.limiteDiario)} hoje`
                : 'Não configurada'
          }
        />
      ) : null}

      {saude.automacao ? (
        <LinhaDeSaude
          icone={Zap}
          rotulo="Mensagens automáticas"
          variante={saude.automacao.habilitadas && saude.automacao.regrasAtivas > 0 ? 'success' : 'outline'}
          texto={
            saude.automacao.habilitadas
              ? `${formatarNumero(saude.automacao.regrasAtivas)} ativas · ${formatarNumero(saude.automacao.regrasInativas)} desativadas`
              : 'Desligadas'
          }
        />
      ) : null}
    </ul>
  )
}
