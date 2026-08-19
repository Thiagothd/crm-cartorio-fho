import { useState } from 'react'
import { ArrowDownLeft, ArrowUpRight, Download } from 'lucide-react'
import { formatarDataHora, formatarTamanhoDeArquivo } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import { salvarAnexo, usarAnexo } from './cache-de-anexos'
import { iconeDeDocumento, rotuloDeTipo, type ItemDeMidia } from './midias-da-conversa'
import { PlayerDeAudio } from './player-de-audio'

/**
 * As linhas das abas Áudio e Documentos.
 *
 * Ficam no mesmo arquivo porque são a mesma coisa com miolo diferente: caixa de
 * uma linha, ícone à esquerda, nome truncado, metadado em mono embaixo e ação à
 * direita. Dois arquivos de cinquenta linhas seriam fragmentação, e a
 * submarcação divergiria no primeiro ajuste.
 */

const CAIXA =
  'flex w-full items-center gap-2 rounded-md border border-linha-forte bg-branco px-2.5 py-2 text-left'

function Direcao({ doCliente }: { doCliente: boolean }) {
  return doCliente ? (
    <ArrowDownLeft className="size-3 shrink-0 text-tinta-fraca" aria-label="Recebido" />
  ) : (
    <ArrowUpRight className="size-3 shrink-0 text-verde" aria-label="Enviado" />
  )
}

/**
 * Áudio da conversa.
 *
 * Nasce sem baixar nada — carregar o fio inteiro de notas de voz ao abrir a aba
 * seria banda por arquivo que ninguém vai ouvir, o mesmo argumento que já vale
 * para documento no fio. O binário só desce quando alguém aperta Reproduzir no
 * `PlayerDeAudio`, que já nasce com a forma final: o mesmo frame que pede o
 * carregamento é o que toca depois, sem trocar de widget no meio do caminho.
 */
export function LinhaDeAudio({ item }: { item: ItemDeMidia }) {
  const [tocar, setTocar] = useState(false)
  const { url, erro } = usarAnexo(item.conversaId, item.mensagemId, tocar)

  return (
    <div className={cn(CAIXA, 'flex-col items-stretch gap-1.5', item.oculta && 'opacity-60')}>
      <div className="flex items-center gap-2">
        <div className="min-w-0 flex-1">
          <p className="flex items-center gap-1 text-xs text-tinta">
            <Direcao doCliente={item.doCliente} />
            <span className="truncate">{item.autor}</span>
          </p>
          <p className="truncate font-dado text-[10px] text-tinta-fraca">
            {formatarDataHora(item.enviadaEm)}
            {item.tamanhoBytes ? ` · ${formatarTamanhoDeArquivo(item.tamanhoBytes)}` : ''}
          </p>
        </div>

        <button
          type="button"
          onClick={() => void salvarAnexo(item.conversaId, item.mensagemId, item.nome)}
          className="flex size-8 shrink-0 items-center justify-center rounded-md text-tinta-fraca transition-colors hover:bg-superficie hover:text-tinta"
          title={`Baixar ${item.nome}`}
        >
          <Download className="size-3.5" />
          <span className="sr-only">Baixar</span>
        </button>
      </div>

      {erro ? (
        <p className="text-[11px] text-tinta-media">{erro}</p>
      ) : (
        <PlayerDeAudio
          url={url}
          carregando={tocar && !url}
          autoPlay
          aoIniciar={() => setTocar(true)}
        />
      )}
    </div>
  )
}

/**
 * Documento da conversa.
 *
 * NUNCA pré-carrega e NUNCA abre no domínio do painel — mantém a decisão de
 * segurança que o fio já toma: arquivo escolhido pelo cliente que abrisse aqui
 * dentro seria código rodando com a sessão de quem atende.
 */
export function LinhaDeDocumento({ item }: { item: ItemDeMidia }) {
  const Icone = iconeDeDocumento(item.tipoMime, item.nome)
  const tamanho = formatarTamanhoDeArquivo(item.tamanhoBytes)

  return (
    <button
      type="button"
      onClick={() => void salvarAnexo(item.conversaId, item.mensagemId, item.nome)}
      className={cn(CAIXA, 'transition-colors hover:bg-superficie', item.oculta && 'opacity-60')}
      title={`Baixar ${item.nome}`}
    >
      <Icone className="size-4 shrink-0 text-tinta-media" />

      <span className="min-w-0 flex-1">
        <span className="block truncate text-xs text-tinta">{item.nome}</span>
        <span className="block truncate font-dado text-[10px] text-tinta-fraca">
          {rotuloDeTipo(item.tipoMime, item.nome)}
          {tamanho ? ` · ${tamanho}` : ''}
          {` · ${formatarDataHora(item.enviadaEm)}`}
        </span>
      </span>

      <Download className="size-3.5 shrink-0 text-tinta-fraca" />
    </button>
  )
}
