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
  emAtendimentoHumano: false,
  roboPausadoEm: null,
  roboPausadoPorNome: null,
  clienteSemRobo: false,
}

const CABECALHO_WEBM = [0x1a, 0x45, 0xdf, 0xa3]

class MockMediaRecorder {
  static isTypeSupported = vi.fn((mime: string) => mime.includes('audio/webm'))
  static instancias = 0
  static primeirosBytes: number[] = CABECALHO_WEBM
  state: 'inactive' | 'recording' | 'paused' = 'inactive'
  mimeType = 'audio/webm;codecs=opus'
  ondataavailable: ((ev: { data: Blob }) => void) | null = null
  onstop: (() => void) | null = null
  onerror: (() => void) | null = null

  constructor() {
    MockMediaRecorder.instancias += 1
  }

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
    const corpo = new Uint8Array(2048)
    corpo.set(MockMediaRecorder.primeirosBytes)
    const blob = new Blob([corpo], { type: 'audio/webm' })
    this.ondataavailable?.({ data: blob })
    this.onstop?.()
  }
}

describe('CaixaDeEnvio — gravação e envio de áudio de voz', () => {
  const stopTrack = vi.fn()
  const enviarAnexoMock = vi.mocked(conversasApi.enviarAnexo)

  beforeEach(() => {
    MockMediaRecorder.instancias = 0
    MockMediaRecorder.primeirosBytes = CABECALHO_WEBM
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

  it('ignora o segundo clique no microfone enquanto o primeiro ainda espera a permissão', async () => {
    renderizarCaixa()

    const gravar = screen.getByRole('button', { name: 'Gravar áudio de voz' })
    fireEvent.click(gravar)
    fireEvent.click(gravar)

    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'Gravação de áudio em andamento' })).toBeInTheDocument()
    })

    // Dois gravadores no mesmo conjunto de pedaços produziam um arquivo sem o
    // cabeçalho no começo (achado em produção, 2026-10-30).
    expect(MockMediaRecorder.instancias).toBe(1)
  })

  it('não envia a gravação que saiu sem o cabeçalho do WebM', async () => {
    MockMediaRecorder.primeirosBytes = [0x43, 0xc3, 0x81, 0x00]

    renderizarCaixa()

    fireEvent.click(screen.getByRole('button', { name: 'Gravar áudio de voz' }))
    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'Gravação de áudio em andamento' })).toBeInTheDocument()
    })

    fireEvent.click(screen.getByRole('button', { name: 'Enviar áudio' }))

    await waitFor(() => {
      expect(screen.queryByRole('region', { name: 'Gravação de áudio em andamento' })).not.toBeInTheDocument()
    })
    expect(enviarAnexoMock).not.toHaveBeenCalled()
  })
})

describe('CaixaDeEnvio — colar imagem com Ctrl+V', () => {
  const enviarAnexoMock = vi.mocked(conversasApi.enviarAnexo)
  const enviarMensagemMock = vi.mocked(conversasApi.enviarMensagem)

  beforeEach(() => {
    enviarAnexoMock.mockReset()
    enviarAnexoMock.mockResolvedValue({} as never)
    enviarMensagemMock.mockReset()
    enviarMensagemMock.mockResolvedValue({} as never)

    vi.mocked(usarInvalidarAtendimento).mockReturnValue(vi.fn())
    vi.mocked(usarRespostasRapidas).mockReturnValue({
      data: [],
    } as unknown as ReturnType<typeof usarRespostasRapidas>)

    if (!URL.createObjectURL) {
      URL.createObjectURL = vi.fn(() => 'blob:imagem-teste')
    } else {
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:imagem-teste')
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
    renderizarComProvedores(
      <CaixaDeEnvio
        conversa={conversaAtiva}
        souOResponsavel
        mensagemRespondida={null}
        aoCancelarResposta={vi.fn()}
        setores={[]}
        nomeCartorio="1º Ofício"
      />,
    )
    return screen.getByPlaceholderText(/Escreva a resposta/) as HTMLTextAreaElement
  }

  /** O que o navegador põe na área de transferência ao colar um print de tela. */
  function areaComPrint() {
    const print = new File([new Uint8Array(512)], 'image.png', { type: 'image/png' })
    return {
      types: ['Files'],
      items: [{ kind: 'file', type: 'image/png', getAsFile: () => print }],
      files: [print],
      getData: () => '',
    }
  }

  it('transforma o print colado em anexo e envia com o texto como legenda', async () => {
    const campo = renderizarCaixa()

    fireEvent.change(campo, { target: { value: 'Segue o print' } })
    const colagemPadraoSeguiu = fireEvent.paste(campo, { clipboardData: areaComPrint() })

    expect(colagemPadraoSeguiu).toBe(false)
    expect(await screen.findByAltText('Prévia da imagem anexada')).toBeInTheDocument()
    expect(screen.getByText(/o texto abaixo vai como legenda/)).toBeInTheDocument()

    fireEvent.keyDown(campo, { key: 'Enter' })

    await waitFor(() => {
      expect(enviarAnexoMock).toHaveBeenCalledTimes(1)
    })

    const [conversaId, arquivoEnviado, legenda] = enviarAnexoMock.mock.calls[0]
    expect(conversaId).toBe('42')
    expect(arquivoEnviado.type).toBe('image/png')
    expect(arquivoEnviado.name).toMatch(/^imagem-\d{4}-\d{2}-\d{2}-\d{2}-\d{2}-\d{2}\.png$/)
    expect(legenda).toBe('Segue o print')
    expect(enviarMensagemMock).not.toHaveBeenCalled()
  })

  it('deixa o colar de texto com o navegador, sem virar anexo', () => {
    const campo = renderizarCaixa()

    const colagemPadraoSeguiu = fireEvent.paste(campo, {
      clipboardData: {
        types: ['text/plain'],
        items: [{ kind: 'string', type: 'text/plain', getAsFile: () => null }],
        files: [],
        getData: () => 'protocolo 2026-00042',
      },
    })

    expect(colagemPadraoSeguiu).toBe(true)
    expect(screen.queryByAltText('Prévia da imagem anexada')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remover arquivo' })).not.toBeInTheDocument()
  })

  it('cola o texto quando a cópia traz texto e imagem juntos, como célula do Excel', () => {
    const campo = renderizarCaixa()
    const area = areaComPrint()

    const colagemPadraoSeguiu = fireEvent.paste(campo, {
      clipboardData: { ...area, types: ['text/plain', 'text/html', 'Files'] },
    })

    expect(colagemPadraoSeguiu).toBe(true)
    expect(screen.queryByRole('button', { name: 'Remover arquivo' })).not.toBeInTheDocument()
  })

  it('não anexa arquivo colado que não é imagem', () => {
    const campo = renderizarCaixa()
    const compactado = new File(['x'], 'dados.zip', { type: 'application/zip' })

    const colagemPadraoSeguiu = fireEvent.paste(campo, {
      clipboardData: {
        types: ['Files'],
        items: [{ kind: 'file', type: 'application/zip', getAsFile: () => compactado }],
        files: [compactado],
        getData: () => '',
      },
    })

    expect(colagemPadraoSeguiu).toBe(true)
    expect(screen.queryByRole('button', { name: 'Remover arquivo' })).not.toBeInTheDocument()
  })
})
