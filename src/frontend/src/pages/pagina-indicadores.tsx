import { useMemo, useState } from 'react'
import { Navigate, useParams } from 'react-router-dom'
import {
  CHAVES_DE_PERIODO,
  resolverIntervalo,
  type ChaveDePeriodo,
  type IntervaloDePeriodo,
} from '@/features/indicadores/periodo'
import { SeletorDePeriodo } from '@/features/indicadores/seletor-de-periodo'
import {
  AbasDeRelatorio,
  SECOES,
  ehSecaoValida,
  type SecaoDeRelatorio,
} from '@/features/indicadores/abas-de-relatorio'
import { AbaDashboard } from '@/features/indicadores/abas/aba-dashboard'
import { AbaAtendimentos } from '@/features/indicadores/abas/aba-atendimentos'
import { AbaAtendentes } from '@/features/indicadores/abas/aba-atendentes'
import { AbaClientes } from '@/features/indicadores/abas/aba-clientes'
import { usarPreferenciaDeTexto } from '@/lib/usar-preferencia-local'

/**
 * Indicadores e Relatórios — quatro seções sobre a mesma operação.
 *
 * A seção vem da URL (`/indicadores/atendentes`), não de estado local: F5 volta
 * na mesma aba, link compartilhado abre onde o colega quer mostrar, e o botão
 * "voltar" do navegador faz o que se espera. Ver `abas-de-relatorio.tsx`.
 *
 * O filtro de período fica FORA das abas, no cabeçalho da tela, e é o mesmo para
 * as três abas que o usam — trocar de aba não perde o recorte escolhido. O
 * Dashboard ignora o filtro de propósito: as janelas dele (hoje / 7 dias / mês)
 * são fixas por definição, e um filtro por cima delas seria decorativo.
 */
export function PaginaIndicadores() {
  const { secao } = useParams<{ secao?: string }>()

  const [chaveDePeriodo, definirChaveDePeriodo] = usarPreferenciaDeTexto<ChaveDePeriodo>(
    'crm-cartorio.indicadores.periodo',
    '30dias',
    CHAVES_DE_PERIODO,
  )
  const [personalizado, definirPersonalizado] = useState<IntervaloDePeriodo>(() => resolverIntervalo('30dias'))

  const intervalo = useMemo(
    () => resolverIntervalo(chaveDePeriodo, personalizado),
    [chaveDePeriodo, personalizado],
  )

  const de = intervalo.de.toISOString()
  const ate = intervalo.ate.toISOString()

  // `/indicadores` sem seção cai no Dashboard, com `replace` para não deixar um
  // passo morto no histórico do navegador.
  if (!ehSecaoValida(secao)) {
    return <Navigate to="/indicadores/dashboard" replace />
  }

  const secaoAtual: SecaoDeRelatorio = secao
  const metadados = SECOES.find((s) => s.chave === secaoAtual)!
  const usaPeriodo = secaoAtual !== 'dashboard'

  return (
    <div className="flex-1 overflow-y-auto p-3 md:p-6">
      {/* `@container`, local — não `/conteudo` do shell: as grades abaixo
          precisam da largura que sobra DEPOIS do padding (`p-3 md:p-6`) e do
          teto de `max-w-6xl`, e consultar o container do shell direto
          ignoraria os dois. */}
      <div className="@container mx-auto flex max-w-6xl flex-col gap-4">
        <div className="flex flex-col gap-3">
          <AbasDeRelatorio className="self-start" />

          <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
            <p className="text-sm text-tinta-media">{metadados.descricao}</p>

            {/* O seletor só aparece na aba que de fato filtra. Mostrá-lo
                desabilitado no Dashboard sugeriria que ali existe um recorte a
                escolher, e não existe. */}
            {usaPeriodo ? (
              <SeletorDePeriodo
                periodo={chaveDePeriodo}
                aoMudarPeriodo={definirChaveDePeriodo}
                personalizado={personalizado}
                aoMudarPersonalizado={definirPersonalizado}
              />
            ) : (
              <p className="text-xs font-medium text-tinta-fraca">
                Janelas fixas — este painel não usa filtro de período
              </p>
            )}
          </div>
        </div>

        {/* Cada aba recebe `ativa` e só busca quando é a que está na tela. As
            outras ficam desmontadas, mas o cache do React Query guarda o que já
            foi carregado — voltar para uma aba visitada é instantâneo. */}
        {secaoAtual === 'dashboard' ? <AbaDashboard ativa /> : null}
        {secaoAtual === 'atendimentos' ? <AbaAtendimentos de={de} ate={ate} ativa /> : null}
        {secaoAtual === 'atendentes' ? <AbaAtendentes de={de} ate={ate} ativa /> : null}
        {secaoAtual === 'clientes' ? <AbaClientes de={de} ate={ate} ativa /> : null}
      </div>
    </div>
  )
}
