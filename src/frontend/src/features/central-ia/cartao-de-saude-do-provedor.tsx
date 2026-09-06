import { Badge } from '@/components/ui/badge'
import { formatarDataHora, formatarNumero, formatarPercentual } from '@/lib/formatacao'
import type { SaudeDoProvedor } from '@/lib/api/central-ia'

const ROTULO_DO_CIRCUITO: Record<SaudeDoProvedor['estadoDoCircuito'], string> = {
  Fechado: 'Circuito fechado',
  Aberto: 'Circuito aberto',
  MeioAberto: 'Testando recuperação',
  SemChave: 'Sem chave configurada',
  NaoSeAplica: 'Sem disjuntor',
}

const BADGE_DO_CIRCUITO: Record<SaudeDoProvedor['estadoDoCircuito'], 'success' | 'warning' | 'outline'> = {
  Fechado: 'success',
  Aberto: 'warning',
  MeioAberto: 'warning',
  SemChave: 'outline',
  NaoSeAplica: 'outline',
}

/** Um provedor (Gemini ou Anthropic) na seção "Saúde agora" — só fatos observados, nunca percentual sobre limite digitado à mão. */
export function CartaoDeSaudeDoProvedor({ saude }: { saude: SaudeDoProvedor }) {
  return (
    <div className="flex flex-col gap-3 rounded-lg border border-linha bg-superficie p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <h3 className="text-sm font-bold text-tinta">{saude.provedor}</h3>
          {saude.modelo ? <span className="font-dado text-xs text-tinta-fraca">{saude.modelo}</span> : null}
        </div>
        <Badge variant={BADGE_DO_CIRCUITO[saude.estadoDoCircuito]}>{ROTULO_DO_CIRCUITO[saude.estadoDoCircuito]}</Badge>
      </div>

      {!saude.configurado ? (
        <p className="text-xs text-tinta-fraca">Não configurado — nenhuma chamada é feita a este provedor.</p>
      ) : (
        <>
          {saude.circuitoVoltaATentarEm ? (
            <p className="text-xs text-tinta-fraca">
              Nova tentativa: {formatarDataHora(saude.circuitoVoltaATentarEm)}
            </p>
          ) : null}

          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <Metrica rotulo="Requisições hoje" valor={formatarNumero(saude.requisicoesHoje)} />
            <Metrica
              rotulo="Taxa de sucesso hoje"
              valor={saude.taxaDeSucessoHoje === null ? '—' : formatarPercentual(saude.taxaDeSucessoHoje)}
              cor={saude.taxaDeSucessoHoje !== null && saude.taxaDeSucessoHoje < 90 ? 'ambar' : 'tinta'}
            />
            <Metrica
              rotulo="429 hoje"
              valor={formatarNumero(saude.total429Hoje)}
              cor={saude.total429Hoje > 0 ? 'ambar' : 'tinta'}
              detalhe={saude.ultimo429Em ? `último: ${formatarDataHora(saude.ultimo429Em)}` : undefined}
            />
            <Metrica
              rotulo="Latência média hoje"
              valor={saude.latenciaMediaMsHoje === null ? '—' : `${Math.round(saude.latenciaMediaMsHoje)}ms`}
            />
            <Metrica
              rotulo="Custo estimado hoje"
              valor={saude.custoEstimadoUsdHoje === null ? 'não medido' : `US$ ${saude.custoEstimadoUsdHoje.toFixed(4)}`}
            />
            <Metrica
              rotulo="Fallbacks originados hoje"
              valor={formatarNumero(saude.fallbacksOriginadosHoje)}
            />
          </div>

          {saude.ultimoErroEm ? (
            <p className="border-t border-linha pt-2.5 text-[11px] text-tinta-fraca">
              Último erro: {saude.ultimoErroTipo}
              {saude.ultimoErroCodigoHttp ? ` (HTTP ${saude.ultimoErroCodigoHttp})` : ''} — {formatarDataHora(saude.ultimoErroEm)}
            </p>
          ) : null}

          {saude.limiteRpdInformado !== null ? (
            <p className="text-[11px] text-tinta-fraca">
              Limite diário informado por você: {formatarNumero(saude.limiteRpdInformado)} requisições — não é leitura oficial do provedor.
            </p>
          ) : null}
        </>
      )}
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
  cor?: 'tinta' | 'ambar'
}) {
  const corDoValor = { tinta: 'text-tinta', ambar: 'text-ambar' }[cor]

  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-[10px] font-semibold uppercase tracking-wider text-tinta-fraca">{rotulo}</span>
      <span className={`font-dado text-base font-bold leading-none ${corDoValor}`}>{valor}</span>
      {detalhe ? <span className="text-[10px] text-tinta-fraca">{detalhe}</span> : null}
    </div>
  )
}
