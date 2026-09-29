import { Building2, Clock3, FileText, MessageSquare, Pencil, Scale } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import type { TipoServico } from '@/lib/api/tipos'

export function FichaDeServico({ servico, podeEditar, aoEditar }: {
  servico: TipoServico
  podeEditar: boolean
  aoEditar: (servico: TipoServico) => void
}) {
  const prazo = servico.prazo
  return (
    <article className="flex min-w-0 flex-col rounded-card border border-linha bg-branco p-4 shadow-card md:p-5" aria-label={servico.nome}>
      <header className="flex items-start gap-3">
        <span className="flex size-11 shrink-0 items-center justify-center rounded-lg border border-linha bg-superficie text-tinta-media"><Scale aria-hidden="true" className="size-5" /></span>
        <div className="min-w-0 flex-1">
          <p className="mb-1 font-dado text-[11px] text-tinta-media [overflow-wrap:anywhere]">{servico.codigo || 'Sem código interno'}</p>
          <h3 className="text-base font-semibold leading-snug tracking-tight text-tinta [overflow-wrap:anywhere]">{servico.nome}</h3>
        </div>
        <Badge variant={servico.ativo ? 'success' : 'secondary'} className="shrink-0 rounded-full">{servico.ativo ? 'Ativo' : 'Inativo'}</Badge>
      </header>

      <p className="mt-4 flex items-start gap-1.5 text-xs text-tinta-media"><Building2 aria-hidden="true" className="mt-0.5 size-3.5 shrink-0" /><span className="[overflow-wrap:anywhere]">{servico.setorNome || (servico.setorId ? 'Setor indisponível' : 'Sem setor · disponível somente no painel')}{servico.setorId ? <> · {servico.servicoPaiNome ? `dentro de ${servico.servicoPaiNome}` : 'menu do setor'}{servico.ordem > 0 ? `, posição ${servico.ordem}` : ''}</> : null}</span></p>
      {servico.mensagemAposDados?.trim() ? <p className="mt-1.5 flex items-start gap-1.5 text-xs text-tinta-media"><MessageSquare aria-hidden="true" className="mt-0.5 size-3.5 shrink-0" /><span>Pede os dados e responde com a mensagem cadastrada</span></p> : null}

      <div className="mt-3 rounded-lg bg-superficie p-3">
        <p className="flex items-center gap-1.5 text-xs text-tinta-media"><Clock3 aria-hidden="true" className="size-3.5" />Prazo do serviço</p>
        <p className="mt-1 text-lg font-semibold tracking-tight text-tinta">{prazo?.prazoDescrito || 'Sem controle de prazo'}</p>
        {prazo ? (
          <div className="mt-2 border-t border-linha pt-2 text-xs text-tinta-media">
            <p>Alerta ao consumir <span className="font-dado font-medium text-tinta">{prazo.percentualAlerta}%</span> do prazo</p>
            <p className="mt-1">{prazo.apenasDiasUteis ? 'Sábados e domingos não consomem prazo.' : 'Contagem em dias corridos.'}</p>
          </div>
        ) : <p className="mt-1 text-xs text-tinta-media">Não participa dos contadores de alerta e atraso.</p>}
      </div>

      {servico.documentosNecessarios?.trim() ? (
        <details className="mt-3 rounded-lg border border-linha open:bg-superficie/50">
          <summary className="cursor-pointer rounded-lg px-3 py-2.5 text-xs font-medium text-tinta">Documentos necessários</summary>
          <p className="whitespace-pre-wrap px-3 pb-3 text-sm leading-relaxed text-tinta-media [overflow-wrap:anywhere]">{servico.documentosNecessarios}</p>
        </details>
      ) : (
        <p className="mt-3 flex items-center gap-1.5 px-1 py-2.5 text-xs text-tinta-media"><FileText aria-hidden="true" className="size-3.5 shrink-0" />Documentos ainda não informados</p>
      )}

      <footer className="mt-auto flex flex-wrap items-center justify-between gap-2 pt-4">
        <span className="inline-flex items-center gap-1.5 text-xs text-tinta-media"><MessageSquare aria-hidden="true" className="size-3.5" /><span><span className="font-dado text-tinta">{servico.conversasAtivas}</span> {servico.conversasAtivas === 1 ? 'atendimento aberto' : 'atendimentos abertos'}</span></span>
        {podeEditar ? <Button variant="outline" size="sm" className="min-h-11 rounded-lg md:min-h-9" onClick={() => aoEditar(servico)} aria-label={`Editar ${servico.nome}`}><Pencil aria-hidden="true" />Editar serviço</Button> : null}
      </footer>
    </article>
  )
}
