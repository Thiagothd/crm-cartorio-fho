import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Loader2, QrCode, TriangleAlert } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { whatsappApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { cn } from '@/lib/utils'

/**
 * Enquanto o QR está na tela, o estado muda no gateway sem avisar ninguém — o
 * pareamento acontece no celular. Cinco segundos é curto o bastante para a tela
 * reagir logo depois da leitura e longo o bastante para não martelar o gateway.
 */
const INTERVALO_DE_ESPERA = 5_000

export function AbaWhatsApp() {
  const [preparando, setPreparando] = useState(false)
  const clienteDeConsultas = useQueryClient()

  const { data, isLoading, isError } = useQuery({
    queryKey: ['whatsapp-situacao'],
    queryFn: () => whatsappApi.situacao(),

    // Só fica perguntando enquanto há um QR esperando leitura. Pareado ou
    // desligado, a tela é estática e a consulta repetida seria ruído.
    refetchInterval: (consulta) =>
      consulta.state.data?.qrCodeBase64 ? INTERVALO_DE_ESPERA : false,
  })

  async function parear() {
    setPreparando(true)

    try {
      const situacao = await whatsappApi.parear()
      clienteDeConsultas.setQueryData(['whatsapp-situacao'], situacao)

      toast.success(
        situacao.conectado
          ? 'Canal já estava conectado.'
          : 'Leia o código com o celular para conectar.',
      )
    } catch (falha) {
      toast.error(
        falha instanceof ErroDaApi ? falha.message : 'Não foi possível falar com o gateway.',
      )
    } finally {
      setPreparando(false)
    }
  }

  return (
    <div className="flex max-w-2xl flex-col gap-4">
      {/* O aviso de piloto vem ANTES de qualquer controle, e não como nota de
          rodapé: é a informação que muda o que a pessoa deve fazer aqui. */}
      <div className="rounded-md border border-ambar/40 bg-superficie p-4">
        <p className="flex items-start gap-2 text-sm font-medium text-tinta">
          <TriangleAlert className="mt-0.5 size-4 shrink-0 text-ambar" />
          Piloto de demonstração
        </p>
        <p className="mt-1.5 pl-6 text-sm leading-relaxed text-tinta-media">
          Este canal usa uma conexão não oficial, para demonstrar o produto sem esperar a
          verificação da conta Meta Business. Conecte o{' '}
          <strong className="text-tinta">número pessoal do desenvolvedor</strong>, nunca o número
          público da serventia: se o número for bloqueado, quem para é a demonstração, não o
          atendimento do cartório.
        </p>
      </div>

      {isLoading ? (
        <Skeleton className="h-48 w-full" />
      ) : isError ? (
        <Estado
          tom="aviso"
          titulo="Não foi possível consultar o canal"
          descricao="A API respondeu com erro. Confira se o contêiner do gateway está no ar."
        />
      ) : !data?.habilitado ? (
        <Estado
          tom="neutro"
          titulo="Canal desligado"
          descricao="Ligue em appsettings.Development.json, seção CanalWhatsApp, e reinicie a API. O passo a passo está em docs/DEPLOY-WHATSAPP.md."
        />
      ) : data.conectado ? (
        <Estado
          tom="ok"
          titulo="Conectado"
          descricao={
            data.instancia
              ? `Instância ${data.instancia}. As mensagens entram e saem por este número.`
              : 'As mensagens entram e saem por este número.'
          }
        />
      ) : (
        <div className="flex flex-col items-center gap-3 rounded-md border border-linha bg-branco p-6">
          {data.qrCodeBase64 ? (
            <>
              {/* Fundo branco explícito e fixo: o leitor do celular precisa de
                  contraste alto, e a textura de papel por trás atrapalharia. */}
              <img
                src={data.qrCodeBase64}
                alt="Código para conectar o WhatsApp"
                className="size-56 rounded-md border border-linha bg-white"
              />
              <p className="text-center text-sm text-tinta-media">
                No celular: <strong className="text-tinta">WhatsApp → Dispositivos conectados
                → Conectar um dispositivo</strong>.
              </p>
              <p className="text-xs text-tinta-fraca">
                A tela se atualiza sozinha assim que a leitura for reconhecida.
              </p>
            </>
          ) : (
            <>
              <QrCode className="size-10 text-tinta-fraca" />
              <p className="text-center text-sm text-tinta-media">
                O canal está ligado, mas nenhum número está pareado.
              </p>
            </>
          )}

          <Button onClick={() => void parear()} disabled={preparando}>
            {preparando ? <Loader2 className="animate-spin" /> : null}
            {data.qrCodeBase64 ? 'Gerar outro código' : 'Conectar número'}
          </Button>
        </div>
      )}
    </div>
  )
}

/**
 * Estado do canal em uma linha. Ícone junto do texto porque cor nunca é o único
 * sinal (DESIGN.md, 10.4). Sem bordô: nada aqui é falha de prazo legal.
 */
function Estado({
  tom,
  titulo,
  descricao,
}: {
  tom: 'ok' | 'aviso' | 'neutro'
  titulo: string
  descricao: string
}) {
  const Icone = tom === 'ok' ? Check : tom === 'aviso' ? TriangleAlert : QrCode

  return (
    <div className="flex items-start gap-2.5 rounded-md border border-linha bg-branco p-4">
      <Icone
        className={cn(
          'mt-0.5 size-4 shrink-0',
          tom === 'ok' ? 'text-verde' : tom === 'aviso' ? 'text-ambar' : 'text-tinta-fraca',
        )}
      />
      <div className="flex flex-col gap-1">
        <p className="text-sm font-medium text-tinta">{titulo}</p>
        <p className="text-sm leading-relaxed text-tinta-media">{descricao}</p>
      </div>
    </div>
  )
}
