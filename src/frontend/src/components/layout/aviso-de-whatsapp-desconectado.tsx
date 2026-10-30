import { Link } from 'react-router-dom'
import { WifiOff } from 'lucide-react'
import { usarAlertasOperacionais } from '@/features/indicadores/consultas'

/**
 * Aviso fixo no cabeçalho enquanto o WhatsApp do cartório está desconectado.
 *
 * Existe porque o alerta só morava dentro do sino: numa queda de 17 horas
 * (2026-10-29) ninguém abriu o sino e as mensagens do painel ficaram sem sair.
 * Aqui ele aparece para TODO atendente, com a tela do canal como destino para
 * quem pode reconectar.
 *
 * Âmbar, nunca bordô: o bordô é do atraso de prazo e tem teto de 3 por tela
 * (DESIGN.md 2.1). No cabeçalho, para não custar altura ao fio de mensagens
 * (DESIGN.md 16.1).
 */
export function AvisoDeWhatsappDesconectado({ habilitado }: { habilitado: boolean }) {
  const { data: alertas } = usarAlertasOperacionais(habilitado)

  const alerta = (alertas ?? []).find((a) => a.tipo === 'whatsapp_desconectado')

  if (!alerta) {
    return null
  }

  return (
    <Link
      to={alerta.rota}
      title={alerta.descricao ?? undefined}
      className="flex h-9 shrink-0 items-center gap-1.5 rounded-lg border border-ambar/45 bg-ambar/8 px-2.5 text-xs font-medium text-ambar transition-colors hover:bg-ambar/15"
    >
      <WifiOff className="size-3.5 shrink-0" aria-hidden />
      <span className="hidden @min-[48rem]/conteudo:inline">WhatsApp desconectado</span>
      <span className="sr-only @min-[48rem]/conteudo:hidden">WhatsApp desconectado. {alerta.descricao}</span>
    </Link>
  )
}
