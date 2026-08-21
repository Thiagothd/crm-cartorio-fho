import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Eye, EyeOff, Pencil, Plus, Scale } from 'lucide-react'
import { EstadoVazio } from '@/components/layout/shell'
import { AcoesDaPagina } from '@/components/layout/contexto-de-acoes-da-pagina'
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
      {/* Duas ações no cabeçalho global custam caro em 360px: somadas à marca,
          ao título do módulo, à busca e ao sino, elas empurravam o cabeçalho
          para duas ou três linhas justamente na tela que tem menos altura.
          No celular o filtro fica só como ícone (com `aria-label`, e o olho
          aberto/fechado é a metáfora certa para "mostrando/escondendo"), e
          "Novo serviço" perde a palavra "serviço" — o contexto já está no
          título do módulo ao lado. */}
      <AcoesDaPagina>
        <Button
          variant={incluirInativos ? 'secondary' : 'outline'}
          size="sm"
          onClick={() => setIncluirInativos((v) => !v)}
          aria-pressed={incluirInativos}
          aria-label={incluirInativos ? 'Mostrando inativos' : 'Mostrando só ativos'}
          title={incluirInativos ? 'Mostrando inativos' : 'Mostrando só ativos'}
        >
          {incluirInativos ? <Eye /> : <EyeOff />}
          <span className="hidden sm:inline">
            {incluirInativos ? 'Mostrando inativos' : 'Só ativos'}
          </span>
        </Button>
        <Button
          size="sm"
          onClick={() => {
            setEmEdicao(null)
            setDialogoAberto(true)
          }}
        >
          <Plus />
          Novo<span className="hidden sm:inline">&nbsp;serviço</span>
        </Button>
      </AcoesDaPagina>

      <div className="flex-1 overflow-y-auto p-3 md:p-6">
        <div className="mb-4 rounded-md border border-linha bg-superficie px-3 py-3 text-sm text-tinta-media md:px-4">
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
                  <table className="w-full table-fixed text-sm md:table-auto">
                    <thead className="bg-superficie text-xs text-tinta-media">
                      <tr>
                        <th className="px-3 py-2.5 text-left font-medium md:px-4">Serviço</th>
                        <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Código</th>
                        <th className="w-24 px-3 py-2.5 text-left font-medium md:w-auto md:px-4">Prazo</th>
                        <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Alerta</th>
                        <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Em aberto</th>
                        <th className="w-14 md:w-12" />
                      </tr>
                    </thead>

                    <tbody>
                      {servicos.map((servico) => (
                        <tr key={servico.id} className="border-t border-linha hover:bg-superficie">
                          <td className="px-3 py-3 md:px-4 md:py-2.5">
                            <div className="flex flex-wrap items-center gap-2">
                              <span className="font-medium">{servico.nome}</span>
                              {!servico.ativo ? (
                                <Badge variant="secondary">Inativo</Badge>
                              ) : null}
                            </div>
                            <p className="mt-1 text-[11px] text-tinta-fraca md:hidden">
                              {servico.conversasAtivas}{' '}
                              {servico.conversasAtivas === 1 ? 'atendimento em aberto' : 'atendimentos em aberto'}
                            </p>
                          </td>

                          <td className="hidden px-4 py-2.5 font-mono text-xs text-tinta-media md:table-cell">
                            {servico.codigo ?? '—'}
                          </td>

                          <td className="px-3 py-3 text-xs text-tinta-media md:px-4 md:py-2.5 md:text-sm">
                            {servico.prazo?.prazoDescrito ?? 'sem prazo'}
                          </td>

                          <td className="hidden px-4 py-2.5 text-tinta-media md:table-cell">
                            {servico.prazo ? `${servico.prazo.percentualAlerta}%` : '—'}
                          </td>

                          <td className="hidden px-4 py-2.5 tabular-nums text-tinta-media md:table-cell">
                            {servico.conversasAtivas}
                          </td>

                          <td className="px-1 py-2.5 md:px-2">
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
