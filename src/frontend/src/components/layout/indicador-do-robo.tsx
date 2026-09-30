import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Bot, ChevronRight, TriangleAlert } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Switch } from '@/components/ui/switch'
import { usarAlterarChave, usarChavesGerais } from '@/features/configuracoes/chave-geral'
import { usarSetores } from '@/features/atendimento/consultas'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { cn } from '@/lib/utils'
import type { ModoDoRoboNoSetor } from '@/lib/api/tipos'

/**
 * O estado do robô do WhatsApp, visível em toda tela: mensagens automáticas e IA.
 * Pedido do cartório parceiro em 2026-09-30 — "pra gente cortar e incluir na hora que tiver
 * acontecendo".
 *
 * São as MESMAS chaves gerais das telas de Automação (`chave-geral.tsx`), com o
 * mesmo cache: mudar aqui muda lá. Todos veem o estado; só quem gerencia cada área
 * liga e desliga, com as permissões de sempre.
 *
 * Neutro por padrão, âmbar quando algo está desligado — nunca bordô: o cabeçalho
 * aparece em toda tela e não pode gastar o orçamento de 3 bordôs (DESIGN.md 2.1).
 * Pelo mesmo motivo os interruptores usam `tom="tinta"`.
 */
export function IndicadorDoRobo({ habilitado }: { habilitado: boolean }) {
  const { pode } = usarAutenticacao()
  const { data: chaves } = usarChavesGerais({ acompanhar: true })
  const [aberto, setAberto] = useState(false)

  if (!habilitado || !chaves) {
    return null
  }

  const mensagens = chaves.mensagensAutomaticasHabilitadas
  const ia = chaves.respostaInteligenteHabilitada
  const tudoLigado = mensagens && ia

  const resumo = tudoLigado
    ? 'Robô ligado'
    : !mensagens && !ia
      ? 'Robô desligado'
      : !mensagens
        ? 'Mensagens desligadas'
        : 'IA desligada'

  return (
    <Popover open={aberto} onOpenChange={setAberto}>
      <PopoverTrigger asChild>
        {/* Mesmo alvo do sino: 44px no celular, 36px a partir de `md`
            (DESIGN.md 12.2). O texto só aparece com espaço de sobra; os dois
            pontos (mensagens, IA) dizem o estado em qualquer largura. */}
        <button
          type="button"
          aria-label={`${resumo}. Mensagens automáticas ${mensagens ? 'ligadas' : 'desligadas'}, IA ${ia ? 'ligada' : 'desligada'}.`}
          className={cn(
            'flex h-11 shrink-0 touch-manipulation items-center gap-2 rounded-lg border px-2.5 text-xs font-medium transition-colors md:h-9',
            tudoLigado
              ? 'border-transparent text-tinta-media hover:border-linha-forte hover:bg-superficie hover:text-tinta'
              : 'border-ambar/40 bg-ambar/[0.06] text-ambar hover:bg-ambar/10',
          )}
        >
          <Bot className="size-4" aria-hidden />
          <span className="hidden @min-[56rem]/conteudo:inline">{resumo}</span>
          <span aria-hidden className="flex items-center gap-1">
            <PontoDeEstado ligado={mensagens} />
            <PontoDeEstado ligado={ia} />
          </span>
        </button>
      </PopoverTrigger>

      <PopoverContent className="w-[22rem] p-0">
        <div className="border-b border-linha px-4 py-3">
          <p className="text-sm font-semibold text-tinta">Robô do WhatsApp</p>
          <p className="mt-0.5 text-xs leading-relaxed text-tinta-media">
            Vale para o cartório inteiro, na próxima mensagem de cada cliente.
          </p>
        </div>

        <div className="flex flex-col divide-y divide-linha">
          <LinhaDaChave
            id="topo-chave-mensagens"
            titulo="Mensagens automáticas"
            habilitada={mensagens}
            podeAlterar={pode('automacao.mensagens.editar')}
            qual="mensagens"
            textoLigado="Boas-vindas, menus, textos dos serviços e avisos."
            textoDesligado="Nenhuma sai — nem o menu de triagem. A equipe responde tudo."
            confirmacao="O cliente que mandar “oi” não recebe nada: nem boas-vindas, nem menu, nem avisos. As conversas continuam chegando na fila."
          />
          <LinhaDaChave
            id="topo-chave-ia"
            titulo="Inteligência artificial"
            habilitada={ia}
            podeAlterar={pode('ia.conhecimento.editar')}
            qual="ia"
            textoLigado="Responde o que o cliente escreve fora do menu."
            textoDesligado="Não responde ninguém; o cliente pode pedir um atendente."
            confirmacao="Quem escrever fora do menu recebe a mensagem de “não encontrei a resposta” e pode pedir um atendente."
          />
        </div>

        {pode('setores.ver') ? <SetoresComRestricao aoNavegar={() => setAberto(false)} /> : null}
      </PopoverContent>
    </Popover>
  )
}

