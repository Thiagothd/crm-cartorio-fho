import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { renderizarComProvedores } from '@/test/utils'
import { conversasApi } from '@/lib/api/endpoints'
import { usarInvalidarAtendimento, usarRespostasRapidas } from './consultas'
import { CaixaDeEnvio } from './caixa-de-envio'
import type { Conversa } from '@/lib/api/tipos'

vi.mock('./consultas', () => ({
  usarInvalidarAtendimento: vi.fn(),
  usarRespostasRapidas: vi.fn(),
}))

vi.mock('@/lib/api/endpoints', () => ({
  conversasApi: {
    enviarAnexo: vi.fn(),
    enviarMensagem: vi.fn(),
  },
}))

const conversaAtiva: Conversa = {
  id: '42',
  protocolo: '2026-00042',
  clienteId: '10',
  clienteNome: 'Maria Oliveira',
  clienteTelefone: '+5564999990000',
  atendenteId: '1',
  atendenteNome: 'cartório parceiro',
  setorId: null,
  setorNome: null,
  etapaId: null,
  etapaNome: null,
  etapaCor: null,
  tipoServicoId: null,
  tipoServicoNome: null,
  status: 'Ativa',
  iniciadaEm: new Date().toISOString(),
  ultimaMensagemEm: new Date().toISOString(),
  mensagensNaoLidas: 0,
  aguardandoTriagem: false,
  prazoIniciadoEm: null,
  prazoLimite: null,
  situacaoPrazo: 'EmDia',
  previaUltimaMensagem: null,
  encerradaEm: null,
  encerradaPorNome: null,
  motivoEncerramento: null,
  reabertaEm: null,
  clienteFotoPerfilUrl: null,
  clienteNomePendente: false,
  atendidaPeloAparelho: false,
}

class MockMediaRecorder {
  static isTypeSupported = vi.fn((mime: string) => mime.includes('audio/webm'))
  state: 'inactive' | 'recording' | 'paused' = 'inactive'
  mimeType = 'audio/webm;codecs=opus'
  ondataavailable: ((ev: { data: Blob }) => void) | null = null
  onstop: (() => void) | null = null
  onerror: (() => void) | null = null

  start() {
    this.state = 'recording'
  }

  pause() {
    this.state = 'paused'
  }

  resume() {
    this.state = 'recording'
  }

  stop() {
    if (this.state === 'inactive') return
    this.state = 'inactive'
    const blob = new Blob([new Uint8Array(2048)], { type: 'audio/webm' })
    this.ondataavailable?.({ data: blob })
    this.onstop?.()
  }
}

describe('CaixaDeEnvio — gravação e envio de áudio de voz', () => {
  const stopTrack = vi.fn()
  const enviarAnexoMock = vi.mocked(conversasApi.enviarAnexo)

  beforeEach(() => {
    stopTrack.mockClear()
    enviarAnexoMock.mockReset()
    enviarAnexoMock.mockResolvedValue({} as never)

    vi.mocked(usarInvalidarAtendimento).mockReturnValue(vi.fn())
    vi.mocked(usarRespostasRapidas).mockReturnValue({
      data: [],
    } as unknown as ReturnType<typeof usarRespostasRapidas>)

    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      writable: true,
      value: {
        getUserMedia: vi.fn().mockResolvedValue({
          getTracks: () => [{ stop: stopTrack }],
        }),
      },
    })
    vi.stubGlobal('MediaRecorder', MockMediaRecorder)
    if (!URL.createObjectURL) {
      URL.createObjectURL = vi.fn(() => 'blob:audio-teste')
    } else {
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:audio-teste')
    }
    if (!URL.revokeObjectURL) {
      URL.revokeObjectURL = vi.fn()
    } else {
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
    }
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  function renderizarCaixa() {
    return renderizarComProvedores(
      <CaixaDeEnvio
        conversa={conversaAtiva}
        souOResponsavel
        mensagemRespondida={null}
        aoCancelarResposta={vi.fn()}
        setores={[]}
        nomeCartorio="1º Ofício"
      />,
    )
  }

  it('exibe o botão de gravar áudio ao lado do botão de anexo na conversa ativa', () => {
    renderizarCaixa()

    expect(screen.getByRole('button', { name: 'Gravar áudio de voz' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Anexar arquivo' })).toBeInTheDocument()
  })

  it('permite gravar, pré-escutar e enviar o áudio como arquivo .webm', async () => {
    renderizarCaixa()

    fireEvent.click(screen.getByRole('button', { name: 'Gravar áudio de voz' }))

    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'Gravação de áudio em andamento' })).toBeInTheDocument()
    })

    // Clicar em "Ouvir" finaliza a gravação e abre a prévia do áudio gravado
    fireEvent.click(screen.getByRole('button', { name: 'Ouvir' }))

    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'Prévia do áudio gravado' })).toBeInTheDocument()
    })

    // Clicar em "Enviar áudio" chama conversasApi.enviarAnexo com o arquivo .webm
    fireEvent.click(screen.getByRole('button', { name: 'Enviar áudio' }))

    await waitFor(() => {
      expect(enviarAnexoMock).toHaveBeenCalledTimes(1)
    })

    const [conversaId, arquivoEnviado, legendaEnviada] = enviarAnexoMock.mock.calls[0]
    expect(conversaId).toBe('42')
    expect(arquivoEnviado).toBeInstanceOf(File)
    expect(arquivoEnviado.name).toMatch(/^audio-.*\.webm$/)
    expect(arquivoEnviado.type).toBe('audio/webm')
    expect(legendaEnviada).toBe('')
    expect(stopTrack).toHaveBeenCalled()
  })

  it('permite enviar o áudio diretamente durante a gravação com 1 clique', async () => {
    renderizarCaixa()

    fireEvent.click(screen.getByRole('button', { name: 'Gravar áudio de voz' }))

    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'Gravação de áudio em andamento' })).toBeInTheDocument()
    })

    fireEvent.click(screen.getByRole('button', { name: 'Enviar áudio' }))

    await waitFor(() => {
      expect(enviarAnexoMock).toHaveBeenCalledTimes(1)
    })
  })

  it('permite cancelar e descartar a gravação sem enviar nada', async () => {
    renderizarCaixa()

    fireEvent.click(screen.getByRole('button', { name: 'Gravar áudio de voz' }))

    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'Gravação de áudio em andamento' })).toBeInTheDocument()
    })

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar gravação' }))

    await waitFor(() => {
      expect(screen.queryByRole('region', { name: 'Gravação de áudio em andamento' })).not.toBeInTheDocument()
    })
    expect(enviarAnexoMock).not.toHaveBeenCalled()
    expect(stopTrack).toHaveBeenCalled()
  })
})
