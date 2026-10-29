import { useDeferredValue, useEffect, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Building2,
  FileWarning,
  MapPinOff,
  Plus,
  Search,
  SlidersHorizontal,
  Upload,
  Users,
  X,
} from 'lucide-react'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { ListaDeClientes } from '@/features/clientes/lista-de-clientes'
import { DialogoDeExclusao } from '@/features/clientes/dialogo-de-exclusao'
import { AcoesDaPagina } from '@/components/layout/contexto-de-acoes-da-pagina'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
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
import { ErroDaApi } from '@/lib/api/cliente-http'
import { cn } from '@/lib/utils'
import { DialogoDeCliente } from '@/features/clientes/dialogo-de-cliente'
import { DialogoDeImportacao } from '@/features/clientes/dialogo-de-importacao'
import { DialogoNovaPendencia } from '@/features/devedores/dialogo-nova-pendencia'
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

export function PaginaClientes() {
  const location = useLocation()
  const { pode } = usarAutenticacao()
  const podeExcluirCliente = pode('clientes.excluir')
  const podeEditarDevedores = pode('devedores.editar')
  const [abaDaEdicao, setAbaDaEdicao] = useState<'contato' | 'observacoes'>('contato')
  const [filtros, setFiltros] = useState<FiltroDeClientes>({ ordenar: 'nome', tamanho: 100 })
  const [emEdicao, setEmEdicao] = useState<Cliente | null>(null)
  const [dialogoAberto, setDialogoAberto] = useState(false)
  const [importacaoAberta, setImportacaoAberta] = useState(false)
  const [filtrosAbertos, setFiltrosAbertos] = useState(false)
  const [emExclusao, setEmExclusao] = useState<Cliente | null>(null)
  const [clienteParaNovaPendencia, setClienteParaNovaPendencia] = useState<Cliente | null>(null)

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

  const { data, isLoading, isError, error, refetch } = useQuery({
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
    setAbaDaEdicao('contato')
    setDialogoAberto(true)
  }

  function abrirEdicao(cliente: Cliente) {
    setEmEdicao(cliente)
    setAbaDaEdicao('contato')
    setDialogoAberto(true)
  }

  function atualizarLista() {
    void clienteDeConsultas.invalidateQueries({ queryKey: ['clientes'] })
    void clienteDeConsultas.invalidateQueries({ queryKey: ['clientes-opcoes'] })
  }

  function abrirDesativacao(cliente: Cliente) {
    setEmExclusao(null)
    setEmEdicao(cliente)
    setAbaDaEdicao('observacoes')
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

  const temFiltro = temFiltroRapido || temFiltroAvancado || Boolean(filtros.busca?.trim()) || Boolean(filtros.incluirInativos)

  const clientesDaLista = data?.itens ?? []
  const comAtendimentoAberto = clientesDaLista.filter((cliente) => cliente.temConversaAtiva).length
  const comPendenciaCadastral = clientesDaLista.filter((cliente) =>
    !cliente.documento || !cliente.logradouro || !cliente.numero || !cliente.cidade || !cliente.uf,
  ).length

  function limparFiltros() {
    setFiltros({ ordenar: 'nome', tamanho: 100, pagina: 1 })
  }

  return (
    <>
      {/* "cliente" sai no celular: o título do módulo ao lado já diz onde se
          está, e a palavra custava ~55px de um cabeçalho que também carrega
          marca, título, busca e sino em 360px. */}
      <AcoesDaPagina>
        <Button size="sm" variant="outline" aria-label="Importar contatos" onClick={() => setImportacaoAberta(true)}>
          <Upload />
          <span className="hidden sm:inline">Importar</span>
        </Button>
        <Button size="sm" onClick={abrirNovo}>
          <Plus />
          <span className="sm:hidden">Novo</span>
          <span className="hidden sm:inline">Novo Cliente</span>
        </Button>
      </AcoesDaPagina>

      {/* Filtros ficam na sua própria bancada; a lista usa o fundo de papel
          para as fichas de cadastro terem hierarquia e leitura independente. */}
      <div className="min-h-0 flex-1 overflow-y-auto p-3 md:p-6">
        {/* `@container`: as grades abaixo (resumo e fichas de cliente)
            precisam reagir à largura que sobra depois do rail empurrar o
            conteúdo, não só ao tamanho da janela. */}
        <div className="@container mx-auto flex max-w-[1600px] flex-col gap-4">
          <section className="rounded-card border border-linha bg-branco shadow-card">
          <div className="flex flex-col gap-3 rounded-[inherit] bg-branco p-4">
            <div className="flex flex-wrap items-center gap-2">
              <div className="relative min-w-0 flex-1 lg:min-w-64">
                <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-tinta-media" />
                <Input
                  value={filtros.busca ?? ''}
                  onChange={(e) => atualizar({ busca: e.target.value })}
                  placeholder="Nome, razão social, telefone ou documento"
                  className="rounded-full bg-superficie pl-9"
                  aria-label="Buscar clientes"
                />
              </div>

              {/* Os quatro selects abaixo viram este botão no celular: cinco
                  campos de largura total antes do primeiro cliente aparecer era
                  a queixa. No desktop eles continuam inline, como sempre. */}
              <Button
                variant={temFiltroAvancado ? 'secondary' : 'outline'}
                size="icon"
                className="shrink-0 rounded-full lg:hidden"
                onClick={() => setFiltrosAbertos(true)}
                aria-label="Abrir filtros avançados"
                title="Filtros"
              >
                <SlidersHorizontal />
                <span className="sr-only">Filtros</span>
              </Button>

              <div className="hidden w-full grid-cols-4 gap-2 lg:grid xl:flex xl:w-auto">
                <CamposDeFiltro filtros={filtros} opcoes={opcoes} aoMudar={atualizar} />
              </div>
            </div>

            <div className="grid grid-cols-2 items-center gap-2 md:flex md:flex-wrap">
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
                className="min-h-11 rounded-full lg:min-h-8"
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
                <span className="col-span-2 basis-full text-xs text-tinta-media md:ml-auto md:basis-auto">
                  <span className="font-semibold text-tinta">{data.total}</span> {data.total === 1 ? 'cliente' : 'clientes'}
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

          </section>

          {data?.itens.length && !isError ? (
            <section aria-label="Resumo da lista de clientes" className="grid gap-3 @xl:grid-cols-3">
              <ResumoDaLista rotulo="Clientes encontrados" valor={data.total} descricao="resultado da busca atual" />
              <ResumoDaLista rotulo="Em atendimento" valor={comAtendimentoAberto} descricao="cadastros desta lista" />
              <ResumoDaLista rotulo="Pendências cadastrais" valor={comPendenciaCadastral} descricao="cadastros desta lista" />
            </section>
          ) : null}

          <section className="rounded-card border border-linha bg-superficie/40 shadow-card">
          <div>
            {isLoading ? (
              <div role="status" aria-label="Carregando clientes" className="grid gap-3 p-3 @lg:grid-cols-2 md:gap-4 md:p-4 @4xl:grid-cols-3">
                {[0, 1, 2, 3, 4, 5].map((i) => <Skeleton key={i} className="h-64 w-full rounded-card" />)}
              </div>
            ) : isError ? (
              <EstadoDaInterface tipo="erro" titulo="Não foi possível carregar os clientes"
                descricao={error instanceof ErroDaApi ? error.message : 'Verifique sua conexão e tente novamente.'}
                acao={<Button variant="outline" onClick={() => void refetch()}>Tentar novamente</Button>} />
            ) : data?.itens.length ? (
              <ListaDeClientes
                clientes={data.itens}
                podeExcluir={podeExcluirCliente}
                aoEditar={abrirEdicao}
                aoRemover={setEmExclusao}
                aoMarcarDevedor={
                  podeEditarDevedores ? (cliente) => setClienteParaNovaPendencia(cliente) : undefined
                }
              />
            ) : (
              <EstadoDaInterface tipo={temFiltro ? 'sem-resultados' : 'vazio'} icone={Users}
                titulo={temFiltro ? 'Nenhum cliente encontrado' : 'Sua base de clientes começa aqui'}
                descricao={temFiltro ? 'Revise os filtros ou busque por outro nome, telefone ou documento.' : 'Cadastre um contato ou importe uma lista para começar.'}
                acao={temFiltro ? <Button variant="outline" onClick={limparFiltros}>Limpar filtros</Button> : <Button size="sm" onClick={abrirNovo}><Plus />Novo Cliente</Button>} />
            )}
          </div>
          {data?.itens.length && !isError ? (
            <div className="flex flex-wrap items-center justify-between gap-2 border-t border-linha bg-branco/70 px-4 py-2.5 text-xs text-tinta-media">
              <span>Exibindo {data.itens.length} de {data.total} {data.total === 1 ? 'cliente' : 'clientes'}</span>
              {temFiltro ? <Button variant="ghost" size="sm" className="h-8" onClick={limparFiltros}><X />Limpar filtros</Button> : null}
            </div>
          ) : null}
          </section>
        </div>
      </div>

      <DialogoDeCliente
        cliente={emEdicao}
        abaInicial={abaDaEdicao}
        aberto={dialogoAberto}
        aoFechar={() => setDialogoAberto(false)}
        aoSalvar={atualizarLista}
      />

      <DialogoDeImportacao
        aberto={importacaoAberta}
        aoFechar={() => setImportacaoAberta(false)}
        aoImportar={atualizarLista}
      />

      {emExclusao ? (
        <DialogoDeExclusao key={emExclusao.id} cliente={emExclusao} aoFechar={() => setEmExclusao(null)} aoExcluir={atualizarLista} aoAbrirDesativacao={abrirDesativacao} />
      ) : null}

      <DialogoNovaPendencia
        aberto={Boolean(clienteParaNovaPendencia)}
        aoMudarAberto={(ab) => {
          if (!ab) setClienteParaNovaPendencia(null)
        }}
        clienteFixo={
          clienteParaNovaPendencia
            ? {
                id: clienteParaNovaPendencia.id,
                nome: clienteParaNovaPendencia.nome,
                telefone: clienteParaNovaPendencia.telefoneWhatsapp,
              }
            : null
        }
        aoSalvar={() => {
          void clienteDeConsultas.invalidateQueries({ queryKey: ['devedores'] })
        }}
      />
    </>
  )
}

function ResumoDaLista({ rotulo, valor, descricao }: { rotulo: string; valor: number; descricao: string }) {
  return (
    <div className="rounded-card border border-linha bg-branco px-4 py-3 shadow-card">
      <p className="text-xs font-medium text-tinta-media">{rotulo}</p>
      <div className="mt-1 flex items-baseline gap-2">
        <span className="font-dado text-2xl text-tinta">{valor}</span>
        <span className="text-xs text-tinta-media">{descricao}</span>
      </div>
    </div>
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
        <SelectTrigger aria-label="Tipo de pessoa" className="w-full bg-superficie xl:w-44">
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
        <SelectTrigger aria-label="Estado civil" className="w-full bg-superficie xl:w-52">
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
        <SelectTrigger aria-label="Cidade" className="w-full bg-superficie xl:w-40">
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
        <SelectTrigger aria-label="Ordenação" className="w-full bg-superficie xl:w-40">
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
        'inline-flex min-h-11 items-center gap-1.5 rounded-full border px-3 text-xs font-medium transition-colors duration-[var(--duracao-rapida)] lg:min-h-8',
        ativo
          ? 'border-selecao bg-selecao text-sobre-selecao'
          : 'border-linha bg-superficie text-tinta-media hover:border-linha-forte hover:text-tinta',
      )}
    >
      <Icone aria-hidden="true" className="size-3.5" />
      {rotulo}
    </button>
  )
}
