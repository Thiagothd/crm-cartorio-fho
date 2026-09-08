/**
 * Minúsculas e sem acento, para a busca digitada no balcão casar com o que
 * está gravado ("servico" acha "Serviço"). Vivia copiada, idêntica, em cada
 * aba desta pasta.
 *
 * A faixa é a dos sinais diacríticos combinantes, que é o que a decomposição
 * NFD separa da letra. Montada com `new RegExp` para o intervalo aparecer
 * escapado no código, e não como dois caracteres invisíveis no arquivo.
 */
const DIACRITICOS = new RegExp('[\\u0300-\\u036f]', 'g')

export function normalizarTexto(texto: string) {
  return texto.normalize('NFD').replace(DIACRITICOS, '').toLowerCase()
}
