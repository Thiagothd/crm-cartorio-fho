import type { ReactNode } from 'react'
import { Check, CornerDownRight, Plus } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { cn } from '@/lib/utils'
import { encontrarMensagem, type PontoDoFluxo, type TrilhaDoFluxo } from './topologia-do-fluxo'
import type { GatilhoDeMensagem, MensagemAutomatica } from '@/lib/api/tipos'

/**
 * O caminho do atendimento automático, desenhado.
 *
 * Reescrito na Fase 3.3 (2026-09-07). A versão anterior era um trilho
 * HORIZONTAL com cartões de 160px e rolagem lateral: numa tela de 1440px
 * sobrava metade da largura vazia, e mesmo assim metade dos pontos ficava fora
 * do campo de visão — quem configurava não conseguia ver o caminho inteiro de
 * uma vez, que é exatamente para o que esta peça existe.
 *
 * Agora são TRILHAS: cada desvio do caminho vira um bloco próprio, com título,
 * explicação e os pontos numa grade que usa a largura toda e reflui em vez de
 * rolar. Nenhuma informação depende de rolagem horizontal.
 *
 * O que NÃO mudou, e é o ponto inteiro desta peça: a topologia é fixa
 * (`topologia-do-fluxo.ts`). Diferente do n8n, não se arrasta nem se conecta
 * nada — `GatilhoDeMensagem` é enum e caminho novo exige código novo. O
 * cadastro é o TEXTO de cada ponto.
 *
 * Orçamento de bordô (DESIGN.md 17.2): esta peça gasta ZERO. Pendência é
 * âmbar, desativado é neutro — nenhum dos dois é erro.
 */
export function FluxoDeMensagens({
  trilhas,
  mensagens,
  somentePendentes,
  aoClicarPonto,
}: {
  trilhas: TrilhaDoFluxo[]
  mensagens: MensagemAutomatica[]
  /** Esconde o que já tem texto — o modo "me mostra só o que falta". */
  somentePendentes: boolean
  aoClicarPonto: (
    gatilho: GatilhoDeMensagem,
    etapaAlvoId: string | null,
    existente: MensagemAutomatica | null,
  ) => void
}) {
  const visiveis = trilhas
    .map((trilha) => ({
      trilha,
      pontos: somentePendentes
        ? trilha.pontos.filter((p) => !encontrarMensagem(mensagens, p.gatilho, p.etapaAlvoId))
        : trilha.pontos,
    }))
    // Trilha sem ponto visível some inteira: no modo "só pendências", um bloco
    // com título e nenhum cartão dentro seria ruído puro. A de etapas do funil
    // é a única que pode ficar vazia fora desse modo — ela tem tratamento
    // próprio, abaixo, porque ali "vazio" significa "falta cadastrar etapa".
    .filter(({ trilha, pontos }) => pontos.length > 0 || trilha.chave === 'funil')

  if (visiveis.length === 0) {
    return (
      <EstadoDaInterface
        tipo="vazio"
        icone={Check}
        titulo="Todo ponto do caminho tem texto"
        descricao="Nenhuma pendência: o sistema tem o que dizer em cada momento do atendimento automático."
      />
    )
  }

  return (
    <div className="grade-com-vida grid gap-3 @4xl:grid-cols-2">
      {visiveis.map(({ trilha, pontos }) => (
        <Trilha
          key={trilha.chave}
          trilha={trilha}
          pontos={pontos}
          mensagens={mensagens}
          aoClicarPonto={aoClicarPonto}
        />
      ))}
    </div>
  )
}

