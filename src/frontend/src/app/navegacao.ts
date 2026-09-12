import type { ComponentType } from 'react'
import type { LucideIcon } from 'lucide-react'
import type { ChaveDePermissao } from '@/lib/api/tipos'
import {
  BarChart3,
  Bot,
  Building2,
  GitBranch,
  History,
  Home,
  Inbox,
  LifeBuoy,
  MessageSquare,
  Palette,
  Plug,
  Scale,
  ShieldCheck,
  UserRound,
  Users,
} from 'lucide-react'

/**
 * Fonte única de verdade da navegação. Rotas (`rotas.tsx`), o rail, o
 * cabeçalho global, a barra inferior do celular, a folha "Mais" e os testes
 * leem daqui — nunca existe uma segunda lista de itens de menu duplicada num
 * componente.
 */
/**
 * `'autenticado'` é a única entrada fora do sistema de permissões, de
 * propósito: Visão Geral (página inicial, todo mundo precisa de uma) e
 * Segurança (MFA da PRÓPRIA conta — tirar isso impediria alguém de se
 * proteger). Todo o resto é uma chave granular real.
 */
export type PermissaoNecessaria = 'autenticado' | ChaveDePermissao

/**
 * Uma chave, ou VÁRIAS querendo dizer "qualquer uma destas".
 *
 * Existe porque uma tela pode reunir seções com permissões diferentes —
 * Mensagens junta `automacao.mensagens.ver`, `automacao.encerramento.ver` e
 * `automacao.respostas.ver`. Quem tem só uma das três precisa ver o item no
 * menu e entrar na tela; o que ele NÃO pode ver são as outras abas, e isso
 * quem decide é a própria página, aba por aba. Uma permissão só no item
 * ampliaria acesso (mostraria aba proibida) ou tiraria acesso (esconderia a
 * tela de quem tem direito a uma parte) — os dois inaceitáveis.
 */
export type PermissaoDeAcesso = PermissaoNecessaria | readonly PermissaoNecessaria[]

export interface ItemDeNavegacao {
  tipo: 'item'
  rotulo: string
  rota: string
  icone: LucideIcon
  permissao: PermissaoDeAcesso
  /** Subtítulo do cabeçalho global quando esta rota está ativa. */
  descricao?: string
  /** Rótulo do grupo/pai mostrado como contexto no cabeçalho (ex.: "Automação"). */
  grupoPai?: string
  /**
   * A tela existe e tem rota, mas NÃO aparece no rail — o caminho até ela é
   * outro (hoje: o menu de perfil do cabeçalho, para Aparência e Segurança).
   *
   * Continua declarada aqui, e não numa segunda lista, porque `todosOsItens()`
   * é quem monta as `<Route>`: tirar do arquivo não esconderia a tela,
   * APAGARIA a rota. Só `navegacaoVisivel()` (o que o rail desenha) respeita
   * esta marca; rota, permissão e prefetch seguem valendo igual aos demais.
   */
  ocultoNoMenu?: boolean
  /**
   * Mesma função usada pelo `lazy()` da rota — chamá-la de novo no
   * hover/foco do item serve de prefetch, porque `import()` do mesmo módulo
   * é deduplicado pelo navegador/bundler.
   */
  carregar: () => Promise<{ default: ComponentType }>
}

export interface SubmenuDeNavegacao {
  tipo: 'submenu'
  rotulo: string
  icone: LucideIcon
  permissao: PermissaoNecessaria
  itens: ItemDeNavegacao[]
}

export type EntradaDeNavegacao = ItemDeNavegacao | SubmenuDeNavegacao

export interface GrupoDeNavegacao {
  /** `null` para a entrada solta do topo (Visão Geral), sem rótulo de grupo. */
  titulo: string | null
  entradas: EntradaDeNavegacao[]
}

function item(dados: Omit<ItemDeNavegacao, 'tipo'>): ItemDeNavegacao {
  return { tipo: 'item', ...dados }
}

