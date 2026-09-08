import { useDeferredValue, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { Clock3, FileText, Info, MessageSquare, Plus, Scale, Search, SlidersHorizontal, X } from 'lucide-react'
import { AcoesDaPagina } from '@/components/layout/contexto-de-acoes-da-pagina'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { servicosApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { cn } from '@/lib/utils'
import { DialogoDeServico } from '@/features/servicos/dialogo-de-servico'
import { FichaDeServico } from '@/features/servicos/ficha-de-servico'
import type { TipoServico } from '@/lib/api/tipos'

const TODOS = '__todos__'
const SEM_SETOR = '__sem_setor__'
function normalizar(texto: string) {
  return texto.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('pt-BR')
}

export function PaginaServicos() {
  const { pode } = usarAutenticacao()
  const podeGerirCadastros = pode('servicos.editar')
  const [emEdicao, setEmEdicao] = useState<TipoServico | null>(null)
  const [dialogoAberto, setDialogoAberto] = useState(false)
  const [incluirInativos, setIncluirInativos] = useState(true)
  const [busca, setBusca] = useState('')
  const [categoria, setCategoria] = useState(TODOS)
  const [setor, setSetor] = useState(TODOS)
  const [semPrazo, setSemPrazo] = useState(false)
  const [semDocumentos, setSemDocumentos] = useState(false)
  const [comAtendimento, setComAtendimento] = useState(false)
  const [filtrosAbertos, setFiltrosAbertos] = useState(false)
  const buscaAdiada = useDeferredValue(busca)
  const clienteDeConsultas = useQueryClient()

  // O catálogo já é retornado integralmente; filtros locais não geram uma
  // requisição por tecla nem alteram os contratos usados pelo atendimento.
  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['servicos-gestao', true],
    queryFn: () => servicosApi.listar(true),
  })
  const catalogo = data ?? []
  const categorias = [...new Map(catalogo.map((s) => [s.categoria, s.categoriaDescrita])).entries()]
  const setores = [...new Map(catalogo.filter((s) => s.setorId).map((s) => [s.setorId!, s.setorNome || 'Setor indisponível'])).entries()]
  const termo = normalizar(buscaAdiada.trim())
  const filtrados = catalogo.filter((s) =>
    (incluirInativos || s.ativo) &&
    (categoria === TODOS || s.categoria === categoria) &&
    (setor === TODOS || (setor === SEM_SETOR ? !s.setorId : s.setorId === setor)) &&
    (!semPrazo || !s.prazo) &&
    (!semDocumentos || !s.documentosNecessarios?.trim()) &&
    (!comAtendimento || s.conversasAtivas > 0) &&
    (!termo || normalizar(`${s.nome} ${s.codigo ?? ''}`).includes(termo)),
  )
  const porCategoria = filtrados.reduce<Record<string, TipoServico[]>>((mapa, servico) => {
    ;(mapa[servico.categoriaDescrita] ??= []).push(servico)
    return mapa
  }, {})
  const temFiltro = Boolean(busca.trim()) || categoria !== TODOS || setor !== TODOS || semPrazo || semDocumentos || comAtendimento || !incluirInativos

  function limparFiltros() {
    setBusca(''); setCategoria(TODOS); setSetor(TODOS)
    setSemPrazo(false); setSemDocumentos(false); setComAtendimento(false); setIncluirInativos(true)
  }
  function abrir(servico: TipoServico | null) {
    setEmEdicao(servico)
    setDialogoAberto(true)
  }

  return (
    <>
      <AcoesDaPagina>
        {podeGerirCadastros ? <Button size="sm" onClick={() => abrir(null)} aria-label="Novo serviço"><Plus aria-hidden="true" />Novo<span className="hidden lg:inline"> serviço</span></Button> : null}
      </AcoesDaPagina>

      <div className="min-h-0 flex-1 overflow-y-auto p-3 md:p-6">
        {/* `@container`: as grades abaixo (resumo e cartões de serviço)
            precisam reagir à largura que sobra depois do rail empurrar o
            conteúdo, não só ao tamanho da janela. */}
        <div className="@container mx-auto flex max-w-[1600px] flex-col gap-4">
          <section aria-label="Filtros de serviços" className="rounded-card border border-linha bg-branco p-4 shadow-card">
            <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_minmax(0,0.6fr)_minmax(0,0.6fr)]">
              <div>
                <label htmlFor="busca-servicos" className="mb-1.5 block text-xs font-medium text-tinta-media">Buscar no catálogo</label>
                <div className="flex gap-2">
                <div className="relative min-w-0 flex-1">
                  <Search aria-hidden="true" className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-tinta-media" />
                  <Input id="busca-servicos" aria-label="Buscar serviços" value={busca} onChange={(e) => setBusca(e.target.value)} placeholder="Nome ou código do serviço" className="rounded-full bg-superficie pl-9" />
                </div>
                <Button variant={categoria !== TODOS || setor !== TODOS ? 'secondary' : 'outline'} size="icon" className="shrink-0 rounded-full md:hidden" aria-label="Categoria e setor" aria-expanded={filtrosAbertos} aria-controls="filtros-servicos" onClick={() => setFiltrosAbertos(!filtrosAbertos)}><SlidersHorizontal aria-hidden="true" /></Button>
                </div>
              </div>
              <div id="filtros-servicos" className={cn('gap-3 md:col-span-2 md:grid md:grid-cols-2', filtrosAbertos ? 'grid' : 'hidden')}>
              <div>
                <label htmlFor="filtro-categoria" className="mb-1.5 block text-xs font-medium text-tinta-media">Categoria</label>
                <Select value={categoria} onValueChange={setCategoria}>
                  <SelectTrigger id="filtro-categoria" className="w-full bg-superficie"><SelectValue /></SelectTrigger>
                  <SelectContent><SelectItem value={TODOS}>Todas as categorias</SelectItem>{categorias.map(([id, nome]) => <SelectItem key={id} value={id}>{nome}</SelectItem>)}</SelectContent>
                </Select>
              </div>
              <div>
                <label htmlFor="filtro-setor" className="mb-1.5 block text-xs font-medium text-tinta-media">Setor</label>
                <Select value={setor} onValueChange={setSetor}>
                  <SelectTrigger id="filtro-setor" className="w-full bg-superficie"><SelectValue /></SelectTrigger>
                  <SelectContent><SelectItem value={TODOS}>Todos os setores</SelectItem><SelectItem value={SEM_SETOR}>Sem setor</SelectItem>{setores.map(([id, nome]) => <SelectItem key={id} value={id}>{nome}</SelectItem>)}</SelectContent>
                </Select>
              </div>
              </div>
            </div>
            <div className="mt-3 flex flex-wrap items-center gap-2">
              <FiltroDeServico ativo={semPrazo} aoClicar={() => setSemPrazo(!semPrazo)}><Clock3 aria-hidden="true" />Sem prazo</FiltroDeServico>
              <FiltroDeServico ativo={semDocumentos} aoClicar={() => setSemDocumentos(!semDocumentos)}><FileText aria-hidden="true" />Sem documentos</FiltroDeServico>
              <FiltroDeServico ativo={comAtendimento} aoClicar={() => setComAtendimento(!comAtendimento)}><MessageSquare aria-hidden="true" />Com atendimento aberto</FiltroDeServico>
              <Button size="sm" variant={incluirInativos ? 'secondary' : 'outline'} className="min-h-11 rounded-full md:min-h-9" aria-label={incluirInativos ? 'Mostrando inativos' : 'Mostrando só ativos'} aria-pressed={incluirInativos} onClick={() => setIncluirInativos(!incluirInativos)}>{incluirInativos ? 'Incluindo inativos' : 'Só ativos'}</Button>
              {temFiltro ? <Button variant="ghost" size="sm" onClick={limparFiltros}><X aria-hidden="true" />Limpar filtros</Button> : null}
            </div>
          </section>

          {data && !isError ? (
            // Container query (`@3xl`), não breakpoint de tela: quem aperta
            // esta grade é o rail empurrando o conteúdo, não o tamanho do
            // aparelho. O `hidden @3xl:contents` da 3ª/4ª pílula usa o MESMO
            // limiar de `@3xl:grid-cols-4` — senão a grade abre 4 colunas
            // com só 2 cartões dentro.
            <section aria-label="Resumo dos serviços exibidos" className="grid grid-cols-2 gap-3 @3xl:grid-cols-4">
              <ResumoDeServicos rotulo="Serviços exibidos" valor={filtrados.length} detalhe={`de ${catalogo.length} no catálogo`} />
              <ResumoDeServicos rotulo="Atendimentos abertos" valor={filtrados.reduce((soma, s) => soma + s.conversasAtivas, 0)} detalhe="nos serviços exibidos" />
              <div className="hidden @3xl:contents">
              <ResumoDeServicos rotulo="Sem prazo" valor={filtrados.filter((s) => !s.prazo).length} detalhe="nos serviços exibidos" />
              <ResumoDeServicos rotulo="Sem documentos" valor={filtrados.filter((s) => !s.documentosNecessarios?.trim()).length} detalhe="nos serviços exibidos" />
              </div>
            </section>
          ) : null}

          <div className="flex items-start gap-2 rounded-lg px-1 text-xs leading-relaxed text-tinta-media">
            <Info aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
            <p>Os serviços e prazos iniciais são sugestões e aguardam validação do cartório parceiro. Revise os cadastros conforme a rotina da serventia.</p>
          </div>

          {isLoading ? (
            <div role="status" aria-label="Carregando serviços" className="grid gap-4 @lg:grid-cols-2 @4xl:grid-cols-3">{[0, 1, 2, 3, 4, 5].map((i) => <Skeleton key={i} className="h-80 rounded-card" />)}</div>
          ) : isError ? (
            <EstadoDaInterface tipo="erro" titulo="Não foi possível carregar os serviços" descricao={error instanceof ErroDaApi ? error.message : 'Verifique sua conexão e tente novamente.'} acao={<Button variant="outline" onClick={() => void refetch()}>Tentar novamente</Button>} />
          ) : filtrados.length ? (
            <div className="flex flex-col gap-6">
              {Object.entries(porCategoria).map(([nome, servicos]) => (
                <section key={nome} aria-label={nome}>
                  <div className="mb-3 flex items-center gap-3 px-1"><h2 className="text-sm font-semibold tracking-tight text-tinta">{nome}</h2><span className="font-dado text-xs text-tinta-media">{servicos.length}</span><span aria-hidden="true" className="h-px flex-1 bg-linha" /></div>
                  <div className="grid gap-4 @lg:grid-cols-2 @4xl:grid-cols-3">{servicos.map((servico) => <FichaDeServico key={servico.id} servico={servico} podeEditar={podeGerirCadastros} aoEditar={abrir} />)}</div>
                </section>
              ))}
            </div>
          ) : (
            <EstadoDaInterface tipo={catalogo.length || temFiltro ? 'sem-resultados' : 'vazio'} icone={Scale} titulo={catalogo.length || temFiltro ? 'Nenhum serviço encontrado' : 'Nenhum serviço cadastrado'} descricao={catalogo.length || temFiltro ? 'Revise os filtros ou busque por outro nome ou código.' : 'Cadastre os atos que a serventia presta.'} acao={temFiltro ? <Button variant="outline" onClick={limparFiltros}>Limpar filtros</Button> : podeGerirCadastros ? <Button variant="outline" onClick={() => abrir(null)}>Cadastrar serviço</Button> : undefined} />
          )}
        </div>
      </div>

      {podeGerirCadastros ? <DialogoDeServico servico={emEdicao} aberto={dialogoAberto} aoFechar={() => setDialogoAberto(false)} aoSalvar={() => {
        void clienteDeConsultas.invalidateQueries({ queryKey: ['servicos-gestao'] })
        void clienteDeConsultas.invalidateQueries({ queryKey: ['servicos'] })
      }} /> : null}
    </>
  )
}

function ResumoDeServicos({ rotulo, valor, detalhe }: { rotulo: string; valor: number; detalhe: string }) {
  return <div className="rounded-card border border-linha bg-branco p-4 shadow-card"><p className="text-xs font-medium text-tinta-media">{rotulo}</p><p className="mt-1 font-dado text-2xl tracking-tight text-tinta">{valor}</p><p className="mt-1 text-xs text-tinta-media">{detalhe}</p></div>
}

function FiltroDeServico({ ativo, aoClicar, children }: { ativo: boolean; aoClicar: () => void; children: ReactNode }) {
  return <button type="button" aria-pressed={ativo} onClick={aoClicar} className={cn('inline-flex min-h-11 items-center gap-1.5 rounded-full border px-3 text-xs font-medium transition-colors duration-[var(--duracao-rapida)] md:min-h-9 [&_svg]:size-3.5', ativo ? 'border-tinta bg-tinta text-branco' : 'border-linha bg-superficie text-tinta-media hover:border-linha-forte hover:text-tinta')}>{children}</button>
}
