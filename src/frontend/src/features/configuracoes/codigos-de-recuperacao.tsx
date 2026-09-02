import { Copy, KeyRound, TriangleAlert } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'

/**
 * Os códigos de recuperação, mostrados UMA única vez.
 *
 * Vive aqui (e não em `components/ui/`) de propósito: `components/ui/` é a
 * zona de primitivos na linguagem de painel (DESIGN.md 15.1), e este bloco é
 * da linguagem de documento — borda de 1px, sem elevação, sem gradiente. As
 * duas telas que o usam (Segurança e matrícula privilegiada) já o desenhavam
 * assim, quase byte a byte iguais; agora é um lugar só.
 */
export function CodigosDeRecuperacao({
  codigos,
  aoFechar,
  rotuloDoBotao = 'Já guardei — concluir',
}: {
  codigos: string[]
  aoFechar: () => void
  rotuloDoBotao?: string
}) {
  return (
    <div className="flex w-full max-w-md flex-col gap-3 rounded-md border border-ambar/40 bg-ambar/[0.06] p-4">
      <p className="flex items-start gap-2 text-sm font-medium text-tinta">
        <TriangleAlert className="mt-0.5 size-4 shrink-0 text-ambar" aria-hidden />
        Guarde estes {codigos.length} códigos de recuperação agora — eles não serão mostrados de novo.
      </p>
      <p className="text-xs text-tinta-media">
        Cada um funciona uma única vez, no lugar do código do autenticador, se você perder o acesso a ele.
      </p>

      <div className="grid grid-cols-2 gap-1.5 font-dado text-xs">
        {codigos.map((codigo) => (
          <code key={codigo} className="rounded border border-linha bg-branco px-2 py-1 text-center">
            {codigo}
          </code>
        ))}
      </div>

      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => {
            void navigator.clipboard.writeText(codigos.join('\n'))
            toast.success('Códigos copiados.')
          }}
        >
          <Copy className="size-3.5" aria-hidden />
          Copiar todos
        </Button>
        <Button type="button" size="sm" onClick={aoFechar}>
          <KeyRound className="size-3.5" aria-hidden />
          {rotuloDoBotao}
        </Button>
      </div>
    </div>
  )
}
