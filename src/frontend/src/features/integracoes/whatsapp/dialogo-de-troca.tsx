import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { whatsappApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import type { SituacaoDoCanal } from '@/lib/api/tipos'

/**
 * Confirma a troca, encerra a sessão atual e mostra o QR do número novo — tudo
 * sem sair da tela. `situacao` e `qrCode` vêm de `PainelDeConexao`, que já
 * mantém a consulta viva com o `refetchInterval` de praxe e guarda o QR fora
 * do cache (o GET de situação é passivo e nunca devolve QR — só o POST de
 * troca devolve, por isso o valor precisa ser repassado, não relido daqui).
 * Este diálogo nunca abre uma segunda consulta em paralelo.
 */
export function DialogoDeTroca({
  situacao,
  qrCode,
  aberto,
  aoFechar,
  aoConcluir,
}: {
  situacao: SituacaoDoCanal | undefined
  qrCode: string | null
  aberto: boolean
  aoFechar: () => void
  aoConcluir: () => void
}) {
  const [passo, setPasso] = useState<'confirmar' | 'qr'>('confirmar')
  const [trocando, setTrocando] = useState(false)

  const clienteDeConsultas = useQueryClient()

  useEffect(() => {
    if (!aberto) {
      setPasso('confirmar')
    }
  }, [aberto])

  // Fecha sozinho assim que o número novo termina de parear — a mesma reação
  // que a tela de conexão já tem para o primeiro pareamento.
  useEffect(() => {
    if (passo !== 'qr' || !situacao?.conectado) return

    toast.success('Número trocado e conectado.')
    aoConcluir()
    aoFechar()
  }, [passo, situacao?.conectado, aoConcluir, aoFechar])

  async function confirmarTroca() {
    setTrocando(true)

    try {
      const nova = await whatsappApi.trocarNumero()
      clienteDeConsultas.setQueryData(['whatsapp-situacao'], nova)
      setPasso('qr')
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível trocar o número.')
    } finally {
      setTrocando(false)
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent>
        {passo === 'confirmar' ? (
          <>
            <DialogHeader>
              <DialogTitle>Trocar o número conectado?</DialogTitle>
              <DialogDescription>
                A sessão atual é encerrada e nenhuma mensagem entra ou sai até o número novo ser
                pareado. O <strong className="text-tinta">histórico das conversas já registradas
                continua intacto</strong> — nada é apagado.
              </DialogDescription>
            </DialogHeader>

            <DialogFooter>
              <Button variant="outline" onClick={aoFechar} disabled={trocando}>
                Cancelar
              </Button>
              <Button onClick={() => void confirmarTroca()} disabled={trocando}>
                {trocando ? <Loader2 className="animate-spin" /> : null}
                Confirmar troca
              </Button>
            </DialogFooter>
          </>
        ) : (
          <>
            <DialogHeader>
              <DialogTitle>Leia o código com o número novo</DialogTitle>
              <DialogDescription>
                No celular: <strong className="text-tinta">WhatsApp → Dispositivos conectados →
                Conectar um dispositivo</strong>. Esta janela fecha sozinha assim que o pareamento for
                reconhecido.
              </DialogDescription>
            </DialogHeader>

            <div className="flex flex-col items-center gap-3 py-2">
              {qrCode ? (
                <img
                  src={qrCode}
                  alt="Código para conectar o número novo"
                  className="size-56 rounded-md border border-linha bg-white"
                />
              ) : (
                <Loader2 className="size-8 animate-spin text-tinta-fraca" />
              )}
            </div>

            <DialogFooter>
              <Button variant="outline" onClick={aoFechar}>
                Fechar
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}
