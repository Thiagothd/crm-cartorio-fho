import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Inbox, MessagesSquare, RefreshCw, TriangleAlert } from 'lucide-react'
import { toast } from 'sonner'
import { CabecalhoDaPagina, EstadoVazio } from '@/components/layout/shell'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { conversasApi } from '@/lib/api/endpoints'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { usarHubDeAtendimento } from '@/lib/realtime/usar-hub-de-atendimento'
import {
  usarAtendentes,
  usarConversa,
  usarConversas,
  usarEtapas,
  usarInvalidarAtendimento,
  usarResumoDoPainel,
  usarServicos,
  usarSetores,
} from '@/features/atendimento/consultas'
import {
  FiltrosDaInbox,
  filtrosParaConsulta,
  type FiltrosVisiveis,
} from '@/features/atendimento/filtros-da-inbox'
import { ItemDeConversa } from '@/features/atendimento/item-de-conversa'
import { ResumoDoPainelAtendimento } from '@/features/atendimento/resumo-do-painel'
import type { IndicadorDoPainel } from '@/features/atendimento/resumo-do-painel'
import { CabecalhoDaConversa } from '@/features/atendimento/cabecalho-da-conversa'
import { FioDeMensagens } from '@/features/atendimento/fio-de-mensagens'
import { CaixaDeEnvio } from '@/features/atendimento/caixa-de-envio'
import { LinhaDoTempo } from '@/features/atendimento/linha-do-tempo'
import { DialogoNovaConversa } from '@/features/atendimento/dialogo-nova-conversa'
import type { Mensagem } from '@/lib/api/tipos'

const FILTROS_INICIAIS: FiltrosVisiveis = {
  status: 'Ativa',
  escopo: 'todas',
  busca: '',
}

