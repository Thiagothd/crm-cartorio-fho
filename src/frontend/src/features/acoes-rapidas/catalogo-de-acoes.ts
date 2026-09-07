import type { LucideIcon } from 'lucide-react'
import { Hourglass, Search, UserRound, UserX } from 'lucide-react'
import { todosOsItens, type PerfilNecessario } from '@/app/navegacao'

export type ComandoDeAcao = 'abrir-busca'
export type ChaveDeContador = 'fila' | 'aguardando' | 'minhas'

interface AcaoBase {
  chave: string
  rotulo: string
  /** Linha de apoio. Vira `sr-only` quando o número toma o lugar dela. */
  descricao: string
  icone: LucideIcon
  permissao: PerfilNecessario
}

export interface AcaoQueNavega extends AcaoBase {
  tipo: 'rota'
  rota: string
  contador?: ChaveDeContador
  /** Tom do número quando > 0. Zero fica sempre em `tinta`. */
  tom?: 'ambar' | 'selo'
}

export interface AcaoQueComanda extends AcaoBase {
  tipo: 'comando'
  comando: ComandoDeAcao
  /** Texto do `<kbd>` — só quando o atalho já existe de verdade no app. */
  atalho?: string
}

export type AcaoRapida = AcaoQueNavega | AcaoQueComanda

/** Trabalho pendente + descoberta. São sempre quatro — a grade depende disso. */
export const ACOES_DE_TRABALHO: AcaoRapida[] = [
  {
    tipo: 'rota',
    chave: 'fila',
    rota: '/atendimentos?escopo=fila',
    rotulo: 'Fila sem responsável',
    descricao: 'Conversas que ninguém assumiu',
    icone: UserX,
    permissao: 'autenticado',
    contador: 'fila',
    tom: 'ambar',
  },
  {
    tipo: 'rota',
    chave: 'aguardando',
    rota: '/atendimentos?aguardando=1',
    rotulo: 'Aguardando resposta',
    descricao: 'O cliente falou e ninguém respondeu ainda',
    icone: Hourglass,
    permissao: 'autenticado',
    contador: 'aguardando',
    tom: 'ambar',
  },
  {
    tipo: 'rota',
    chave: 'minhas',
    rota: '/atendimentos?escopo=minhas',
    rotulo: 'Meus atendimentos',
    descricao: 'O que está sob a sua responsabilidade',
    icone: UserRound,
    permissao: 'autenticado',
    contador: 'minhas',
  },
  {
    tipo: 'comando',
    chave: 'buscar',
    comando: 'abrir-busca',
    rotulo: 'Buscar',
    descricao: 'Protocolo, nome do cliente ou telefone',
    icone: Search,
    permissao: 'autenticado',
    atalho: 'Ctrl K',
  },
]

const ROTAS_DE_GESTAO = ['/automacao/respostas', '/indicadores', '/integracoes/whatsapp'] as const

const ROTULO_CURTO: Record<string, string> = {
  '/indicadores': 'Indicadores',
  '/integracoes/whatsapp': 'Canal WhatsApp',
}

const PELA_ROTA = new Map(todosOsItens().map((item) => [item.rota, item]))

/**
 * As pílulas de gestão, derivadas de `app/navegacao.ts` — rótulo, ícone,
 * permissão e prefetch vêm de lá para não duplicar a fonte única de verdade.
 * `flatMap` com lista vazia, não `!`: se uma rota for renomeada em
 * `navegacao.ts`, a pílula some em silêncio em vez de derrubar a Visão Geral.
 */
export function acoesDeGestao(): AcaoQueNavega[] {
  return ROTAS_DE_GESTAO.flatMap((rota) => {
    const item = PELA_ROTA.get(rota)
    if (!item) return []

    return [
      {
        tipo: 'rota' as const,
        chave: rota,
        rota,
        rotulo: ROTULO_CURTO[rota] ?? item.rotulo,
        descricao: item.descricao ?? '',
        icone: item.icone,
        permissao: item.permissao,
      },
    ]
  })
}

/** Prefetch da rota, reusando o mesmo thunk do `lazy()`. Ignora query string. */
export function carregarRota(rota: string): (() => void) | undefined {
  const item = PELA_ROTA.get(rota.split('?')[0])
  return item ? () => void item.carregar() : undefined
}
