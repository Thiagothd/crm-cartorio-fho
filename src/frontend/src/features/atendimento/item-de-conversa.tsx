import { BotOff, HandCoins, Lock, Smartphone } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Avatar } from '@/components/ui/avatar'
import { formatarMomentoRelativo, nomeDeExibicao } from '@/lib/formatacao'
import { formatarReais } from '@/features/devedores/utilitarios'
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
/**
 * Nome de etapa para a linha compacta da inbox.
 *
 * Etapas como "Em processamento / exigência" e "Pronto / entrega" seguem o
 * padrão "principal / detalhe" — o pedaço depois da barra qualifica, mas quem
 * escaneia a lista precisa do principal. Na coluna de 360px do desktop e nos
 * ~375px do celular, o nome inteiro ao lado do protocolo e do prazo (os dois
 * nunca encolhem) sobrava pouco espaço e cortava no meio da palavra. Etapa sem
 * "/" no nome sai exatamente como está — isto é só um corte de exibição, não
 * mexe no cadastro nem no que aparece no seletor do cabeçalho.
 */
function nomeCompactoDaEtapa(nome: string) {
  const [principal] = nome.split(' / ')
  return principal
}

export function ItemDeConversa({
  conversa,
  selecionada,
  souOResponsavel,
  etapaEhFinal,
  aoSelecionar,
  aoMarcarDevedor,
  aoVerPendencias,
}: {
  conversa: Conversa
  selecionada: boolean
  souOResponsavel: boolean
  etapaEhFinal: boolean
  aoSelecionar: (id: string) => void
  aoMarcarDevedor?: (conversa: Conversa) => void
  aoVerPendencias?: (conversa: Conversa) => void
}) {
  const naFila = conversa.atendenteId === null && conversa.status === 'Ativa'
  const encerrada = conversa.status === 'Encerrada'
  const cores = coresDaEtapa(conversa.etapaCor)
  const nomeExibido = nomeDeExibicao(
    conversa.clienteNome,
    conversa.clienteTelefone,
    conversa.clienteNomePendente,
  )
  const pendenciasAbertas = conversa.clientePendenciasAbertas ?? 0
  const saldoDevedor = conversa.clienteSaldoDevedor ?? 0

  return (
    <button
      type="button"
      onClick={() => aoSelecionar(conversa.id)}
      className={cn(
        // Fase 3.2 (docs/DESIGN.md, secao 16): a linha vira cartao — mesmo
        // tratamento de indicador-numerico.tsx. Altura intacta (px-3 py-2.5
        // continuam identicos ao de antes; so a borda trocou de lugar).
        'group flex w-full gap-3 rounded-lg border border-transparent px-3 py-2.5 text-left transition-colors',
        selecionada
          ? 'border-linha bg-superficie shadow-card'
          : 'hover:border-linha hover:bg-superficie',
      )}
    >
      {/* Avatar do cliente na cor da etapa atual. Muda junto com a etapa. */}
      <Avatar
        nome={nomeExibido}
        fotoUrl={conversa.clienteFotoPerfilUrl}
        tamanho="md"
        corDeFundo={corSolidaDaEtapa(conversa.etapaCor)}
      />

      <div className="min-w-0 flex-1">
        {/* Linha 1 — nome, hora e não lidas. O que se lê primeiro. */}
        <div className="flex items-baseline justify-between gap-2">
          <span
            className={cn(
              'flex min-w-0 items-baseline gap-1.5 font-medium',
              encerrada ? 'text-tinta-fraca' : 'text-tinta',
            )}
          >
            <span className="truncate">{nomeExibido}</span>

            {/* Sem nome utilizável, o telefone acima ocupa o lugar do nome — e
                este selo explica ao atendente por que ele está vendo um número.
                Sem ele, pareceria falha de cadastro. */}
            {conversa.clienteNomePendente ? (
              <span className="shrink-0 rounded-full border border-linha px-1.5 text-[10px] font-normal text-tinta-fraca">
                sem nome
              </span>
            ) : null}
          </span>

          <span className="flex shrink-0 items-center gap-1.5">
            <span className="whitespace-nowrap font-dado text-[11px] text-tinta-fraca">
              {formatarMomentoRelativo(conversa.ultimaMensagemEm)}
            </span>

            {conversa.mensagensNaoLidas > 0 ? (
              // Fase 3.2: badge solido — bordo aqui marca o que exige acao
              // (o que chegou e nao foi lido), junto com Enviar. "+ Novo
              // atendimento" desceu para outline por causa disso (DESIGN.md,
              // secao 16.5) — o orcamento de 3 bordos por tela fecha certo.
              <span className="inline-flex h-[18px] min-w-[18px] shrink-0 items-center justify-center rounded-full bg-primary px-1 font-dado text-[10px] font-semibold text-primary-foreground">
                {conversa.mensagensNaoLidas}
              </span>
            ) : null}
          </span>
        </div>

        {/* Linha 2 — etapa à esquerda (status), pílula de devedor no fluxo
            natural (nunca sobrepondo texto) e prazo à direita. */}
        <div className="mt-1 flex items-center gap-1 overflow-hidden">
          {conversa.etapaNome ? (
            <span
              title={conversa.etapaNome}
              className="inline-flex min-w-0 shrink items-center truncate rounded-full border px-1.5 py-0.5 text-[11px] font-medium"
              style={{
                backgroundColor: cores.fundo,
                color: cores.texto,
                borderColor: cores.borda,
              }}
            >
              {nomeCompactoDaEtapa(conversa.etapaNome)}
            </span>
          ) : null}

          {pendenciasAbertas > 0 ? (
            <span
              role={aoVerPendencias ? 'button' : undefined}
              tabIndex={aoVerPendencias ? 0 : undefined}
              onClick={
                aoVerPendencias
                  ? (e) => {
                      e.stopPropagation()
                      aoVerPendencias(conversa)
                    }
                  : undefined
              }
              onKeyDown={
                aoVerPendencias
                  ? (e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault()
                        e.stopPropagation()
                        aoVerPendencias(conversa)
                      }
                    }
                  : undefined
              }
              title={
                saldoDevedor > 0
                  ? `Cliente com ${pendenciasAbertas} pendência(s) financeira(s) em aberto (${formatarReais(saldoDevedor)}). Clique para ver ou dar baixa.`
                  : `Cliente com ${pendenciasAbertas} pendência(s) financeira(s) em aberto. Clique para ver ou dar baixa.`
              }
              className={cn(
                'inline-flex shrink-0 items-center gap-1 rounded-full border border-ambar/40 bg-ambar/10 px-1.5 py-0.5 font-dado text-[10px] font-medium text-ambar',
                aoVerPendencias &&
                  'cursor-pointer transition-colors hover:border-ambar hover:bg-ambar/20',
              )}
            >
              <HandCoins className="size-2.5" aria-hidden />
              {saldoDevedor > 0
                ? conversa.situacaoPrazo === 'SemPrazo'
                  ? `Devendo ${formatarReais(saldoDevedor)}`
                  : formatarReais(saldoDevedor)
                : 'Devendo'}
            </span>
          ) : aoMarcarDevedor ? (
            <span
              role="button"
              tabIndex={0}
              onClick={(e) => {
                e.stopPropagation()
                aoMarcarDevedor(conversa)
              }}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault()
                  e.stopPropagation()
                  aoMarcarDevedor(conversa)
                }
              }}
              title="Marcar este contato como devedor / registrar pendência"
              aria-label={`Marcar ${nomeExibido} como devedor`}
              className="hidden shrink-0 cursor-pointer items-center gap-1 rounded-full border border-linha-forte bg-branco px-1.5 py-0.5 text-[10px] font-medium text-tinta-media transition-colors hover:border-ambar/60 hover:bg-ambar/10 hover:text-tinta focus-visible:inline-flex group-hover:inline-flex"
            >
              <HandCoins className="size-2.5 text-ambar" aria-hidden />+ Devedor
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

          {/* Robô pausado nesta conversa (ou contato atendido sempre sem robô):
              quem abre a lista precisa saber que ali ninguém automático vai
              responder — o cliente depende de uma pessoa. Âmbar, como pendência. */}
          {conversa.status === 'Ativa' && conversa.roboPausadoEm ? (
            <span
              className="inline-flex shrink-0 items-center gap-1 rounded-full border border-ambar/40 px-1.5 py-0.5 text-[11px] font-medium text-ambar"
              title={
                conversa.clienteSemRobo
                  ? 'Contato atendido sempre sem robô: só a equipe responde.'
                  : `Robô pausado${conversa.roboPausadoPorNome ? ` por ${conversa.roboPausadoPorNome}` : ''}: só a equipe responde.`
              }
            >
              <BotOff className="size-3" aria-hidden />
              {conversa.clienteSemRobo ? 'Sem robô' : 'Pausado'}
            </span>
          ) : null}

          {/* Sem dono no painel, mas alguém da serventia já está respondendo
              pelo celular: continua na fila (qualquer um pode assumir), só que
              o cliente não está esperando ninguém. Verde, como quem atende. */}
          {naFila && conversa.atendidaPeloAparelho ? (
            <span
              className="inline-flex shrink-0 items-center gap-1 rounded-full border border-verde/40 px-1.5 py-0.5 text-[11px] font-medium text-verde"
              title="Sendo atendida pelo WhatsApp do celular. O robô e a IA estão pausados; qualquer atendente pode assumir."
            >
              <Smartphone className="size-3" aria-hidden />
              No celular
            </span>
          ) : naFila ? (
            <span className="inline-flex shrink-0 items-center rounded-full border border-linha-forte px-1.5 py-0.5 text-[11px] font-medium text-tinta-media">
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