function Trilha({
  trilha,
  pontos,
  mensagens,
  aoClicarPonto,
}: {
  trilha: TrilhaDoFluxo
  pontos: PontoDoFluxo[]
  mensagens: MensagemAutomatica[]
  aoClicarPonto: (
    gatilho: GatilhoDeMensagem,
    etapaAlvoId: string | null,
    existente: MensagemAutomatica | null,
  ) => void
}) {
  const comTexto = trilha.pontos.filter((p) =>
    encontrarMensagem(mensagens, p.gatilho, p.etapaAlvoId),
  ).length

  return (
    <section
      className={cn(
        // Container NOMEADO: a grade de pontos precisa saber a largura da
        // própria trilha, que é metade da tela quando `larga` é falso. Sem o
        // nome, uma consulta aqui dentro casaria com o container mais próximo
        // — que é este mesmo, mas o nome deixa a intenção explícita e
        // sobrevive a um container novo entrar no meio.
        '@container/trilha flex flex-col rounded-card border border-linha bg-superficie p-3.5 sm:p-4',
        trilha.larga && '@4xl:col-span-2',
      )}
    >
      <div className="flex items-start gap-3">
        <span
          aria-hidden
          className="flex size-9 shrink-0 items-center justify-center rounded-lg border border-linha bg-branco text-tinta-media"
        >
          <trilha.icone className="size-4" />
        </span>

        <div className="min-w-0 flex-1">
          <h3 className="text-[15px] font-bold leading-tight tracking-tight text-tinta">
            {trilha.titulo}
          </h3>
          <p className="mt-1 text-xs leading-snug text-tinta-media">{trilha.descricao}</p>
        </div>

        {trilha.pontos.length > 0 ? (
          <span
            className="shrink-0 font-dado text-[11px] font-medium text-tinta-fraca"
            title={`${comTexto} de ${trilha.pontos.length} pontos com texto cadastrado`}
          >
            {comTexto}/{trilha.pontos.length}
          </span>
        ) : null}
      </div>

      {trilha.antes ? <Nota>{trilha.antes}</Nota> : null}

      {pontos.length === 0 ? (
        <p className="mt-3 rounded-md border border-dashed border-linha-forte bg-branco px-3 py-4 text-center text-xs text-tinta-media">
          Nenhuma etapa cadastrada ainda. As etapas do funil vêm de{' '}
          <strong className="font-semibold text-tinta">Estrutura &gt; Etapas</strong>.
        </p>
      ) : (
        <div
          className={cn(
            'mt-3 grid gap-2.5',
            // Trilha de um ponto só ocupa a largura inteira: numa grade de duas
            // colunas o cartão único ficaria na metade esquerda, com metade da
            // trilha vazia ao lado sem nada explicando por quê.
            trilha.pontos.length > 1 && '@sm/trilha:grid-cols-2',
            // Só as trilhas largas passam de duas colunas: as demais têm no
            // máximo dois pontos, e uma grade de três deixaria uma coluna
            // vazia permanente no fim da linha.
            trilha.larga && '@2xl/trilha:grid-cols-3 @5xl/trilha:grid-cols-4 @7xl/trilha:grid-cols-5',
          )}
        >
          {pontos.map((p) => {
            const mensagem = encontrarMensagem(mensagens, p.gatilho, p.etapaAlvoId)

            return (
              <Ponto
                key={`${p.gatilho}-${p.etapaAlvoId ?? ''}`}
                ponto={p}
                mensagem={mensagem}
                // A numeração é a ordem na TOPOLOGIA, não na lista filtrada:
                // no modo "só pendências", ver "2" e "4" é a informação certa
                // — renumerar para 1 e 2 esconderia que existem pontos no meio.
                ordem={trilha.sequencial ? trilha.pontos.indexOf(p) + 1 : null}
                aoClicar={() => aoClicarPonto(p.gatilho, p.etapaAlvoId, mensagem)}
              />
            )
          })}
        </div>
      )}

      {trilha.depois ? <Nota>{trilha.depois}</Nota> : null}
    </section>
  )
}

/** Comportamento do sistema que emoldura a trilha — nunca é cadastro. */
function Nota({ children }: { children: ReactNode }) {
  return (
    <p className="mt-3 flex items-start gap-1.5 text-[11px] leading-snug text-tinta-fraca">
      <CornerDownRight className="mt-px size-3.5 shrink-0" aria-hidden />
      <span>{children}</span>
    </p>
  )
}

function Ponto({
  ponto,
  mensagem,
  ordem,
  aoClicar,
}: {
  ponto: PontoDoFluxo
  mensagem: MensagemAutomatica | null
  ordem: number | null
  aoClicar: () => void
}) {
  const semMensagem = !mensagem
  const inativa = mensagem ? !mensagem.ativo : false

  return (
    <button
      type="button"
      onClick={aoClicar}
      className={cn(
        // Fase 3.1: superfície elevada, o mesmo vocabulário do cartão de
        // atendente e do cartão de indicador. `text-left` porque isto é um
        // registro clicável, não um botão de comando.
        'group/ponto flex h-full flex-col gap-2 rounded-card border bg-branco p-3 text-left shadow-card transition-all duration-200 hover:-translate-y-0.5 hover:shadow-card-hover',
        semMensagem ? 'border-dashed border-linha-forte' : 'border-linha',
      )}
    >
      <div className="flex items-start gap-2">
        {ordem !== null ? (
          <span
            aria-hidden
            className="mt-px flex size-5 shrink-0 items-center justify-center rounded-full bg-superficie font-dado text-[10px] font-semibold text-tinta-media"
          >
            {ordem}
          </span>
        ) : null}

        <span
          className={cn(
            'min-w-0 flex-1 text-[13px] font-semibold leading-tight',
            inativa ? 'text-tinta-media' : 'text-tinta',
          )}
        >
          {ponto.rotulo}
        </span>

        {semMensagem ? (
          <Plus
            className="mt-px size-3.5 shrink-0 text-ambar opacity-0 transition-opacity duration-150 group-hover/ponto:opacity-100"
            aria-hidden
          />
        ) : !inativa ? (
          <Check className="mt-px size-3.5 shrink-0 text-verde" aria-label="Com texto ativo" />
        ) : null}
      </div>

      <p className="text-[11px] leading-snug text-tinta-fraca">{ponto.quando}</p>

      {/* `mt-auto`: numa grade de quatro colunas os cartões têm alturas de
          conteúdo diferentes, e o texto da mensagem alinhado no rodapé dá a
          linha de leitura que a grade sozinha não dá. */}
      <div className="mt-auto border-t border-linha pt-2">
        {semMensagem ? (
          <p className="flex flex-col gap-0.5">
            <span className="text-[11px] font-semibold text-ambar">Falta escrever</span>
            <span className="text-[11px] leading-snug text-tinta-media">{ponto.seFaltar}</span>
          </p>
        ) : (
          <>
            {inativa ? (
              <Badge variant="secondary" className="mb-1">
                Desativada
              </Badge>
            ) : null}
            <p
              className={cn(
                'line-clamp-3 whitespace-pre-wrap text-[11px] leading-relaxed',
                inativa ? 'text-tinta-fraca' : 'text-tinta-media',
              )}
            >
              {mensagem.conteudo}
            </p>
          </>
        )}
      </div>
    </button>
  )
}
