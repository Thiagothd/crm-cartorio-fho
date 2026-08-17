import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
// MessageSquarePlus, e nao um icone de robo: a interface e livro de registro, nao
// painel de SaaS (DESIGN.md, secao 1). O icone diz "falta cadastrar mensagem", que
// e o que o estado vazio significa.
import { MessageSquarePlus, Pencil, Plus } from 'lucide-react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { EstadoVazio } from '@/components/layout/shell'
import { etapasApi, mensagensAutomaticasApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { MARCADORES } from '@/features/atendimento/placeholders'
import { FluxoDeMensagens } from './fluxo-de-mensagens'
import type { Etapa, GatilhoDeMensagem, MensagemAutomatica } from '@/lib/api/tipos'

/**
 * Mesmo limite da coluna. É menor que os 4000 de uma mensagem porque o que se grava
 * aqui é o MODELO, e os marcadores crescem na substituição — {MenuSetores} em
 * especial.
 */
const LIMITE_DO_TEXTO = 1000

/**
 * Espelho de `GatilhoDeMensagem` no backend. É lista fechada porque cada valor
 * corresponde a um ponto de código que precisa existir para disparar — gatilho novo
 * exige release. O que é cadastro é o TEXTO e QUAL etapa dispara.
 */
const GATILHOS: { valor: GatilhoDeMensagem; rotulo: string; explicacao: string }[] = [
  {
    valor: 'NovoAtendimento',
    rotulo: 'Primeiro contato do cliente',
    explicacao:
      'Sai quando a conversa é aberta sem setor definido. Use {MenuSetores} para o cliente escolher o assunto.',
  },
  {
    valor: 'TriagemSemCorrespondencia',
    rotulo: 'Cliente não escolheu setor no menu',
    explicacao:
      'Sai quando a resposta ao menu não corresponde a nenhum setor. Depois disso o atendimento segue com uma pessoa.',
  },
  {
    valor: 'TriagemComCorrespondencia',
    rotulo: 'Setor reconhecido, falta escolher o serviço',
    explicacao:
      'Sai logo depois que o cliente acerta o setor no menu. Use {MenuServicos} para o cliente escolher o serviço específico.',
  },
  {
    valor: 'ServicoEscolhido',
    rotulo: 'Cliente escolheu o serviço',
    explicacao:
      'Sai quando o cliente identifica o serviço no menu de 2º nível. Use {DocumentosNecessarios} e {PrazoServico}.',
  },
  {
    valor: 'EtapaAlterada',
    rotulo: 'Atendimento muda de etapa',
    explicacao: 'Sai quando a conversa entra na etapa escolhida abaixo.',
  },
  {
    valor: 'PrazoProximoVencimento',
    rotulo: 'Prazo perto de vencer',
    explicacao:
      'Sai no percentual de alerta já configurado em cada serviço, na tela de Serviços.',
  },
  {
    valor: 'PrazoVencido',
    rotulo: 'Prazo vencido',
    explicacao: 'Sai quando o prazo-limite do serviço passa.',
  },
]

export function AbaMensagensAutomaticas() {
  const [emEdicao, setEmEdicao] = useState<MensagemAutomatica | null>(null)
  const [preenchimento, setPreenchimento] = useState<{
    gatilho: GatilhoDeMensagem
    etapaAlvoId: string | null
  } | null>(null)
  const [aberto, setAberto] = useState(false)

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['mensagens-automaticas-gestao'],
    queryFn: () => mensagensAutomaticasApi.listar(true),
  })

  const { data: etapas } = useQuery({
    queryKey: ['etapas-gestao'],
    queryFn: () => etapasApi.listar(true),
  })

  function recarregar() {
    void clienteDeConsultas.invalidateQueries({ queryKey: ['mensagens-automaticas-gestao'] })
  }

  function abrirNova() {
    setEmEdicao(null)
    setPreenchimento(null)
    setAberto(true)
  }

  /** Chamado pelo trilho: edita, se o ponto já tem mensagem, ou abre em modo novo já com o gatilho certo. */
  function abrirPonto(gatilho: GatilhoDeMensagem, etapaAlvoId: string | null, existente: MensagemAutomatica | null) {
    setEmEdicao(existente)
    setPreenchimento(existente ? null : { gatilho, etapaAlvoId })
    setAberto(true)
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start justify-between gap-4">
        <p className="max-w-2xl text-sm text-tinta-media">
          Mensagens que o sistema envia <strong className="text-tinta">sozinho</strong> quando um
          evento do atendimento acontece. Os textos abaixo são rascunho e aguardam a redação do
          cartório parceiro.
        </p>

        <Button size="sm" onClick={abrirNova}>
          <Plus />
          Nova mensagem
        </Button>
      </div>

      <FluxoDeMensagens mensagens={data ?? []} etapas={etapas ?? []} aoClicarPonto={abrirPonto} />

      {isLoading ? (
        <div className="flex flex-col gap-2">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : data?.length ? (
        <div className="overflow-hidden rounded-md border border-linha">
          <table className="w-full text-sm">
            <thead className="bg-superficie text-xs text-tinta-media">
              <tr>
                <th className="w-72 px-4 py-2.5 text-left font-medium">Quando dispara</th>
                <th className="px-4 py-2.5 text-left font-medium">Mensagem</th>
                <th className="w-10" />
              </tr>
            </thead>

            <tbody>
              {data.map((mensagem) => (
                <tr key={mensagem.id} className="border-t border-linha align-top hover:bg-superficie">
                  <td className="px-4 py-2.5">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium">{mensagem.gatilhoDescrito}</span>
                      {!mensagem.ativo ? <Badge variant="secondary">Inativa</Badge> : null}
                    </div>
                    {mensagem.etapaAlvoNome ? (
                      <p className="mt-0.5 text-xs text-tinta-media">
                        Etapa: {mensagem.etapaAlvoNome}
                      </p>
                    ) : null}
                  </td>

                  <td className="px-4 py-2.5">
                    <p className="line-clamp-2 whitespace-pre-wrap text-xs text-tinta-media">
                      {mensagem.conteudo}
                    </p>
                  </td>

                  <td className="px-2 py-2.5">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      onClick={() => {
                        setEmEdicao(mensagem)
                        setAberto(true)
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
      ) : (
        /* Diferente das abas de setor e etapa, esta pode legitimamente estar vazia:
           nenhuma mensagem automática é obrigatória. Sem este estado, a tela seria
           uma tabela só com cabeçalho e ninguém saberia se faltou cadastro ou se
           falhou o carregamento. */
        <EstadoVazio
          icone={MessageSquarePlus}
          titulo="Nenhuma mensagem automática"
          descricao="Sem regra cadastrada, o sistema não envia nada sozinho — nenhum evento do atendimento dispara mensagem ao cliente."
          acao={
            <Button size="sm" onClick={abrirNova}>
              <Plus />
              Cadastrar a primeira
            </Button>
          }
        />
      )}

      <DialogoDeMensagemAutomatica
        mensagem={emEdicao}
        preenchimento={preenchimento}
        etapas={etapas ?? []}
        aberto={aberto}
        aoFechar={() => setAberto(false)}
        aoSalvar={recarregar}
      />
    </div>
  )
}

function DialogoDeMensagemAutomatica({
  mensagem,
  preenchimento,
  etapas,
  aberto,
  aoFechar,
  aoSalvar,
}: {
  mensagem: MensagemAutomatica | null
  /** Gatilho (e etapa, se for o caso) já escolhidos ao abrir em modo "nova" a partir do trilho. */
  preenchimento: { gatilho: GatilhoDeMensagem; etapaAlvoId: string | null } | null
  etapas: Etapa[]
  aberto: boolean
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [gatilho, setGatilho] = useState<GatilhoDeMensagem>('NovoAtendimento')
  const [etapaAlvoId, setEtapaAlvoId] = useState('')
  const [conteudo, setConteudo] = useState('')
  const [ativo, setAtivo] = useState(true)
  const [salvando, setSalvando] = useState(false)
  const campo = useRef<HTMLTextAreaElement>(null)

  const exigeEtapa = gatilho === 'EtapaAlterada'
  const explicacao = GATILHOS.find((g) => g.valor === gatilho)?.explicacao

  useEffect(() => {
    if (!aberto) return

    setGatilho(mensagem?.gatilho ?? preenchimento?.gatilho ?? 'NovoAtendimento')
    setEtapaAlvoId(mensagem?.etapaAlvoId ?? preenchimento?.etapaAlvoId ?? '')
    setConteudo(mensagem?.conteudo ?? '')
    setAtivo(mensagem?.ativo ?? true)
  }, [aberto, mensagem, preenchimento])

  /**
   * Insere o marcador na posição do cursor.
   *
   * Existe porque o backend RECUSA marcador desconhecido: um "{NomeClinte}" digitado
   * errado sairia literal na mensagem que o cliente do cartório lê. Clicar na lista
   * é mais rápido que digitar e não erra.
   */
  function inserirMarcador(token: string) {
    const elemento = campo.current

    if (!elemento) {
      setConteudo((atual) => atual + token)
      return
    }

    const inicio = elemento.selectionStart
    const fim = elemento.selectionEnd

    setConteudo((atual) => atual.slice(0, inicio) + token + atual.slice(fim))

    requestAnimationFrame(() => {
      elemento.focus()
      elemento.setSelectionRange(inicio + token.length, inicio + token.length)
    })
  }

  async function salvar() {
    setSalvando(true)

    try {
      const dados = {
        gatilho,
        etapaAlvoId: exigeEtapa ? etapaAlvoId || null : null,
        conteudo,
        ativo,
      }

      if (mensagem) {
        await mensagensAutomaticasApi.atualizar(mensagem.id, dados)
      } else {
        await mensagensAutomaticasApi.criar(dados)
      }

      toast.success(mensagem ? 'Mensagem atualizada.' : 'Mensagem criada.')
      aoSalvar()
      aoFechar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent className="max-w-xl">
        <DialogHeader>
          <DialogTitle>{mensagem ? 'Editar mensagem' : 'Nova mensagem automática'}</DialogTitle>
          <DialogDescription>
            Desativar interrompe os disparos daqui para a frente. As mensagens que esta regra já
            colocou nas conversas continuam no histórico — mensagem nunca é apagada.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="mensagem-gatilho">Quando dispara</Label>
            <Select value={gatilho} onValueChange={(v) => setGatilho(v as GatilhoDeMensagem)}>
              <SelectTrigger id="mensagem-gatilho">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {GATILHOS.map((opcao) => (
                  <SelectItem key={opcao.valor} value={opcao.valor}>
                    {opcao.rotulo}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {explicacao ? <p className="text-xs text-tinta-media">{explicacao}</p> : null}
          </div>

          {/* Só aparece no gatilho que usa: perguntar a etapa em "prazo vencido"
              seria pedir um dado que a regra ignora. */}
          {exigeEtapa ? (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="mensagem-etapa">Etapa que dispara</Label>
              <Select value={etapaAlvoId} onValueChange={setEtapaAlvoId}>
                <SelectTrigger id="mensagem-etapa">
                  <SelectValue placeholder="Escolha a etapa" />
                </SelectTrigger>
                <SelectContent>
                  {etapas.map((etapa) => (
                    <SelectItem key={etapa.id} value={etapa.id}>
                      {etapa.nome}
                      {etapa.ehFinal ? ' (conclusão)' : ''}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          ) : null}

          <div className="flex flex-col gap-1.5">
            <div className="flex items-baseline justify-between gap-2">
              <Label htmlFor="mensagem-conteudo">Texto da mensagem</Label>
              {/* Só aparece perto do limite. Sem isso o campo simplesmente para de
                  aceitar texto aos 1000 caracteres, sem dizer por quê. */}
              {conteudo.length > LIMITE_DO_TEXTO * 0.8 ? (
                <span className="font-dado text-[11px] text-tinta-fraca">
                  {conteudo.length}/{LIMITE_DO_TEXTO}
                </span>
              ) : null}
            </div>
            <Textarea
              id="mensagem-conteudo"
              ref={campo}
              value={conteudo}
              onChange={(e) => setConteudo(e.target.value)}
              rows={7}
              maxLength={LIMITE_DO_TEXTO}
              className="resize-none"
              placeholder="Ex.: Olá, {NomeCliente}! Aqui é o {NomeCartorio}."
            />

            <div className="flex flex-col gap-1.5 rounded-md border border-linha bg-superficie px-3 py-2">
              <p className="text-xs text-tinta-media">
                Clique para inserir. Marcador escrito à mão e com erro é recusado ao salvar.
              </p>
              <div className="flex flex-wrap gap-1.5">
                {MARCADORES.map((marcador) => (
                  <button
                    key={marcador.token}
                    type="button"
                    onClick={() => inserirMarcador(marcador.token)}
                    title={marcador.descricao}
                    className="rounded-md border border-linha-forte bg-branco px-1.5 py-0.5 font-dado text-[11px] text-tinta-media transition-colors hover:border-tinta hover:text-tinta"
                  >
                    {marcador.token}
                  </button>
                ))}
              </div>
            </div>
          </div>

          <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
            <div>
              <Label htmlFor="mensagem-ativa">Regra ativa</Label>
              <p className="text-xs text-tinta-media">Desativada, o evento acontece e nada é enviado.</p>
            </div>
            <Switch id="mensagem-ativa" checked={ativo} onCheckedChange={setAtivo} />
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Cancelar
          </Button>
          <Button onClick={() => void salvar()} disabled={salvando}>
            Salvar
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
