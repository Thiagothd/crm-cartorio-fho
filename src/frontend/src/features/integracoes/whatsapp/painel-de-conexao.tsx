import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Loader2, LogOut, QrCode, RefreshCw } from 'lucide-react'
import { toast } from 'sonner'
import { Avatar } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { Painel } from '@/components/ui/painel'
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

  // O GET é deliberadamente passivo — nunca gera nem devolve outro QR, só
  // confirma o estado atual (ver IEvolutionClient.ObterEstadoAsync). O código
  // que veio do POST fica AQUI, fora do cache da consulta, porque um poll
  // passivo sobrescreveria `qrCodeBase64` para nulo e apagaria a imagem da
  // tela 5s depois de ela aparecer.
  const [qrCode, setQrCode] = useState<string | null>(null)

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['whatsapp-situacao'],
    queryFn: () => whatsappApi.situacao(),

    // Continua perguntando enquanto há QR na tela OU uma tentativa em curso no
    // gateway (`connecting`) — é o único jeito de saber que o celular concluiu
    // a leitura, já que o GET não devolve QR para comparar.
    refetchInterval: (consulta) => {
      const situacao = consulta.state.data
      const conectando = situacao?.estado.toLowerCase() === 'connecting'

      return qrCode || conectando ? INTERVALO_DE_ESPERA : false
    },
  })

  // Sincroniza o QR local com o que a consulta passiva confirma: limpa quando
  // o canal desliga, conecta ou fecha de vez; adota o que vier preenchido (só
  // acontece na resposta do POST, nunca do GET).
  useEffect(() => {
    if (!data) return

    const estado = data.estado.toLowerCase()

    if (!data.habilitado || data.conectado || estado === 'close') {
      setQrCode(null)
    } else if (data.qrCodeBase64) {
      setQrCode(data.qrCodeBase64)
    }
  }, [data])

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
      setQrCode(situacao.qrCodeBase64)
      clienteDeConsultas.setQueryData(['whatsapp-situacao'], situacao)

      toast.success(
        situacao.conectado
          ? 'Canal já estava conectado.'
          : situacao.qrCodeBase64
            ? 'Leia o código com o celular para conectar.'
            : 'Conexão iniciada. Aguarde a confirmação do gateway.',
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

  const conectando = data?.estado.toLowerCase() === 'connecting'

  return (
    <>
      <Painel
        titulo="Conexão do canal"
        descricao="O número que fala com os clientes do cartório — parear, trocar ou desconectar."
        acao={
          data?.conectado ? (
            <Badge variant="success">
              <Check className="size-3" />
              Conectado
            </Badge>
          ) : null
        }
      >
      {isLoading ? (
        <Skeleton className="h-40 w-full" />
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
        // Identidade e ações lado a lado na largura devolvida pela grade da
        // página; empilham sozinhas quando não couberem.
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="flex min-w-0 items-center gap-3">
            <Avatar
              nome={data.perfilNome ?? data.numero ?? 'WhatsApp'}
              fotoUrl={data.perfilFotoUrl}
              tamanho="lg"
            />

            <div className="min-w-0">
              <p className="truncate text-[15px] font-bold tracking-tight text-tinta">
                {data.perfilNome ?? 'Número conectado'}
              </p>

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
        <div className="flex flex-col items-center gap-3 py-2">
          {qrCode ? (
            <>
              {/* Fundo branco explícito e fixo: o leitor do celular precisa de
                  contraste alto, e a textura de papel por trás atrapalharia. */}
              <img
                src={qrCode}
                alt="Código para conectar o WhatsApp"
                className="size-56 rounded-md border border-linha bg-white"
              />
              <p role="status" className="text-center text-sm text-tinta-media">
                Aguardando leitura. No celular: <strong className="text-tinta">WhatsApp →
                Dispositivos conectados → Conectar um dispositivo</strong>.
              </p>
              <p className="text-center text-xs text-tinta-fraca">
                A tela se atualiza sozinha assim que a leitura for reconhecida. Deixe o leitor do
                celular já aberto antes de gerar um novo código.
              </p>
            </>
          ) : conectando ? (
            <>
              <Loader2 className="size-10 animate-spin text-tinta-fraca" />
              <p role="status" className="text-center text-sm text-tinta-media">
                Conexão em andamento — aguarde a confirmação do gateway. A tela se atualiza
                sozinha.
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

          {conectando && !qrCode ? null : (
            <Button onClick={() => void parear()} disabled={preparando || segundosDeEspera > 0}>
              {preparando ? <Loader2 className="animate-spin" /> : null}
              {segundosDeEspera > 0
                ? `Aguarde ${segundosDeEspera}s`
                : qrCode
                  ? 'Gerar outro código'
                  : 'Conectar número'}
            </Button>
          )}

          {segundosDeEspera > 0 ? (
            <p className="text-center text-xs text-tinta-fraca">
              Pareamentos repetidos em sequência são bloqueados temporariamente pela Meta.
            </p>
          ) : null}
        </div>
      )}
      </Painel>

      <DialogoDeTroca
        situacao={data}
        qrCode={qrCode}
        aberto={trocaAberta}
        aoFechar={() => setTrocaAberta(false)}
        aoConcluir={() => void clienteDeConsultas.invalidateQueries({ queryKey: ['whatsapp-conexoes'] })}
      />
    </>
  )
}
