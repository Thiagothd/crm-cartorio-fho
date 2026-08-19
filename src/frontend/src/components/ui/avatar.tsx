import { useEffect, useState } from 'react'
import { cn } from '@/lib/utils'
import { iniciaisDe } from '@/lib/formatacao'

const tamanhos = {
  sm: 'size-7 text-[10px]',
  md: 'size-9 text-[11px]',
  lg: 'size-11 text-xs',
}

/**
 * Iniciais em caixa quadrada ou foto de perfil real quando disponível.
 *
 * Tratamento:
 * - Com `fotoUrl` válido: exibe a imagem de perfil do WhatsApp. Caso falhe ao
 *   carregar ou retorne 404, faz fallback automático e gracioso para as iniciais.
 * - Sem `corDeFundo`: caixa clara com borda 1px e iniciais em tinta. É o padrão
 *   para avatar sem etapa associada (ex.: cliente na listagem de cadastro, atendente).
 * - Com `corDeFundo`: fundo sólido e iniciais em branco. Usado no atendimento,
 *   onde a cor identifica a ETAPA da conversa (docs/DESIGN.md, seção 2.2).
 */
export function Avatar({
  nome,
  fotoUrl,
  tamanho = 'md',
  corDeFundo,
  className,
}: {
  nome: string
  fotoUrl?: string | null
  tamanho?: keyof typeof tamanhos
  corDeFundo?: string | null
  className?: string
}) {
  const [erroAoCarregar, setErroAoCarregar] = useState(false)
  const solido = Boolean(corDeFundo)

  useEffect(() => {
    setErroAoCarregar(false)
  }, [fotoUrl])

  if (fotoUrl && !erroAoCarregar) {
    return (
      <span
        className={cn(
          'inline-flex shrink-0 items-center justify-center overflow-hidden rounded-md border border-linha-forte bg-superficie transition-colors',
          tamanhos[tamanho],
          className,
        )}
        title={nome}
      >
        <img
          src={fotoUrl}
          alt={nome}
          className="size-full object-cover"
          loading="lazy"
          onError={() => setErroAoCarregar(true)}
        />
      </span>
    )
  }

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