function PontoDeEstado({ ligado }: { ligado: boolean }) {
  return <span className={cn('size-2 rounded-full', ligado ? 'bg-verde' : 'bg-ambar')} />
}

/**
 * Um interruptor com confirmação ao desligar, dentro da própria janela: um
 * diálogo por cima de um popover rouba o foco e fecha o que o usuário estava
 * olhando. Ligar não pergunta — só devolve o comportamento de sempre.
 */
function LinhaDaChave({
  id,
  titulo,
  habilitada,
  podeAlterar,
  qual,
  textoLigado,
  textoDesligado,
  confirmacao,
}: {
  id: string
  titulo: string
  habilitada: boolean
  podeAlterar: boolean
  qual: 'mensagens' | 'ia'
  textoLigado: string
  textoDesligado: string
  confirmacao: string
}) {
  const alterar = usarAlterarChave(qual)
  const [confirmando, setConfirmando] = useState(false)

  async function aplicar(novoValor: boolean) {
    try {
      await alterar.mutateAsync(novoValor)
      toast.success(`${titulo} ${novoValor ? (qual === 'ia' ? 'ligada' : 'ligadas') : qual === 'ia' ? 'desligada' : 'desligadas'}.`)
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    } finally {
      setConfirmando(false)
    }
  }

  return (
    <div className="px-4 py-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 pt-1.5">
          <Label htmlFor={id} className="text-sm font-medium text-tinta">
            {titulo}
          </Label>
          <p className="mt-0.5 text-xs leading-relaxed text-tinta-media">
            {habilitada ? textoLigado : textoDesligado}
          </p>
        </div>
        <Switch
          id={id}
          tom="tinta"
          checked={habilitada}
          disabled={!podeAlterar || alterar.isPending}
          onCheckedChange={(valor) => (valor ? void aplicar(true) : setConfirmando(true))}
          className="-mr-2"
        />
      </div>

      {!podeAlterar ? (
        <p className="mt-1.5 text-[11px] text-tinta-fraca">Só quem gerencia esta área pode mudar.</p>
      ) : null}

      {confirmando ? (
        <div role="alert" className="mt-2.5 rounded-md border border-ambar/40 bg-ambar/[0.06] p-3">
          <p className="flex items-start gap-2 text-xs leading-relaxed text-tinta">
            <TriangleAlert className="mt-0.5 size-3.5 shrink-0 text-ambar" aria-hidden />
            <span>{confirmacao}</span>
          </p>
          <div className="mt-2.5 flex justify-end gap-2">
            <Button size="sm" variant="ghost" onClick={() => setConfirmando(false)} disabled={alterar.isPending}>
              Cancelar
            </Button>
            <Button size="sm" variant="outline" onClick={() => void aplicar(false)} disabled={alterar.isPending}>
              Desligar
            </Button>
          </div>
        </div>
      ) : null}
    </div>
  )
}

const DESCRICAO_DO_MODO: Record<Exclude<ModoDoRoboNoSetor, 'Ligado'>, string> = {
  SoEquipe: 'só equipe',
  ForaDoMenu: 'fora do menu',
}

/** Setores em que o robô não atende — para ninguém esquecer uma exceção ligada. */
function SetoresComRestricao({ aoNavegar }: { aoNavegar: () => void }) {
  const { data: setores } = usarSetores()
  const comRestricao = (setores ?? []).filter((s) => s.modoDoRobo !== 'Ligado')

  return (
    <div className="border-t border-linha px-4 py-3">
      {comRestricao.length > 0 ? (
        <ul className="mb-2 flex flex-col gap-1">
          {comRestricao.map((setor) => (
            <li key={setor.id} className="flex items-baseline justify-between gap-3 text-xs">
              <span className="truncate text-tinta">{setor.nome}</span>
              <span className="shrink-0 text-ambar">
                {DESCRICAO_DO_MODO[setor.modoDoRobo as Exclude<ModoDoRoboNoSetor, 'Ligado'>]}
              </span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="mb-2 text-xs text-tinta-media">O robô atende todos os setores.</p>
      )}
      <Link
        to="/estrutura/setores"
        onClick={aoNavegar}
        className="inline-flex items-center gap-1 text-xs font-medium text-tinta-media underline-offset-2 hover:text-tinta hover:underline"
      >
        Robô por setor
        <ChevronRight className="size-3.5" aria-hidden />
      </Link>
    </div>
  )
}
