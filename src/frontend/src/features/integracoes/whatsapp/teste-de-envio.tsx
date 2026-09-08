import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Check, Loader2, Send, TriangleAlert } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Painel } from '@/components/ui/painel'
import { whatsappApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { mascararTelefone, somenteDigitos } from '@/lib/mascaras'
import { cn } from '@/lib/utils'

/**
 * Manda um texto fixo a um número e mostra se saiu — sem abrir conversa nem
 * protocolo no painel. Prova que o canal funciona sem precisar de outro
 * celular na mão.
 */
export function TesteDeEnvio() {
  const [telefone, setTelefone] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [resultado, setResultado] = useState<{ enviado: boolean; motivo: string | null } | null>(null)

  const clienteDeConsultas = useQueryClient()

  async function enviar() {
    if (!telefone.trim() || enviando) return

    setEnviando(true)
    setResultado(null)

    try {
      const resposta = await whatsappApi.testarEnvio(somenteDigitos(telefone))
      setResultado(resposta)

      if (!resposta.enviado) {
        void clienteDeConsultas.invalidateQueries({ queryKey: ['whatsapp-descartes'] })
      }
    } catch (falha) {
      setResultado({
        enviado: false,
        motivo: falha instanceof ErroDaApi ? falha.message : 'Não foi possível falar com o WhatsApp agora.',
      })
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Painel
      titulo="Testar envio"
      descricao="Manda uma mensagem de teste para um número, sem criar conversa nem protocolo — só para confirmar que o canal está funcionando."
    >
      <div className="flex flex-col gap-1.5 sm:max-w-xs">
        <Label htmlFor="teste-telefone">Telefone</Label>
        <div className="flex gap-2">
          <Input
            id="teste-telefone"
            value={telefone}
            onChange={(e) => {
              setTelefone(mascararTelefone(e.target.value))
              setResultado(null)
            }}
            placeholder="(38) 99999-0000"
            inputMode="numeric"
            className="font-dado"
          />
          <Button
            variant="outline"
            onClick={() => void enviar()}
            disabled={!telefone.trim() || enviando}
          >
            {enviando ? <Loader2 className="animate-spin" /> : <Send />}
            Enviar
          </Button>
        </div>
      </div>

      {resultado ? (
        <p
          role="status"
          className={cn(
            'mt-3 flex items-start gap-2 text-sm',
            resultado.enviado ? 'text-verde' : 'text-ambar',
          )}
        >
          {resultado.enviado ? (
            <Check className="mt-0.5 size-4 shrink-0" />
          ) : (
            <TriangleAlert className="mt-0.5 size-4 shrink-0" />
          )}
          {resultado.enviado
            ? 'Mensagem enviada. Confira no celular do número testado.'
            : (resultado.motivo ?? 'Não foi possível enviar.')}
        </p>
      ) : null}
    </Painel>
  )
}