function submenu(dados: Omit<SubmenuDeNavegacao, 'tipo'>): SubmenuDeNavegacao {
  return { tipo: 'submenu', ...dados }
}

/**
 * Adapta o thunk de `import()` (que devolve o módulo inteiro, com export
 * nomeado) para o formato `{ default }` que `React.lazy` espera — num lugar
 * só, para as 13 entradas abaixo não repetirem o `.then(...)`.
 */
function pagina<Chave extends string>(
  importar: () => Promise<Record<Chave, ComponentType>>,
  nomeExportado: Chave,
): () => Promise<{ default: ComponentType }> {
  return () => importar().then((modulo) => ({ default: modulo[nomeExportado] }))
}

export const NAVEGACAO: GrupoDeNavegacao[] = [
  {
    titulo: null,
    entradas: [
      item({
        rotulo: 'Visão Geral',
        rota: '/',
        icone: Home,
        permissao: 'autenticado',
        descricao: 'Como está a operação agora',
        carregar: pagina(() => import('@/pages/pagina-visao-geral'), 'PaginaVisaoGeral'),
      }),
    ],
  },
  {
    titulo: 'Operação',
    entradas: [
      item({
        rotulo: 'Atendimento',
        rota: '/atendimentos',
        icone: Inbox,
        permissao: 'atendimento.ver',
        descricao: 'Conversas do WhatsApp do cartório',
        carregar: pagina(() => import('@/pages/pagina-inbox'), 'PaginaInbox'),
      }),
      item({
        rotulo: 'Clientes',
        rota: '/clientes',
        icone: Users,
        permissao: 'clientes.ver',
        descricao: 'Contatos e qualificação das partes atendidas',
        carregar: pagina(() => import('@/pages/pagina-clientes'), 'PaginaClientes'),
      }),
      item({
        rotulo: 'Serviços',
        rota: '/servicos',
        icone: Scale,
        permissao: 'servicos.ver',
        descricao: 'Catálogo de atos e prazo de cada um',
        carregar: pagina(() => import('@/pages/pagina-servicos'), 'PaginaServicos'),
      }),
    ],
  },
  {
    titulo: 'Gestão',
    entradas: [
      item({
        rotulo: 'Indicadores e Relatórios',
        rota: '/indicadores',
        icone: BarChart3,
        permissao: 'relatorios.ver',
        descricao: 'Análise do período — setor, etapa, equipe e automação',
        carregar: pagina(() => import('@/pages/pagina-indicadores'), 'PaginaIndicadores'),
      }),
    ],
  },
  {
    titulo: 'Automação',
    entradas: [
      // Automáticas, encerramento e respostas rápidas eram TRÊS itens aqui —
      // três destinos para variações da mesma coisa (texto que sai da
      // serventia), cada uma com a PRÓPRIA chave de permissão. Viraram seções
      // de UMA tela (`/automacao/mensagens/:secao`); o array abaixo é
      // "qualquer uma destas" — quem tem só uma das três chaves já vê o item
      // e entra na tela, e é a própria `PaginaMensagens` quem decide, aba por
      // aba, quais seções esta conta específica alcança. As rotas antigas
      // continuam resolvendo por redirect em `app/rotas.tsx`.
      item({
        rotulo: 'Mensagens',
        rota: '/automacao/mensagens',
        icone: MessageSquare,
        permissao: ['automacao.mensagens.ver', 'automacao.respostas.ver', 'automacao.encerramento.ver'],
        grupoPai: 'Automação',
        descricao: 'Automáticas por gatilho, encerramento por tempo e respostas rápidas',
        carregar: pagina(() => import('@/pages/pagina-mensagens'), 'PaginaMensagens'),
      }),
      // Mesma fusão: "Conhecimento da IA" virou seção de `/automacao/ia`.
      item({
        rotulo: 'IA',
        rota: '/automacao/ia',
        icone: Bot,
        permissao: ['ia.ver', 'ia.conhecimento.ver'],
        grupoPai: 'Automação',
        descricao: 'O que a Resposta Inteligente entregou e o que ela sabe',
        carregar: pagina(() => import('@/pages/pagina-ia'), 'PaginaIa'),
      }),
    ],
  },
  {
    titulo: 'Administração',
    entradas: [
      item({
        rotulo: 'Equipe',
        rota: '/equipe',
        icone: UserRound,
        permissao: 'equipe.ver',
        descricao: 'Atendentes, cargos e permissões de cada um',
        carregar: pagina(() => import('@/pages/pagina-equipe'), 'PaginaEquipe'),
      }),
      // Blindagem LGPD (2026-09): quem abriu qual conversa/ficha/anexo, os
      // eventos do SEC-03 e o resumo do que já está protegido. Visível no
      // rail (não é conta pessoal como Aparência/Segurança) — é o que
      // sustenta a promessa "o programador não lê sem deixar rastro".
      item({
        rotulo: 'Acesso aos dados',
        rota: '/acesso-aos-dados',
        icone: History,
        permissao: 'seguranca.eventos.ver',
        descricao: 'Quem abriu o quê, e o que já está protegido',
        carregar: pagina(() => import('@/pages/pagina-acesso-aos-dados'), 'PaginaAcessoAosDados'),
      }),
      // suporte.solicitar_acesso é exclusiva da conta técnica da equipe do projeto —
      // excluída de propósito de todo preset comum (CatalogoDePermissoes.cs).
      // Ninguém do cartório vê este item; só a conta técnica, quando logada.
      item({
        rotulo: 'Suporte técnico',
        rota: '/suporte',
        icone: LifeBuoy,
        permissao: 'suporte.solicitar_acesso',
        descricao: 'Pedir acesso técnico temporário, com motivo',
        carregar: pagina(() => import('@/pages/pagina-suporte-tecnico'), 'PaginaSuporteTecnico'),
      }),
      submenu({
        rotulo: 'Estrutura da operação',
        icone: GitBranch,
        // Container: visível se QUALQUER filho for — a permissão aqui não é
        // checada sozinha, é a filtragem de itens em `navegacaoVisivel` que decide.
        permissao: 'etapas.ver',
        itens: [
          item({
            rotulo: 'Etapas',
            rota: '/estrutura/etapas',
            icone: GitBranch,
            permissao: 'etapas.ver',
            grupoPai: 'Estrutura da operação',
            descricao: 'Etapas do funil de atendimento',
            carregar: pagina(() => import('@/pages/pagina-etapas'), 'PaginaEtapas'),
          }),
          item({
            rotulo: 'Setores',
            rota: '/estrutura/setores',
            icone: Building2,
            permissao: 'setores.ver',
            grupoPai: 'Estrutura da operação',
            descricao: 'Quem atende o quê',
            carregar: pagina(() => import('@/pages/pagina-setores'), 'PaginaSetores'),
          }),
        ],
      }),
      // Era um submenu de UM filho só: dois cliques para chegar num destino
      // único. Virou item direto — o dia que existir uma segunda
      // integração, volta a ser submenu.
      item({
        rotulo: 'WhatsApp',
        rota: '/integracoes/whatsapp',
        icone: Plug,
        permissao: 'whatsapp.ver',
        descricao: 'Pareamento do canal real',
        carregar: pagina(() => import('@/pages/pagina-whatsapp'), 'PaginaWhatsApp'),
      }),
      // Fora do rail, no menu de perfil: escolhe a textura de fundo do fio e
      // se configura uma vez na vida — não sustenta assento permanente numa
      // navegação que o atendente percorre o dia inteiro.
      item({
        rotulo: 'Aparência',
        rota: '/aparencia',
        icone: Palette,
        permissao: 'aparencia.ver',
        ocultoNoMenu: true,
        descricao: 'Textura de fundo do fio de mensagens',
        carregar: pagina(() => import('@/pages/pagina-aparencia'), 'PaginaAparencia'),
      }),
      // Idem, e por um motivo a mais: é tela de CONTA, não de operação. Vive
      // no menu de perfil, que é onde "minha conta" pertence e onde TODO
      // autenticado chega — a permissão continua `autenticado` de propósito
      // (cada um gerencia o próprio MFA; não é ação de gestão, e por isso
      // também não podia virar sub-item de Equipe).
      item({
        rotulo: 'Segurança',
        rota: '/seguranca',
        icone: ShieldCheck,
        permissao: 'autenticado',
        ocultoNoMenu: true,
        descricao: 'Verificação em duas etapas da sua conta',
        carregar: pagina(() => import('@/pages/pagina-seguranca'), 'PaginaSeguranca'),
      }),
    ],
  },
]

