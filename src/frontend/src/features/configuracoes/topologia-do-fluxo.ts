import {
  CalendarClock,
  Contact,
  ListChecks,
  MessageCircleQuestion,
  Route,
  Sparkles,
  TimerOff,
  UserRound,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { Etapa, GatilhoDeMensagem, MensagemAutomatica } from '@/lib/api/tipos'

/**
 * A topologia do caminho automático — a mesma que `fluxo-de-mensagens.tsx`
 * desenhava embutida, agora em dado puro.
 *
 * Está separada da tela por duas razões. A primeira é que a contagem do topo
 * ("falta escrever em 4 pontos") precisa da MESMA lista que o desenho usa: se
 * fossem duas listas, o número contaria pontos que o desenho não mostra.
 * A segunda é que o caminho NÃO é livre — `GatilhoDeMensagem` é enum, e
 * "gatilho novo exige código novo" (GatilhoDeMensagem.cs). O que é cadastro é
 * o TEXTO e QUAL etapa dispara, nunca a topologia; por isso ela é constante
 * aqui, e não algo montado pela tela.
 */

export interface PontoDoFluxo {
  gatilho: GatilhoDeMensagem
  /** Só em `EtapaAlterada`; nos demais é sempre `null`. */
  etapaAlvoId: string | null
  rotulo: string
  /** Uma linha: em que momento exato o sistema chega neste ponto. */
  quando: string
  /** O que acontece se ninguém escrever o texto. Nunca é "quebra". */
  seFaltar: string
}

export interface TrilhaDoFluxo {
  chave: string
  titulo: string
  descricao: string
  icone: LucideIcon
  /** `true` numera os pontos: eles acontecem nesta ordem, um depois do outro. */
  sequencial: boolean
  /** Ocupa a linha inteira da grade — as trilhas longas, para não espremer. */
  larga: boolean
  pontos: PontoDoFluxo[]
  /** Comportamento do sistema que emoldura a trilha. Não é cadastro. */
  antes?: string
  depois?: string
}

/**
 * Espelho de `GatilhoDeMensagem` no backend, com o texto longo que o diálogo de
 * edição mostra. Lista fechada pela mesma razão da topologia.
 */
export const GATILHOS: { valor: GatilhoDeMensagem; rotulo: string; explicacao: string }[] = [
  {
    valor: 'PedidoDeNome',
    rotulo: 'Contato sem nome no WhatsApp',
    explicacao:
      'Sai antes das boas-vindas, só quando o perfil do contato não traz um nome utilizável (".", emoji, vazio). Depois da resposta, o menu sai já com o nome certo. Não use {NomeCliente} aqui: esta mensagem existe justamente porque ainda não há nome. Sem este texto o sistema manda o menu de sempre.',
  },
  {
    valor: 'NovoAtendimento',
    rotulo: 'Primeiro contato do cliente',
    explicacao:
      'Sai quando a conversa é aberta sem setor definido. Use {MenuSetores} para o cliente escolher o assunto.',
  },
  {
    valor: 'TriagemSemCorrespondencia',
    rotulo: 'Cliente não escolheu setor no menu',
    explicacao:
      'Sai quando a resposta ao menu não corresponde a nenhum setor. Depois disso o atendimento segue com uma pessoa.',
  },
  {
    valor: 'TriagemComCorrespondencia',
    rotulo: 'Setor reconhecido, falta escolher o serviço',
    explicacao:
      'Sai logo depois que o cliente acerta o setor no menu. Use {MenuServicos} para o cliente escolher o serviço específico.',
  },
  {
    valor: 'ServicoEscolhido',
    rotulo: 'Cliente escolheu o serviço',
    explicacao:
      'Sai quando o cliente identifica o serviço no menu de 2º nível. Use {DocumentosNecessarios} e {PrazoServico}.',
  },
  {
    valor: 'MenuDeContinuacao',
    rotulo: 'Atendimento estruturado terminou',
    explicacao:
      'Sai logo depois do checklist de documento, oferecendo continuar, trocar de setor, falar com atendente ou encerrar.',
  },
  {
    valor: 'PedidoDeAtendente',
    rotulo: 'Pediu atendente no meio do atendimento',
    explicacao:
      'Sai quando o cliente pede pessoa/atendente DEPOIS de já identificado — diferente do fallback de primeiro contato.',
  },
  {
    valor: 'Despedida',
    rotulo: 'Cliente escolheu encerrar',
    explicacao: 'Sai quando o cliente escolhe "Encerrar atendimento" no menu de continuação.',
  },
  {
    valor: 'EncerramentoPeloAtendente',
    rotulo: 'Atendente encerrou o atendimento',
    explicacao:
      'Sai quando um atendente encerra a conversa pelo painel. Diferente de "Cliente escolheu encerrar": ali quem fechou foi o cliente. Sem este texto o atendimento encerra do mesmo jeito, só sem avisar. O motivo digitado no encerramento NÃO vai para o cliente — fica só no registro interno.',
  },
  {
    valor: 'RespostaSemCorrespondencia',
    rotulo: 'IA sem resposta específica',
    explicacao:
      'Sai quando a IA não encontra resposta no catálogo cadastrado E o cliente não pediu atendente. Nunca promete atendente — convida a pedir um se quiser.',
  },
  {
    valor: 'ConfirmacaoDeEncerramento',
    rotulo: 'Conversa parada: posso encerrar?',
    explicacao:
      'Sai depois do tempo de silêncio configurado na aba Encerramento. Mantenha o menu numerado no texto: é por "1" e "2" que o sistema entende a resposta.',
  },
  {
    valor: 'EncerramentoPorInatividade',
    rotulo: 'Encerrado por falta de resposta',
    explicacao:
      'Sai quando o cliente não responde à pergunta acima e a conversa encerra sozinha. Sem este texto a conversa encerra do mesmo jeito, só sem avisar.',
  },
  {
    valor: 'EtapaAlterada',
    rotulo: 'Atendimento muda de etapa',
    explicacao: 'Sai quando a conversa entra na etapa escolhida abaixo.',
  },
  {
    valor: 'PrazoProximoVencimento',
    rotulo: 'Prazo perto de vencer',
    explicacao: 'Sai no percentual de alerta já configurado em cada serviço, na tela de Serviços.',
  },
  {
    valor: 'PrazoVencido',
    rotulo: 'Prazo vencido',
    explicacao: 'Sai quando o prazo-limite do serviço passa.',
  },
]

/** Achata a lista acima para consulta por chave, sem varrer o array toda vez. */
const EXPLICACAO_POR_GATILHO = new Map(GATILHOS.map((g) => [g.valor, g.explicacao]))

export function explicacaoDoGatilho(gatilho: GatilhoDeMensagem): string | undefined {
  return EXPLICACAO_POR_GATILHO.get(gatilho)
}

function ponto(
  gatilho: GatilhoDeMensagem,
  rotulo: string,
  quando: string,
  seFaltar: string,
): PontoDoFluxo {
  return { gatilho, etapaAlvoId: null, rotulo, quando, seFaltar }
}

/**
 * Monta as trilhas na ordem em que a serventia deveria lê-las: primeiro o
 * roteiro que a maioria percorre, depois as etapas do funil, e só então os
 * desvios e o que dispara por tempo em vez de por resposta do cliente.
 *
 * As duas primeiras são as `largas` (ocupam a linha inteira da grade). Vir
 * antes não é só editorial: com uma trilha estreita na frente, a larga não
 * cabe ao lado dela e a grade abre um buraco de meia largura na primeira
 * linha.
 *
 * As etapas do funil entram como trilha PRÓPRIA, e não coladas no fim do
 * roteiro como antes: mudar de etapa não é a continuação de "cliente
 * encerrou", é um eixo paralelo que pode acontecer a qualquer momento. A
 * versão anterior sugeria uma sequência que não existe.
 */
export function montarTrilhas(etapas: Etapa[]): TrilhaDoFluxo[] {
  const etapasOrdenadas = [...etapas].sort((a, b) => a.ordem - b.ordem)

  return [
    {
      chave: 'roteiro',
      titulo: 'Roteiro guiado',
      descricao: 'O caminho que a maior parte dos atendimentos percorre, do primeiro oi ao adeus.',
      icone: Route,
      sequencial: true,
      larga: true,
      pontos: [
        ponto(
          'NovoAtendimento',
          'Primeiro contato',
          'Conversa aberta sem setor definido.',
          'O cliente fala e não recebe menu nenhum.',
        ),
        ponto(
          'TriagemComCorrespondencia',
          'Setor reconhecido',
          'O cliente acertou o setor no menu.',
          'O cliente escolhe o setor e não recebe a lista de serviços.',
        ),
        ponto(
          'ServicoEscolhido',
          'Documentos e prazo',
          'O cliente identificou o serviço no menu de 2º nível.',
          'O cliente escolhe o serviço e não recebe documentos nem prazo.',
        ),
        ponto(
          'MenuDeContinuacao',
          'Posso ajudar em mais algo?',
          'Logo depois do checklist de documento.',
          'O atendimento automático termina em silêncio, sem oferecer saída.',
        ),
        ponto(
          'Despedida',
          'Cliente encerrou',
          'O cliente escolheu "Encerrar atendimento" no menu acima.',
          'A conversa encerra sem despedida.',
        ),
      ],
    },
    {
      chave: 'funil',
      titulo: 'Mudança de etapa',
      descricao:
        'Um aviso por etapa do funil, disparado quando o atendimento entra nela. As etapas vêm do cadastro em Estrutura > Etapas.',
      icone: ListChecks,
      sequencial: false,
      // Larga só quando há etapa que justifique: com duas etapas cadastradas,
      // a linha inteira deixaria metade da trilha vazia. O funil do cartório parceiro
      // ainda não está fechado (REQUISITOS 5), então o número é variável de
      // verdade, não uma constante disfarçada.
      larga: etapasOrdenadas.length > 2,
      pontos: etapasOrdenadas.map((etapa) => ({
        gatilho: 'EtapaAlterada' as GatilhoDeMensagem,
        etapaAlvoId: etapa.id,
        rotulo: etapa.nome,
        quando: etapa.ehFinal
          ? 'O atendimento chega nesta etapa de conclusão.'
          : 'O atendimento entra nesta etapa.',
        seFaltar: 'A etapa muda sem o cliente saber.',
      })),
    },
    {
      chave: 'entrada',
      titulo: 'Quem chega sem nome',
      descricao: 'Quem já tem nome no perfil do WhatsApp não passa por aqui.',
      icone: Contact,
      sequencial: true,
      larga: false,
      pontos: [
        ponto(
          'PedidoDeNome',
          'Contato sem nome',
          'O perfil do WhatsApp traz ".", emoji ou nada utilizável.',
          'O sistema manda o menu de sempre, sem o nome.',
        ),
      ],
      depois: 'Depois de responder, segue para o roteiro guiado — agora com o nome certo.',
    },
    {
      chave: 'fora-do-roteiro',
      titulo: 'Fora do roteiro',
      descricao: 'Quando o cliente escreve algo que o menu não previu.',
      icone: Sparkles,
      sequencial: true,
      larga: false,
      antes:
        'A IA tenta responder pelo catálogo cadastrado. Isso é comportamento do sistema, não cadastro — e ela nunca inventa.',
      pontos: [
        ponto(
          'RespostaSemCorrespondencia',
          'IA sem resposta específica',
          'Nada no catálogo responde, e o cliente não pediu atendente.',
          'O cliente escreve e fica sem resposta nenhuma.',
        ),
      ],
    },
    {
      chave: 'atendente',
      titulo: 'Quando o cliente pede uma pessoa',
      descricao: 'Só quando ele pede — nunca porque a IA não soube responder.',
      icone: UserRound,
      sequencial: false,
      larga: false,
      pontos: [
        ponto(
          'TriagemSemCorrespondencia',
          'Atendente no 1º contato',
          'A resposta ao menu não bate com setor nenhum.',
          'O atendimento passa para uma pessoa sem avisar o cliente.',
        ),
        ponto(
          'PedidoDeAtendente',
          'Atendente já em atendimento',
          'O cliente pede pessoa depois de já identificado.',
          'O pedido é registrado e o cliente não recebe confirmação.',
        ),
      ],
    },
    {
      chave: 'silencio',
      titulo: 'Conversa parada',
      descricao: 'Pergunta antes de fechar. O tempo é configurado na aba Encerramento.',
      icone: TimerOff,
      sequencial: true,
      larga: false,
      pontos: [
        ponto(
          'ConfirmacaoDeEncerramento',
          'Posso encerrar?',
          'Passou o tempo de silêncio configurado.',
          'A conversa não é encerrada: sem a pergunta, o ciclo não começa.',
        ),
        ponto(
          'EncerramentoPorInatividade',
          'Encerrou por falta de resposta',
          'O cliente não respondeu à pergunta acima.',
          'A conversa encerra do mesmo jeito, só sem avisar.',
        ),
      ],
    },
    {
      chave: 'encerramento-pelo-atendente',
      titulo: 'Atendente encerra pelo painel',
      descricao: 'Diferente de o cliente escolher encerrar: aqui quem fechou foi a serventia.',
      icone: MessageCircleQuestion,
      sequencial: false,
      larga: false,
      pontos: [
        ponto(
          'EncerramentoPeloAtendente',
          'Atendente encerrou',
          'Alguém fecha a conversa pelo painel.',
          'O atendimento encerra do mesmo jeito, só sem avisar.',
        ),
      ],
      depois: 'O motivo digitado no encerramento nunca vai para o cliente — fica no registro interno.',
    },
    {
      chave: 'prazo',
      titulo: 'Prazo do serviço',
      descricao: 'Dispara por tempo, não por resposta do cliente. O percentual de alerta é de cada serviço.',
      icone: CalendarClock,
      sequencial: false,
      larga: false,
      pontos: [
        ponto(
          'PrazoProximoVencimento',
          'Prazo perto de vencer',
          'O atendimento cruza o percentual de alerta do serviço.',
          'Ninguém é avisado antes de o prazo estourar.',
        ),
        ponto(
          'PrazoVencido',
          'Prazo vencido',
          'O prazo-limite do serviço passou.',
          'O prazo vence em silêncio.',
        ),
      ],
    },
  ]
}

export function encontrarMensagem(
  mensagens: MensagemAutomatica[],
  gatilho: GatilhoDeMensagem,
  etapaAlvoId: string | null,
): MensagemAutomatica | null {
  return mensagens.find((m) => m.gatilho === gatilho && m.etapaAlvoId === etapaAlvoId) ?? null
}

export interface ContagemDoFluxo {
  /** Pontos que a topologia desenha — cresce com o número de etapas do funil. */
  pontos: number
  comTexto: number
  faltando: number
  desativados: number
}

/**
 * Conta sobre a TOPOLOGIA, não sobre a lista de mensagens: o número que
 * importa para quem configura é "quantos pontos do caminho ainda estão mudos",
 * e mensagem cadastrada para uma etapa já apagada não é ponto do caminho.
 */
export function contarFluxo(
  trilhas: TrilhaDoFluxo[],
  mensagens: MensagemAutomatica[],
): ContagemDoFluxo {
  let pontos = 0
  let comTexto = 0
  let faltando = 0
  let desativados = 0

  for (const trilha of trilhas) {
    for (const p of trilha.pontos) {
      pontos += 1
      const mensagem = encontrarMensagem(mensagens, p.gatilho, p.etapaAlvoId)

      if (!mensagem) faltando += 1
      else if (mensagem.ativo) comTexto += 1
      else desativados += 1
    }
  }

  return { pontos, comTexto, faltando, desativados }
}
