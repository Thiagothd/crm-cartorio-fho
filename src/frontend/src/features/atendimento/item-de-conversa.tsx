import { Lock } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Avatar } from '@/components/ui/avatar'
import { formatarMomentoRelativo } from '@/lib/formatacao'
import { EtiquetaDePrazo } from './etiqueta-de-prazo'
import { coresDaEtapa, corSolidaDaEtapa } from './cores-de-etapa'
import type { Conversa } from '@/lib/api/tipos'

/**
 * A linha da inbox.
 *
 * Reduzida em 2026-08-19: a versão anterior mostrava protocolo, telefone,
 * prévia, etapa, setor E o nome completo de quem atende — cinco informações
 * competindo por três linhas, e o resultado eram pílulas cortadas pela metade
 * ("Registr...", "José Ma..."). DESIGN.md, seção 7, promete "~3 linhas de
 * conteúdo"; o que sobrou aqui de fato cabe nisso.
 *
 * O que saiu, e por quê:
 * - Telefone: já está no cabeçalho da conversa e no cadastro do cliente. Na
 *   lista, o protocolo já identifica o atendimento — o telefone aqui era
 *   redundância, não achado.
 * - Setor: não faz parte do trio que DESIGN.md 2.2 exige na inbox (esse trio é
 *   sobre a COR da etapa, não sobre o nome do setor). Quem precisa filtrar por
 *   setor usa o filtro, que já existe.
 * - Nome de quem atende: vira ícone de cadeado, sem o nome — era o elemento de
 *   largura mais imprevisível, e o nome completo já não cabia mesmo antes.
 *   "Você" continua por extenso, porque é curto e é a distinção que mais
 *   importa ao escanear a lista.
 */
export function ItemDeConversa({
  conversa,
  selecionada,
  souOResponsavel,
  etapaEhFinal,
  aoSelecionar,
}: {
  conversa: Conversa
  selecionada: boolean
  souOResponsavel: boolean
  etapaEhFinal: boolean
  aoSelecionar: (id: string) => void
}) {
  const naFila = conversa.atendenteId === null && conversa.status === 'Ativa'
  const encerrada = conversa.status === 'Encerrada'
  const cores = coresDaEtapa(conversa.etapaCor)

  return (
    <button
      type="button"
      onClick={() => aoSelecionar(conversa.id)}
      className={cn(
        'flex w-full gap-3 border-b border-linha px-3 py-2.5 text-left transition-colors',
        selecionada ? 'bg-superficie' : 'hover:bg-superficie/60',
        // Barra lateral marca a selecionada. É a linha mais fina que ainda se vê.
        selecionada && 'border-l-2 border-l-tinta pl-[10px]',
      )}
    >
      {/* Avatar do cliente na cor da etapa atual. Muda junto com a etapa. */}
      <Avatar
        nome={conversa.clienteNome}
        fotoUrl={conversa.clienteFotoPerfilUrl}
        tamanho="md"
        corDeFundo={corSolidaDaEtapa(conversa.etapaCor)}
      />

      <div className="min-w-0 flex-1">
        {/* Linha 1 — nome, hora e não lidas. O que se lê primeiro. */}
        <div className="flex items-baseline justify-between gap-2">
          <span
            className={cn(
              'truncate font-medium',
              encerrada ? 'text-tinta-fraca' : 'text-tinta',
            )}
          >
            {conversa.clienteNome}
          </span>

          <span className="flex shrink-0 items-center gap-1.5">
            <span className="whitespace-nowrap font-dado text-[11px] text-tinta-fraca">
              {formatarMomentoRelativo(conversa.ultimaMensagemEm)}
            </span>

            {conversa.mensagensNaoLidas > 0 ? (
              <span className="inline-flex min-w-4 shrink-0 items-center justify-center rounded-md border border-selo/45 bg-selo/8 px-1 font-dado text-[11px] font-medium text-selo">
                {conversa.mensagensNaoLidas}
              </span>
            ) : null}
          </span>
        </div>

        {/* Linha 2 — protocolo e etapa à esquerda (identidade e status), prazo
            à direita (o que exige atenção). Protocolo em etiqueta com borda
            (DESIGN.md, seção 10.1); etapa na mesma cor da pílula do funil e do
            avatar (seção 2.2). */}
        <div className="mt-1 flex items-center gap-1.5 overflow-hidden">
          <span className="etiqueta-protocolo shrink-0">{conversa.protocolo}</span>

          {conversa.etapaNome ? (
            <span
              className="inline-flex min-w-0 shrink items-center truncate rounded-md border px-1.5 py-0.5 text-[11px] font-medium"
              style={{
                backgroundColor: cores.fundo,
                color: cores.texto,
                borderColor: cores.borda,
              }}
            >
              {conversa.etapaNome}
            </span>
          ) : null}

          <span className="ml-auto shrink-0">
            <EtiquetaDePrazo
              situacao={conversa.situacaoPrazo}
              prazoLimite={conversa.prazoLimite}
              compacta
            />
          </span>
        </div>

        {/* Linha 3 — prévia da mensagem e quem responde. A prévia é o que dá
            contexto sem abrir a conversa; a responsabilidade é só um sinal
            discreto, não mais um nome inteiro competindo por espaço. */}
        <div className="mt-1 flex items-center gap-2">
          {conversa.previaUltimaMensagem ? (
            <p className="min-w-0 flex-1 truncate text-xs text-tinta-media">
              {conversa.previaUltimaMensagem}
            </p>
          ) : (
            <span className="min-w-0 flex-1" />
          )}

          {naFila ? (
            <span className="inline-flex shrink-0 items-center rounded-md border border-linha-forte px-1.5 py-0.5 text-[11px] font-medium text-tinta-media">
              Na fila
            </span>
          ) : conversa.atendenteNome ? (
            <span
              className={cn(
                'inline-flex shrink-0 items-center gap-1 text-[11px]',
                souOResponsavel ? 'text-verde' : 'text-tinta-fraca',
              )}
              title={souOResponsavel ? undefined : `Atendido por ${conversa.atendenteNome}`}
            >
              <Lock className="size-3" />
              {souOResponsavel ? 'Você' : null}
            </span>
          ) : null}
        </div>
      </div>

      {/* O selo. Único elemento com personalidade da tela — só aparece quando o
          ato chegou à etapa final. DESIGN.md, seção 6. */}
      {etapaEhFinal ? (
        <div className="flex shrink-0 items-center">
          <span className="selo-concluido selo-concluido-mini" aria-label="Ato concluído">
            Ato
            <br />
            pronto
          </span>
        </div>
      ) : null}
    </button>
  )
}
