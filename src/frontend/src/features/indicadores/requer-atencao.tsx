import { Link } from 'react-router-dom'
import { ChevronRight, CircleCheck, Clock, Lock, TriangleAlert, UserX } from 'lucide-react'
import { Avatar } from '@/components/ui/avatar'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { EtiquetaDePrazo } from '@/features/atendimento/etiqueta-de-prazo'
import { coresDaEtapa, corSolidaDaEtapa } from '@/features/atendimento/cores-de-etapa'
import { formatarMomentoRelativo, formatarTelefone } from '@/lib/formatacao'
import type { GrupoRequerAtencao, ItemRequerAtencao } from '@/lib/api/tipos'

const ICONE_DO_GRUPO: Record<string, typeof TriangleAlert> = {
  atrasadas: TriangleAlert,
  alerta: Clock,
  fila: UserX,
}

function rotaDoGrupo(chave: string): string {
  if (chave === 'atrasadas') return '/atendimentos?situacao=Atrasada'
  if (chave === 'alerta') return '/atendimentos?situacao=EmAlerta'
  return '/atendimentos?escopo=fila'
}

/** Etapa "Em processamento / exigência" vira "Em processamento" — mesmo corte da inbox. */
function nomeCompactoDaEtapa(nome: string) {
  const [principal] = nome.split(' / ')
  return principal
}

/**
 * A linha é deliberadamente a MESMA leitura de `item-de-conversa.tsx`: avatar do
 * cliente na cor da etapa, nome, telefone, pílula de etapa, prazo e quem
 * responde. Quem escaneia a Visão Geral e depois abre a lista de atendimentos vê
 * o mesmo atendimento com a mesma cara, em vez de duas representações
 * diferentes da mesma coisa.
 *
 * O protocolo saiu — pelo mesmo motivo que saiu da inbox em 2026-08-19: competia
 * por espaço com a etapa e cortava os dois. Continua acessível pela busca (que
 * aceita protocolo) e no cabeçalho da conversa assim que ela abre.
 */
function LinhaDeAtendimento({ item }: { item: ItemRequerAtencao }) {
  const cores = coresDaEtapa(item.etapaCor)
  const naFila = item.atendenteNome === null

  return (
    <Link
      to={`/atendimentos/${item.conversaId}`}
      className="group flex items-center gap-3 rounded-lg px-2 py-2 transition-colors duration-150 hover:bg-superficie"
    >
      {/* Avatar de 28px no celular, 36px a partir de `sm`: os 8px devolvidos
          entram na segunda linha, que é onde falta espaço. */}
      <Avatar
        nome={item.clienteNome}
        fotoUrl={item.clienteFotoPerfilUrl}
        tamanho="sm"
        corDeFundo={corSolidaDaEtapa(item.etapaCor)}
        className="sm:size-9 sm:text-[11px]"
      />

      <div className="min-w-0 flex-1">
        <div className="flex items-baseline justify-between gap-2">
          <span className="truncate text-sm font-semibold text-tinta">{item.clienteNome}</span>
          <span className="shrink-0 whitespace-nowrap font-dado text-[11px] text-tinta-fraca">
            {formatarMomentoRelativo(item.ultimaMensagemEm)}
          </span>
        </div>

        {/* Ordem de sacrifício desta linha em 360px, a mesma lógica da 10.8 do
            DESIGN.md: somados, telefone (92px) + "Na fila" (58px) + etiqueta de
            prazo (83px) pediam 251px onde há 204px, e como a linha é
            `overflow-hidden` o que sobrava cortado era justamente o PRAZO — o
            motivo de o item estar neste painel.
            1. Sai o telefone (está no cadastro e no cabeçalho da conversa).
            2. A pílula de etapa encolhe e trunca.
            3. Prazo e responsável nunca saem. */}
        <div className="mt-1 flex items-center gap-1.5 overflow-hidden">
          {item.clienteTelefone ? (
            <span className="hidden shrink-0 font-dado text-[11px] text-tinta-media sm:inline">
              {formatarTelefone(item.clienteTelefone)}
            </span>
          ) : null}

          {item.etapaNome ? (
            <span
              title={item.etapaNome}
              className="inline-flex min-w-0 shrink items-center truncate rounded-md border px-1.5 py-0.5 text-[10px] font-medium"
              style={{ backgroundColor: cores.fundo, color: cores.texto, borderColor: cores.borda }}
            >
              {nomeCompactoDaEtapa(item.etapaNome)}
            </span>
          ) : null}

          <span className="ml-auto flex shrink-0 items-center gap-1.5">
            {naFila ? (
              <span className="inline-flex items-center rounded-md border border-linha-forte px-1.5 py-0.5 text-[10px] font-medium text-tinta-media">
                Na fila
              </span>
            ) : (
              <span
                className="inline-flex items-center gap-1 text-[10px] text-tinta-fraca"
                title={`Atendido por ${item.atendenteNome}`}
              >
                <Lock className="size-3" aria-hidden />
              </span>
            )}

            <EtiquetaDePrazo situacao={item.situacaoPrazo} prazoLimite={item.prazoLimite} compacta />
          </span>
        </div>
      </div>

      {/* Afordância de hover, então não existe no celular — e ali ela custava
          16px de uma linha que já não fechava. Espaço reservado por `opacity`,
          não por montagem condicional, para nada se deslocar quando o mouse
          entra na linha. */}
      <ChevronRight
        aria-hidden
        className="hidden size-4 shrink-0 text-tinta-fraca opacity-0 transition-opacity duration-150 group-hover:opacity-100 sm:block"
      />
    </Link>
  )
}

