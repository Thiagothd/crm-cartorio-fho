import { useState } from 'react'
import { Bot, BotOff, Loader2 } from 'lucide-react'
import { toast } from 'sonner'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { setoresApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { cn } from '@/lib/utils'
import type { ModoDoRoboNoSetor, Setor } from '@/lib/api/tipos'
import { usarInvalidarEstrutura } from './consultas-de-estrutura'
import { EscolhaDoModoDoRobo, MODOS_DO_ROBO } from './modos-do-robo'

/**
 * Liga e desliga do robô direto na linha do setor, sem abrir o cadastro — pedido do
 * Thiago em 2026-09-30, para cortar o robô de um setor "na hora".
 *
 * Escolher já salva: é um interruptor, não um formulário. O resto do cadastro vai
 * junto do jeito que está, porque a API de setor salva o registro inteiro.
 */
export function BotaoDoRoboNoSetor({ setor }: { setor: Setor }) {
  const { pode } = usarAutenticacao()
  const invalidar = usarInvalidarEstrutura()
  const [aberto, setAberto] = useState(false)
  const [salvando, setSalvando] = useState(false)

  const podeAlterar = pode('setores.editar')
  const ligado = setor.modoDoRobo === 'Ligado'
  const rotulo = ligado ? 'Robô' : (MODOS_DO_ROBO.find((m) => m.valor === setor.modoDoRobo)?.titulo ?? 'Robô')

  async function escolher(modo: ModoDoRoboNoSetor) {
    if (modo === setor.modoDoRobo) {
      setAberto(false)
      return
    }

    setSalvando(true)
    try {
      await setoresApi.atualizar(setor.id, {
        nome: setor.nome,
        descricao: setor.descricao,
        ordem: setor.ordem,
        ativo: setor.ativo,
        modoDoRobo: modo,
      })
      invalidar('setores')
      toast.success(`${setor.nome}: ${MODOS_DO_ROBO.find((m) => m.valor === modo)?.titulo.toLowerCase()}.`)
      setAberto(false)
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar. Tente novamente.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Popover open={aberto} onOpenChange={(v) => !salvando && setAberto(v)}>
      <PopoverTrigger asChild>
        <button
          type="button"
          aria-label={`Robô no setor ${setor.nome}: ${rotulo}. Mudar.`}
          title="Robô do WhatsApp neste setor"
          className={cn(
            'inline-flex h-10 shrink-0 items-center gap-1.5 rounded-full border px-3 text-xs font-medium transition-colors duration-[var(--duracao-rapida)] md:h-8',
            ligado
              ? 'border-linha text-tinta-media hover:border-linha-forte hover:bg-superficie hover:text-tinta'
              : 'border-ambar/40 bg-ambar/[0.06] text-ambar hover:bg-ambar/10',
          )}
        >
          {ligado ? <Bot className="size-3.5" aria-hidden /> : <BotOff className="size-3.5" aria-hidden />}
          {rotulo}
        </button>
      </PopoverTrigger>

      <PopoverContent className="w-[22rem] p-0">
        <div className="flex items-start justify-between gap-3 border-b border-linha px-4 py-3">
          <div className="min-w-0">
            <p className="text-sm font-semibold text-tinta">Robô no setor {setor.nome}</p>
            <p className="mt-0.5 text-xs leading-relaxed text-tinta-media">
              Vale na próxima mensagem de cada cliente.
            </p>
          </div>
          {salvando ? <Loader2 className="mt-0.5 size-4 shrink-0 animate-spin text-tinta-fraca" aria-hidden /> : null}
        </div>

        <div className="px-4 py-3">
          <EscolhaDoModoDoRobo
            valor={setor.modoDoRobo}
            aoEscolher={(modo) => void escolher(modo)}
            desabilitado={!podeAlterar || salvando}
            rotulo={`Robô no setor ${setor.nome}`}
          />
          {!podeAlterar ? (
            <p className="mt-2 text-[11px] text-tinta-fraca">Só quem gerencia os setores pode mudar.</p>
          ) : null}
        </div>
      </PopoverContent>
    </Popover>
  )
}
