import { useEffect, useRef, useState } from 'react'
import QRCode from 'qrcode'
import { KeyRound, Loader2, ShieldCheck, TriangleAlert } from 'lucide-react'
import { mfaApi } from '@/lib/api/endpoints'
import { ErroDaApi, gravarToken } from '@/lib/api/cliente-http'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

/**
 * SEC-03 — matrícula de MFA para conta PRIVILEGIADA (Titular ou
 * Administrador), usando o ticket emitido fora de banda por quem já tem MFA
 * (ou pela ferramenta de bootstrap). Rota pública: a conta ainda não tem
 * sessão — e-mail + senha + ticket substituem o JWT que a tela de
 * Segurança normal exige.
 */
export function PaginaMatriculaMfa() {
  const [etapa, setEtapa] = useState<'ticket' | 'autenticador' | 'codigos'>('ticket')

  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [ticket, setTicket] = useState('')
  const [desafio, setDesafio] = useState<string | null>(null)

  const [iniciado, setIniciado] = useState<{ otpauthUri: string; segredoBase32: string } | null>(null)
  const [codigo, setCodigo] = useState('')
  const [codigosDeRecuperacao, setCodigosDeRecuperacao] = useState<string[] | null>(null)

  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  async function aoConfirmarTicket() {
    setEnviando(true)
    setErro(null)

    try {
      const resposta = await mfaApi.iniciarMatriculaPrivilegiada(email, senha, ticket)
      setDesafio(resposta.desafio)

      const autenticador = await mfaApi.iniciarAutenticadorViaDesafio(resposta.desafio)
      setIniciado(autenticador)
      setEtapa('autenticador')
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível validar o ticket.')
    } finally {
      setEnviando(false)
    }
  }

  async function aoConfirmarCodigo() {
    if (!desafio) return

    setEnviando(true)
    setErro(null)

    try {
      const resposta = await mfaApi.confirmarViaDesafio(desafio, codigo)
      gravarToken(resposta.sessao.token)
      setCodigosDeRecuperacao(resposta.codigosDeRecuperacao)
      setEtapa('codigos')
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Código inválido.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <main className="flex min-h-dvh items-center justify-center bg-papel px-4 py-8">
      <section className="w-full max-w-md rounded-card border border-linha bg-branco p-7 shadow-card">
        <div className="mb-6 flex flex-col items-center gap-2 text-center">
          <span className="flex size-11 items-center justify-center rounded-lg bg-selo/10 text-selo">
            <ShieldCheck className="size-5" aria-hidden />
          </span>
          <h1 className="text-xl font-bold text-tinta">Matrícula de segurança</h1>
          <p className="text-sm text-tinta-media">
            Conta privilegiada (Titular ou Administrador) — exige o ticket que você recebeu fora do
            sistema, além da senha.
          </p>
        </div>

        {etapa === 'ticket' ? (
          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="email-matricula">E-mail</Label>
              <Input
                id="email-matricula"
                type="email"
                autoComplete="username"
                value={email}
                onChange={(evento) => setEmail(evento.target.value)}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="senha-matricula">Senha</Label>
              <Input
                id="senha-matricula"
                type="password"
                autoComplete="current-password"
                value={senha}
                onChange={(evento) => setSenha(evento.target.value)}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="ticket-matricula">Ticket recebido fora de banda</Label>
              <Input
                id="ticket-matricula"
                autoComplete="off"
                value={ticket}
                onChange={(evento) => setTicket(evento.target.value)}
                className="font-mono text-xs"
              />
            </div>

            {erro ? (
              <div className="flex items-start gap-2 rounded-lg border border-selo/30 bg-selo/[0.07] px-3 py-2.5">
                <TriangleAlert className="mt-0.5 size-4 shrink-0 text-selo" aria-hidden />
                <p role="alert" className="text-[13px] leading-relaxed text-selo">
                  {erro}
                </p>
              </div>
            ) : null}

            <Button
              type="button"
              disabled={enviando || !email || !senha || !ticket}
              onClick={() => void aoConfirmarTicket()}
              className="h-11"
            >
              {enviando ? <Loader2 className="animate-spin" aria-hidden /> : null}
              {enviando ? 'Validando…' : 'Continuar'}
            </Button>
          </div>
        ) : null}

        {etapa === 'autenticador' && iniciado ? (
          <div className="flex flex-col gap-4">
            <p className="text-sm text-tinta-media">
              Escaneie o QR Code com seu aplicativo autenticador.
            </p>

            <QrCodeDoSegredo otpauthUri={iniciado.otpauthUri} />

            <div className="flex flex-col gap-1">
              <Label>Código manual</Label>
              <code className="rounded-md border border-linha bg-superficie px-2 py-1.5 text-center text-sm tracking-widest">
                {iniciado.segredoBase32}
              </code>
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="codigo-matricula">Código de 6 dígitos gerado agora</Label>
              <Input
                id="codigo-matricula"
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

            <Button
              type="button"
              disabled={enviando || codigo.length < 6}
              onClick={() => void aoConfirmarCodigo()}
              className="h-11"
            >
              {enviando ? 'Confirmando…' : 'Confirmar e concluir'}
            </Button>
          </div>
        ) : null}

        {etapa === 'codigos' && codigosDeRecuperacao ? (
          <div className="flex flex-col gap-3">
            <p className="text-sm font-medium text-verde">Matrícula concluída.</p>
            <div className="flex flex-col gap-2 rounded-md border border-ambar/40 bg-ambar/[0.06] p-3">
              <p className="flex items-start gap-2 text-sm font-medium text-tinta">
                <TriangleAlert className="mt-0.5 size-4 shrink-0 text-ambar" aria-hidden />
                Guarde estes 10 códigos de recuperação — não serão mostrados de novo.
              </p>
              <div className="grid grid-cols-2 gap-1.5 font-mono text-xs">
                {codigosDeRecuperacao.map((codigoDeRecuperacao) => (
                  <code
                    key={codigoDeRecuperacao}
                    className="rounded border border-linha bg-branco px-2 py-1"
                  >
                    {codigoDeRecuperacao}
                  </code>
                ))}
              </div>
            </div>
            <Button
              type="button"
              onClick={() => {
                // Recarga completa (nao React Router): o contexto de
                // autenticacao so le o token do localStorage no MOUNT — sem
                // isto, /  acharia que ninguem esta logado e voltaria pro
                // login mesmo com o token ja gravado.
                window.location.assign('/')
              }}
              className="h-11"
            >
              <KeyRound className="size-4" aria-hidden />
              Já guardei — entrar no CRM
            </Button>
          </div>
        ) : null}
      </section>
    </main>
  )
}

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
