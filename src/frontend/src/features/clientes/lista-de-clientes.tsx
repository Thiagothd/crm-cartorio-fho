import { Archive, FileText, Mail, MapPin, MessageSquare, Pencil, Phone, Trash2 } from 'lucide-react'
import { Avatar } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { formatarDocumento, formatarTelefone, nomeDeExibicao } from '@/lib/formatacao'
import type { Cliente } from '@/lib/api/tipos'

/**
 * Fichas de cadastro, em vez de uma tabela horizontal: cada cliente reúne os
 * dados que o atendente consulta sem obrigar a percorrer colunas distantes.
 */
export function ListaDeClientes({ clientes, podeExcluir, aoEditar, aoRemover }: {
  clientes: Cliente[]
  podeExcluir: boolean
  aoEditar: (cliente: Cliente) => void
  aoRemover: (cliente: Cliente) => void
}) {
  return (
    <div role="list" aria-label="Clientes cadastrados" className="grid gap-3 p-3 sm:grid-cols-2 md:gap-4 md:p-4 xl:grid-cols-3">
      {clientes.map((cliente) => {
        const nome = nomeDeExibicao(cliente.nome, cliente.telefoneWhatsapp, cliente.nomePendente)
        const temHistorico = cliente.totalConversas > 0 || cliente.temConversaAtiva
        const localidade = cliente.cidade ? `${cliente.cidade}${cliente.uf ? `/${cliente.uf}` : ''}` : 'Endereço não informado'

        return (
          <article
            role="listitem"
            key={cliente.id}
            className="group flex min-w-0 flex-col rounded-card border border-linha bg-branco p-4 shadow-card transition-[border-color,box-shadow,transform] duration-[var(--duracao-rapida)] hover:-translate-y-0.5 hover:border-linha-forte hover:shadow-card-hover focus-within:border-linha-forte focus-within:shadow-card-hover motion-reduce:transform-none"
          >
            <header className="flex items-start gap-3">
              <Avatar nome={nome} fotoUrl={cliente.fotoPerfilUrl} tamanho="lg" className="rounded-lg bg-superficie" />
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-x-1.5 gap-y-1">
                  <button type="button" onClick={() => aoEditar(cliente)} className="min-w-0 text-left font-semibold text-tinta hover:underline focus-visible:rounded-sm" title={nome}>
                    <span className="line-clamp-2">{nome}</span>
                  </button>
                  {cliente.nomePendente ? (
                    <span className="rounded-full border border-linha px-1.5 py-0.5 text-[10px] text-tinta-media" title={cliente.nomePerguntadoEm ? 'O sistema já pediu o nome pelo WhatsApp e o cliente não respondeu.' : 'O sistema vai pedir o nome no próximo contato pelo WhatsApp.'}>sem nome</span>
                  ) : null}
                  {!cliente.ativo ? <Badge variant="secondary" className="rounded-full">Inativo</Badge> : null}
                </div>
                <p className="mt-0.5 text-xs text-tinta-media">
                  {cliente.tipo === 'Fisica' ? 'Pessoa física' : 'Pessoa jurídica'}
                  <span aria-hidden="true"> · </span>{localidade}
                </p>
              </div>
              <div className="flex shrink-0 gap-1">
                <Button variant="ghost" size="icon-sm" className="size-10 rounded-lg" onClick={() => aoEditar(cliente)} aria-label={`Editar ${nome}`} title="Editar cadastro"><Pencil /></Button>
              </div>
            </header>

            <div className="mt-4 grid grid-cols-2 gap-x-4 gap-y-3 border-y border-linha py-3">
              <DadoDoCliente icone={Phone} rotulo="WhatsApp" valor={formatarTelefone(cliente.telefoneWhatsapp)} dado />
              <DadoDoCliente icone={FileText} rotulo={cliente.tipo === 'Fisica' ? 'CPF' : 'CNPJ'} valor={formatarDocumento(cliente.documento) ?? 'Não informado'} dado />
              <DadoDoCliente icone={Mail} rotulo="E-mail" valor={cliente.email ?? 'Não informado'} />
              <DadoDoCliente icone={MapPin} rotulo="Cadastro" valor={cliente.logradouro && cliente.numero && cliente.cidade && cliente.uf ? 'Endereço completo' : 'Endereço pendente'} />
            </div>

            <div className="mt-3">
              <div className="flex items-center justify-between gap-3 text-xs">
                <span className="font-medium text-tinta-media">Qualificação</span>
                <span className="font-dado text-tinta">{cliente.percentualDeQualificacao}%</span>
              </div>
              <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-linha/70" role="progressbar" aria-label={`Qualificação de ${nome}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={cliente.percentualDeQualificacao}>
                <div className="h-full rounded-full bg-verde" style={{ width: `${cliente.percentualDeQualificacao}%` }} />
              </div>
            </div>

            <footer className="mt-4 flex flex-wrap items-center justify-between gap-2 text-xs">
              <span className="inline-flex items-center gap-1.5 text-tinta-media"><MessageSquare aria-hidden="true" className="size-3.5" /><span><span className="font-dado text-tinta">{cliente.totalConversas}</span> {cliente.totalConversas === 1 ? 'atendimento' : 'atendimentos'}</span></span>
              {cliente.temConversaAtiva ? <Badge variant="success" className="whitespace-nowrap rounded-full">em aberto</Badge> : <span className="text-tinta-fraca">Sem atendimento aberto</span>}
            </footer>
            {podeExcluir ? (
              <Button
                variant="ghost"
                size="sm"
                className="mt-3 w-full justify-center border border-linha text-tinta-media hover:border-selo/50 hover:bg-selo/10 hover:text-selo"
                onClick={() => aoRemover(cliente)}
                aria-label={`${temHistorico ? 'Opções de remoção de' : 'Excluir'} ${nome}`}
                title={temHistorico ? 'Cadastro com histórico: opções de remoção' : 'Excluir cadastro sem histórico'}
              >
                {temHistorico ? <Archive /> : <Trash2 />}
                {temHistorico ? 'Opções de exclusão' : 'Excluir'}
              </Button>
            ) : null}
          </article>
        )
      })}
    </div>
  )
}

function DadoDoCliente({ icone: Icone, rotulo, valor, dado = false }: {
  icone: typeof Phone
  rotulo: string
  valor: string
  dado?: boolean
}) {
  return (
    <div className="min-w-0">
      <span className="flex items-center gap-1 text-[11px] text-tinta-media"><Icone aria-hidden="true" className="size-3" />{rotulo}</span>
      <p className={`mt-0.5 truncate text-xs text-tinta ${dado ? 'font-dado' : ''}`} title={valor}>{valor}</p>
    </div>
  )
}
