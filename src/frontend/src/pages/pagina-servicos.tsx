import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Plus, Scale } from 'lucide-react'
import { CabecalhoDaPagina, EstadoVazio } from '@/components/layout/shell'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { servicosApi } from '@/lib/api/endpoints'
import { DialogoDeServico } from '@/features/servicos/dialogo-de-servico'
import type { TipoServico } from '@/lib/api/tipos'

export function PaginaServicos() {
  const [emEdicao, setEmEdicao] = useState<TipoServico | null>(null)
  const [dialogoAberto, setDialogoAberto] = useState(false)
  const [incluirInativos, setIncluirInativos] = useState(true)

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['servicos-gestao', incluirInativos],
    queryFn: () => servicosApi.listar(incluirInativos),
  })

  const porCategoria = (data ?? []).reduce<Record<string, TipoServico[]>>((mapa, servico) => {
    ;(mapa[servico.categoriaDescrita] ??= []).push(servico)
    return mapa
  }, {})

  return (
    <>
      <CabecalhoDaPagina
        titulo="Serviços"
        descricao="Catálogo de atos e prazo de cada um"
        acoes={
          <>
            <Button
              variant={incluirInativos ? 'secondary' : 'outline'}
              size="sm"
              onClick={() => setIncluirInativos((v) => !v)}
            >
              {incluirInativos ? 'Mostrando inativos' : 'Só ativos'}
            </Button>
            <Button
              size="sm"
              onClick={() => {
                setEmEdicao(null)
                setDialogoAberto(true)
              }}
            >
              <Plus />
              Novo serviço
            </Button>
          </>
        }
      />

      <div className="flex-1 overflow-y-auto p-6">
        <div className="mb-4 rounded-md border border-linha bg-superficie px-4 py-3 text-sm text-tinta-media">
          Os serviços e prazos abaixo vieram do padrão de mercado e{' '}
          <strong className="font-medium text-tinta">aguardam validação do cartório parceiro</strong>. Tudo
          aqui é cadastro — trocar nome, prazo ou categoria não exige alteração de código.
        </div>

        {isLoading ? (
          <div className="flex flex-col gap-2">
            {[0, 1, 2, 3, 4].map((i) => (
              <Skeleton key={i} className="h-14 w-full" />
            ))}
          </div>
        ) : data?.length ? (
          <div className="flex flex-col gap-6">
            {Object.entries(porCategoria).map(([categoria, servicos]) => (
              <section key={categoria}>
                <h2 className="mb-2 text-sm font-semibold text-tinta-media">{categoria}</h2>

                <div className="overflow-hidden rounded-md border border-linha">
                  <table className="w-full text-sm">
                    <thead className="bg-superficie text-xs text-tinta-media">
                      <tr>
                        <th className="px-4 py-2.5 text-left font-medium">Serviço</th>
                        <th className="px-4 py-2.5 text-left font-medium">Código</th>
                        <th className="px-4 py-2.5 text-left font-medium">Prazo</th>
                        <th className="px-4 py-2.5 text-left font-medium">Alerta</th>
                        <th className="px-4 py-2.5 text-left font-medium">Em aberto</th>
                        <th className="w-10" />
                      </tr>
                    </thead>

                    <tbody>
                      {servicos.map((servico) => (
                        <tr key={servico.id} className="border-t border-linha hover:bg-superficie">
                          <td className="px-4 py-2.5">
                            <div className="flex items-center gap-2">
                              <span className="font-medium">{servico.nome}</span>
                              {!servico.ativo ? (
                                <Badge variant="secondary">Inativo</Badge>
                              ) : null}
                            </div>
                          </td>

                          <td className="px-4 py-2.5 font-mono text-xs text-tinta-media">
                            {servico.codigo ?? '—'}
                          </td>

                          <td className="px-4 py-2.5 text-tinta-media">
                            {servico.prazo?.prazoDescrito ?? 'sem prazo'}
                          </td>

                          <td className="px-4 py-2.5 text-tinta-media">
                            {servico.prazo ? `${servico.prazo.percentualAlerta}%` : '—'}
                          </td>

                          <td className="px-4 py-2.5 tabular-nums text-tinta-media">
                            {servico.conversasAtivas}
                          </td>

                          <td className="px-2 py-2.5">
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              onClick={() => {
                                setEmEdicao(servico)
                                setDialogoAberto(true)
                              }}
                            >
                              <Pencil />
                              <span className="sr-only">Editar</span>
                            </Button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>
            ))}
          </div>
        ) : (
          <EstadoVazio
            icone={Scale}
            titulo="Nenhum serviço cadastrado"
            descricao="Cadastre os atos que a serventia presta."
          />
        )}
      </div>

      <DialogoDeServico
        servico={emEdicao}
        aberto={dialogoAberto}
        aoFechar={() => setDialogoAberto(false)}
        aoSalvar={() => {
          void clienteDeConsultas.invalidateQueries({ queryKey: ['servicos-gestao'] })
          void clienteDeConsultas.invalidateQueries({ queryKey: ['servicos'] })
        }}
      />
    </>
  )
}
