import { useEffect, useRef } from 'react'
import type { RespostaRapida } from '@/lib/api/tipos'
import { cn } from '@/lib/utils'

export const ID_DA_LISTA = 'seletor-de-resposta-rapida'

export const idDaOpcao = (indice: number) => `${ID_DA_LISTA}-opcao-${indice}`

/**
 * Lista das respostas rápidas que abre ao digitar "/" no campo de resposta.
 *
 * É apresentação pura: o estado de abertura e a navegação por teclado vivem na
 * CaixaDeEnvio, porque quem recebe as teclas é o `textarea` — o foco NUNCA sai
 * dele. É o padrão combobox: a opção ativa é anunciada por `aria-activedescendant`
 * em vez de receber foco de verdade, senão cada seta interromperia a digitação.
 *
 * Inserção assistida, nunca envio: escolher preenche o campo e para por aí. O
 * atendente lê, edita se quiser e envia — a assinatura da mensagem continua sendo
 * dele.
 */
export function SeletorDeRespostaRapida({
  respostas,
  termo,
  indiceAtivo,
  aoEscolher,
}: {
  respostas: RespostaRapida[]
  termo: string
  indiceAtivo: number
  aoEscolher: (resposta: RespostaRapida) => void
}) {
  const lista = useRef<HTMLUListElement>(null)

  // Mantém a opção ativa visível ao navegar com as setas. 'nearest' rola o mínimo
  // necessário, sem sacudir a lista a cada tecla.
  useEffect(() => {
    lista.current
      ?.querySelector(`[data-indice="${indiceAtivo}"]`)
      ?.scrollIntoView({ block: 'nearest' })
  }, [indiceAtivo])

  return (
    /* Sem sombra: papel não flutua (DESIGN.md, seção 4). A separação do fundo vem
       da borda forte e do fundo branco, como em todo painel da aplicação. */
    <div className="absolute bottom-full left-0 right-0 z-20 mb-2 overflow-hidden rounded-md border border-linha-forte bg-branco">
      <div className="flex items-center justify-between gap-2 border-b border-linha bg-superficie px-3 py-1.5">
        <span className="text-[11px] font-medium text-tinta-media">Respostas rápidas</span>
        <span className="text-[11px] text-tinta-fraca">
          ↑↓ navega · Enter insere · Esc fecha
        </span>
      </div>

      {respostas.length === 0 ? (
        <p className="px-3 py-3 text-xs text-tinta-media">
          Nenhuma resposta rápida para <span className="font-dado">/{termo}</span>. Cadastre em
          Configurações → Respostas rápidas.
        </p>
      ) : (
        <ul
          ref={lista}
          id={ID_DA_LISTA}
          role="listbox"
          aria-label="Respostas rápidas"
          className="max-h-64 overflow-y-auto py-1"
        >
          {respostas.map((resposta, indice) => {
            const ativa = indice === indiceAtivo

            return (
              <li
                key={resposta.id}
                id={idDaOpcao(indice)}
                data-indice={indice}
                role="option"
                aria-selected={ativa}
                /* onMouseDown, e não onClick: o clique tira o foco do textarea antes
                   do click disparar, o campo perde o "/" e a lista fecha sem inserir
                   nada. Prevenir o mousedown mantém o foco onde ele tem de estar. */
                onMouseDown={(evento) => {
                  evento.preventDefault()
                  aoEscolher(resposta)
                }}
                className={cn(
                  'cursor-pointer px-3 py-2 transition-colors',
                  ativa ? 'bg-superficie' : 'hover:bg-superficie',
                )}
              >
                <div className="flex items-baseline gap-2">
                  <span className="font-dado text-xs text-tinta-media">/{resposta.atalho}</span>
                  <span className="truncate text-sm font-medium text-tinta">{resposta.titulo}</span>
                </div>
                <p className="mt-0.5 line-clamp-1 text-xs text-tinta-fraca">{resposta.conteudo}</p>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}

/**
 * Normaliza para comparar: minúsculas e sem acento.
 *
 * O atendente digita com pressa, no meio do atendimento. Exigir que ele acerte o
 * acento de um atalho que ele nem vê escrito seria transformar um atalho em
 * obstáculo.
 */
function normalizar(valor: string) {
  return valor
    .toLowerCase()
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
}

/** Filtra por atalho e por título — o atendente pode lembrar de qualquer um dos dois. */
export function filtrarRespostas(respostas: RespostaRapida[], termo: string) {
  const busca = normalizar(termo.trim())

  if (busca.length === 0) {
    return respostas
  }

  return respostas.filter(
    (resposta) =>
      normalizar(resposta.atalho).includes(busca) || normalizar(resposta.titulo).includes(busca),
  )
}
