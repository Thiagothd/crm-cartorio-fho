import { NavLink } from 'react-router-dom'
import type { LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'

export interface SecaoDeAba<Chave extends string = string> {
  chave: Chave
  rotulo: string
  icone: LucideIcon
  /** Aparece sob o título, no cabeçalho da seção — opcional. */
  descricao?: string
}

/**
 * Abas de seção de uma tela, como LINKS — a seção vive na URL
 * (`/automacao/mensagens/respostas`), nunca em estado local.
 *
 * Custa o mesmo e resolve três coisas que estado local não resolve: F5 volta na
 * mesma aba, o link compartilhado abre onde o colega quer mostrar, e o botão
 * "voltar" do navegador faz o que a pessoa espera. Foi a mesma razão que levou
 * os filtros da inbox a serem semeáveis por query string, e a que já valia para
 * as seções de Indicadores (`features/indicadores/abas-de-relatorio.tsx`, que
 * delega para cá).
 *
 * As seções ficam FORA de `app/navegacao.ts` de propósito: são subdivisões de
 * UMA tela, não destinos do sistema. Colocá-las no menu do rail inflaria a
 * navegação principal com detalhe de uma página só — foi exatamente o que
 * motivou juntar mensagens/encerramento/respostas numa tela e situação/
 * conhecimento noutra.
 */
export function AbasDeSecao<Chave extends string>({
  base,
  secoes,
  rotuloDaNavegacao,
  className,
}: {
  /** Prefixo da rota; o link de cada aba é `${base}/${chave}`. */
  base: string
  secoes: readonly SecaoDeAba<Chave>[]
  /** `aria-label` do `<nav>` — o que estas abas dividem. */
  rotuloDaNavegacao: string
  className?: string
}) {
  return (
    <nav
      aria-label={rotuloDaNavegacao}
      className={cn(
        // Rola na horizontal no celular em vez de quebrar em duas linhas: abas
        // com ícone não cabem em 360px, e uma segunda linha empurraria o
        // conteúdo para baixo justamente na tela que tem menos altura.
        'flex gap-1 overflow-x-auto rounded-full border border-linha bg-superficie p-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden',
        className,
      )}
    >
      {secoes.map((secao) => (
        <NavLink
          key={secao.chave}
          to={`${base}/${secao.chave}`}
          className={({ isActive }) =>
            cn(
              // 44px no celular (DESIGN.md 12.2): este é o ÚNICO caminho entre
              // as seções, e alvo menor que isso é toque errado no balcão.
              'flex min-h-11 shrink-0 touch-manipulation items-center gap-2 rounded-full px-3.5 text-[13px] font-semibold transition-all duration-200 sm:min-h-9',
              isActive
                ? 'bg-tinta text-branco shadow-card'
                : 'text-tinta-media hover:bg-branco hover:text-tinta',
            )
          }
        >
          <secao.icone className="size-4 shrink-0" aria-hidden />
          {secao.rotulo}
        </NavLink>
      ))}
    </nav>
  )
}

/** True quando `valor` é uma das chaves declaradas — para validar `useParams`. */
export function ehSecaoDe<Chave extends string>(
  secoes: readonly SecaoDeAba<Chave>[],
  valor: string | undefined,
): valor is Chave {
  return secoes.some((secao) => secao.chave === valor)
}
