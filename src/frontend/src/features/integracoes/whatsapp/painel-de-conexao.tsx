import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Loader2, LogOut, QrCode, RefreshCw } from 'lucide-react'
import { toast } from 'sonner'
import { Avatar } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Skeleton } from '@/components/ui/skeleton'
import { whatsappApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { formatarDataHora, formatarTelefone } from '@/lib/formatacao'
import { DialogoDeTroca } from './dialogo-de-troca'

/**
 * Enquanto o QR está na tela, o estado muda no gateway sem avisar ninguém — o
 * pareamento acontece no celular. Cinco segundos é curto o bastante para a tela
 * reagir logo depois da leitura e longo o bastante para não martelar o gateway.
 */
const INTERVALO_DE_ESPERA = 5_000

/**
 * Conexão do canal: número pareado (ou QR para parear), trocar e desconectar.
 *
 * É o único bloco da tela de integração que fala com `whatsapp-situacao` — os
 * outros blocos (regras, teste, histórico) têm sua própria fonte de dados.
 */
export function PainelDeConexao() {
  const [preparando, setPreparando] = useState(false)
  const [desconectando, setDesconectando] = useState(false)
  const [trocaAberta, setTrocaAberta] = useState(false)
  const [segundosDeEspera, setSegundosDeEspera] = useState(0)

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['whatsapp-situacao'],
    queryFn: () => whatsappApi.situacao(),

    // Só fica perguntando enquanto há um QR esperando leitura. Pareado ou
    // desligado, a tela é estática e a consulta repetida seria ruído.
    refetchInterval: (consulta) =>
      consulta.state.data?.qrCodeBase64 ? INTERVALO_DE_ESPERA : false,
  })

  // Contagem regressiva do bloqueio anti-tentativa-repetida (backend, 30s).
  useEffect(() => {
    if (segundosDeEspera <= 0) return

    const id = setInterval(() => setSegundosDeEspera((s) => Math.max(0, s - 1)), 1000)

    return () => clearInterval(id)
  }, [segundosDeEspera])

  function tratarErroDePareamento(falha: unknown) {
    if (falha instanceof ErroDaApi) {
      if (falha.codigo === 'pareamento_muito_recente') {
        setSegundosDeEspera(Number(falha.detalhes.segundosRestantes ?? 0))
      }

      toast.error(falha.message)
    } else {
      toast.error('Não foi possível falar com o WhatsApp agora.')
    }
  }

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
      tratarErroDePareamento(falha)
    } finally {
      setPreparando(false)
    }
  }

  async function desconectar() {
    setDesconectando(true)

    try {
      const situacao = await whatsappApi.desconectar()
      clienteDeConsultas.setQueryData(['whatsapp-situacao'], situacao)
      void clienteDeConsultas.invalidateQueries({ queryKey: ['whatsapp-conexoes'] })

      toast.success('Canal desconectado.')
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível desconectar.')
    } finally {
      setDesconectando(false)
    }
  }

  return (
    <>
      {isLoading ? (
        <Skeleton className="h-48 w-full" />
      ) : isError ? (
        <EstadoDaInterface
          tipo="erro"
          titulo="Não foi possível consultar o canal"
          descricao="Tente novamente em instantes. Se continuar assim, informe o suporte técnico."
          acao={
            <Button size="sm" variant="outline" onClick={() => void refetch()}>
              <RefreshCw />
              Tentar de novo
            </Button>
          }
        />
      ) : !data?.habilitado ? (
        <EstadoDaInterface
          tipo="vazio"
          icone={QrCode}
          titulo="Canal não configurado neste servidor"
          descricao="Fale com o suporte técnico para habilitar o canal de WhatsApp."
        />
      ) : data.conectado ? (
        <div className="flex flex-col gap-4 rounded-md border border-linha bg-branco p-4">
          <div className="flex items-center gap-3">
            <Avatar
              nome={data.perfilNome ?? data.numero ?? 'WhatsApp'}
              fotoUrl={data.perfilFotoUrl}
              tamanho="lg"
            />

            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-1.5">
                <p className="truncate font-titulo text-base text-tinta">
                  {data.perfilNome ?? 'Número conectado'}
                </p>
                <Badge variant="success">
                  <Check className="size-3" />
                  Conectado
                </Badge>
              </div>

              <p className="truncate font-dado text-sm text-tinta-media">
                {data.numero ? formatarTelefone(data.numero) : '—'}
              </p>

              {data.conectadoEm ? (
                <p className="text-xs text-tinta-fraca">
                  Conectado desde {formatarDataHora(data.conectadoEm)}
                </p>
              ) : null}
            </div>
          </div>

          <div className="flex flex-wrap gap-2">
            <Button variant="outline" onClick={() => setTrocaAberta(true)}>
              <RefreshCw />
              Trocar número
            </Button>
            <Button variant="destructive" onClick={() => void desconectar()} disabled={desconectando}>
              {desconectando ? <Loader2 className="animate-spin" /> : <LogOut />}
              Desconectar
            </Button>
          </div>
        </div>
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
              <p className="text-center text-xs text-tinta-fraca">
                A tela se atualiza sozinha assim que a leitura for reconhecida. Deixe o leitor do
                celular já aberto antes de gerar um novo código.
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

          <Button onClick={() => void parear()} disabled={preparando || segundosDeEspera > 0}>
            {preparando ? <Loader2 className="animate-spin" /> : null}
            {segundosDeEspera > 0
              ? `Aguarde ${segundosDeEspera}s`
              : data.qrCodeBase64
                ? 'Gerar outro código'
                : 'Conectar número'}
          </Button>

          {segundosDeEspera > 0 ? (
            <p className="text-center text-xs text-tinta-fraca">
              Pareamentos repetidos em sequência são bloqueados temporariamente pela Meta.
            </p>
          ) : null}
        </div>
      )}

      <DialogoDeTroca
        situacao={data}
        aberto={trocaAberta}
        aoFechar={() => setTrocaAberta(false)}
        aoConcluir={() => void clienteDeConsultas.invalidateQueries({ queryKey: ['whatsapp-conexoes'] })}
      />
    </>
  )
}