/** Testa uma chave — o mesmo formato de `usarAutenticacao().pode`. */
export type VerificadorDePermissao = (chave: ChaveDePermissao) => boolean

/**
 * Regra de visibilidade por permissão — exportada para não ser duplicada por
 * quem monta navegação própria fora do menu (ex.: `features/acoes-rapidas`).
 *
 * Um array em `permissao` é "qualquer uma destas" — o item (ou a rota) fica
 * visível para quem tem PELO MENOS uma das seções da tela. É a página quem
 * decide, aba por aba, qual seção cada permissão específica realmente abre;
 * aqui só se decide "existe algum motivo para esta conta ver isto".
 */
export function podeVer(permissao: PermissaoDeAcesso, pode: VerificadorDePermissao): boolean {
  if (Array.isArray(permissao)) return permissao.some((chave) => podeVer(chave, pode))
  if (permissao === 'autenticado') return true
  return pode(permissao as ChaveDePermissao)
}

/**
 * A árvore filtrada pelo que esta conta pode ver — nunca amplia permissão,
 * só esconde. O backend continua sendo quem decide de verdade em cada
 * endpoint (`ctx.Pode`): esconder aqui é conforto de navegação, não
 * segurança.
 */
export function navegacaoVisivel(pode: VerificadorDePermissao): GrupoDeNavegacao[] {
  return NAVEGACAO.map((grupo) => ({
    ...grupo,
    entradas: grupo.entradas
      .map((entrada) =>
        entrada.tipo === 'submenu'
          ? { ...entrada, itens: entrada.itens.filter((filho) => podeVer(filho.permissao, pode)) }
          : entrada,
      )
      .filter((entrada) =>
        entrada.tipo === 'submenu'
          ? entrada.itens.length > 0
          : // `ocultoNoMenu` é só sobre DESENHAR no rail: a rota, a permissão
            // e o prefetch do item continuam existindo em `todosOsItens()`.
            !entrada.ocultoNoMenu && podeVer(entrada.permissao, pode),
      ),
  })).filter((grupo) => grupo.entradas.length > 0)
}

