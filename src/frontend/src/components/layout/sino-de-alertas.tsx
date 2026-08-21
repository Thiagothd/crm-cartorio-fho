import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Bell, Clock, Gauge, TriangleAlert, UserX, WifiOff } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { DropdownMenu, DropdownMenuContent, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { usarAlertasOperacionais } from '@/features/indicadores/consultas'
import { formatarMomentoRelativo } from '@/lib/formatacao'
import { cn } from '@/lib/utils'

const CHAVE_ULTIMA_ABERTURA = 'crm-cartorio.alertas-vistos'

const ICONE_POR_TIPO: Record<string, LucideIcon> = {
  atrasada: TriangleAlert,
  prazo_proximo: Clock,
  fila_sem_responsavel: UserX,
  whatsapp_desconectado: WifiOff,
  ia_limite_atingido: Gauge,
}

function lerUltimaAbertura(): number {
  try {
    const valor = localStorage.getItem(CHAVE_ULTIMA_ABERTURA)
    return valor ? Number(valor) : 0
  } catch {
    return 0
  }
}

function gravarUltimaAbertura(instante: number) {
  try {
    localStorage.setItem(CHAVE_ULTIMA_ABERTURA, String(instante))
  } catch {
    // Preferência de tela, não dado — não gravar não pode quebrar o sino.
  }
}

/**
 * "Alertas operacionais", não "notificações": não existe leitura persistida
 * por atendente no servidor (exigiria schema novo). O que existe é "novos
 * desde a última vez que ESTE navegador abriu o painel" — e a própria tela diz
 * isso, para não fingir um recurso que não tem.
 */
export function SinoDeAlertas({ habilitado }: { habilitado: boolean }) {
  const { data: alertas } = usarAlertasOperacionais(habilitado)
  const [ultimaAbertura, setUltimaAbertura] = useState(lerUltimaAbertura)
  const [aberto, setAberto] = useState(false)

  const novos = (alertas ?? []).filter((a) => new Date(a.quando).getTime() > ultimaAbertura).length

  function aoMudarAbertura(abrindo: boolean) {
    setAberto(abrindo)

    if (abrindo) {
      const agora = Date.now()

      // Atraso curto para o badge não zerar antes do atendente ver o que era
      // novo — a marca acontece depois que o painel já mostrou a contagem.
      window.setTimeout(() => {
        setUltimaAbertura(agora)
        gravarUltimaAbertura(agora)
      }, 500)
    }
  }

  return (
    <DropdownMenu open={aberto} onOpenChange={aoMudarAbertura}>
      <DropdownMenuTrigger asChild>
        {/* `size-11` = 44px de alvo no celular (DESIGN.md 12.2), caindo para
            36px a partir de `md`. Estava 36px em toda largura. */}
        <button
          type="button"
          aria-label={novos > 0 ? `Alertas operacionais — ${novos} novo${novos > 1 ? 's' : ''}` : 'Alertas operacionais'}
          className="relative flex size-11 touch-manipulation items-center justify-center rounded-lg border border-transparent text-tinta-media transition-colors hover:border-linha-forte hover:bg-superficie hover:text-tinta md:size-9"
        >
          <Bell className="size-4" aria-hidden />
          {novos > 0 ? (
            // Neutro de propósito, nao bg-selo: o sino aparece em toda tela, e
            // um alerta critico dentro dele (ex.: WhatsApp desconectado) ja
            // soma ao orcamento de 3 bordos por tela (DESIGN.md 2.1) — a
            // severidade fica so dentro do painel aberto, por icone, nunca
            // no badge que fica sempre visivel no cabecalho.
            // A posicao acompanha o tamanho do botao: com o alvo de 44px do
            // celular, `right-1 top-1` jogava o badge no canto e ele parecia
            // solto do sino em vez de grudado nele.
            <span
              aria-hidden
              className="absolute right-2 top-2 flex size-4 items-center justify-center rounded-full bg-tinta text-[9px] font-medium text-branco md:right-1 md:top-1"
            >
              {novos > 9 ? '9+' : novos}
            </span>
          ) : null}
        </button>
      </DropdownMenuTrigger>

      <DropdownMenuContent align="end" className="w-80 p-0">
        <p className="border-b border-linha px-3 py-2 text-xs font-semibold text-tinta">Alertas operacionais</p>

        <div className="max-h-[70dvh] overflow-y-auto p-1.5">
          {!alertas || alertas.length === 0 ? (
            <p className="p-4 text-center text-sm text-tinta-fraca">Nenhum alerta agora.</p>
          ) : (
            alertas.map((alerta) => {
              const Icone = ICONE_POR_TIPO[alerta.tipo] ?? TriangleAlert
              const ehNovo = new Date(alerta.quando).getTime() > ultimaAbertura

              return (
                <Link
                  key={alerta.id}
                  to={alerta.rota}
                  onClick={() => setAberto(false)}
                  className="flex items-start gap-2.5 rounded-lg px-2 py-2 text-sm transition-colors hover:bg-superficie"
                >
                  <Icone
                    className={cn(
                      'mt-0.5 size-4 shrink-0',
                      alerta.severidade === 'Critica'
                        ? 'text-selo'
                        : alerta.severidade === 'Alerta'
                          ? 'text-ambar'
                          : 'text-tinta-media',
                    )}
                    aria-hidden
                  />
                  <span className="min-w-0 flex-1">
                    <span className="flex items-center gap-1.5">
                      <span className="truncate text-tinta">{alerta.titulo}</span>
                      {/* Neutro: marca "novo", nao "critico" — a severidade ja
                          esta no icone a esquerda. */}
                      {ehNovo ? <span aria-hidden className="size-1.5 shrink-0 rounded-full bg-tinta" /> : null}
                    </span>
                    {alerta.descricao ? (
                      <span className="block truncate text-xs text-tinta-media">{alerta.descricao}</span>
                    ) : null}
                    <span className="block text-[11px] text-tinta-fraca">{formatarMomentoRelativo(alerta.quando)}</span>
                  </span>
                </Link>
              )
            })
          )}
        </div>

        <p className="border-t border-linha px-3 py-1.5 text-[10px] text-tinta-fraca">
          Alertas ativos agora — o ponto marca o que é novo neste navegador, não um sistema de leitura por usuário.
        </p>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
