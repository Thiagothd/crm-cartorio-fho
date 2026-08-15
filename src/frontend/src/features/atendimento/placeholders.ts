import type { Conversa, Setor } from '@/lib/api/tipos'

/**
 * Marcadores aceitos no texto de mensagem automática e de resposta rápida.
 *
 * ESPELHO de `Modules/Configuracoes/Domain/PlaceholdersDeMensagem.cs`. Mexer lá
 * exige mexer aqui — mesma disciplina que `lib/api/tipos.ts` mantém com os
 * contratos.
 *
 * As duas implementações existem porque servem a momentos diferentes: o backend
 * resolve os marcadores no instante em que o sistema dispara a mensagem; este
 * arquivo resolve na hora em que o atendente insere uma resposta rápida no campo,
 * para que ele leia o texto final antes de enviar — e não um "{NomeCliente}" cru.
 */
export const MARCADORES: { token: string; descricao: string }[] = [
  { token: '{NomeCliente}', descricao: 'Nome do cliente como está no cadastro.' },
  { token: '{NomeCartorio}', descricao: 'Nome da serventia.' },
  { token: '{Servico}', descricao: 'Tipo de serviço vinculado ao atendimento.' },
  { token: '{Protocolo}', descricao: 'Protocolo do atendimento (AAAAMM-NNNN).' },
  { token: '{MenuSetores}', descricao: 'Lista numerada dos setores ativos.' },
]

/**
 * Texto usado em {Servico} quando o atendimento ainda não tem serviço captado.
 *
 * Escolhido para não criar concordância de gênero na frase — "Serviço: não
 * informado" funciona, "certidão está pronto" não.
 */
export const SERVICO_NAO_INFORMADO = 'não informado'

export interface DadosParaMensagem {
  nomeCliente: string
  nomeCartorio: string
  servico: string | null
  protocolo: string
  menuSetores: string
}

/** Lista numerada dos setores, na mesma ordem e formato que o backend monta. */
export function montarMenuDeSetores(setores: Setor[]): string {
  return setores.map((setor, indice) => `${indice + 1} - ${setor.nome}`).join('\n')
}

export function dadosDaConversa(
  conversa: Conversa,
  nomeCartorio: string,
  setores: Setor[],
): DadosParaMensagem {
  return {
    nomeCliente: conversa.clienteNome,
    nomeCartorio,
    servico: conversa.tipoServicoNome,
    protocolo: conversa.protocolo,
    menuSetores: montarMenuDeSetores(setores),
  }
}

/** Substituição literal, sem expressão nem condicional — igual ao backend. */
export function aplicarMarcadores(modelo: string, dados: DadosParaMensagem): string {
  return modelo
    .replaceAll('{NomeCliente}', dados.nomeCliente)
    .replaceAll('{NomeCartorio}', dados.nomeCartorio)
    .replaceAll('{Servico}', dados.servico?.trim() ? dados.servico : SERVICO_NAO_INFORMADO)
    .replaceAll('{Protocolo}', dados.protocolo)
    .replaceAll('{MenuSetores}', dados.menuSetores)
}
