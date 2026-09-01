import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import QRCode from 'qrcode'
import { Copy, KeyRound, ShieldCheck, ShieldOff, TriangleAlert } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { mfaApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'

/**
 * SEC-03 — gestão do próprio MFA (TOTP). Três estados: sem MFA (convite a
 * ativar), ativando (QR + confirmação + códigos de recuperação mostrados uma
 * única vez), e ativo (desativar/regenerar, ambos com step-up).
 *
 * Nunca usa JWT sozinho para nada sensível: toda ação aqui pede a senha atual
 * de novo, e — quando já há MFA — também o código do autenticador. Um token
 * roubado não troca, não desativa e não regenera nada.
 */
export function AbaSeguranca() {
  const clienteDeConsultas = useQueryClient()

  const { data: situacao, isLoading } = useQuery({
    queryKey: ['mfa-situacao'],
    queryFn: () => mfaApi.situacao(),
  })

  const [modo, setModo] = useState<'nenhum' | 'ativando'>('nenhum')

  function aoConcluirOuCancelar() {
    setModo('nenhum')
    void clienteDeConsultas.invalidateQueries({ queryKey: ['mfa-situacao'] })
  }

  if (isLoading) {
    return (
      <div className="flex max-w-2xl flex-col gap-4">
        <Skeleton className="h-20 w-full" />
        <Skeleton className="h-10 w-40" />
      </div>
    )
  }

  if (!situacao) return null

  if (modo === 'ativando') {
    return <FluxoDeAtivacao trocandoAutenticador={situacao.ativo} aoConcluir={aoConcluirOuCancelar} />
  }

  return (
    <div className="flex max-w-2xl flex-col gap-5">
      <p className="text-sm text-tinta-media">
        A verificação em duas etapas pede, além da senha, um código de 6 dígitos gerado por um
        aplicativo autenticador (Google Authenticator, Microsoft Authenticator, Authy) a cada login.
        Protege a conta mesmo que a senha vaze.
      </p>

      <div className="flex items-center justify-between rounded-md border border-linha bg-superficie p-3">
        <div className="flex flex-col gap-0.5">
          <span className="flex items-center gap-2 text-sm font-medium text-tinta">
            {situacao.ativo ? (
              <ShieldCheck className="size-4 text-verde" aria-hidden />
            ) : (
              <ShieldOff className="size-4 text-tinta-fraca" aria-hidden />
            )}
            {situacao.ativo ? 'Verificação em duas etapas ativa' : 'Verificação em duas etapas desligada'}
          </span>
          <span className="text-xs text-tinta-media">
            {situacao.ativo
              ? `${situacao.codigosDeRecuperacaoRestantes ?? 0} código(s) de recuperação restante(s).`
              : situacao.obrigatorio
                ? 'A política da serventia exige que você ative para continuar acessando.'
                : 'Recomendado para administradores e para o titular da serventia.'}
          </span>
        </div>

        {!situacao.ativo ? (
          <Button type="button" onClick={() => setModo('ativando')}>
            Ativar
          </Button>
        ) : null}
      </div>

      {situacao.ativo ? (
        <div className="flex flex-col gap-3">
          {situacao.obrigatorio ? (
            <p className="flex items-start gap-2 text-xs text-tinta-media">
              <TriangleAlert className="mt-0.5 size-3.5 shrink-0 text-ambar" aria-hidden />
              A política da serventia exige verificação em duas etapas para o seu perfil — não é
              possível desativar enquanto ela estiver ligada.
            </p>
          ) : null}

          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" onClick={() => setModo('ativando')}>
              Trocar autenticador
            </Button>
            <AcaoComStepUp
              rotulo="Gerar novos códigos de recuperação"
              confirmar="Isto invalida os 10 códigos atuais. Continuar?"
              executar={(senha, codigo) => mfaApi.regenerarCodigos(senha, codigo)}
              aoSucesso={(resposta) => {
                clienteDeConsultas.invalidateQueries({ queryKey: ['mfa-situacao'] })
                return resposta ? resposta.codigosDeRecuperacao : null
              }}
            />
            {!situacao.obrigatorio ? (
              <AcaoComStepUp
                rotulo="Desativar"
                variante="destructive"
                confirmar="Desativar a verificação em duas etapas desta conta?"
                executar={(senha, codigo) => mfaApi.desativar(senha, codigo)}
                aoSucesso={() => {
                  toast.success('Verificação em duas etapas desativada.')
                  void clienteDeConsultas.invalidateQueries({ queryKey: ['mfa-situacao'] })
                  return null
                }}
              />
            ) : null}
          </div>
        </div>
      ) : null}
    </div>
  )
}

/**
 * Botão que abre um miniformulário de step-up (senha + código) inline, e só
 * então executa a ação. `aoSucesso` pode devolver códigos de recuperação
 * novos (regeneração), mostrados uma vez, ou `null` (desativação).
 */
function AcaoComStepUp({
  rotulo,
  confirmar,
  executar,
  aoSucesso,
  variante = 'outline',
}: {
  rotulo: string
  confirmar: string
  executar: (senha: string, codigo: string) => Promise<{ codigosDeRecuperacao: string[] } | void>
  aoSucesso: (resposta: { codigosDeRecuperacao: string[] } | void) => string[] | null
  variante?: 'outline' | 'destructive'
}) {
  const [aberto, setAberto] = useState(false)
  const [senha, setSenha] = useState('')
  const [codigo, setCodigo] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [codigosNovos, setCodigosNovos] = useState<string[] | null>(null)

  if (codigosNovos) {
    return <CodigosDeRecuperacao codigos={codigosNovos} aoFechar={() => setCodigosNovos(null)} />
  }

  if (!aberto) {
    return (
      <Button type="button" variant={variante} onClick={() => setAberto(true)}>
        {rotulo}
      </Button>
    )
  }

  async function confirmarAcao() {
    if (!window.confirm(confirmar)) return

    setEnviando(true)
    setErro(null)

    try {
      const resposta = await executar(senha, codigo)
      const novos = aoSucesso(resposta ?? undefined)
      if (novos) setCodigosNovos(novos)
      setAberto(false)
      setSenha('')
      setCodigo('')
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível concluir.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <div className="flex w-full flex-col gap-2 rounded-md border border-linha bg-superficie p-3 sm:max-w-sm">
      <span className="text-sm font-medium text-tinta">{rotulo} — confirme sua identidade</span>
      <Input
        type="password"
        placeholder="Senha atual"
        autoComplete="current-password"
        value={senha}
        onChange={(evento) => setSenha(evento.target.value)}
      />
      <Input
        type="text"
        placeholder="Código do autenticador ou de recuperação"
        autoComplete="one-time-code"
        value={codigo}
        onChange={(evento) => setCodigo(evento.target.value)}
      />
      {erro ? (
        <p role="alert" className="text-xs text-erro">
          {erro}
        </p>
      ) : null}
      <div className="flex gap-2">
        <Button
          type="button"
          size="sm"
          disabled={enviando || !senha || !codigo}
          onClick={() => void confirmarAcao()}
        >
          {enviando ? 'Confirmando…' : 'Confirmar'}
        </Button>
        <Button
          type="button"
          size="sm"
          variant="ghost"
          disabled={enviando}
          onClick={() => {
            setAberto(false)
            setErro(null)
          }}
        >
          Cancelar
        </Button>
      </div>
    </div>
  )
}

/** Etapas 1 (senha) e 2 (QR + confirmação) da ativação — primeira vez ou troca de autenticador. */
function FluxoDeAtivacao({
  trocandoAutenticador,
  aoConcluir,
}: {
  trocandoAutenticador: boolean
  aoConcluir: () => void
}) {
  const [senha, setSenha] = useState('')
  const [codigoAtual, setCodigoAtual] = useState('')
  const [iniciado, setIniciado] = useState<{ otpauthUri: string; segredoBase32: string } | null>(null)
  const [codigo, setCodigo] = useState('')
  const [codigosDeRecuperacao, setCodigosDeRecuperacao] = useState<string[] | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  async function iniciar() {
    setEnviando(true)
    setErro(null)

    try {
      const resposta = await mfaApi.iniciar(senha, trocandoAutenticador ? codigoAtual : null)
      setIniciado(resposta)
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível iniciar a ativação.')
    } finally {
      setEnviando(false)
    }
  }

  async function confirmar() {
    setEnviando(true)
    setErro(null)

    try {
      const resposta = await mfaApi.confirmar(codigo)
      setCodigosDeRecuperacao(resposta.codigosDeRecuperacao)
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Código inválido.')
    } finally {
      setEnviando(false)
    }
  }

  if (codigosDeRecuperacao) {
    return (
      <div className="flex max-w-2xl flex-col gap-4">
        <p className="text-sm font-medium text-verde">Verificação em duas etapas ativada.</p>
        <CodigosDeRecuperacao codigos={codigosDeRecuperacao} aoFechar={aoConcluir} />
      </div>
    )
  }

  if (iniciado) {
    return (
      <div className="flex max-w-md flex-col gap-4">
        <p className="text-sm text-tinta-media">
          Escaneie o QR Code com seu aplicativo autenticador, ou digite o código manualmente.
        </p>

        <QrCodeDoSegredo otpauthUri={iniciado.otpauthUri} />

        <div className="flex flex-col gap-1">
          <Label>Código manual</Label>
          <code className="rounded-md border border-linha bg-superficie px-2 py-1.5 text-center text-sm tracking-widest">
            {iniciado.segredoBase32}
          </code>
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="confirmar-codigo">Digite o código de 6 dígitos gerado agora</Label>
          <Input
            id="confirmar-codigo"
            inputMode="numeric"
            autoFocus
            value={codigo}
            onChange={(evento) => setCodigo(evento.target.value)}
            className="text-center tracking-[0.3em]"
          />
        </div>

        {erro ? (
          <p role="alert" className="text-xs text-erro">
            {erro}
          </p>
        ) : null}

        <div className="flex gap-2">
          <Button type="button" disabled={enviando || codigo.length < 6} onClick={() => void confirmar()}>
            {enviando ? 'Confirmando…' : 'Confirmar e ativar'}
          </Button>
          <Button type="button" variant="ghost" disabled={enviando} onClick={aoConcluir}>
            Cancelar
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div className="flex max-w-md flex-col gap-4">
      <p className="text-sm text-tinta-media">
        {trocandoAutenticador
          ? 'Confirme a senha atual e o código do autenticador em uso para trocar por um novo.'
          : 'Confirme a senha atual para gerar o segredo do autenticador.'}
      </p>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="senha-ativacao">Senha atual</Label>
        <Input
          id="senha-ativacao"
          type="password"
          autoComplete="current-password"
          autoFocus
          value={senha}
          onChange={(evento) => setSenha(evento.target.value)}
        />
      </div>

      {trocandoAutenticador ? (
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="codigo-atual">Código do autenticador atual</Label>
          <Input
            id="codigo-atual"
            inputMode="numeric"
            value={codigoAtual}
            onChange={(evento) => setCodigoAtual(evento.target.value)}
          />
        </div>
      ) : null}

      {erro ? (
        <p role="alert" className="text-xs text-erro">
          {erro}
        </p>
      ) : null}

      <div className="flex gap-2">
        <Button
          type="button"
          disabled={enviando || !senha || (trocandoAutenticador && !codigoAtual)}
          onClick={() => void iniciar()}
        >
          {enviando ? 'Gerando…' : 'Continuar'}
        </Button>
        <Button type="button" variant="ghost" disabled={enviando} onClick={aoConcluir}>
          Cancelar
        </Button>
      </div>
    </div>
  )
}

/**
 * Renderiza o QR Code NO NAVEGADOR — a URI `otpauth://` (que carrega o
 * segredo) nunca sai daqui como imagem gerada por um serviço de terceiro.
 */
function QrCodeDoSegredo({ otpauthUri }: { otpauthUri: string }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    if (!canvasRef.current) return
    void QRCode.toCanvas(canvasRef.current, otpauthUri, { width: 200, margin: 1 })
  }, [otpauthUri])

  return (
    <div className="flex justify-center rounded-md border border-linha bg-branco p-4">
      <canvas ref={canvasRef} role="img" aria-label="QR Code para o aplicativo autenticador" />
    </div>
  )
}

/** Os 10 códigos, mostrados UMA vez, com aviso e cópia rápida. */
function CodigosDeRecuperacao({ codigos, aoFechar }: { codigos: string[]; aoFechar: () => void }) {
  return (
    <div className="flex max-w-md flex-col gap-3 rounded-md border border-ambar/40 bg-ambar/[0.06] p-4">
      <p className="flex items-start gap-2 text-sm font-medium text-tinta">
        <TriangleAlert className="mt-0.5 size-4 shrink-0 text-ambar" aria-hidden />
        Guarde estes 10 códigos de recuperação agora — eles não serão mostrados de novo.
      </p>
      <p className="text-xs text-tinta-media">
        Cada um funciona uma única vez, no lugar do código do autenticador, se você perder o acesso a
        ele.
      </p>

      <div className="grid grid-cols-2 gap-1.5 font-mono text-xs">
        {codigos.map((codigoDeRecuperacao) => (
          <code key={codigoDeRecuperacao} className="rounded border border-linha bg-branco px-2 py-1">
            {codigoDeRecuperacao}
          </code>
        ))}
      </div>

      <div className="flex gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => {
            void navigator.clipboard.writeText(codigos.join('\n'))
            toast.success('Códigos copiados.')
          }}
        >
          <Copy className="size-3.5" aria-hidden />
          Copiar todos
        </Button>
        <Button type="button" size="sm" onClick={aoFechar}>
          <KeyRound className="size-3.5" aria-hidden />
          Já guardei — concluir
        </Button>
      </div>
    </div>
  )
}
