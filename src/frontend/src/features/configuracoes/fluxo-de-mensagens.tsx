import { ArrowRight, CornerDownRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { ReactNode } from 'react'
import type { Etapa, GatilhoDeMensagem, MensagemAutomatica } from '@/lib/api/tipos'

/**
 * Trilho horizontal do caminho automático — pedido do Thiago nesta sessão,
 * referência declarada: os canais do n8n.
 *
 * Diferente do n8n: o caminho aqui NÃO é livre. `GatilhoDeMensagem` é enum —
 * "gatilho novo exige código novo" (GatilhoDeMensagem.cs). Por isso o sistema
 * DESENHA o caminho real (topologia fixa, definida abaixo) em vez de deixar
 * arrastar e conectar nós, o que sugeriria caminho inventável pela tela.
 *
 * Sem sombra, gradiente nem cor fora dos tokens (DESIGN.md, seção 4) — o
 * trilho não gasta nenhuma das 3 ocorrências de bordô da tela.
 */

interface PontoFixo {
  gatilho: GatilhoDeMensagem
  rotulo: string
}

const PRIMEIRO_CONTATO: PontoFixo = { gatilho: 'NovoAtendimento', rotulo: 'Primeiro contato' }
const QUAL_SERVICO: PontoFixo = { gatilho: 'TriagemComCorrespondencia', rotulo: 'Qual serviço' }
const SERVICO_ESCOLHIDO: PontoFixo = { gatilho: 'ServicoEscolhido', rotulo: 'Documentos e prazo' }
const MENU_DE_CONTINUACAO: PontoFixo = { gatilho: 'MenuDeContinuacao', rotulo: 'Posso ajudar em mais algo?' }
const DESPEDIDA: PontoFixo = { gatilho: 'Despedida', rotulo: 'Cliente encerrou' }
const IA_SEM_CORRESPONDENCIA: PontoFixo = {
  gatilho: 'RespostaSemCorrespondencia',
  rotulo: 'IA sem resposta específica',
}
const ATENDENTE_PRIMEIRO_CONTATO: PontoFixo = {
  gatilho: 'TriagemSemCorrespondencia',
  rotulo: 'Atendente (1º contato)',
}
const ATENDENTE_EM_ATENDIMENTO: PontoFixo = {
  gatilho: 'PedidoDeAtendente',
  rotulo: 'Atendente (em atendimento)',
}
const PRAZO_ALERTA: PontoFixo = { gatilho: 'PrazoProximoVencimento', rotulo: 'Prazo perto de vencer' }
const PRAZO_VENCIDO: PontoFixo = { gatilho: 'PrazoVencido', rotulo: 'Prazo vencido' }
const PERGUNTA_DE_ENCERRAMENTO: PontoFixo = {
  gatilho: 'ConfirmacaoDeEncerramento',
  rotulo: 'Posso encerrar?',
}
const ENCERROU_SOZINHO: PontoFixo = {
  gatilho: 'EncerramentoPorInatividade',
  rotulo: 'Encerrou por falta de resposta',
}

export function FluxoDeMensagens({
  mensagens,
  etapas,
  aoClicarPonto,
}: {
  mensagens: MensagemAutomatica[]
  etapas: Etapa[]
  aoClicarPonto: (
    gatilho: GatilhoDeMensagem,
    etapaAlvoId: string | null,
    existente: MensagemAutomatica | null,
  ) => void
}) {
  function encontrar(gatilho: GatilhoDeMensagem, etapaAlvoId: string | null = null) {
    return mensagens.find((m) => m.gatilho === gatilho && m.etapaAlvoId === etapaAlvoId) ?? null
  }

  // As etapas do funil são cadastro do cartório parceiro (nome, ordem — sem papel fixo no
  // código), então o trilho segue a lista de verdade em vez de supor "em
  // análise"/"concluído" como dois pontos fixos.
  const etapasOrdenadas = [...etapas].sort((a, b) => a.ordem - b.ordem)

  const caminhoPrincipal = [
    { ...PRIMEIRO_CONTATO, etapaAlvoId: null as string | null },
    { ...QUAL_SERVICO, etapaAlvoId: null as string | null },
    { ...SERVICO_ESCOLHIDO, etapaAlvoId: null as string | null },
    { ...MENU_DE_CONTINUACAO, etapaAlvoId: null as string | null },
    { ...DESPEDIDA, etapaAlvoId: null as string | null },
    ...etapasOrdenadas.map((etapa) => ({
      gatilho: 'EtapaAlterada' as GatilhoDeMensagem,
      rotulo: etapa.nome,
      etapaAlvoId: etapa.id as string | null,
    })),
  ]

  return (
    <div className="flex flex-col gap-4 rounded-md border border-linha bg-superficie p-4">
      <div>
        <p className="text-sm font-semibold">Caminho do atendimento automático</p>
        <p className="text-xs text-tinta-media">
          Cada ponto é uma mensagem cadastrada. Clique para editar o texto ou cadastrar o que
          falta.
        </p>
      </div>

      <Trilho>
        {caminhoPrincipal.map((ponto, indice) => (
          <PontoComSeta key={`${ponto.gatilho}-${ponto.etapaAlvoId ?? ''}`} primeiro={indice === 0}>
            <Ponto
              rotulo={ponto.rotulo}
              mensagem={encontrar(ponto.gatilho, ponto.etapaAlvoId)}
              aoClicar={() =>
                aoClicarPonto(ponto.gatilho, ponto.etapaAlvoId, encontrar(ponto.gatilho, ponto.etapaAlvoId))
              }
            />
          </PontoComSeta>
        ))}
      </Trilho>

      <div>
        <div className="flex items-center gap-1.5 text-xs text-tinta-fraca">
          <CornerDownRight className="size-3.5 shrink-0" />
          <span>
            Se o cliente sai do roteiro, ou nenhuma flag de fluxo está esperando resposta — a IA
            nunca deixa passar em branco:
          </span>
        </div>

        <div className="mt-2 pl-5">
          <Trilho>
            <PontoComSeta primeiro>
              <div className="flex h-full w-40 shrink-0 items-center rounded-md border border-dashed border-linha bg-branco px-3 py-2 text-[11px] text-tinta-fraca">
                A IA tenta responder — não é cadastro, é comportamento do sistema.
              </div>
            </PontoComSeta>
            <PontoComSeta>
              <Ponto
                rotulo={IA_SEM_CORRESPONDENCIA.rotulo}
                mensagem={encontrar(IA_SEM_CORRESPONDENCIA.gatilho)}
                aoClicar={() =>
                  aoClicarPonto(
                    IA_SEM_CORRESPONDENCIA.gatilho,
                    null,
                    encontrar(IA_SEM_CORRESPONDENCIA.gatilho),
                  )
                }
              />
            </PontoComSeta>
          </Trilho>
        </div>

        <div className="mt-3 flex items-center gap-1.5 text-xs text-tinta-fraca">
          <CornerDownRight className="size-3.5 shrink-0" />
          <span>Chama atendente só quando o cliente pede — nunca por a IA não saber responder:</span>
        </div>

        <div className="mt-2 pl-5">
          <Trilho>
            {[ATENDENTE_PRIMEIRO_CONTATO, ATENDENTE_EM_ATENDIMENTO].map((ponto, indice) => (
              <PontoComSeta key={ponto.gatilho} primeiro={indice === 0}>
                <Ponto
                  rotulo={ponto.rotulo}
                  mensagem={encontrar(ponto.gatilho)}
                  aoClicar={() => aoClicarPonto(ponto.gatilho, null, encontrar(ponto.gatilho))}
                />
              </PontoComSeta>
            ))}
          </Trilho>
        </div>

        <div className="mt-3 flex items-center gap-1.5 text-xs text-tinta-fraca">
          <CornerDownRight className="size-3.5 shrink-0" />
          <span>
            Se a conversa fica parada, pergunta antes de fechar — o tempo é configurado em
            Automação &gt; Encerramento automático:
          </span>
        </div>

        <div className="mt-2 pl-5">
          <Trilho>
            {[PERGUNTA_DE_ENCERRAMENTO, ENCERROU_SOZINHO].map((ponto, indice) => (
              <PontoComSeta key={ponto.gatilho} primeiro={indice === 0}>
                <Ponto
                  rotulo={ponto.rotulo}
                  mensagem={encontrar(ponto.gatilho)}
                  aoClicar={() => aoClicarPonto(ponto.gatilho, null, encontrar(ponto.gatilho))}
                />
              </PontoComSeta>
            ))}
          </Trilho>
        </div>
      </div>

      <div className="border-t border-linha pt-3">
        <p className="text-xs font-medium text-tinta-media">
          Dispara por prazo do serviço, não por resposta do cliente
        </p>
        <div className="mt-2 flex flex-wrap gap-2">
          {[PRAZO_ALERTA, PRAZO_VENCIDO].map((ponto) => (
            <Ponto
              key={ponto.gatilho}
              rotulo={ponto.rotulo}
              mensagem={encontrar(ponto.gatilho)}
              aoClicar={() => aoClicarPonto(ponto.gatilho, null, encontrar(ponto.gatilho))}
            />
          ))}
        </div>
      </div>
    </div>
  )
}

function Trilho({ children }: { children: ReactNode }) {
  return (
    <div className="overflow-x-auto pb-1">
      <div className="flex w-max items-stretch">{children}</div>
    </div>
  )
}

function PontoComSeta({ children, primeiro }: { children: ReactNode; primeiro?: boolean }) {
  return (
    <div className="flex shrink-0 items-stretch">
      {primeiro ? null : (
        <div className="flex shrink-0 items-center px-1 text-tinta-fraca">
          <ArrowRight className="size-3.5" />
        </div>
      )}
      {children}
    </div>
  )
}

function Ponto({
  rotulo,
  mensagem,
  aoClicar,
}: {
  rotulo: string
  mensagem: MensagemAutomatica | null
  aoClicar: () => void
}) {
  const semMensagem = !mensagem
  const inativa = mensagem ? !mensagem.ativo : false

  return (
    <button
      type="button"
      onClick={aoClicar}
      className={cn(
        'flex h-full w-40 shrink-0 flex-col gap-1 rounded-md border px-3 py-2 text-left transition-colors',
        semMensagem && 'border-dashed border-linha bg-branco hover:border-ambar',
        !semMensagem && inativa && 'border-linha bg-branco hover:border-linha-forte',
        !semMensagem && !inativa && 'border-linha-forte bg-branco hover:border-tinta',
      )}
    >
      <span className={cn('text-xs font-medium', inativa && 'text-tinta-fraca')}>{rotulo}</span>
      {semMensagem ? (
        <span className="text-[11px] text-ambar">sem mensagem</span>
      ) : (
        <span className={cn('line-clamp-2 text-[11px]', inativa ? 'text-tinta-fraca' : 'text-tinta-media')}>
          {inativa ? 'Inativa — ' : ''}
          {mensagem.conteudo}
        </span>
      )}
    </button>
  )
}
