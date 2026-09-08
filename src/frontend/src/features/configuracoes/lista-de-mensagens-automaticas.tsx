import { Pencil } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import type { MensagemAutomatica } from '@/lib/api/tipos'

/** Todo marcador tem a forma `{Nome}` — o backend recusa qualquer outra. */
const MARCADOR = /\{[A-Za-zÀ-ÿ]+\}/g

function marcadoresUsados(conteudo: string): string[] {
  return [...new Set(conteudo.match(MARCADOR) ?? [])]
}

/**
 * A lista de tudo que está cadastrado, em cartões — não mais em tabela.
 *
 * Por que existe, se o fluxo já mostra cada ponto: o fluxo é a topologia, e a
 * lista é o cadastro. São coisas diferentes em dois casos reais — uma mensagem
 * cadastrada para uma etapa do funil que depois foi apagada não aparece em
 * ponto nenhum do caminho, e quem procura "onde falei em cartório de notas"
 * quer buscar pelo TEXTO, que o desenho recorta em três linhas.
 *
 * Cartão e não tabela pela mesma razão que a Equipe virou cartão: o dado
 * principal aqui é um parágrafo, não uma célula, e tabela com parágrafo dentro
 * ou vira rolagem horizontal ou vira `line-clamp` de uma linha só.
 */
export function ListaDeMensagensAutomaticas({
  mensagens,
  aoEditar,
}: {
  mensagens: MensagemAutomatica[]
  aoEditar: (mensagem: MensagemAutomatica) => void
}) {
  return (
    <div className="grade-com-vida grid gap-2.5 @3xl:grid-cols-2 @6xl:grid-cols-3">
      {mensagens.map((mensagem) => {
        const marcadores = marcadoresUsados(mensagem.conteudo)

        return (
          <button
            key={mensagem.id}
            type="button"
            onClick={() => aoEditar(mensagem)}
            className={cn(
              'group flex h-full flex-col gap-2 rounded-card border border-linha bg-branco p-3.5 text-left shadow-card transition-all duration-200 hover:-translate-y-0.5 hover:shadow-card-hover',
              !mensagem.ativo && 'opacity-70',
            )}
          >
            <div className="flex items-start gap-2">
              <div className="min-w-0 flex-1">
                <span className="block text-[13px] font-semibold leading-tight text-tinta">
                  {mensagem.gatilhoDescrito}
                </span>
                {mensagem.etapaAlvoNome ? (
                  <span className="mt-0.5 block text-[11px] text-tinta-fraca">
                    Etapa: {mensagem.etapaAlvoNome}
                  </span>
                ) : null}
              </div>

              {!mensagem.ativo ? <Badge variant="secondary">Desativada</Badge> : null}

              <Pencil
                aria-hidden
                className="mt-px size-3.5 shrink-0 text-tinta-fraca opacity-0 transition-opacity duration-150 group-hover:opacity-100 group-focus-visible:opacity-100"
              />
            </div>

            <p className="line-clamp-4 whitespace-pre-wrap border-t border-linha pt-2 text-[11px] leading-relaxed text-tinta-media">
              {mensagem.conteudo}
            </p>

            {/* Os marcadores que o texto usa, em mono — é literal que o sistema
                substitui na hora do envio, não palavra que o cliente lê. Ver de
                relance que uma mensagem de boas-vindas NÃO usa {MenuSetores} é
                a diferença entre um menu que aparece e um que não aparece. */}
            {marcadores.length > 0 ? (
              <div className="mt-auto flex flex-wrap gap-1">
                {marcadores.map((marcador) => (
                  <span
                    key={marcador}
                    className="rounded-md border border-linha bg-superficie px-1.5 py-0.5 font-dado text-[10px] text-tinta-media"
                  >
                    {marcador}
                  </span>
                ))}
              </div>
            ) : null}
          </button>
        )
      })}
    </div>
  )
}
