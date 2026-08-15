import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Plus, Zap } from 'lucide-react'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { EstadoVazio } from '@/components/layout/shell'
import { respostasRapidasApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { MARCADORES } from '@/features/atendimento/placeholders'
import type { RespostaRapida } from '@/lib/api/tipos'

/** Mesmo limite da coluna, igual ao da mensagem automática. */
const LIMITE_DO_TEXTO = 1000

export function AbaRespostasRapidas() {
  const [emEdicao, setEmEdicao] = useState<RespostaRapida | null>(null)
  const [aberto, setAberto] = useState(false)

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['respostas-rapidas-gestao'],
    queryFn: () => respostasRapidasApi.listar(true),
  })

  function recarregar() {
    void clienteDeConsultas.invalidateQueries({ queryKey: ['respostas-rapidas-gestao'] })
    // O seletor "/" da conversa lê a mesma lista, só que apenas as ativas.
    void clienteDeConsultas.invalidateQueries({ queryKey: ['respostas-rapidas'] })
  }

  function abrirNova() {
    setEmEdicao(null)
    setAberto(true)
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-start justify-between gap-4">
        <p className="max-w-2xl text-sm text-tinta-media">
          Textos prontos para o dia a dia. Na conversa, o atendente digita{' '}
          <span className="font-dado">/</span> seguido do atalho, escolhe e o texto entra no campo.
          Ele <strong className="text-tinta">lê e edita antes de enviar</strong> — nada sai daqui
          sozinho.
        </p>

        <Button size="sm" onClick={abrirNova}>
          <Plus />
          Nova resposta
        </Button>
      </div>

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
                <th className="w-32 px-4 py-2.5 text-left font-medium">Atalho</th>
                <th className="w-64 px-4 py-2.5 text-left font-medium">Título</th>
                <th className="px-4 py-2.5 text-left font-medium">Texto</th>
                <th className="w-10" />
              </tr>
            </thead>

            <tbody>
              {data.map((resposta) => (
                <tr key={resposta.id} className="border-t border-linha align-top hover:bg-superficie">
                  {/* Mono: é um literal que o atendente digita caractere a caractere,
                      igual ao protocolo (DESIGN.md, seção 3). */}
                  <td className="px-4 py-2.5 font-dado text-xs text-tinta-media">
                    /{resposta.atalho}
                  </td>

                  <td className="px-4 py-2.5">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium">{resposta.titulo}</span>
                      {!resposta.ativo ? <Badge variant="secondary">Inativa</Badge> : null}
                    </div>
                  </td>

                  <td className="px-4 py-2.5">
                    <p className="line-clamp-2 text-xs text-tinta-media">{resposta.conteudo}</p>
                  </td>

                  <td className="px-2 py-2.5">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      onClick={() => {
                        setEmEdicao(resposta)
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
        <EstadoVazio
          icone={Zap}
          titulo="Nenhuma resposta rápida"
          descricao="Sem cadastro, a barra não abre nada na conversa. Comece pelo que a equipe mais repete: horário, prazo, documentos."
          acao={
            <Button size="sm" onClick={abrirNova}>
              <Plus />
              Cadastrar a primeira
            </Button>
          }
        />
      )}

      <DialogoDeRespostaRapida
        resposta={emEdicao}
        aberto={aberto}
        aoFechar={() => setAberto(false)}
        aoSalvar={recarregar}
      />
    </div>
  )
}

function DialogoDeRespostaRapida({
  resposta,
  aberto,
  aoFechar,
  aoSalvar,
}: {
  resposta: RespostaRapida | null
  aberto: boolean
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [atalho, setAtalho] = useState('')
  const [titulo, setTitulo] = useState('')
  const [conteudo, setConteudo] = useState('')
  const [ativo, setAtivo] = useState(true)
  const [salvando, setSalvando] = useState(false)
  const campo = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    if (!aberto) return

    setAtalho(resposta?.atalho ?? '')
    setTitulo(resposta?.titulo ?? '')
    setConteudo(resposta?.conteudo ?? '')
    setAtivo(resposta?.ativo ?? true)
  }, [aberto, resposta])

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
      const dados = { atalho, titulo, conteudo, ativo }

      if (resposta) {
        await respostasRapidasApi.atualizar(resposta.id, dados)
      } else {
        await respostasRapidasApi.criar(dados)
      }

      toast.success(resposta ? 'Resposta atualizada.' : 'Resposta criada.')
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
          <DialogTitle>{resposta ? 'Editar resposta' : 'Nova resposta rápida'}</DialogTitle>
          <DialogDescription>
            O atalho é o que se digita depois da barra. Sem espaço e sem acento — ele é digitado no
            meio do atendimento, com pressa.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex gap-3">
            <div className="flex w-40 flex-col gap-1.5">
              <Label htmlFor="resposta-atalho">Atalho</Label>
              <div className="flex items-center gap-1">
                <span className="font-dado text-sm text-tinta-fraca">/</span>
                <Input
                  id="resposta-atalho"
                  value={atalho}
                  onChange={(e) => setAtalho(e.target.value)}
                  placeholder="prazo"
                  maxLength={30}
                  className="font-dado"
                  autoComplete="off"
                  spellCheck={false}
                />
              </div>
            </div>

            <div className="flex flex-1 flex-col gap-1.5">
              <Label htmlFor="resposta-titulo">Título</Label>
              <Input
                id="resposta-titulo"
                value={titulo}
                onChange={(e) => setTitulo(e.target.value)}
                placeholder="Ex.: Situação do protocolo"
                maxLength={120}
              />
              <p className="text-xs text-tinta-media">Aparece na lista do seletor, ao lado do atalho.</p>
            </div>
          </div>

          <div className="flex flex-col gap-1.5">
            <div className="flex items-baseline justify-between gap-2">
              <Label htmlFor="resposta-conteudo">Texto inserido</Label>
              {conteudo.length > LIMITE_DO_TEXTO * 0.8 ? (
                <span className="font-dado text-[11px] text-tinta-fraca">
                  {conteudo.length}/{LIMITE_DO_TEXTO}
                </span>
              ) : null}
            </div>
            <Textarea
              id="resposta-conteudo"
              ref={campo}
              value={conteudo}
              onChange={(e) => setConteudo(e.target.value)}
              rows={6}
              maxLength={LIMITE_DO_TEXTO}
              className="resize-none"
            />

            <div className="flex flex-col gap-1.5 rounded-md border border-linha bg-superficie px-3 py-2">
              <p className="text-xs text-tinta-media">
                Clique para inserir. Os marcadores já vêm resolvidos no campo do atendente.
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
              <Label htmlFor="resposta-ativa">Resposta ativa</Label>
              <p className="text-xs text-tinta-media">Desativada, some do seletor sem sair do cadastro.</p>
            </div>
            <Switch id="resposta-ativa" checked={ativo} onCheckedChange={setAtivo} />
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
