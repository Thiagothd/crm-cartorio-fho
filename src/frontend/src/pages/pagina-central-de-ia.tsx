import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { KeyRound, Loader2, RefreshCw, ShieldAlert, TriangleAlert } from 'lucide-react'
import {
  entrar,
  ErroDaCentralDeIa,
  lerTokenDaCentral,
  situacao,
  type EstadoGlobalDoGemini,
  type SituacaoGlobalDaIa,
  type SituacaoPorTenant,
} from '@/lib/api/central-ia'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Painel } from '@/components/ui/painel'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { formatarNumero, formatarReais } from '@/lib/formatacao'

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
    <main className="flex min-h-dvh items-center justify-center bg-papel px-4 py-10">
      <div className="w-full max-w-5xl">
        <header className="mb-6 flex items-center gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-lg border border-linha-forte bg-branco text-tinta-media">
            <ShieldAlert className="size-4" aria-hidden />
          </span>
          <div>
            <h1 className="font-titulo text-xl font-bold text-tinta">Central de IA</h1>
            <p className="text-sm text-tinta-media">Área privada do desenvolvedor — fora do sistema do cartório.</p>
          </div>
        </header>

        {autenticado ? (
          <PainelDeSituacao aoPerderSessao={() => setAutenticado(false)} />
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

const ROTULO_DO_STATUS_DO_GEMINI: Record<EstadoGlobalDoGemini['status'], string> = {
  Disponivel: 'Disponível',
  Contingencia: 'Em contingência',
  SemChave: 'Sem chave configurada',
}

const BADGE_DO_STATUS_DO_GEMINI: Record<EstadoGlobalDoGemini['status'], 'success' | 'warning' | 'outline'> = {
  Disponivel: 'success',
  Contingencia: 'warning',
  SemChave: 'outline',
}

function PainelDeSituacao({ aoPerderSessao }: { aoPerderSessao: () => void }) {
  const [dados, setDados] = useState<SituacaoGlobalDaIa | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  const buscar = useCallback(async () => {
    setCarregando(true)
    setErro(null)

    try {
      const resposta = await situacao()
      setDados(resposta)
    } catch (falha) {
      if (falha instanceof ErroDaCentralDeIa && falha.naoAutenticado) {
        aoPerderSessao()
        return
      }

      setErro(falha instanceof ErroDaCentralDeIa ? falha.message : 'Não foi possível conectar ao servidor.')
    } finally {
      setCarregando(false)
    }
  }, [aoPerderSessao])

  useEffect(() => {
    void buscar()
  }, [buscar])

  if (carregando) {
    return <EstadoDaInterface tipo="carregando" titulo="Carregando a situação da IA" />
  }

  if (erro || !dados) {
    return (
      <EstadoDaInterface
        tipo="erro"
        descricao={erro ?? 'Não foi possível carregar a situação da IA.'}
        acao={
          <Button size="sm" onClick={() => void buscar()}>
            <RefreshCw />
            Tentar de novo
          </Button>
        }
      />
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <Painel titulo="Disjuntor do Gemini" descricao="Global ao processo — não varia por tenant.">
        <div className="flex flex-wrap items-center gap-3">
          <Badge variant={BADGE_DO_STATUS_DO_GEMINI[dados.gemini.status]}>
            {ROTULO_DO_STATUS_DO_GEMINI[dados.gemini.status]}
          </Badge>
          {dados.gemini.motivo ? <p className="text-xs text-tinta-fraca">{dados.gemini.motivo}</p> : null}
          {dados.gemini.voltaATentarEm ? (
            <p className="text-xs text-tinta-fraca">
              Nova tentativa: {new Date(dados.gemini.voltaATentarEm).toLocaleString('pt-BR')}
            </p>
          ) : null}
        </div>
      </Painel>

      <Painel
        titulo="Por tenant"
        descricao={`Período: ${new Date(dados.periodoDe).toLocaleString('pt-BR')} a ${new Date(dados.periodoAte).toLocaleString('pt-BR')}`}
        acao={
          <Button variant="ghost" size="icon-sm" onClick={() => void buscar()} aria-label="Atualizar">
            <RefreshCw className="size-3.5" />
          </Button>
        }
      >
        {dados.porTenant.length === 0 ? (
          <p className="text-sm text-tinta-media">Nenhum tenant com chamada de IA registrada no período.</p>
        ) : (
          <div className="flex flex-col gap-3">
            {dados.porTenant.map((item) => (
              <CartaoDeTenant key={item.tenantId} item={item} />
            ))}
          </div>
        )}
      </Painel>
    </div>
  )
}

function CartaoDeTenant({ item }: { item: SituacaoPorTenant }) {
  const motivos = item.motivosDeFallback

  return (
    <div className="rounded-lg border border-linha bg-superficie p-4">
      <div className="mb-3 flex flex-wrap items-baseline justify-between gap-2">
        <h3 className="text-sm font-bold text-tinta">{item.nomeTenant}</h3>
        <span className="font-dado text-xs text-tinta-fraca">{item.tenantId}</span>
      </div>

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-6">
        <Metrica rotulo="Total" valor={formatarNumero(item.totalDeChamadas)} />
        <Metrica rotulo="Sucessos" valor={formatarNumero(item.sucessos)} cor="verde" />
        <Metrica rotulo="Falhas" valor={formatarNumero(item.falhas)} cor={item.falhas > 0 ? 'ambar' : 'tinta'} />
        <Metrica rotulo="Pelo Gemini" valor={formatarNumero(item.atendidasPeloGemini)} />
        <Metrica rotulo="Pelo Haiku" valor={formatarNumero(item.atendidasPeloHaiku)} />
        <Metrica rotulo="Custo estimado" valor={formatarReais(item.custoEstimadoReais)} detalhe={`US$ ${item.custoEstimadoUsd.toFixed(4)}`} />
      </div>

      {motivos.quota + motivos.rateLimit + motivos.indisponibilidade + motivos.configuracao + motivos.outros > 0 ? (
        <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1 border-t border-linha pt-3 text-[11px] text-tinta-media">
          <span>Motivos de fallback:</span>
          {motivos.quota > 0 ? <span>Quota {motivos.quota}</span> : null}
          {motivos.rateLimit > 0 ? <span>Rate limit {motivos.rateLimit}</span> : null}
          {motivos.indisponibilidade > 0 ? <span>Indisponibilidade {motivos.indisponibilidade}</span> : null}
          {motivos.configuracao > 0 ? <span>Configuração {motivos.configuracao}</span> : null}
          {motivos.outros > 0 ? <span>Outros {motivos.outros}</span> : null}
        </div>
      ) : null}
    </div>
  )
}

function Metrica({
  rotulo,
  valor,
  detalhe,
  cor = 'tinta',
}: {
  rotulo: string
  valor: string
  detalhe?: string
  cor?: 'tinta' | 'verde' | 'ambar'
}) {
  const corDoValor = { tinta: 'text-tinta', verde: 'text-verde', ambar: 'text-ambar' }[cor]

  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-[10px] font-semibold uppercase tracking-wider text-tinta-fraca">{rotulo}</span>
      <span className={`font-dado text-base font-bold leading-none ${corDoValor}`}>{valor}</span>
      {detalhe ? <span className="text-[10px] text-tinta-fraca">{detalhe}</span> : null}
    </div>
  )
}
