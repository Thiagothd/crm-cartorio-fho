import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { Inbox, MessagesSquare, RefreshCw, TriangleAlert } from 'lucide-react'
import { toast } from 'sonner'
import { EstadoVazio } from '@/components/layout/shell'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { cn } from '@/lib/utils'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { conversasApi } from '@/lib/api/endpoints'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { usarHubDeAtendimento } from '@/lib/realtime/usar-hub-de-atendimento'
import { usarPreferenciaLocal } from '@/lib/usar-preferencia-local'
import { usarAbaixoDe } from '@/lib/usar-abaixo-de'
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
import type { PainelDaConversa } from '@/features/atendimento/cabecalho-da-conversa'
import { FioDeMensagens } from '@/features/atendimento/fio-de-mensagens'
import { CaixaDeEnvio } from '@/features/atendimento/caixa-de-envio'
import { LinhaDoTempo } from '@/features/atendimento/linha-do-tempo'
import { PainelLateralDaConversa } from '@/features/atendimento/painel-lateral-da-conversa'
import { PainelDeMidias } from '@/features/atendimento/painel-de-midias'
import { VisualizadorDeImagem } from '@/features/atendimento/visualizador-de-imagem'
import { usarMidiasDaConversa } from '@/features/atendimento/midias-da-conversa'
import { descartarOutrasConversas } from '@/features/atendimento/cache-de-anexos'
import { DialogoEncaminhar } from '@/features/atendimento/dialogo-encaminhar'
import { BarraDeSelecao } from '@/features/atendimento/barra-de-selecao'
import type { Mensagem, SituacaoPrazo, StatusConversa } from '@/lib/api/tipos'

const FILTROS_INICIAIS: FiltrosVisiveis = {
  status: 'Ativa',
  escopo: 'todas',
  busca: '',
}

