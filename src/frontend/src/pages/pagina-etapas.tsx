import { useState } from 'react'
import { Plus } from 'lucide-react'
import { AcoesDaPagina } from '@/components/layout/contexto-de-acoes-da-pagina'
import { Button } from '@/components/ui/button'
import { ResumoDasEtapas } from '@/features/configuracoes/resumo-das-etapas'
import { ListaDeEtapas } from '@/features/configuracoes/lista-de-etapas'
import { DialogoDeEtapa } from '@/features/configuracoes/dialogo-de-etapa'
import {
  usarEtapasDeGestao,
  usarInvalidarEstrutura,
} from '@/features/configuracoes/consultas-de-estrutura'
import type { Etapa } from '@/lib/api/tipos'

/**
 * Etapas do funil — Fase 3.1 (DESIGN.md 17.5). Mesma moldura de Equipe,
 * Clientes e Mensagens: teto de 1600px e `@container` local, para as grades
 * medirem a largura que sobra depois do padding e do teto, não a do shell.
 *
 * A ação primária vive no cabeçalho global (`AcoesDaPagina`), como nas outras
 * telas de gestão — e é a única ocorrência de bordô que esta tela gasta além
 * do item ativo do rail.
 */
export function PaginaEtapas() {
  const { data: etapas } = usarEtapasDeGestao()
  const invalidar = usarInvalidarEstrutura()

  const [emEdicao, setEmEdicao] = useState<Etapa | null>(null)
  const [dialogoAberto, setDialogoAberto] = useState(false)

  function abrirNova() {
    setEmEdicao(null)
    setDialogoAberto(true)
  }

  return (
    <>
      <AcoesDaPagina>
        <Button size="sm" onClick={abrirNova}>
          <Plus aria-hidden="true" />
          <span>
            Nova<span className="hidden lg:inline"> Etapa</span>
          </span>
        </Button>
      </AcoesDaPagina>

      <div className="min-h-0 flex-1 overflow-y-auto p-3 md:p-6">
        <div className="@container mx-auto flex max-w-[1600px] flex-col gap-4">
          <p className="max-w-2xl text-sm text-tinta-media">
            Etapa é <strong className="text-tinta">em que ponto está</strong> o atendimento — o
            eixo de progressão. A ordem abaixo define a sequência do funil no painel.
          </p>

          {etapas ? <ResumoDasEtapas etapas={etapas} /> : null}

          <ListaDeEtapas
            aoEditar={(etapa) => {
              setEmEdicao(etapa)
              setDialogoAberto(true)
            }}
            aoCriar={abrirNova}
          />
        </div>
      </div>

      <DialogoDeEtapa
        etapa={emEdicao}
        aberto={dialogoAberto}
        proximaOrdem={(etapas?.length ?? 0) + 1}
        aoFechar={() => setDialogoAberto(false)}
        aoSalvar={() => invalidar('etapas')}
      />
    </>
  )
}