export function PaginaInbox() {
  const { id: idDaRota } = useParams<{ id: string }>()
  const navegar = useNavigate()

  const { atendente, tenant, podeOperarConversaDeTerceiro } = usarAutenticacao()
  const invalidar = usarInvalidarAtendimento()

  // Mesma regra do backend ao resolver {NomeCartorio}: fantasia, e a razão social
  // como reserva. Os dois lados precisam produzir o mesmo texto.
  const nomeCartorio = tenant?.nomeFantasia ?? tenant?.razaoSocial ?? ''

  const [filtros, setFiltros] = useState<FiltrosVisiveis>(FILTROS_INICIAIS)
  const [historicoVisivel, setHistoricoVisivel] = useState(false)
  const [assumindo, setAssumindo] = useState(false)
  const [mensagemRespondida, setMensagemRespondida] = useState<Mensagem | null>(null)

  const consulta = useMemo(() => filtrosParaConsulta(filtros), [filtros])

  const { data: lista, isLoading: carregandoLista } = usarConversas(consulta)
  const { data: resumo } = usarResumoDoPainel()
  const { data: setores } = usarSetores()
  const { data: etapas } = usarEtapas()
  const { data: servicos } = usarServicos()
  const { data: atendentes } = usarAtendentes()

  const selecionadaId = idDaRota ?? null
  const {
    data: detalhe,
    isLoading: carregandoDetalhe,
    error: erroDoDetalhe,
  } = usarConversa(selecionadaId)

  const conversa = detalhe?.conversa ?? null
  const souOResponsavel = conversa?.atendenteId === atendente?.id

  const indicadorSelecionado = useMemo<IndicadorDoPainel | undefined>(() => {
    if (filtros.apenasEncerradasHoje) return 'encerradas-hoje'
    if (filtros.situacaoPrazo === 'Atrasada') return 'atrasadas'
    if (filtros.situacaoPrazo === 'EmAlerta') return 'alerta'
    if (filtros.status !== 'Ativa' || filtros.etapaId || filtros.setorId || filtros.busca) return undefined
    if (filtros.escopo === 'fila') return 'fila'
    if (filtros.escopo === 'minhas') return 'minhas'
    return 'ativas'
  }, [filtros])

  function selecionarIndicador(indicador: IndicadorDoPainel) {
    const novos: Record<IndicadorDoPainel, FiltrosVisiveis> = {
      ativas: FILTROS_INICIAIS,
      fila: { ...FILTROS_INICIAIS, escopo: 'fila' },
      minhas: { ...FILTROS_INICIAIS, escopo: 'minhas' },
      alerta: { ...FILTROS_INICIAIS, situacaoPrazo: 'EmAlerta' },
      atrasadas: { ...FILTROS_INICIAIS, situacaoPrazo: 'Atrasada' },
      'encerradas-hoje': {
        ...FILTROS_INICIAIS,
        status: 'Encerrada',
        apenasEncerradasHoje: true,
      },
    }

    setFiltros(novos[indicador])
  }

  // Quais etapas contam como conclusao. Vem do cadastro (EhFinal), nao de nome
  // fixo — e o que decide se a conversa ganha o selo (docs/DESIGN.md, secao 6).
  const etapasFinais = useMemo(
    () => new Set((etapas ?? []).filter((e) => e.ehFinal).map((e) => e.id)),
    [etapas],
  )

  // Abrir a conversa zera o contador de não lidas. Uma ref evita repetir a
  // chamada a cada re-render enquanto a mesma conversa estiver aberta.
  const ultimaMarcada = useRef<string | null>(null)

  useEffect(() => {
    if (!conversa || conversa.mensagensNaoLidas === 0 || ultimaMarcada.current === conversa.id) {
      return
    }

    ultimaMarcada.current = conversa.id
    conversasApi
      .marcarComoLida(conversa.id)
      .then(() => invalidar(conversa.id))
      .catch(() => {
        // Contador desatualizado não justifica interromper o atendimento.
      })
  }, [conversa, invalidar])

  useEffect(() => {
    if (selecionadaId !== ultimaMarcada.current) {
      ultimaMarcada.current = null
    }

    setMensagemRespondida(null)
  }, [selecionadaId])

  // Tempo real: qualquer evento do tenant reflete na lista, no painel e — se for
  // da conversa aberta — no fio de mensagens.
  const aoReceberEvento = useCallback(
    (idAfetado: string) => {
      invalidar(idAfetado === selecionadaId ? idAfetado : undefined)
    },
    [invalidar, selecionadaId],
  )

  usarHubDeAtendimento(
    {
      onConversaAtribuida: (c) => aoReceberEvento(c.id),
      onConversaLiberada: (c) => aoReceberEvento(c.id),
      onConversaTransferida: (c) => {
        aoReceberEvento(c.id)

        if (c.atendenteId === atendente?.id) {
          toast.info(`${c.protocolo} foi transferida para você.`)
        }
      },
      onConversaAtualizada: (c) => aoReceberEvento(c.id),
      onConversaEncerrada: (c) => aoReceberEvento(c.id),
      onConversaReaberta: (c) => aoReceberEvento(c.id),
      onMensagemRecebida: ({ mensagem, conversa: c }) => {
        aoReceberEvento(c.id)

        if (mensagem.autorTipo === 'Cliente' && c.atendenteId === atendente?.id && c.id !== selecionadaId) {
          toast.message(`Nova mensagem de ${c.clienteNome}`, {
            description: mensagem.conteudo.slice(0, 90),
          })
        }
      },
    },
    Boolean(atendente),
  )

  function selecionar(id: string) {
    navegar(`/conversas/${id}`)
  }

  async function executar(acao: () => Promise<unknown>, sucesso?: string) {
    try {
      await acao()
      invalidar(selecionadaId ?? undefined)

      if (sucesso) {
        toast.success(sucesso)
      }
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.ehConflitoDeAtribuicao) {
        // O caso que o produto existe para evitar. A mensagem diz quem ganhou a
        // corrida, e a lista é recarregada para refletir a trava na hora.
        toast.warning('Conversa já assumida', {
          description: `${falha.atendenteQueAssumiu ?? 'Outro atendente'} chegou primeiro.`,
        })
        invalidar(selecionadaId ?? undefined)
        return
      }

      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível concluir a ação.')
    }
  }

  async function assumir() {
    if (!selecionadaId) return

    setAssumindo(true)
    await executar(() => conversasApi.assumir(selecionadaId), 'Conversa assumida.')
    setAssumindo(false)
  }

  return (
    <>
      <CabecalhoDaPagina
        titulo="Atendimento"
        descricao="Conversas do WhatsApp do cartório"
        acoes={<DialogoNovaConversa aoCriar={selecionar} />}
      />

      {resumo ? (
        <ResumoDoPainelAtendimento
          resumo={resumo}
          etapaSelecionada={filtros.etapaId}
          indicadorSelecionado={indicadorSelecionado}
          aoSelecionarIndicador={selecionarIndicador}
          aoSelecionarEtapa={(etapaId) =>
            setFiltros({ ...FILTROS_INICIAIS, etapaId })
          }
        />
      ) : null}

      <div className="flex min-h-0 flex-1">
        <section className="flex w-[360px] shrink-0 flex-col overflow-hidden border-r border-linha">
          <FiltrosDaInbox
            filtros={filtros}
            setores={setores ?? []}
            etapas={etapas ?? []}
            aoMudar={setFiltros}
          />

          <div className="flex-1 overflow-y-auto">
            {carregandoLista ? (
              <div className="flex flex-col gap-3 p-3">
                {[0, 1, 2, 3, 4].map((i) => (
                  <Skeleton key={i} className="h-20 w-full" />
                ))}
              </div>
            ) : lista?.itens.length ? (
              lista.itens.map((item) => (
                <ItemDeConversa
                  key={item.id}
                  conversa={item}
                  selecionada={item.id === selecionadaId}
                  souOResponsavel={item.atendenteId === atendente?.id}
                  etapaEhFinal={item.etapaId !== null && etapasFinais.has(item.etapaId)}
                  aoSelecionar={selecionar}
                />
              ))
            ) : (
              <EstadoVazio
                icone={Inbox}
                titulo="Nenhuma conversa aqui"
                descricao="Ajuste os filtros ou abra um novo atendimento."
              />
            )}
          </div>

          {lista ? (
            <div className="shrink-0 border-t border-linha px-3 py-2 text-[11px] text-tinta-media">
              {lista.itens.length} de {lista.total}
            </div>
          ) : null}
        </section>

        <section className="flex min-w-0 flex-1 flex-col overflow-hidden">
          {!selecionadaId ? (
            <EstadoVazio
              icone={MessagesSquare}
              titulo="Escolha uma conversa"
              descricao="Selecione um atendimento à esquerda para ver o histórico e responder."
            />
          ) : erroDoDetalhe ? (
            /* Sem este ramo, falha ao carregar o detalhe caía no skeleton abaixo
               e a tela ficava "carregando" para sempre, sem dizer nada. O caso
               mais comum e a lista estar desatualizada: a conversa foi encerrada
               ou removida depois que esta aba carregou. */
            <EstadoVazio
              icone={TriangleAlert}
              titulo={
                erroDoDetalhe instanceof ErroDaApi &&
                erroDoDetalhe.codigo === 'conversa_nao_encontrada'
                  ? 'Esta conversa não existe mais'
                  : 'Não foi possível abrir a conversa'
              }
              descricao={
                erroDoDetalhe instanceof ErroDaApi &&
                erroDoDetalhe.codigo === 'conversa_nao_encontrada'
                  ? 'A lista desta aba está desatualizada. Atualize para ver os atendimentos que existem agora.'
                  : erroDoDetalhe instanceof ErroDaApi
                    ? erroDoDetalhe.message
                    : 'Verifique se a API está no ar e tente novamente.'
              }
              acao={
                <Button
                  size="sm"
                  onClick={() => {
                    invalidar()
                    navegar('/')
                  }}
                >
                  <RefreshCw />
                  Atualizar a lista
                </Button>
              }
            />
          ) : carregandoDetalhe || !detalhe || !conversa ? (
            <div className="flex flex-1 flex-col gap-4 p-6">
              <Skeleton className="h-16 w-full" />
              <Skeleton className="h-full w-full" />
            </div>
          ) : (
            <>
              <CabecalhoDaConversa
                conversa={conversa}
                souOResponsavel={souOResponsavel}
                podeOperarDeTerceiro={podeOperarConversaDeTerceiro}
                etapas={etapas ?? []}
                setores={setores ?? []}
                servicos={servicos ?? []}
                atendentes={atendentes ?? []}
                assumindo={assumindo}
                etapaEhFinal={conversa.etapaId !== null && etapasFinais.has(conversa.etapaId)}
                historicoVisivel={historicoVisivel}
                aoAlternarHistorico={() => setHistoricoVisivel((v) => !v)}
                aoAssumir={() => void assumir()}
                aoLiberar={() =>
                  void executar(() => conversasApi.liberar(conversa.id), 'Conversa devolvida à fila.')
                }
                aoMover={(dados) =>
                  void executar(() => conversasApi.mover(conversa.id, dados), 'Conversa movida.')
                }
                aoVincularServico={(tipoServicoId) =>
                  void executar(
                    () => conversasApi.vincularServico(conversa.id, tipoServicoId),
                    'Serviço vinculado e prazo iniciado.',
                  )
                }
                aoTransferir={(destinoId) =>
                  void executar(
                    () => conversasApi.transferir(conversa.id, destinoId),
                    'Atendimento transferido.',
                  )
                }
                aoEncerrar={(motivo) =>
                  void executar(
                    () => conversasApi.encerrar(conversa.id, motivo),
                    'Atendimento encerrado e arquivado.',
                  )
                }
                aoReabrir={() =>
                  void executar(() => conversasApi.reabrir(conversa.id), 'Atendimento reaberto.')
                }
              />

              <div className="flex min-h-0 flex-1">
                <div className="flex min-w-0 flex-1 flex-col bg-superficie">
                  <FioDeMensagens
                    mensagens={detalhe.mensagens}
                    clienteNome={conversa.clienteNome}
                    aoResponder={setMensagemRespondida}
                  />

                  <CaixaDeEnvio
                    conversa={conversa}
                    souOResponsavel={souOResponsavel}
                    podeAssumir={conversa.atendenteId === null}
                    assumindo={assumindo}
                    aoAssumir={() => void assumir()}
                    mensagemRespondida={mensagemRespondida}
                    aoCancelarResposta={() => setMensagemRespondida(null)}
                    setores={setores ?? []}
                    nomeCartorio={nomeCartorio}
                  />
                </div>

                {historicoVisivel ? <LinhaDoTempo eventos={detalhe.historico} /> : null}
              </div>
            </>
          )}
        </section>
      </div>
    </>
  )
}
