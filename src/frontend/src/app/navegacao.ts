import type { ComponentType } from 'react'
import type { LucideIcon } from 'lucide-react'
import {
  BarChart3,
  Bot,
  Building2,
  GitBranch,
  Home,
  Inbox,
  MessageSquare,
  Palette,
  Plug,
  Scale,
  ShieldCheck,
  TimerOff,
  UserRound,
  Users,
  Zap,
} from 'lucide-react'

/**
 * Fonte única de verdade da navegação. Rotas (`rotas.tsx`), o rail, o
 * cabeçalho global, a barra inferior do celular, a folha "Mais" e os testes
 * leem daqui — nunca existe uma segunda lista de itens de menu duplicada num
 * componente.
 */
export type PerfilNecessario = 'autenticado' | 'gestao' | 'administrador'

export interface ItemDeNavegacao {
  tipo: 'item'
  rotulo: string
  rota: string
  icone: LucideIcon
  permissao: PerfilNecessario
  /** Subtítulo do cabeçalho global quando esta rota está ativa. */
  descricao?: string
  /** Rótulo do grupo/pai mostrado como contexto no cabeçalho (ex.: "Automação"). */
  grupoPai?: string
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
  permissao: PerfilNecessario
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
        permissao: 'autenticado',
        descricao: 'Conversas do WhatsApp do cartório',
        carregar: pagina(() => import('@/pages/pagina-inbox'), 'PaginaInbox'),
      }),
      item({
        rotulo: 'Clientes',
        rota: '/clientes',
        icone: Users,
        permissao: 'autenticado',
        descricao: 'Contatos e qualificação das partes atendidas',
        carregar: pagina(() => import('@/pages/pagina-clientes'), 'PaginaClientes'),
      }),
      item({
        rotulo: 'Serviços',
        rota: '/servicos',
        icone: Scale,
        permissao: 'gestao',
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
        permissao: 'gestao',
        descricao: 'Análise do período — setor, etapa, equipe e automação',
        carregar: pagina(() => import('@/pages/pagina-indicadores'), 'PaginaIndicadores'),
      }),
    ],
  },
  {
    titulo: 'Automação',
    entradas: [
      item({
        rotulo: 'Mensagens automáticas',
        rota: '/automacao/mensagens',
        icone: MessageSquare,
        permissao: 'gestao',
        grupoPai: 'Automação',
        descricao: 'Regras de resposta por gatilho',
        carregar: pagina(() => import('@/pages/pagina-mensagens-automaticas'), 'PaginaMensagensAutomaticas'),
      }),
      item({
        rotulo: 'Respostas rápidas',
        rota: '/automacao/respostas',
        icone: Zap,
        permissao: 'gestao',
        grupoPai: 'Automação',
        descricao: 'Atalhos de texto para o atendente',
        carregar: pagina(() => import('@/pages/pagina-respostas-rapidas'), 'PaginaRespostasRapidas'),
      }),
      item({
        rotulo: 'IA',
        rota: '/automacao/ia',
        icone: Bot,
        permissao: 'gestao',
        grupoPai: 'Automação',
        descricao: 'Resposta Inteligente — status e uso do dia',
        carregar: pagina(() => import('@/pages/pagina-ia'), 'PaginaIa'),
      }),
      item({
        rotulo: 'Encerramento automático',
        rota: '/automacao/encerramento',
        icone: TimerOff,
        permissao: 'gestao',
        grupoPai: 'Automação',
        descricao: 'Tempo até perguntar e até encerrar conversa parada',
        carregar: pagina(
          () => import('@/pages/pagina-encerramento-automatico'),
          'PaginaEncerramentoAutomatico',
        ),
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
        permissao: 'administrador',
        descricao: 'Atendentes, perfis e cargos',
        carregar: pagina(() => import('@/pages/pagina-equipe'), 'PaginaEquipe'),
      }),
      submenu({
        rotulo: 'Estrutura da operação',
        icone: GitBranch,
        permissao: 'gestao',
        itens: [
          item({
            rotulo: 'Etapas',
            rota: '/estrutura/etapas',
            icone: GitBranch,
            permissao: 'gestao',
            grupoPai: 'Estrutura da operação',
            descricao: 'Etapas do funil de atendimento',
            carregar: pagina(() => import('@/pages/pagina-etapas'), 'PaginaEtapas'),
          }),
          item({
            rotulo: 'Setores',
            rota: '/estrutura/setores',
            icone: Building2,
            permissao: 'gestao',
            grupoPai: 'Estrutura da operação',
            descricao: 'Quem atende o quê',
            carregar: pagina(() => import('@/pages/pagina-setores'), 'PaginaSetores'),
          }),
        ],
      }),
      item({
        rotulo: 'Aparência',
        rota: '/aparencia',
        icone: Palette,
        permissao: 'gestao',
        descricao: 'Textura de fundo do fio de mensagens',
        carregar: pagina(() => import('@/pages/pagina-aparencia'), 'PaginaAparencia'),
      }),
      submenu({
        rotulo: 'Integrações',
        icone: Plug,
        permissao: 'administrador',
        itens: [
          item({
            rotulo: 'WhatsApp',
            rota: '/integracoes/whatsapp',
            icone: Plug,
            permissao: 'administrador',
            grupoPai: 'Integrações',
            descricao: 'Pareamento do canal real',
            carregar: pagina(() => import('@/pages/pagina-whatsapp'), 'PaginaWhatsApp'),
          }),
        ],
      }),
    ],
  },
  {
    titulo: 'Conta',
    entradas: [
      item({
        rotulo: 'Segurança',
        rota: '/seguranca',
        icone: ShieldCheck,
        // Qualquer autenticado gerencia o PRÓPRIO MFA — não é ação de gestão.
        permissao: 'autenticado',
        descricao: 'Verificação em duas etapas da sua conta',
        carregar: pagina(() => import('@/pages/pagina-seguranca'), 'PaginaSeguranca'),
      }),
    ],
  },
]

interface PerfilParaVisibilidade {
  podeGerirCadastros: boolean
  podeGerirAtendentes: boolean
}

function permitido(permissao: PerfilNecessario, perfil: PerfilParaVisibilidade): boolean {
  if (permissao === 'administrador') return perfil.podeGerirAtendentes
  if (permissao === 'gestao') return perfil.podeGerirCadastros
  return true
}

/**
 * A árvore filtrada pelo que este perfil pode ver — nunca amplia permissão,
 * só esconde. O backend continua sendo quem decide de verdade (regra 17 do
 * AGENTS.md): esconder aqui é conforto de navegação, não segurança.
 */
export function navegacaoVisivel(perfil: PerfilParaVisibilidade): GrupoDeNavegacao[] {
  return NAVEGACAO.map((grupo) => ({
    ...grupo,
    entradas: grupo.entradas
      .map((entrada) =>
        entrada.tipo === 'submenu'
          ? { ...entrada, itens: entrada.itens.filter((filho) => permitido(filho.permissao, perfil)) }
          : entrada,
      )
      .filter((entrada) =>
        entrada.tipo === 'submenu' ? entrada.itens.length > 0 : permitido(entrada.permissao, perfil),
      ),
  })).filter((grupo) => grupo.entradas.length > 0)
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
