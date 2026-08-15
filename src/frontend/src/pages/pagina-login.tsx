import { useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { Building2, Loader2 } from 'lucide-react'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

export function PaginaLogin() {
  const { atendente, carregando, entrar } = usarAutenticacao()
  const navegar = useNavigate()

  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  if (!carregando && atendente) {
    return <Navigate to="/" replace />
  }

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault()
    setErro(null)
    setEnviando(true)

    try {
      await entrar(email, senha)
      navegar('/', { replace: true })
    } catch (falha) {
      setErro(
        falha instanceof ErroDaApi
          ? falha.message
          : 'Não foi possível conectar ao servidor. Verifique se a API está no ar.',
      )
    } finally {
      setEnviando(false)
    }
  }

  return (
    <div className="flex min-h-dvh items-center justify-center bg-superficie p-4">
      <div className="w-full max-w-sm">
        <div className="mb-6 flex flex-col items-center gap-3 text-center">
          <div className="flex size-11 items-center justify-center rounded-md border border-linha-forte bg-branco text-tinta-media">
            <Building2 className="size-5" />
          </div>
          <div>
            <h1 className="font-titulo text-xl text-tinta">Cartório de Gameleiras</h1>
            <p className="mt-0.5 text-[11px] uppercase tracking-wider text-tinta-fraca">
              Atendimento via WhatsApp
            </p>
          </div>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Entrar</CardTitle>
            <CardDescription>Use o e-mail cadastrado na serventia.</CardDescription>
          </CardHeader>

          <CardContent>
            <form onSubmit={aoEnviar} className="flex flex-col gap-4">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="email">E-mail</Label>
                <Input
                  id="email"
                  type="email"
                  autoComplete="username"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="nome@cartorio.com.br"
                />
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="senha">Senha</Label>
                <Input
                  id="senha"
                  type="password"
                  autoComplete="current-password"
                  required
                  value={senha}
                  onChange={(e) => setSenha(e.target.value)}
                />
              </div>

              {erro ? (
                <p
                  role="alert"
                  className="rounded-md bg-destructive/10 px-3 py-2 text-sm text-destructive"
                >
                  {erro}
                </p>
              ) : null}

              <Button type="submit" disabled={enviando} className="mt-1">
                {enviando ? <Loader2 className="animate-spin" /> : null}
                Entrar
              </Button>
            </form>
          </CardContent>
        </Card>

        <p className="mt-4 text-center text-xs text-tinta-media">
          Fase 1 — dados de demonstração, sem WhatsApp real.
        </p>
      </div>
    </div>
  )
}
