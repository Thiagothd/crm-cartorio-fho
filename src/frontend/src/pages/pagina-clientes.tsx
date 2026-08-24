import { useDeferredValue, useEffect, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Building2,
  FileWarning,
  MapPinOff,
  Pencil,
  Plus,
  Search,
  SlidersHorizontal,
  Upload,
  Users,
  X,
} from 'lucide-react'
import { EstadoVazio } from '@/components/layout/shell'
import { AcoesDaPagina } from '@/components/layout/contexto-de-acoes-da-pagina'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Avatar } from '@/components/ui/avatar'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { clientesApi } from '@/lib/api/endpoints'
import {
  formatarDataHora,
  formatarDocumento,
  formatarTelefone,
  nomeDeExibicao,
} from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import { DialogoDeCliente } from '@/features/clientes/dialogo-de-cliente'
import { DialogoDeImportacao } from '@/features/clientes/dialogo-de-importacao'
import type {
  Cliente,
  EstadoCivil,
  FiltroDeClientes,
  OpcoesDeClientes,
  TipoDePessoa,
} from '@/lib/api/tipos'

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
  const location = useLocation()
  const [filtros, setFiltros] = useState<FiltroDeClientes>({ ordenar: 'nome', tamanho: 100 })
  const [emEdicao, setEmEdicao] = useState<Cliente | null>(null)
  const [dialogoAberto, setDialogoAberto] = useState(false)
  const [importacaoAberta, setImportacaoAberta] = useState(false)
  const [filtrosAbertos, setFiltrosAbertos] = useState(false)

  // A busca global (cabeçalho) manda o cliente encontrado para aqui via
  // `?busca=`. Sem parâmetro, nada muda do comportamento de sempre.
  useEffect(() => {
    const buscaDaUrl = new URLSearchParams(location.search).get('busca')
    if (buscaDaUrl) {
      setFiltros((atuais) => ({ ...atuais, busca: buscaDaUrl, pagina: 1 }))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.search])

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

  // Os quatro campos abaixo (tipo, estado civil, cidade, ordenação) são os que
  // recolhem para o diálogo no celular — usado para destacar o botão "Filtros"
  // quando algum deles não está no padrão.
  const temFiltroAvancado =
    Boolean(filtros.tipo) ||
    Boolean(filtros.estadoCivil) ||
    Boolean(filtros.cidade) ||
    Boolean(filtros.ordenar && filtros.ordenar !== 'nome')

  return (
    <>
      {/* "cliente" sai no celular: o título do módulo ao lado já diz onde se
          está, e a palavra custava ~55px de um cabeçalho que também carrega
          marca, título, busca e sino em 360px. */}
      <AcoesDaPagina>
        <Button size="sm" variant="outline" onClick={() => setImportacaoAberta(true)}>
          <Upload />
          <span className="hidden sm:inline">Importar</span>
        </Button>
        <Button size="sm" onClick={abrirNovo}>
          <Plus />
          Novo<span className="hidden sm:inline">&nbsp;cliente</span>
        </Button>
      </AcoesDaPagina>

      {/* Filtro e lista dividem UM scroll só no celular: a barra de filtros sai
          da tela ao rolar, devolvendo altura para a lista — que é justamente o
          que falta quando cinco controles empilham em 320-390px de largura. No
          desktop ela cabe numa linha só, então `md:sticky` a prende no topo
          como sempre esteve, sem mudar nada pra quem já usa a tela grande. */}
      <div className="flex-1 overflow-y-auto">
        <div className="flex flex-col gap-3 border-b border-linha bg-superficie px-3 py-3 md:sticky md:top-0 md:z-10 md:px-6">
          <div className="flex items-center gap-2">
            <div className="relative min-w-0 flex-1 md:max-w-md">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-tinta-media" />
              <Input
                value={filtros.busca ?? ''}
                onChange={(e) => atualizar({ busca: e.target.value })}
                placeholder="Nome, razão social, telefone ou documento"
                className="pl-8"
                aria-label="Buscar clientes"
              />
            </div>

            {/* Os quatro selects abaixo viram este botão no celular: cinco
                campos de largura total antes do primeiro cliente aparecer era
                a queixa. No desktop eles continuam inline, como sempre. */}
            <Button
              variant={temFiltroAvancado ? 'secondary' : 'outline'}
              size="icon"
              className="shrink-0 md:hidden"
              onClick={() => setFiltrosAbertos(true)}
              aria-label="Abrir filtros avançados"
              title="Filtros"
            >
              <SlidersHorizontal />
              <span className="sr-only">Filtros</span>
            </Button>

            <div className="hidden md:flex md:flex-wrap md:items-center md:gap-2">
              <CamposDeFiltro filtros={filtros} opcoes={opcoes} aoMudar={atualizar} />
            </div>
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
              <span className="basis-full text-xs text-tinta-media md:ml-auto md:basis-auto">
                {data.total} {data.total === 1 ? 'cliente' : 'clientes'}
              </span>
            ) : null}
          </div>
        </div>

        <Dialog open={filtrosAbertos} onOpenChange={setFiltrosAbertos}>
          <DialogContent className="md:max-w-md">
            <DialogHeader>
              <DialogTitle>Filtrar clientes</DialogTitle>
              <DialogDescription>
                Refina a lista sem perder a busca e os filtros rápidos que já estão selecionados.
              </DialogDescription>
            </DialogHeader>
            <div className="flex flex-col gap-3">
              <CamposDeFiltro filtros={filtros} opcoes={opcoes} aoMudar={atualizar} />
            </div>
            <Button variant="outline" onClick={() => setFiltrosAbertos(false)}>
              Concluir
            </Button>
          </DialogContent>
        </Dialog>

        <div className="p-3 md:p-6">
          {isLoading ? (
            <div className="flex flex-col gap-2">
              {[0, 1, 2, 3, 4, 5].map((i) => (
                <Skeleton key={i} className="h-14 w-full" />
              ))}
            </div>
          ) : data?.itens.length ? (
            <div className="overflow-hidden rounded-md border border-linha">
              <table className="w-full table-fixed text-sm md:table-auto">
                <thead className="bg-superficie text-xs text-tinta-media">
                  <tr>
                    <th className="px-4 py-2.5 text-left font-medium">Cliente</th>
                    <th className="w-32 px-3 py-2.5 text-left font-medium md:w-auto md:px-4">Contato</th>
                    <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Documento</th>
                    <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Qualificação</th>
                    <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Atendimentos</th>
                    <th className="w-14 md:w-12"><span className="sr-only">Ações</span></th>
                  </tr>
                </thead>

                <tbody>
                  {data.itens.map((cliente) => (
                    <tr key={cliente.id} className="border-t border-linha hover:bg-superficie">
                      <td className="px-3 py-3 md:px-4 md:py-2.5">
                        <div className="flex items-center gap-2.5">
                          <Avatar
                            nome={nomeDeExibicao(cliente.nome, cliente.telefoneWhatsapp, cliente.nomePendente)}
                            fotoUrl={cliente.fotoPerfilUrl}
                            tamanho="sm"
                          />
                          <div className="min-w-0">
                            <span className="flex min-w-0 items-baseline gap-1.5">
                              <span className="truncate font-medium text-tinta">
                                {nomeDeExibicao(cliente.nome, cliente.telefoneWhatsapp, cliente.nomePendente)}
                              </span>

                              {/* O WhatsApp não trouxe nome utilizável, então o
                                  telefone está no lugar do nome. O selo evita que
                                  isso pareça cadastro quebrado. */}
                              {cliente.nomePendente ? (
                                <span
                                  className="shrink-0 rounded border border-linha px-1 text-[10px] font-normal text-tinta-fraca"
                                  title={
                                    cliente.nomePerguntadoEm
                                      ? 'O sistema já pediu o nome pelo WhatsApp e o cliente não respondeu.'
                                      : 'O sistema vai pedir o nome no próximo contato pelo WhatsApp.'
                                  }
                                >
                                  sem nome
                                </span>
                              ) : null}
                            </span>
                            <span className="text-xs text-tinta-fraca">
                              {tipoDescrito[cliente.tipo]}
                              {cliente.cidade ? ` · ${cliente.cidade}${cliente.uf ? `/${cliente.uf}` : ''}` : ''}
                            </span>
                            {!cliente.ativo ? <Badge variant="secondary" className="ml-2">Inativo</Badge> : null}
                            <span className="mt-1 block text-[11px] text-tinta-fraca md:hidden">
                              {cliente.percentualDeQualificacao}% qualificado · {cliente.totalConversas}{' '}
                              {cliente.totalConversas === 1 ? 'atendimento' : 'atendimentos'}
                              {cliente.temConversaAtiva ? ' · em aberto' : ''}
                            </span>
                          </div>
                        </div>
                      </td>

                      <td className="px-3 py-3 md:px-4 md:py-2.5">
                        <span className="block font-dado text-xs text-tinta-media">
                          {formatarTelefone(cliente.telefoneWhatsapp)}
                        </span>
                        <span className="hidden truncate text-xs text-tinta-fraca md:block">
                          {cliente.email ?? `Desde ${formatarDataHora(cliente.primeiroContatoEm)}`}
                        </span>
                      </td>

                      <td className="hidden px-4 py-2.5 font-dado text-xs text-tinta-media md:table-cell">
                        {formatarDocumento(cliente.documento) ?? '—'}
                      </td>

                      <td className="hidden px-4 py-2.5 md:table-cell">
                        <div className="flex min-w-32 items-center gap-2">
                          <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-linha" role="progressbar" aria-label={`Qualificação de ${cliente.nome}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={cliente.percentualDeQualificacao}>
                            <div className="h-full bg-verde" style={{ width: `${cliente.percentualDeQualificacao}%` }} />
                          </div>
                          <span className="w-9 text-right font-dado text-xs text-tinta-media">
                            {cliente.percentualDeQualificacao}%
                          </span>
                        </div>
                      </td>

                      <td className="hidden px-4 py-2.5 md:table-cell">
                        <div className="flex items-center gap-2">
                          <span className="font-dado text-xs">{cliente.totalConversas}</span>
                          {cliente.temConversaAtiva ? <Badge variant="success">em aberto</Badge> : null}
                        </div>
                      </td>

                      <td className="px-1 py-2.5 md:px-2">
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

      <DialogoDeImportacao
        aberto={importacaoAberta}
        aoFechar={() => setImportacaoAberta(false)}
        aoImportar={() => {
          void clienteDeConsultas.invalidateQueries({ queryKey: ['clientes'] })
          void clienteDeConsultas.invalidateQueries({ queryKey: ['clientes-opcoes'] })
        }}
      />
    </>
  )
}

/**
 * Os quatro selects de filtro — tipo de pessoa, estado civil, cidade e
 * ordenação. Um componente só, usado tanto na linha inline do desktop quanto
 * dentro do diálogo do celular, para as duas cópias nunca divergirem.
 */
function CamposDeFiltro({
  filtros,
  opcoes,
  aoMudar,
}: {
  filtros: FiltroDeClientes
  opcoes: OpcoesDeClientes | undefined
  aoMudar: (parcial: Partial<FiltroDeClientes>) => void
}) {
  return (
    <>
      <Select
        value={filtros.tipo ?? TODOS}
        onValueChange={(valor) => aoMudar({ tipo: valor === TODOS ? undefined : (valor as TipoDePessoa) })}
      >
        <SelectTrigger className="w-full md:w-44">
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
        onValueChange={(valor) => aoMudar({ estadoCivil: valor === TODOS ? undefined : (valor as EstadoCivil) })}
      >
        <SelectTrigger className="w-full md:w-44">
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
        onValueChange={(valor) => aoMudar({ cidade: valor === TODOS ? undefined : valor })}
      >
        <SelectTrigger className="w-full md:w-44">
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
        onValueChange={(valor) => aoMudar({ ordenar: valor as FiltroDeClientes['ordenar'] })}
      >
        <SelectTrigger className="w-full md:w-44">
          <SelectValue placeholder="Ordenação" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="nome">Nome</SelectItem>
          <SelectItem value="recentes">Contato recente</SelectItem>
          <SelectItem value="qualificacao">Menos qualificados</SelectItem>
        </SelectContent>
      </Select>
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
        'inline-flex h-10 items-center gap-1.5 rounded-md border px-2.5 text-xs font-medium transition-colors md:h-8',
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
