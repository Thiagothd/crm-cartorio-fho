import { useMemo, useState } from 'react'
import {
  CHAVES_DE_PERIODO,
  resolverIntervalo,
  type ChaveDePeriodo,
  type IntervaloDePeriodo,
} from '@/features/indicadores/periodo'
import { SeletorDePeriodo } from '@/features/indicadores/seletor-de-periodo'
import { PainelDeProtecao } from '@/features/seguranca/painel-de-protecao'
import { PainelDeAcessosTecnicos } from '@/features/seguranca/painel-de-acessos-tecnicos'
import { TabelaDeAcessos } from '@/features/seguranca/tabela-de-acessos'
import { TabelaDeEventos } from '@/features/seguranca/tabela-de-eventos'
import { usarPreferenciaDeTexto } from '@/lib/usar-preferencia-local'

/**
 * "Acesso aos dados" — blindagem LGPD (2026-09). A resposta, visível na tela,
 * para "o programador consegue ver as conversas do meu cliente sem eu
 * saber?" (docs/PROTECAO-DE-DADOS.md): o que já está protegido, quem abriu o
 * quê, e os eventos de segurança do SEC-03 que antes não tinham tela própria.
 *
 * Mesmo padrão de filtro de período de `pagina-indicadores.tsx`: um seletor
 * no topo, compartilhado pelas duas tabelas — trocar de período não perde o
 * recorte escolhido ao ler a outra tabela.
 */
export function PaginaAcessoAosDados() {
  const [chaveDePeriodo, definirChaveDePeriodo] = usarPreferenciaDeTexto<ChaveDePeriodo>(
    'crm-cartorio.acesso-aos-dados.periodo',
    '7dias',
    CHAVES_DE_PERIODO,
  )
  const [personalizado, definirPersonalizado] = useState<IntervaloDePeriodo>(() => resolverIntervalo('7dias'))

  const intervalo = useMemo(
    () => resolverIntervalo(chaveDePeriodo, personalizado),
    [chaveDePeriodo, personalizado],
  )

  const de = intervalo.de.toISOString()
  const ate = intervalo.ate.toISOString()

  return (
    <div className="min-h-0 flex-1 overflow-y-auto p-3 md:p-6">
      <div className="mx-auto flex max-w-[1600px] flex-col gap-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h1 className="text-lg font-bold tracking-tight text-tinta">Acesso aos dados</h1>
            <p className="text-xs text-tinta-media">
              Quem abriu o quê, e o que já está protegido nas conversas do cartório.
            </p>
          </div>

          <SeletorDePeriodo
            periodo={chaveDePeriodo}
            aoMudarPeriodo={definirChaveDePeriodo}
            personalizado={personalizado}
            aoMudarPersonalizado={definirPersonalizado}
          />
        </div>

        <div className="rounded-md border border-linha bg-superficie px-4 py-3 text-xs text-tinta-media">
          <p className="font-semibold text-tinta">O que tem nesta tela</p>
          <p className="mt-1">
            <span className="font-semibold text-tinta">Proteção ativa</span> resume se a cifra em
            repouso está ligada neste ambiente. <span className="font-semibold text-tinta">Acesso
            técnico</span> mostra pedidos de suporte que precisam da sua aprovação (só aparece para
            quem pode aprovar). <span className="font-semibold text-tinta">Acessos</span> lista, um
            por um, toda vez que alguém do cartório abriu uma conversa, baixou um anexo ou abriu a
            ficha completa de um cliente — quem, o quê e quando. <span className="font-semibold
            text-tinta">Eventos de segurança</span> traz login, ativação de segundo fator e reset de
            senha. O período escolhido acima filtra as duas tabelas de uma vez.
          </p>
        </div>

        <PainelDeProtecao />
        <PainelDeAcessosTecnicos />
        <TabelaDeAcessos de={de} ate={ate} />
        <TabelaDeEventos de={de} ate={ate} />
      </div>
    </div>
  )
}
