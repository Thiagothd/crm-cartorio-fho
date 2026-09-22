import { useState } from 'react'
import type { ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { TriangleAlert } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Painel } from '@/components/ui/painel'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { serventiaApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import type { ChavesGerais } from '@/lib/api/tipos'

/**
 * Chaves gerais de liga/desliga do cartório: mensagens automáticas e
 * Resposta Inteligente (IA). Vivem no `Tenant` (migração 049) e valem na
 * próxima mensagem — sem deploy, sem mexer em arquivo de servidor.
 *
 * Cada interruptor mora na tela do que ele desliga (Automáticas e IA ›
 * Situação), não numa tela de "configurações gerais" à parte: quem abre as
 * mensagens automáticas é quem precisa ver, no topo, que elas estão caladas.
 *
 * Desligar pede confirmação e ligar não. Religar só devolve o comportamento
 * de sempre; desligar muda o que o cliente recebe no WhatsApp, e quem clica
 * precisa ler isso antes — em especial que o bot de triagem sai junto.
 *
 * Bordô: o switch fica em `selo` (é o switch que comanda a seção). Nas duas
 * telas onde entra, somado ao item ativo do rail e ao botão primário do
 * cabeçalho, fecha exatamente as 3 ocorrências (DESIGN.md 2.1).
 */

const CHAVE_DA_CONSULTA = ['chaves-gerais'] as const

function usarChavesGerais() {
  return useQuery({
    queryKey: CHAVE_DA_CONSULTA,
    queryFn: () => serventiaApi.chavesGerais(),
  })
}

function usarAlterarChave(qual: 'mensagens' | 'ia') {
  const clienteDeConsultas = useQueryClient()

  return useMutation({
    mutationFn: (habilitada: boolean) =>
      qual === 'mensagens'
        ? serventiaApi.definirMensagensAutomaticas(habilitada)
        : serventiaApi.definirRespostaInteligente(habilitada),
    onSuccess: (chaves: ChavesGerais) => {
      clienteDeConsultas.setQueryData(CHAVE_DA_CONSULTA, chaves)
      // A situação da IA e a saúde da Visão Geral leem a mesma chave.
      void clienteDeConsultas.invalidateQueries({ queryKey: ['ia'] })
      void clienteDeConsultas.invalidateQueries({ queryKey: ['indicadores', 'visao-geral'] })
    },
  })
}

/**
 * `tom="tinta"` quando a tela já gasta o terceiro bordô em outro lugar — a
 * aba de Automáticas sem nenhuma regra mostra um segundo botão primário
 * ("Cadastrar a primeira") no estado vazio.
 */
export function ChaveGeralDasMensagensAutomaticas({ tom = 'selo' }: { tom?: 'selo' | 'tinta' }) {
  const { pode } = usarAutenticacao()
  const { data, isLoading } = usarChavesGerais()
  const alterar = usarAlterarChave('mensagens')

  return (
    <CartaoDeChaveGeral
      id="chave-geral-mensagens"
      tom={tom}
      titulo="Mensagens automáticas"
      carregando={isLoading}
      habilitada={data?.mensagensAutomaticasHabilitadas}
      podeAlterar={pode('automacao.mensagens.editar')}
      alterando={alterar.isPending}
      aoAlterar={(habilitada) => alterar.mutateAsync(habilitada)}
      textoLigado="Ligadas. Cada regra abaixo dispara quando o evento acontece, se estiver ativa."
      textoDesligado="Desligadas. Nenhuma mensagem automática sai — inclusive o menu de triagem."
      avisoDesligado={
        <>
          <strong className="text-tinta">Todas as mensagens automáticas estão desligadas.</strong> O
          cliente que mandar “oi” não recebe nada: nem boas-vindas, nem pedido de nome, nem menu de
          setores ou de serviços, nem despedida, nem aviso de prazo. As regras continuam cadastradas
          e voltam a valer ao religar.
        </>
      }
      tituloDaConfirmacao="Desligar todas as mensagens automáticas?"
      confirmacao={
        <>
          <p>
            Isto desliga <strong className="text-tinta">todos</strong> os gatilhos, inclusive o bot
            de triagem. A partir da próxima mensagem:
          </p>
          <ul className="list-disc space-y-1 pl-5">
            <li>
              o cliente que mandar “oi” <strong className="text-tinta">não recebe resposta nenhuma</strong>{' '}
              — nem boas-vindas, nem pedido de nome, nem menu de setores ou de serviços;
            </li>
            <li>avisos de etapa e de prazo, despedida e a pergunta de encerramento também param;</li>
            <li>a conversa continua entrando na fila e um atendente pode responder normalmente.</li>
          </ul>
          <p>
            Nada é apagado. Para calar só uma mensagem, desative a regra dela em vez de usar esta
            chave.
          </p>
        </>
      }
      rotuloDeConfirmar="Desligar mensagens"
    />
  )
}

export function ChaveGeralDaIa() {
  const { pode } = usarAutenticacao()
  const { data, isLoading } = usarChavesGerais()
  const alterar = usarAlterarChave('ia')

  return (
    <CartaoDeChaveGeral
      id="chave-geral-ia"
      titulo="Resposta Inteligente"
      carregando={isLoading}
      habilitada={data?.respostaInteligenteHabilitada}
      podeAlterar={pode('ia.conhecimento.editar')}
      alterando={alterar.isPending}
      aoAlterar={(habilitada) => alterar.mutateAsync(habilitada)}
      textoLigado="Ligada. A IA tenta responder quando o cliente escreve algo fora do menu."
      textoDesligado="Desligada. A IA não responde ninguém; o cliente pode pedir um atendente."
      avisoDesligado={
        <>
          <strong className="text-tinta">A Resposta Inteligente está desligada.</strong> Quando o
          cliente escreve algo fora do menu, ele recebe a mensagem de sempre para quem a IA não soube
          responder e pode pedir um atendente. O conhecimento cadastrado continua salvo.
        </>
      }
      tituloDaConfirmacao="Desligar a Resposta Inteligente?"
      confirmacao={
        <>
          <p>
            A partir da próxima mensagem, a IA não responde mais nenhum cliente. Quem escrever algo
            fora do menu recebe a mensagem automática de “não encontrei a resposta” e pode pedir um
            atendente — o mesmo caminho de quando a IA não sabe responder.
          </p>
          <p>O conhecimento aprovado não é apagado e volta a valer ao religar.</p>
        </>
      }
      rotuloDeConfirmar="Desligar a IA"
    />
  )
}

function CartaoDeChaveGeral({
  id,
  tom = 'selo',
  titulo,
  carregando,
  habilitada,
  podeAlterar,
  alterando,
  aoAlterar,
  textoLigado,
  textoDesligado,
  avisoDesligado,
  tituloDaConfirmacao,
  confirmacao,
  rotuloDeConfirmar,
}: {
  id: string
  tom?: 'selo' | 'tinta'
  titulo: string
  carregando: boolean
  habilitada: boolean | undefined
  podeAlterar: boolean
  alterando: boolean
  aoAlterar: (habilitada: boolean) => Promise<ChavesGerais>
  textoLigado: string
  textoDesligado: string
  avisoDesligado: ReactNode
  tituloDaConfirmacao: string
  confirmacao: ReactNode
  rotuloDeConfirmar: string
}) {
  const [confirmando, setConfirmando] = useState(false)

  if (carregando) {
    return <Skeleton className="h-[72px] rounded-card" />
  }

  // Falha ao ler não esconde a tela inteira: o resto da aba continua útil, e
  // o interruptor simplesmente não aparece até a próxima leitura.
  if (habilitada === undefined) {
    return null
  }

  async function aplicar(novoValor: boolean) {
    try {
      await aoAlterar(novoValor)
      toast.success(`${titulo} — chave geral ${novoValor ? 'ligada' : 'desligada'}.`)
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    }
  }

  function aoMudar(novoValor: boolean) {
    if (novoValor) {
      void aplicar(true)
      return
    }

    setConfirmando(true)
  }

  return (
    <>
      <Painel>
        <div className="flex items-center justify-between gap-3">
          <div className="flex min-w-0 flex-col">
            <Label htmlFor={id} className="text-sm font-semibold text-tinta">
              {titulo}
            </Label>
            <span className="text-xs text-tinta-media">
              {habilitada ? textoLigado : textoDesligado}
              {podeAlterar ? null : ' Só quem gerencia esta área pode mudar.'}
            </span>
          </div>

          <Switch
            id={id}
            tom={tom}
            checked={habilitada}
            onCheckedChange={aoMudar}
            disabled={!podeAlterar || alterando}
          />
        </div>

        {habilitada ? null : (
          <p
            role="status"
            className="mt-3 flex items-start gap-2 rounded-md border border-ambar/40 bg-ambar/[0.06] px-3 py-2 text-xs leading-relaxed text-tinta-media"
          >
            <TriangleAlert className="mt-0.5 size-3.5 shrink-0 text-ambar" aria-hidden="true" />
            <span>{avisoDesligado}</span>
          </p>
        )}
      </Painel>

      <Dialog open={confirmando} onOpenChange={setConfirmando}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{tituloDaConfirmacao}</DialogTitle>
            <DialogDescription asChild>
              <div className="flex flex-col gap-2 text-sm text-tinta-media">{confirmacao}</div>
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setConfirmando(false)}>
              Cancelar
            </Button>
            <Button
              type="button"
              disabled={alterando}
              onClick={() => {
                setConfirmando(false)
                void aplicar(false)
              }}
            >
              {rotuloDeConfirmar}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