/**
 * Cabeçalho de cada grupo é ícone + texto, deliberadamente NEUTRO — o
 * orçamento de bordô da tela (DESIGN.md 2.1) já vai para o indicador
 * "Atrasados" no topo e para a etiqueta de prazo de cada linha. Repetir a cor
 * no cabeçalho seria a mesma informação gastando o orçamento três vezes.
 */
export function RequerAtencao({ grupos }: { grupos: GrupoRequerAtencao[] }) {
  if (grupos.length === 0) {
    return (
      <EstadoDaInterface
        tipo="vazio"
        icone={CircleCheck}
        titulo="Nada pedindo atenção agora"
        descricao="Nenhum atraso, alerta de prazo ou conversa esperando responsável."
      />
    )
  }

  return (
    <div className="flex flex-col gap-4">
      {grupos.map((grupo) => {
        const Icone = ICONE_DO_GRUPO[grupo.chave] ?? TriangleAlert

        return (
          <div key={grupo.chave}>
            <div className="mb-1.5 flex items-center justify-between gap-2">
              <p className="flex items-center gap-2 text-[13px] font-bold text-tinta">
                <span
                  aria-hidden
                  className="flex size-6 shrink-0 items-center justify-center rounded-lg bg-superficie text-tinta-media"
                >
                  <Icone className="size-3.5" />
                </span>
                {grupo.total} {grupo.rotulo.toLocaleLowerCase('pt-BR')}
              </p>

              {/* "ver todos" no cabeçalho, não no pé: é a ação do GRUPO, e no pé
                  se confundia com mais um item da lista. */}
              {grupo.total > grupo.itens.length ? (
                <Link
                  to={rotaDoGrupo(grupo.chave)}
                  // `min-h-11` no celular: é a única saída do grupo para a lista
                  // completa, e tinha 21px de altura de alvo.
                  className="inline-flex min-h-11 shrink-0 touch-manipulation items-center rounded-md px-1.5 text-[11px] font-semibold text-tinta-media transition-colors hover:bg-superficie hover:text-tinta sm:min-h-0 sm:py-0.5"
                >
                  ver todos ({grupo.total})
                </Link>
              ) : null}
            </div>

            <ul className="flex flex-col divide-y divide-linha/70">
              {grupo.itens.map((item) => (
                <li key={item.conversaId}>
                  <LinhaDeAtendimento item={item} />
                </li>
              ))}
            </ul>
          </div>
        )
      })}
    </div>
  )
}
