import { useDeferredValue, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, FileWarning, MapPinOff, Pencil, Plus, Search, Users, X } from 'lucide-react'
import { CabecalhoDaPagina, EstadoVazio } from '@/components/layout/shell'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Avatar } from '@/components/ui/avatar'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { clientesApi } from '@/lib/api/endpoints'
import { formatarDataHora, formatarDocumento, formatarTelefone } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import { DialogoDeCliente } from '@/features/clientes/dialogo-de-cliente'
import type { Cliente, EstadoCivil, FiltroDeClientes, TipoDePessoa } from '@/lib/api/tipos'

const TODOS = '__todos__'

const estadosCivis: { valor: EstadoCivil; rotulo: string }[] = [
  { valor: 'Solteiro', rotulo: 'Solteiro(a)' },
  { valor: 'Casado', rotulo: 'Casado(a)' },
  { valor: 'Divorciado', rotulo: 'Divorciado(a)' },
  { valor: 'Viuvo', rotulo: 'Viúvo(a)' },
  { valor: 'Separado', rotulo: 'Separado(a)' },
  { valor: 'UniaoEstavel', rotulo: 'União estável' },
]

const tipoDescrito: Record<TipoDePessoa, string> = {
  Fisica: 'Pessoa física',
  Juridica: 'Pessoa jurídica',
}

