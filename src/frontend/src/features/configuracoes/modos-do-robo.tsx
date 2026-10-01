import { cn } from '@/lib/utils'
import type { ModoDoRoboNoSetor } from '@/lib/api/tipos'

/**
 * As três formas de o robô tratar um setor — ver `ModoDoRoboNoSetor` no backend.
 * Uma lista só para o diálogo do setor e o botão da linha: o mesmo texto nos dois
 * lugares, para ninguém ler "só equipe" com dois sentidos.
 */
export const MODOS_DO_ROBO: { valor: ModoDoRoboNoSetor; titulo: string; descricao: string }[] = [
  {
    valor: 'Ligado',
    titulo: 'Robô ligado',
    descricao: 'Como sempre: menu de serviços, textos e IA.',
  },
  {
    valor: 'SoEquipe',
    titulo: 'Só equipe',
    descricao:
      'Aparece no menu, mas quem escolhe vai direto para uma pessoa, com o aviso de que um atendente vai continuar.',
  },
  {
    valor: 'ForaDoMenu',
    titulo: 'Fora do menu',
    descricao: 'Some do menu do robô. Continua valendo no painel: dá para mover conversas para ele.',
  },
]

/** Os três modos como cartões de escolha (radio), no padrão das escolhas do painel. */
export function EscolhaDoModoDoRobo({
  valor,
  aoEscolher,
  desabilitado = false,
  rotulo,
}: {
  valor: ModoDoRoboNoSetor
  aoEscolher: (modo: ModoDoRoboNoSetor) => void
  desabilitado?: boolean
  rotulo: string
}) {
  return (
    <div role="radiogroup" aria-label={rotulo} className="flex flex-col gap-2">
      {MODOS_DO_ROBO.map((modo) => {
        const ativa = valor === modo.valor

        return (
          <button
            key={modo.valor}
            type="button"
            role="radio"
            aria-checked={ativa}
            disabled={desabilitado}
            onClick={() => aoEscolher(modo.valor)}
            className={cn(
              'flex w-full items-start gap-2.5 rounded-md border px-3 py-2.5 text-left transition-colors duration-[var(--duracao-rapida)] disabled:cursor-not-allowed disabled:opacity-60',
              ativa ? 'border-tinta/40 bg-superficie' : 'border-linha hover:bg-superficie',
            )}
          >
            <span
              aria-hidden
              className={cn(
                'mt-0.5 flex size-4 shrink-0 items-center justify-center rounded-full border',
                ativa ? 'border-tinta' : 'border-linha-forte',
              )}
            >
              {ativa ? <span className="size-2 rounded-full bg-tinta" /> : null}
            </span>
            <span className="min-w-0">
              <span className="block text-sm font-medium text-tinta">{modo.titulo}</span>
              <span className="block text-xs leading-relaxed text-tinta-media">{modo.descricao}</span>
            </span>
          </button>
        )
      })}
    </div>
  )
}
