import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Inbox, MessagesSquare, RefreshCw, TriangleAlert } from 'lucide-react'
import { toast } from 'sonner'
import { CabecalhoDaPagina, EstadoVazio } from '@/components/layout/shell'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { cn } from '@/lib/utils'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { conversasApi } from '@/lib/api/endpoints'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { usarHubDeAtendimento } from '@/lib/realtime/usar-hub-de-atendimento'
import { usarPreferenciaLocal } from '@/lib/usar-preferencia-local'
import {
  usarAparenciaDaConversa,
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
  temFiltroAplicado,
  type FiltrosVisiveis,
} from '@/features/atendimento/filtros-da-inbox'
import { ItemDeConversa } from '@/features/atendimento/item-de-conversa'
import { TrilhoDeConversas } from '@/features/atendimento/trilho-de-conversas'
import { ResumoDoPainelAtendimento } from '@/features/atendimento/resumo-do-painel'
import type { IndicadorDoPainel } from '@/features/atendimento/resumo-do-painel'
import { CabecalhoDaConversa } from '@/features/atendimento/cabecalho-da-conversa'
import { FioDeMensagens } from '@/features/atendimento/fio-de-mensagens'
import { CaixaDeEnvio } from '@/features/atendimento/caixa-de-envio'
import { LinhaDoTempo } from '@/features/atendimento/linha-do-tempo'
import { DialogoNovaConversa } from '@/features/atendimento/dialogo-nova-conversa'
import { DialogoEncaminhar } from '@/features/atendimento/dialogo-encaminhar'
import { BarraDeSelecao } from '@/features/atendimento/barra-de-selecao'
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

  // Nasce RECOLHIDO. Indicadores e funil comem ~110px fixos do topo, e num
  // notebook isso sai direto do fio de mensagens — que é onde o atendimento
  // acontece. Quem quiser o painel expande uma vez e fica lembrado.
  //
  // O sufixo .v2 na chave é obrigatório: a versão anterior do hook gravava no
  // localStorage já na montagem, então todo navegador que abriu a inbox uma vez
  // tem 'false' guardado sem ninguém ter escolhido nada. Sem trocar a chave, o
  // novo padrão não chegaria em quem já usa o sistema.
  const [painelRecolhido, alternarPainel] = usarPreferenciaLocal(
    'crm-cartorio.painel-recolhido.v2',
    true,
  )

  // Recolher a lista devolve 300px de largura para o fio e para a caixa de
  // resposta. Nasce ABERTA: quem esta satisfeito com a tela de hoje nao pode
  // encontrar a lista sumida sem ter pedido nada.
  const [listaRecolhida, alternarLista] = usarPreferenciaLocal(
    'crm-cartorio.lista-recolhida',
    false,
  )

  const [filtros, setFiltros] = useState<FiltrosVisiveis>(FILTROS_INICIAIS)
  const [historicoVisivel, setHistoricoVisivel] = useState(false)
  const [assumindo, setAssumindo] = useState(false)
  const [mensagemRespondida, setMensagemRespondida] = useState<Mensagem | null>(null)

  // Seleção múltipla e encaminhamento vivem aqui, e não no fio, porque as duas
  // coisas atravessam componentes: a barra fica acima do fio e o diálogo abre
  // por cima da página inteira.
  const [selecionadas, setSelecionadas] = useState<Set<string>>(new Set())
  const [modoSelecao, setModoSelecao] = useState(false)
  const [idsParaEncaminhar, setIdsParaEncaminhar] = useState<string[]>([])

  const consulta = useMemo(() => filtrosParaConsulta(filtros), [filtros])

  const { data: lista, isLoading: carregandoLista } = usarConversas(consulta)
  const { data: resumo } = usarResumoDoPainel()
  const { data: setores } = usarSetores()
  const { data: etapas } = usarEtapas()
  const { data: servicos } = usarServicos()
  const { data: atendentes } = usarAtendentes()
  const { data: aparencia } = usarAparenciaDaConversa()

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
    // Trocar de conversa zera a seleção: selecionar mensagens de um atendimento
    // e agir sobre elas em outro seria o pior erro possível aqui.
    setSelecionadas(new Set())
    setModoSelecao(false)
  }, [selecionadaId])

  // Escape sai do modo de seleção. Toda entrada precisa de saída óbvia, e o
  // teclado é a saída que não exige mirar num botão.
  useEffect(() => {
    if (!modoSelecao) return

    function aoTeclar(evento: KeyboardEvent) {
      if (evento.key === 'Escape') {
        setModoSelecao(false)
        setSelecionadas(new Set())
      }
    }

    window.addEventListener('keydown', aoTeclar)
    return () => window.removeEventListener('keydown', aoTeclar)
  }, [modoSelecao])

  function alternarSelecao(id: string) {
    setSelecionadas((atual) => {
      const proxima = new Set(atual)

      if (proxima.has(id)) {
        proxima.delete(id)
      } else {
        proxima.add(id)
      }

      return proxima
    })
  }

  async function fixarMensagem(mensagem: Mensagem) {
    if (!selecionadaId) return

    await executar(
      () => conversasApi.fixarMensagem(selecionadaId, mensagem.id, !mensagem.fixada),
      mensagem.fixada ? 'Mensagem desafixada.' : 'Mensagem fixada no topo.',
    )
  }

  async function ocultarMensagens(ids: string[], ocultar: boolean) {
    if (!selecionadaId || ids.length === 0) return

    await executar(
      () => conversasApi.ocultarMensagens(selecionadaId, ids, ocultar),
      ocultar
        ? `${ids.length === 1 ? 'Mensagem ocultada' : `${ids.length} mensagens ocultadas`} — o registro continua no histórico.`
        : 'Mensagem reexibida no fio.',
    )

    setSelecionadas(new Set())
    setModoSelecao(false)
  }

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
          recolhido={painelRecolhido}
          aoAlternarRecolhido={alternarPainel}
        />
      ) : null}

      <div className="flex min-h-0 flex-1">
        {/* So a LARGURA muda ao recolher. shrink-0 e overflow-hidden continuam
            aqui, e o irmao continua com min-w-0 flex-1 — e essa combinacao que
            segura a rolagem interna das duas colunas. */}
        <section
          className={cn(
            'flex shrink-0 flex-col overflow-hidden border-r border-linha',
            listaRecolhida ? 'w-14' : 'w-[360px]',
          )}
        >
          {listaRecolhida ? (
            <TrilhoDeConversas
              conversas={lista?.itens ?? []}
              total={lista?.total ?? 0}
              selecionadaId={selecionadaId}
              temFiltro={temFiltroAplicado(filtros)}
              aoSelecionar={selecionar}
              aoAbrir={alternarLista}
            />
          ) : (
            <>
              <FiltrosDaInbox
                filtros={filtros}
                setores={setores ?? []}
                etapas={etapas ?? []}
                aoMudar={setFiltros}
                aoRecolher={alternarLista}
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
            </>
          )}
        </section>

        {/* @container: o que aperta o cabecalho da conversa nao e o tamanho da
            janela, e a largura QUE SOBRA para esta coluna — a lista aberta e o
            painel de historico mudam isso sem a janela mudar de tamanho. */}
        <section className="@container flex min-w-0 flex-1 flex-col overflow-hidden">
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
                {/* min-h-0 também aqui: sem ele, o overflow-y-auto do fio não tem
                    limite para respeitar e a coluna cresce além da tela em vez de
                    rolar por dentro. */}
                <div className="flex min-h-0 min-w-0 flex-1 flex-col bg-superficie">
                  {modoSelecao ? (
                    <BarraDeSelecao
                      total={selecionadas.size}
                      aoEncaminhar={() => setIdsParaEncaminhar([...selecionadas])}
                      aoOcultar={() => void ocultarMensagens([...selecionadas], true)}
                      aoSair={() => {
                        setModoSelecao(false)
                        setSelecionadas(new Set())
                      }}
                    />
                  ) : null}

                  <FioDeMensagens
                    mensagens={detalhe.mensagens}
                    clienteNome={conversa.clienteNome}
                    aparencia={aparencia?.aparenciaDaConversa ?? 'PapelLiso'}
                    selecionadas={selecionadas}
                    modoSelecao={modoSelecao}
                    aoResponder={setMensagemRespondida}
                    aoAlternarSelecao={alternarSelecao}
                    aoEntrarEmSelecao={(id) => {
                      setModoSelecao(true)
                      setSelecionadas(new Set([id]))
                    }}
                    aoFixar={(m) => void fixarMensagem(m)}
                    aoOcultar={(ids, ocultar) => void ocultarMensagens(ids, ocultar)}
                    aoEncaminhar={setIdsParaEncaminhar}
                  />

                  <CaixaDeEnvio
                    conversa={conversa}
                    souOResponsavel={souOResponsavel}
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

      {selecionadaId ? (
        <DialogoEncaminhar
          conversaOrigemId={selecionadaId}
          mensagemIds={idsParaEncaminhar}
          aberto={idsParaEncaminhar.length > 0}
          aoFechar={() => setIdsParaEncaminhar([])}
          aoConcluir={() => {
            invalidar(selecionadaId)
            setSelecionadas(new Set())
            setModoSelecao(false)
          }}
        />
      ) : null}
    </>
  )
}
