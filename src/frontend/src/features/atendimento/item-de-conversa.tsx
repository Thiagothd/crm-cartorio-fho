import { Lock } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Avatar } from '@/components/ui/avatar'
import { formatarMomentoRelativo, formatarTelefone } from '@/lib/formatacao'
import { EtiquetaDePrazo } from './etiqueta-de-prazo'
import { coresDaEtapa, corSolidaDaEtapa } from './cores-de-etapa'
import type { Conversa } from '@/lib/api/tipos'

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
        tamanho="md"
        corDeFundo={corSolidaDaEtapa(conversa.etapaCor)}
      />

      <div className="min-w-0 flex-1">
        <div className="flex items-baseline justify-between gap-2">
          <span
            className={cn(
              'truncate font-medium',
              encerrada ? 'text-tinta-fraca' : 'text-tinta',
            )}
          >
            {conversa.clienteNome}
          </span>
          <span className="shrink-0 font-dado text-[11px] text-tinta-fraca">
            {formatarMomentoRelativo(conversa.ultimaMensagemEm)}
          </span>
        </div>

        {/* Protocolo e telefone em mono: dado que se confere caractere por caractere.
            O protocolo vai em etiqueta com borda (DESIGN.md, secao 10). */}
        <div className="mt-0.5 flex items-center gap-1.5 font-dado text-[11px] text-tinta-fraca">
          <span className="etiqueta-protocolo">{conversa.protocolo}</span>
          <span className="truncate">{formatarTelefone(conversa.clienteTelefone)}</span>
        </div>

        {conversa.previaUltimaMensagem ? (
          <p className="mt-1 line-clamp-1 text-xs text-tinta-media">
            {conversa.previaUltimaMensagem}
          </p>
        ) : null}

        <div className="mt-1.5 flex flex-wrap items-center gap-x-2.5 gap-y-1">
          {/* Mini-pílula da etapa, mesmo tratamento da pílula do funil no topo.
              Isto era um ponto de 6px enquanto "Captação" usava bordô — pílula
              preenchida em cada linha espalhava a cor do selo pela tela. Com a
              paleta de etapa (DESIGN.md, 2.2) nenhuma etapa é bordô, e a
              restrição deixou de existir. */}
          {conversa.etapaNome ? (
            <span
              className="inline-flex items-center rounded-md border px-1.5 py-0.5 text-[11px] font-medium"
              style={{
                backgroundColor: cores.fundo,
                color: cores.texto,
                borderColor: cores.borda,
              }}
            >
              {conversa.etapaNome}
            </span>
          ) : null}

          {conversa.setorNome ? (
            <span className="text-[11px] text-tinta-fraca">{conversa.setorNome}</span>
          ) : null}

          <EtiquetaDePrazo
            situacao={conversa.situacaoPrazo}
            prazoLimite={conversa.prazoLimite}
            compacta
          />

          {naFila ? (
            <span className="inline-flex items-center rounded-md border border-linha-forte px-1.5 py-0.5 text-[11px] font-medium text-tinta-media">
              Na fila
            </span>
          ) : conversa.atendenteNome ? (
            <span
              className={cn(
                'inline-flex items-center gap-1 text-[11px]',
                souOResponsavel ? 'text-verde' : 'text-tinta-fraca',
              )}
            >
              <Lock className="size-3" />
              {souOResponsavel ? 'Você' : conversa.atendenteNome}
            </span>
          ) : null}

          {conversa.mensagensNaoLidas > 0 ? (
            <span className="ml-auto inline-flex min-w-4 items-center justify-center rounded-md border border-selo/45 bg-selo/8 px-1 font-dado text-[11px] font-medium text-selo">
              {conversa.mensagensNaoLidas}
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