/**
 * Os itens que existem como tela mas não aparecem no rail — hoje o que o menu
 * de perfil do cabeçalho desenha (Aparência e Segurança). Filtrado pela mesma
 * permissão do rail: esconder aqui é conforto, o backend é quem barra.
 */
export function itensForaDoMenu(pode: VerificadorDePermissao): ItemDeNavegacao[] {
  return todosOsItens().filter((item) => item.ocultoNoMenu && podeVer(item.permissao, pode))
}

/** Todas as folhas (itens de rota), sem os submenus — para montar as `<Route>`. */
export function todosOsItens(): ItemDeNavegacao[] {
  return NAVEGACAO.flatMap((grupo) =>
    grupo.entradas.flatMap((entrada) => (entrada.tipo === 'submenu' ? entrada.itens : [entrada])),
  )
}

/**
 * O item cuja rota corresponde ao caminho atual — usado pelo cabeçalho global
 * para título/descrição e pelo rail para marcar o item ativo. Ordenado do
 * caminho mais específico para o mais genérico, para `/atendimentos/:id`
 * casar com o item de `/atendimentos`.
 */
export function itemPelaRota(pathname: string): ItemDeNavegacao | undefined {
  return [...todosOsItens()]
    .sort((a, b) => b.rota.length - a.rota.length)
    .find((candidato) => pathname === candidato.rota || pathname.startsWith(`${candidato.rota}/`))
}
