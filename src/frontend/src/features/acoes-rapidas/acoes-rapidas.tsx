import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { usarBuscaGlobal } from '@/components/layout/contexto-de-busca-global'
import { podeVer } from '@/app/navegacao'
import { cn } from '@/lib/utils'
import { ACOES_DE_TRABALHO, acoesDeGestao, carregarRota, type AcaoRapida } from './catalogo-de-acoes'
import { CartaoDeAcao } from './cartao-de-acao'
import { CartaoPrincipal } from './cartao-principal'

export interface ContadoresDeAcoes {
  /** `undefined` = carregando (só na primeira carga) · `null` = indisponível. */
  fila: number | null | undefined
  aguardando: number | null | undefined
  minhas: number | null | undefined
}

/**
 * Bloco de ações rápidas da Visão Geral. Deliberadamente burro: recebe os
 * contadores e os dois disparadores de criação por prop, não conhece React
 * Query nem diálogo — quem decide a fonte dos números é a página. Só conhece
 * `usarAutenticacao()` (para esconder o que o perfil não pode ver, mesma
 * regra do menu) e `usarBuscaGlobal()` (contexto do Shell).
 */
export function AcoesRapidas({
  contadores,
  aoAbrirNovoAtendimento,
  aoAbrirNovoCliente,
  className,
}: {
  contadores: ContadoresDeAcoes
  aoAbrirNovoAtendimento: () => void
  aoAbrirNovoCliente: () => void
  className?: string
}) {
  const { podeGerirCadastros, podeGerirAtendentes } = usarAutenticacao()
  const busca = usarBuscaGlobal()

  const perfil = { podeGerirCadastros, podeGerirAtendentes }

  // Esconder, nunca desabilitar (mesmo padrão do rail de navegação). O
  // backend continua sendo quem decide de verdade.
  const gestao = useMemo(
    () => acoesDeGestao().filter((acao) => podeVer(acao.permissao, perfil)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [podeGerirCadastros, podeGerirAtendentes],
  )

  function comandoDe(acao: Extract<AcaoRapida, { tipo: 'comando' }>) {
    if (acao.comando === 'abrir-busca') return busca?.abrir
    return undefined
  }

  return (
    <section aria-labelledby="acoes-rapidas-titulo" className={cn('flex flex-col gap-2.5', className)}>
      <h2
        id="acoes-rapidas-titulo"
        className="px-0.5 text-[11px] font-bold uppercase tracking-wider text-tinta-fraca"
      >
        Ações rápidas
      </h2>

      {/* `gap-3`, não `gap-4`: são cartões pequenos e muitos — 16px entre eles
          soltaria o conjunto e ele deixaria de ler como UM bloco. */}
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <CartaoPrincipal aoAbrirAtendimento={aoAbrirNovoAtendimento} aoAbrirCliente={aoAbrirNovoCliente} />

        {ACOES_DE_TRABALHO.map((acao) => {
          if (acao.tipo === 'rota') {
            return (
              <CartaoDeAcao
                key={acao.chave}
                acao={acao}
                contador={acao.contador ? contadores[acao.contador] : null}
                aoPrefetch={carregarRota(acao.rota)}
              />
            )
          }

          const acionar = comandoDe(acao)

          // Sem provedor de busca montado (fora do Shell), o card
          // simplesmente não aparece — nunca um botão que não faz nada.
          if (!acionar) return null

          return <CartaoDeAcao key={acao.chave} acao={acao} contador={null} aoAcionar={acionar} />
        })}
      </div>

      {gestao.length > 0 ? (
        <div className="mt-0.5 flex flex-wrap items-center gap-1.5 border-t border-linha/70 pt-3">
          <span className="mr-0.5 shrink-0 text-[10px] font-bold uppercase tracking-wider text-tinta-fraca">
            Gestão
          </span>
          {gestao.map((acao) => (
            <Link
              key={acao.chave}
              to={acao.rota}
              title={acao.descricao}
              onPointerEnter={carregarRota(acao.rota)}
              onFocus={carregarRota(acao.rota)}
              className="inline-flex min-h-11 touch-manipulation items-center gap-1.5 rounded-full border border-linha bg-branco px-3 text-[11px] font-semibold text-tinta-media transition-all duration-200 hover:border-linha-forte hover:text-tinta hover:shadow-card md:min-h-8"
            >
              <acao.icone className="size-3.5 shrink-0" aria-hidden />
              {acao.rotulo}
            </Link>
          ))}
        </div>
      ) : null}
    </section>
  )
}
