import { useState } from 'react'
import { EyeOff } from 'lucide-react'
import { Avatar } from '@/components/ui/avatar'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { formatarTelefone } from '@/lib/formatacao'
import { corSolidaDaEtapa } from './cores-de-etapa'
import { LinhaDeAudio, LinhaDeDocumento } from './linhas-de-anexo'
import { MiniaturaDeAnexo } from './miniatura-de-anexo'
import type { ColecaoDeMidias } from './midias-da-conversa'
import type { Conversa } from '@/lib/api/tipos'

/**
 * A galeria da conversa: tudo que foi trocado neste atendimento, separado por
 * tipo, sem rolar o fio inteiro atrás do PDF de semana passada.
 *
 * NÃO BUSCA NADA. A coleção é derivada das mensagens que a página já carregou
 * (`midias-da-conversa.ts`), e os dados do cliente vêm da conversa que o
 * cabeçalho já mostra. Zero chamada nova ao backend, e o tempo real do fio vale
 * para a galeria de graça.
 */
export function PainelDeMidias({
  conversa,
  colecao,
  mostrarOcultas,
  aoAlternarOcultas,
  aoAbrirImagem,
}: {
  conversa: Conversa
  colecao: ColecaoDeMidias
  mostrarOcultas: boolean
  aoAlternarOcultas: () => void
  aoAbrirImagem: (mensagemId: string) => void
}) {
  // Abre na primeira aba que tem conteúdo: numa conversa só de documentos,
  // cair numa grade vazia faria a galeria parecer quebrada.
  const [aba, setAba] = useState(() =>
    colecao.midias.length > 0
      ? 'midia'
      : colecao.documentos.length > 0
        ? 'documento'
        : 'audio',
  )

  return (
    <>
      <div className="flex shrink-0 items-center gap-2.5 border-b border-linha px-4 py-3">
        <Avatar
          nome={conversa.clienteNome}
          fotoUrl={conversa.clienteFotoPerfilUrl}
          tamanho="md"
          corDeFundo={corSolidaDaEtapa(conversa.etapaCor)}
        />

        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium text-tinta">{conversa.clienteNome}</p>
          <p className="flex min-w-0 items-center gap-1.5 font-dado text-[11px] text-tinta-fraca">
            <span className="truncate">{formatarTelefone(conversa.clienteTelefone)}</span>
            <span className="etiqueta-protocolo shrink-0">{conversa.protocolo}</span>
          </p>
        </div>
      </div>

      <Tabs value={aba} onValueChange={setAba} className="flex min-h-0 flex-1 flex-col">
        <div className="shrink-0 px-4 pt-3">
          <TabsList className="grid w-full grid-cols-3">
            <Aba valor="midia" rotulo="Mídia" total={colecao.midias.length} />
            <Aba valor="audio" rotulo="Áudio" total={colecao.audios.length} />
            <Aba valor="documento" rotulo="Docs" total={colecao.documentos.length} />
          </TabsList>
        </div>

        <TabsContent value="midia" className="mt-0 min-h-0 flex-1 overflow-y-auto px-4 py-3">
          {colecao.midias.length === 0 ? (
            <Vazio texto="Nenhuma mídia compartilhada nesta conversa." />
          ) : (
            <div className="grid grid-cols-3 gap-1.5">
              {colecao.midias.map((item) => (
                /* Chave pelo id da mensagem, nunca pelo índice: o array cresce
                   com o tempo real, e chave por posição faria o React reciclar
                   as <img> e trocar as fotos de lugar. */
                <MiniaturaDeAnexo key={item.mensagemId} item={item} aoAbrir={aoAbrirImagem} />
              ))}
            </div>
          )}
        </TabsContent>

        <TabsContent value="audio" className="mt-0 min-h-0 flex-1 overflow-y-auto px-4 py-3">
          {colecao.audios.length === 0 ? (
            <Vazio texto="Nenhum áudio nesta conversa." />
          ) : (
            <div className="flex flex-col gap-1.5">
              {colecao.audios.map((item) => (
                <LinhaDeAudio key={item.mensagemId} item={item} />
              ))}
            </div>
          )}
        </TabsContent>

        <TabsContent value="documento" className="mt-0 min-h-0 flex-1 overflow-y-auto px-4 py-3">
          {colecao.documentos.length === 0 ? (
            <Vazio texto="Nenhum documento nesta conversa." />
          ) : (
            <div className="flex flex-col gap-1.5">
              {colecao.documentos.map((item) => (
                <LinhaDeDocumento key={item.mensagemId} item={item} />
              ))}
            </div>
          )}
        </TabsContent>
      </Tabs>

      {/* Ocultar não é apagar. O fio oferece o mesmo interruptor, e a galeria
          repete a regra em vez de inventar outra: escondido por padrão, para o
          botão "Ocultar da conversa" não virar mentira, mas alcançável, para o
          painel não ser onde o registro some. */}
      {colecao.ocultasIgnoradas > 0 || mostrarOcultas ? (
        <div className="flex shrink-0 items-center justify-between gap-2 border-t border-linha px-4 py-2">
          <p className="flex min-w-0 items-center gap-1.5 text-[11px] text-tinta-media">
            <EyeOff className="size-3.5 shrink-0" />
            <span className="truncate">
              {mostrarOcultas
                ? 'Mostrando arquivos de mensagens ocultadas'
                : colecao.ocultasIgnoradas === 1
                  ? '1 arquivo em mensagem ocultada'
                  : `${colecao.ocultasIgnoradas} arquivos em mensagens ocultadas`}
            </span>
          </p>

          <button
            type="button"
            onClick={aoAlternarOcultas}
            aria-pressed={mostrarOcultas}
            className="shrink-0 rounded-md border border-linha-forte bg-branco px-2 py-0.5 text-[11px] text-tinta-media transition-colors hover:text-tinta"
          >
            {mostrarOcultas ? 'Esconder' : 'Mostrar'}
          </button>
        </div>
      ) : null}
    </>
  )
}

function Aba({ valor, rotulo, total }: { valor: string; rotulo: string; total: number }) {
  return (
    <TabsTrigger value={valor} className="px-1.5 text-xs">
      {rotulo}
      <span className="font-dado text-[10px] text-tinta-fraca">{total}</span>
    </TabsTrigger>
  )
}

/**
 * Vazio no formato do vizinho — o `LinhaDoTempo` resolve "sem movimentações"
 * com uma linha de texto. O `EstadoVazio` de página inteira tem `p-10` e não
 * cabe numa coluna de 256px.
 */
function Vazio({ texto }: { texto: string }) {
  return <p className="text-xs text-tinta-media">{texto}</p>
}
