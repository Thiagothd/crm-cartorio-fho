import { cn } from '@/lib/utils'
import { iniciaisDe } from '@/lib/formatacao'

const tamanhos = {
  sm: 'size-7 text-[10px]',
  md: 'size-9 text-[11px]',
  lg: 'size-11 text-xs',
}

/**
 * Iniciais em caixa quadrada.
 *
 * Dois tratamentos:
 *
 * - Sem `corDeFundo`: caixa clara com borda 1px e iniciais em tinta. E o padrao
 *   para avatar sem etapa associada — cliente na listagem de cadastro, atendente.
 *
 * - Com `corDeFundo`: fundo solido e iniciais em branco. Usado no atendimento,
 *   onde a cor identifica a ETAPA da conversa (docs/DESIGN.md, secao 2.2).
 *
 * A versao original gerava a cor por hash do NOME. Isso foi removido e nao deve
 * voltar: era cor arbitraria fora da paleta, e pior, dizia respeito a nada — duas
 * conversas do mesmo cliente em etapas diferentes ficavam iguais. Cor por etapa
 * carrega informacao; cor por hash so enfeitava.
 */
export function Avatar({
  nome,
  tamanho = 'md',
  corDeFundo,
  className,
}: {
  nome: string
  tamanho?: keyof typeof tamanhos
  corDeFundo?: string | null
  className?: string
}) {
  const solido = Boolean(corDeFundo)

  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center justify-center rounded-md border font-titulo font-semibold uppercase tracking-wider transition-colors',
        solido
          ? 'border-transparent text-branco'
          : 'border-linha-forte bg-branco text-tinta-media',
        tamanhos[tamanho],
        className,
      )}
      style={solido ? { backgroundColor: corDeFundo! } : undefined}
      title={nome}
    >
      {iniciaisDe(nome)}
    </span>
  )
}
