import { useState, type FormEvent } from 'react'
import { KeyRound, Loader2, ShieldAlert, TriangleAlert } from 'lucide-react'
import { entrar, ErroDaCentralDeIa, lerTokenDaCentral } from '@/lib/api/central-ia'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { SeletorDeTema } from '@/components/ui/seletor-de-tema'
import { PainelDeObservabilidade } from '@/features/central-ia/painel-de-observabilidade'

/**
 * Área privada do desenvolvedor da equipe do projeto — fora do sistema normal do
 * cartório. Rota pública e independente (`/central-ia`, `app/rotas.tsx`), sem
 * `Shell`, sem entrada em `navegacao.ts`: quem não tem o link não sabe que
 * esta tela existe.
 *
 * Diferente de `pagina-ia.tsx` (painel `/automacao/ia`, visível a
 * Supervisor/Administrador do cartório-cliente, que esconde provedor/modelo/
 * custo de propósito), esta tela é PARA o desenvolvedor — mostra nome de
 * provedor, custo em USD e em reais, e consolida TODOS os tenants lado a
 * lado. As duas telas nunca deveriam parecer a mesma coisa.
 */
export function PaginaCentralDeIa() {
  const [autenticado, setAutenticado] = useState(() => Boolean(lerTokenDaCentral()))

  return (
    <main className="min-h-dvh bg-papel px-4 py-6 sm:px-6 sm:py-10">
      <div className={autenticado ? 'mx-auto flex max-w-7xl flex-col gap-6' : 'mx-auto flex max-w-5xl flex-col gap-6'}>
        <header className="flex items-center gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-lg border border-linha-forte bg-branco text-tinta-media">
            <ShieldAlert className="size-4" aria-hidden />
          </span>
          <div className="min-w-0 flex-1">
            <h1 className="font-titulo text-xl font-bold text-tinta">Central de IA</h1>
            <p className="text-sm text-tinta-media">Área privada do desenvolvedor — fora do sistema do cartório.</p>
          </div>
          <SeletorDeTema />
        </header>

        {autenticado ? (
          <PainelDeObservabilidade aoPerderSessao={() => setAutenticado(false)} />
        ) : (
          <FormularioDeEntrada aoEntrar={() => setAutenticado(true)} />
        )}
      </div>
    </main>
  )
}

function FormularioDeEntrada({ aoEntrar }: { aoEntrar: () => void }) {
  const [segredo, setSegredo] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  async function aoEnviar(evento: FormEvent) {
    evento.preventDefault()
    setErro(null)
    setEnviando(true)

    try {
      await entrar(segredo)
      aoEntrar()
    } catch (falha) {
      setErro(falha instanceof ErroDaCentralDeIa ? falha.message : 'Não foi possível conectar ao servidor.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <section className="mx-auto max-w-sm rounded-card border border-linha bg-branco p-6 shadow-card">
      <div className="mb-5 flex flex-col gap-1">
        <h2 className="text-[15px] font-bold text-tinta">Entrar</h2>
        <p className="text-xs text-tinta-media">Segredo exclusivo da Central — não é a senha de nenhum atendente.</p>
      </div>

      <form onSubmit={aoEnviar} className="flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="segredo-central" className="text-[13px] font-semibold text-tinta">
            Segredo
          </Label>
          <div className="relative">
            <span aria-hidden className="pointer-events-none absolute inset-y-0 left-0 flex w-10 items-center justify-center text-tinta-fraca">
              <KeyRound className="size-4" />
            </span>
            <Input
              id="segredo-central"
              type="password"
              autoComplete="off"
              autoFocus
              required
              aria-invalid={erro ? true : undefined}
              aria-describedby={erro ? 'erro-central' : undefined}
              value={segredo}
              onChange={(evento) => {
                setSegredo(evento.target.value)
                setErro(null)
              }}
              className="pl-10"
            />
          </div>
        </div>

        {erro ? (
          <div className="flex items-start gap-2.5 rounded-lg border border-selo/30 bg-selo/[0.07] px-3 py-2.5">
            <TriangleAlert className="mt-0.5 size-4 shrink-0 text-selo" aria-hidden />
            <p id="erro-central" role="alert" className="text-[13px] leading-relaxed text-selo">
              {erro}
            </p>
          </div>
        ) : null}

        <Button type="submit" disabled={enviando || !segredo} className="h-10 w-full">
          {enviando ? <Loader2 className="animate-spin" aria-hidden="true" /> : null}
          {enviando ? 'Entrando…' : 'Entrar'}
        </Button>
      </form>
    </section>
  )
}
