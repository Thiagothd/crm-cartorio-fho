import { useState } from 'react'
import { Plus } from 'lucide-react'
import { AcoesDaPagina } from '@/components/layout/contexto-de-acoes-da-pagina'
import { Button } from '@/components/ui/button'
import { ResumoDosSetores } from '@/features/configuracoes/resumo-dos-setores'
import { ListaDeSetores } from '@/features/configuracoes/lista-de-setores'
import { DialogoDeSetor } from '@/features/configuracoes/dialogo-de-setor'
import {
  usarSetoresDeGestao,
  usarInvalidarEstrutura,
} from '@/features/configuracoes/consultas-de-estrutura'
import type { Setor } from '@/lib/api/tipos'

/** Gêmea de `PaginaEtapas` — mesma moldura, mesma ordem de peças (DESIGN.md 17.5). */
export function PaginaSetores() {
  const { data: setores } = usarSetoresDeGestao()
  const invalidar = usarInvalidarEstrutura()

  const [emEdicao, setEmEdicao] = useState<Setor | null>(null)
  const [dialogoAberto, setDialogoAberto] = useState(false)

  function abrirNovo() {
    setEmEdicao(null)
    setDialogoAberto(true)
  }

  return (
    <>
      <AcoesDaPagina>
        <Button size="sm" onClick={abrirNovo}>
          <Plus aria-hidden="true" />
          <span>
            Novo<span className="hidden lg:inline"> Setor</span>
          </span>
        </Button>
      </AcoesDaPagina>

      <div className="min-h-0 flex-1 overflow-y-auto p-3 md:p-6">
        <div className="@container mx-auto flex max-w-[1600px] flex-col gap-4">
          <p className="max-w-2xl text-sm text-tinta-media">
            Setor é <strong className="text-tinta">quem atende</strong> — o eixo de roteamento da
            conversa. Não confundir com etapa, que é o ponto do atendimento no funil.
          </p>

          {setores ? <ResumoDosSetores setores={setores} /> : null}

          <ListaDeSetores
            aoEditar={(setor) => {
              setEmEdicao(setor)
              setDialogoAberto(true)
            }}
            aoCriar={abrirNovo}
          />
        </div>
      </div>

      <DialogoDeSetor
        setor={emEdicao}
        aberto={dialogoAberto}
        proximaOrdem={(setores?.length ?? 0) + 1}
        aoFechar={() => setDialogoAberto(false)}
        aoSalvar={() => invalidar('setores')}
      />
    </>
  )
}