export function PaginaInbox() {
  const { id: idDaRota } = useParams<{ id: string }>()
  const navegar = useNavigate()
  const location = useLocation()

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

  // Abaixo de 1024px — mais estreito que o gatilho do menu principal (1280px),
  // porque o menu já recolheu primeiro e sobrou mais coluna aqui — a lista
  // recolhe sozinha para devolver espaço ao fio. `listaRecolhidaEfetiva` é o
  // que a tela usa para decidir o que mostrar; `listaRecolhida` continua sendo
  // só a escolha manual, salva, que volta a valer assim que a janela alarga.
  const estreitoParaLista = usarAbaixoDe(1024)
  const listaRecolhidaEfetiva = listaRecolhida || estreitoParaLista

  const [filtros, setFiltros] = useState<FiltrosVisiveis>(FILTROS_INICIAIS)
  const [assumindo, setAssumindo] = useState(false)
  const [mensagemRespondida, setMensagemRespondida] = useState<Mensagem | null>(null)

  // Semeia o filtro a partir da query string — é o que faz os números e as
  // etapas da Visão Geral não serem decorativos: clicar neles leva para a
  // inbox JÁ filtrada. Sem parâmetro, comportamento idêntico a antes. Só
  // roda quando a própria query string muda, para não sobrescrever um ajuste
  // manual do atendente feito depois de chegar aqui.
  useEffect(() => {
    if (!location.search) return

    const parametros = new URLSearchParams(location.search)
    const etapaId = parametros.get('etapa') ?? undefined
    const setorId = parametros.get('setor') ?? undefined
    const situacaoPrazo = (parametros.get('situacao') as SituacaoPrazo | null) ?? undefined
    const escopo = (parametros.get('escopo') as FiltrosVisiveis['escopo'] | null) ?? undefined
    const status = (parametros.get('status') as StatusConversa | null) ?? undefined
    const aguardando = parametros.get('aguardando') === '1'

    if (!etapaId && !setorId && !situacaoPrazo && !escopo && !status && !aguardando) return

    setFiltros({
      ...FILTROS_INICIAIS,
      ...(etapaId ? { etapaId } : {}),
      ...(setorId ? { setorId } : {}),
      ...(situacaoPrazo ? { situacaoPrazo } : {}),
      ...(escopo ? { escopo } : {}),
      ...(status ? { status } : {}),
      ...(aguardando ? { apenasAguardandoResposta: true } : {}),
    })

    // Limpa a query string depois de consumida: sem isso, voltar a esta
    // MESMA URL pelo histórico do navegador (ou uma aba duplicada) reaplica
    // o filtro antigo por cima de qualquer ajuste manual feito depois.
    navegar(idDaRota ? `/atendimentos/${idDaRota}` : '/atendimentos', { replace: true })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.search])

  // Histórico e mídias dividem UM estado, e não dois booleanos: os dois painéis
  // ocupam a mesma coluna de 256px, e abrir os dois juntos num monitor de
  // 1366px deixaria ~250px de fio. Um estado só também dispensa decidir "qual
  // ganha" quando os dois estivessem ligados.
  const [painelLateral, setPainelLateral] = useState<PainelDaConversa>(null)

  // UM interruptor de ocultas para o fio E para a galeria. Vive aqui porque a
  // lista que o visualizador navega e derivada dele: com dois independentes,
  // clicar numa imagem oculta que o fio esta exibindo abriria um visualizador
  // cuja lista nao a contem, e ele fecharia na hora.
  const [mostrarOcultas, setMostrarOcultas] = useState(false)

  // Identidade por ID da mensagem, nunca por índice: o array de mensagens cresce
  // com o tempo real, e um índice guardado passaria a apontar para outra foto.
  const [imagemAberta, setImagemAberta] = useState<string | null>(null)

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

  // A galeria e a navegação do visualizador saem das MESMAS mensagens que o fio
  // já tem na tela — sem endpoint novo e sem segunda fonte de verdade. Como é a
  // consulta que o SignalR invalida, a galeria acompanha o tempo real de graça.
  const midias = usarMidiasDaConversa(
    detalhe?.mensagens ?? [],
    conversa?.clienteNome ?? '',
    mostrarOcultas,
  )

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

    // Pela mesma razão, a foto ampliada e o interruptor de ocultas não podem
    // atravessar a troca: um modal continuar exibindo o documento do cliente
    // anterior é exatamente o erro que a limpeza acima existe para evitar.
    setImagemAberta(null)
    setMostrarOcultas(false)

    // Os binários da conversa que ficou para trás não têm por que continuar
    // ocupando memória. Só sai o que ninguém está usando.
    descartarOutrasConversas(selecionadaId)
  }, [selecionadaId])

  // Escape sai do modo de seleção. Toda entrada precisa de saída óbvia, e o
  // teclado é a saída que não exige mirar num botão.
  //
  // Cede a vez ao visualizador: com a foto aberta, um Escape que fizesse as duas
  // coisas fecharia o modal E derrubaria a seleção múltipla de uma vez só.
  useEffect(() => {
    if (!modoSelecao || imagemAberta) return

    function aoTeclar(evento: KeyboardEvent) {
      if (evento.key === 'Escape') {
        setModoSelecao(false)
        setSelecionadas(new Set())
      }
    }

    window.addEventListener('keydown', aoTeclar)
    return () => window.removeEventListener('keydown', aoTeclar)
  }, [modoSelecao, imagemAberta])

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
    navegar(`/atendimentos/${id}`)
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
      {/* No celular, com a conversa aberta, o painel de indicadores não diz
          nada que a conversa não diga — e come espaço do fio. Some, e volta
          ao fechar a conversa. O cabeçalho global já cuida de si mesmo (ver
          `Shell`); esta classe é só sobre o painel de indicadores. */}
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
          className={selecionadaId ? 'hidden md:flex' : undefined}
        />
      ) : null}

      <div className="flex min-h-0 flex-1">
        {/* So a LARGURA muda ao recolher. shrink-0 e overflow-hidden continuam
            aqui, e o irmao continua com min-w-0 flex-1 — e essa combinacao que
            segura a rolagem interna das duas colunas.

            NO CELULAR e uma coluna so, e quem decide qual aparece e a ROTA que
            ja existe: "/" mostra a lista, "/conversas/:id" mostra a conversa.
            Nada de matchMedia — trocar layout por estado de JS desmontaria os
            componentes e perderia a rolagem da lista ao voltar. Escondida por
            `display`, a lista continua montada e volta onde estava. */}
        <section
          className={cn(
            'flex-col overflow-hidden border-r border-linha',
            selecionadaId ? 'hidden' : 'flex w-full',
            'md:flex md:w-auto md:shrink-0',
            listaRecolhidaEfetiva ? 'md:w-14' : 'md:w-[360px]',
          )}
        >
          {/* A lista completa. SEMPRE no celular: o trilho só existe para
              poupar largura quando a coluna divide a tela com o fio, o que
              nunca acontece numa única coluna móvel. Sem o `md:hidden`
              condicional, recolher a lista no desktop e depois cruzar o
              breakpoint `md` — redimensionar a janela, girar um tablet —
              travava o celular no trilho de 56px, sem busca nem filtro. */}
          <div className={cn('flex min-h-0 flex-1 flex-col', listaRecolhidaEfetiva && 'md:hidden')}>
            <FiltrosDaInbox
              filtros={filtros}
              setores={setores ?? []}
              etapas={etapas ?? []}
              aoMudar={setFiltros}
              aoRecolher={alternarLista}
              aoCriarConversa={selecionar}
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
          </div>

          {/* O trilho recolhido. Só a partir de `md` — abaixo disso a lista e a
              conversa nunca dividem a tela, e recolher não teria o que poupar. */}
          {listaRecolhidaEfetiva ? (
            <div className="hidden min-h-0 md:flex md:flex-1 md:flex-col">
              <TrilhoDeConversas
                conversas={lista?.itens ?? []}
                total={lista?.total ?? 0}
                selecionadaId={selecionadaId}
                temFiltro={temFiltroAplicado(filtros)}
                aoSelecionar={selecionar}
                aoAbrir={alternarLista}
                permiteExpandir={!estreitoParaLista}
              />
            </div>
          ) : null}
        </section>

        {/* @container: o que aperta o cabecalho da conversa nao e o tamanho da
            janela, e a largura QUE SOBRA para esta coluna — a lista aberta e o
            painel lateral mudam isso sem a janela mudar de tamanho. NAO trocar
            por breakpoint de tela: em 1024x768 esta coluna tem ~424px, quase o
            mesmo que um celular de 430px, e a tela grande regrediria junto. */}
        <section
          className={cn(
            '@container min-w-0 flex-1 flex-col overflow-hidden',
            selecionadaId ? 'flex' : 'hidden md:flex',
          )}
        >
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
                    navegar('/atendimentos')
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
                painelAtivo={painelLateral}
                aoAlternarPainel={(qual) =>
                  setPainelLateral((atual) => (atual === qual ? null : qual))
                }
                aoVoltar={() => navegar('/atendimentos')}
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
                    aoAbrirImagem={setImagemAberta}
                    mostrarOcultas={mostrarOcultas}
                    aoAlternarOcultas={() => setMostrarOcultas((v) => !v)}
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

                {painelLateral === 'historico' ? (
                  <PainelLateralDaConversa
                    titulo="Histórico"
                    descricao="Mudanças de etapa e setor"
                    aoFechar={() => setPainelLateral(null)}
                    escapeAtivo={!imagemAberta}
                  >
                    <LinhaDoTempo eventos={detalhe.historico} />
                  </PainelLateralDaConversa>
                ) : null}

                {painelLateral === 'midias' ? (
                  <PainelLateralDaConversa
                    titulo="Mídias e arquivos"
                    descricao="Anexos trocados nesta conversa"
                    aoFechar={() => setPainelLateral(null)}
                    escapeAtivo={!imagemAberta}
                  >
                    <PainelDeMidias
                      conversa={conversa}
                      colecao={midias}
                      mostrarOcultas={mostrarOcultas}
                      aoAlternarOcultas={() => setMostrarOcultas((v) => !v)}
                      aoAbrirImagem={setImagemAberta}
                    />
                  </PainelLateralDaConversa>
                ) : null}
              </div>
            </>
          )}
        </section>
      </div>

      {/* Uma instância só, aqui em cima: o fio e a galeria abrem o MESMO
          visualizador, com a mesma lista de navegação. Dois modais competindo
          pelo foco seria o resultado de montar um em cada lugar. */}
      <VisualizadorDeImagem
        imagens={midias.imagens}
        mensagemAberta={imagemAberta}
        aoFechar={() => setImagemAberta(null)}
        aoNavegar={setImagemAberta}
      />

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