export function PaginaClientes() {
  const [filtros, setFiltros] = useState<FiltroDeClientes>({ ordenar: 'nome', tamanho: 100 })
  const [emEdicao, setEmEdicao] = useState<Cliente | null>(null)
  const [dialogoAberto, setDialogoAberto] = useState(false)

  const buscaAdiada = useDeferredValue(filtros.busca)
  const clienteDeConsultas = useQueryClient()

  const filtroDaConsulta = { ...filtros, busca: buscaAdiada?.trim() || undefined }

  const { data, isLoading } = useQuery({
    queryKey: ['clientes', filtroDaConsulta],
    queryFn: () => clientesApi.listar(filtroDaConsulta),
  })

  const { data: opcoes } = useQuery({
    queryKey: ['clientes-opcoes'],
    queryFn: () => clientesApi.opcoes(),
    staleTime: 5 * 60_000,
  })

  function atualizar(parcial: Partial<FiltroDeClientes>) {
    setFiltros((atuais) => ({ ...atuais, ...parcial, pagina: 1 }))
  }

  function abrirNovo() {
    setEmEdicao(null)
    setDialogoAberto(true)
  }

  function abrirEdicao(cliente: Cliente) {
    setEmEdicao(cliente)
    setDialogoAberto(true)
  }

  const temFiltroRapido =
    Boolean(filtros.semDocumento) ||
    Boolean(filtros.semEndereco) ||
    Boolean(filtros.comConversaAtiva)

  return (
    <>
      <CabecalhoDaPagina
        titulo="Clientes"
        descricao="Contatos e qualificação das partes atendidas pelo cartório"
        acoes={
          <Button size="sm" onClick={abrirNovo}>
            <Plus />
            Novo cliente
          </Button>
        }
      />

      <div className="flex shrink-0 flex-col gap-3 border-b border-linha bg-superficie px-6 py-3">
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative min-w-64 max-w-md flex-1">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-tinta-media" />
            <Input
              value={filtros.busca ?? ''}
              onChange={(e) => atualizar({ busca: e.target.value })}
              placeholder="Nome, razão social, telefone ou documento"
              className="pl-8"
              aria-label="Buscar clientes"
            />
          </div>

          <Select
            value={filtros.tipo ?? TODOS}
            onValueChange={(valor) => atualizar({ tipo: valor === TODOS ? undefined : (valor as TipoDePessoa) })}
          >
            <SelectTrigger className="w-44">
              <SelectValue placeholder="Tipo de pessoa" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Qualquer pessoa</SelectItem>
              <SelectItem value="Fisica">Pessoa física</SelectItem>
              <SelectItem value="Juridica">Pessoa jurídica</SelectItem>
            </SelectContent>
          </Select>

          <Select
            value={filtros.estadoCivil ?? TODOS}
            disabled={filtros.tipo === 'Juridica'}
            onValueChange={(valor) => atualizar({ estadoCivil: valor === TODOS ? undefined : (valor as EstadoCivil) })}
          >
            <SelectTrigger className="w-44">
              <SelectValue placeholder="Estado civil" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Qualquer estado civil</SelectItem>
              {estadosCivis.map((estado) => (
                <SelectItem key={estado.valor} value={estado.valor}>
                  {estado.rotulo}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Select
            value={filtros.cidade ?? TODOS}
            onValueChange={(valor) => atualizar({ cidade: valor === TODOS ? undefined : valor })}
          >
            <SelectTrigger className="w-44">
              <SelectValue placeholder="Cidade" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Qualquer cidade</SelectItem>
              {(opcoes?.cidades ?? []).map((cidade) => (
                <SelectItem key={cidade} value={cidade}>
                  {cidade}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Select
            value={filtros.ordenar ?? 'nome'}
            onValueChange={(valor) => atualizar({ ordenar: valor as FiltroDeClientes['ordenar'] })}
          >
            <SelectTrigger className="w-44">
              <SelectValue placeholder="Ordenação" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="nome">Nome</SelectItem>
              <SelectItem value="recentes">Contato recente</SelectItem>
              <SelectItem value="qualificacao">Menos qualificados</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <FiltroRapido
            icone={FileWarning}
            rotulo="Sem CPF/CNPJ"
            ativo={Boolean(filtros.semDocumento)}
            aoClicar={() => atualizar({ semDocumento: !filtros.semDocumento })}
          />
          <FiltroRapido
            icone={MapPinOff}
            rotulo="Endereço incompleto"
            ativo={Boolean(filtros.semEndereco)}
            aoClicar={() => atualizar({ semEndereco: !filtros.semEndereco })}
          />
          <FiltroRapido
            icone={Building2}
            rotulo="Com atendimento aberto"
            ativo={Boolean(filtros.comConversaAtiva)}
            aoClicar={() => atualizar({ comConversaAtiva: !filtros.comConversaAtiva })}
          />

          <Button
            variant={filtros.incluirInativos ? 'secondary' : 'outline'}
            size="sm"
            onClick={() => atualizar({ incluirInativos: !filtros.incluirInativos })}
            aria-pressed={Boolean(filtros.incluirInativos)}
          >
            {filtros.incluirInativos ? 'Incluindo inativos' : 'Só ativos'}
          </Button>

          {temFiltroRapido ? (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => atualizar({ semDocumento: false, semEndereco: false, comConversaAtiva: false })}
            >
              <X />
              Limpar pendências
            </Button>
          ) : null}

          {data ? (
            <span className="ml-auto text-xs text-tinta-media">
              {data.total} {data.total === 1 ? 'cliente' : 'clientes'}
            </span>
          ) : null}
        </div>
      </div>

      <div className="flex-1 overflow-y-auto p-6">
        {isLoading ? (
          <div className="flex flex-col gap-2">
            {[0, 1, 2, 3, 4, 5].map((i) => (
              <Skeleton key={i} className="h-14 w-full" />
            ))}
          </div>
        ) : data?.itens.length ? (
          <div className="overflow-x-auto rounded-md border border-linha">
            <table className="w-full min-w-[920px] text-sm">
              <thead className="bg-superficie text-xs text-tinta-media">
                <tr>
                  <th className="px-4 py-2.5 text-left font-medium">Cliente</th>
                  <th className="px-4 py-2.5 text-left font-medium">Contato</th>
                  <th className="px-4 py-2.5 text-left font-medium">Documento</th>
                  <th className="px-4 py-2.5 text-left font-medium">Qualificação</th>
                  <th className="px-4 py-2.5 text-left font-medium">Atendimentos</th>
                  <th className="w-10"><span className="sr-only">Ações</span></th>
                </tr>
              </thead>

              <tbody>
                {data.itens.map((cliente) => (
                  <tr key={cliente.id} className="border-t border-linha hover:bg-superficie">
                    <td className="px-4 py-2.5">
                      <div className="flex items-center gap-2.5">
                        <Avatar nome={cliente.nome} tamanho="sm" />
                        <div className="min-w-0">
                          <span className="block truncate font-medium text-tinta">{cliente.nome}</span>
                          <span className="text-xs text-tinta-fraca">
                            {tipoDescrito[cliente.tipo]}
                            {cliente.cidade ? ` · ${cliente.cidade}${cliente.uf ? `/${cliente.uf}` : ''}` : ''}
                          </span>
                          {!cliente.ativo ? <Badge variant="secondary" className="ml-2">Inativo</Badge> : null}
                        </div>
                      </div>
                    </td>

                    <td className="px-4 py-2.5">
                      <span className="block font-dado text-xs text-tinta-media">
                        {formatarTelefone(cliente.telefoneWhatsapp)}
                      </span>
                      <span className="block truncate text-xs text-tinta-fraca">
                        {cliente.email ?? `Desde ${formatarDataHora(cliente.primeiroContatoEm)}`}
                      </span>
                    </td>

                    <td className="px-4 py-2.5 font-dado text-xs text-tinta-media">
                      {formatarDocumento(cliente.documento) ?? '—'}
                    </td>

                    <td className="px-4 py-2.5">
                      <div className="flex min-w-32 items-center gap-2">
                        <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-linha" role="progressbar" aria-label={`Qualificação de ${cliente.nome}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={cliente.percentualDeQualificacao}>
                          <div className="h-full bg-verde" style={{ width: `${cliente.percentualDeQualificacao}%` }} />
                        </div>
                        <span className="w-9 text-right font-dado text-xs text-tinta-media">
                          {cliente.percentualDeQualificacao}%
                        </span>
                      </div>
                    </td>

                    <td className="px-4 py-2.5">
                      <div className="flex items-center gap-2">
                        <span className="font-dado text-xs">{cliente.totalConversas}</span>
                        {cliente.temConversaAtiva ? <Badge variant="success">em aberto</Badge> : null}
                      </div>
                    </td>

                    <td className="px-2 py-2.5">
                      <Button variant="ghost" size="icon-sm" onClick={() => abrirEdicao(cliente)} aria-label={`Editar ${cliente.nome}`}>
                        <Pencil />
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <EstadoVazio
            icone={Users}
            titulo="Nenhum cliente encontrado"
            descricao="Cadastre o primeiro contato ou ajuste os filtros."
            acao={
              <Button size="sm" onClick={abrirNovo}>
                <Plus />
                Novo cliente
              </Button>
            }
          />
        )}
      </div>

      <DialogoDeCliente
        cliente={emEdicao}
        aberto={dialogoAberto}
        aoFechar={() => setDialogoAberto(false)}
        aoSalvar={() => {
          void clienteDeConsultas.invalidateQueries({ queryKey: ['clientes'] })
          void clienteDeConsultas.invalidateQueries({ queryKey: ['clientes-opcoes'] })
        }}
      />
    </>
  )
}

function FiltroRapido({
  icone: Icone,
  rotulo,
  ativo,
  aoClicar,
}: {
  icone: typeof FileWarning
  rotulo: string
  ativo: boolean
  aoClicar: () => void
}) {
  return (
    <button
      type="button"
      onClick={aoClicar}
      aria-pressed={ativo}
      className={cn(
        'inline-flex h-8 items-center gap-1.5 rounded-md border px-2.5 text-xs font-medium transition-colors',
        ativo
          ? 'border-linha-forte bg-branco text-tinta'
          : 'border-linha bg-superficie text-tinta-media hover:border-linha-forte hover:text-tinta',
      )}
    >
      <Icone className="size-3.5" />
      {rotulo}
    </button>
  )
}
