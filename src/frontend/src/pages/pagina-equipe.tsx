import { useState } from 'react'
import { Plus } from 'lucide-react'
import { AcoesDaPagina } from '@/components/layout/contexto-de-acoes-da-pagina'
import { Button } from '@/components/ui/button'
import { ResumoDaEquipe } from '@/features/equipe/resumo-da-equipe'
import { ListaDeAtendentes } from '@/features/equipe/lista-de-atendentes'
import { DialogoDeAtendente } from '@/features/equipe/dialogo-de-atendente'
import { DialogoDeSenha } from '@/features/equipe/dialogo-de-senha'
import { DialogoDeMfa } from '@/features/equipe/dialogo-de-mfa'
import { usarAtendentes } from '@/features/equipe/consultas'
import type { Atendente } from '@/lib/api/tipos'

export function PaginaEquipe() {
  const { data: atendentes } = usarAtendentes(true)
  const [emEdicao, setEmEdicao] = useState<Atendente | null>(null)
  const [dialogoAberto, setDialogoAberto] = useState(false)
  const [senhaDe, setSenhaDe] = useState<Atendente | null>(null)
  const [mfaDe, setMfaDe] = useState<Atendente | null>(null)

  return (
    <>
      <AcoesDaPagina>
        <Button
          size="sm"
          onClick={() => {
            setEmEdicao(null)
            setDialogoAberto(true)
          }}
        >
          <Plus aria-hidden="true" />
          <span>
            Novo<span className="hidden lg:inline"> Atendente</span>
          </span>
        </Button>
      </AcoesDaPagina>

      <div className="min-h-0 flex-1 overflow-y-auto p-3 md:p-6">
        <div className="mx-auto flex max-w-[1600px] flex-col gap-4">
          {atendentes ? <ResumoDaEquipe atendentes={atendentes} /> : null}

          <ListaDeAtendentes
            aoEditar={(atendente) => {
              setEmEdicao(atendente)
              setDialogoAberto(true)
            }}
            aoRedefinirSenha={setSenhaDe}
            aoAbrirMfa={setMfaDe}
          />
        </div>
      </div>

      <DialogoDeAtendente atendente={emEdicao} aberto={dialogoAberto} aoFechar={() => setDialogoAberto(false)} />
      <DialogoDeSenha atendente={senhaDe} aoFechar={() => setSenhaDe(null)} />
      <DialogoDeMfa atendente={mfaDe} aoFechar={() => setMfaDe(null)} />
    </>
  )
}
