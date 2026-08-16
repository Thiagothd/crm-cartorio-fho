/**
 * Máscaras de ENTRADA: formatam enquanto se digita e aguentam valor pela metade.
 *
 * Não confundir com `lib/formatacao.ts`, que é o par de SAÍDA. Aquelas funções
 * exigem o valor completo e devolvem lixo com valor parcial —
 * `formatarTelefone('389')` produz `+389`. As duas famílias coexistem de
 * propósito: uma serve a tabela, a outra serve ao campo.
 *
 * Regra que vale para todas: **nunca emitir separador à direita**. `38` vira
 * `(38`, jamais `(38) `. Sem isso o backspace trava, porque a máscara recoloca
 * na hora o separador que o usuário acabou de apagar e o campo nunca esvazia.
 */

export function somenteDigitos(valor: string) {
  return valor.replace(/\D/g, '')
}

/** `5538999000045` ou `38999000045` -> `(38) 99912-0045`. Fixo sai `(38) 3221-1234`. */
export function mascararTelefone(valor: string) {
  let digitos = somenteDigitos(valor)

  // O banco guarda em E.164 e devolve assim na edição. No campo o número aparece
  // como o balcão fala, sem o código do país; o backend recoloca o 55 ao salvar
  // (ClienteService.ValidarTelefone). Só descarta acima de 11 dígitos, senão um
  // celular de DDD 55 (Santa Maria/RS) perderia o próprio DDD.
  if (digitos.startsWith('55') && digitos.length > 11) {
    digitos = digitos.slice(2)
  }

  digitos = digitos.slice(0, 11)

  if (digitos.length <= 2) {
    return digitos.length > 0 ? `(${digitos}` : ''
  }

  const ddd = digitos.slice(0, 2)
  const numero = digitos.slice(2)
  // Celular tem 9 dígitos e quebra em 5; fixo tem 8 e quebra em 4.
  const corte = numero.length > 8 ? 5 : 4

  return numero.length <= corte
    ? `(${ddd}) ${numero}`
    : `(${ddd}) ${numero.slice(0, corte)}-${numero.slice(corte)}`
}

/** `39505000` -> `39505-000`. */
export function mascararCep(valor: string) {
  const digitos = somenteDigitos(valor).slice(0, 8)

  return digitos.length > 5 ? `${digitos.slice(0, 5)}-${digitos.slice(5)}` : digitos
}

/** `12345678909` -> `123.456.789-09`. */
export function mascararCpf(valor: string) {
  const d = somenteDigitos(valor).slice(0, 11)
  let saida = d.slice(0, 3)

  if (d.length > 3) saida += `.${d.slice(3, 6)}`
  if (d.length > 6) saida += `.${d.slice(6, 9)}`
  if (d.length > 9) saida += `-${d.slice(9)}`

  return saida
}

/** `21351226000110` -> `21.351.226/0001-10`. */
export function mascararCnpj(valor: string) {
  const d = somenteDigitos(valor).slice(0, 14)
  let saida = d.slice(0, 2)

  if (d.length > 2) saida += `.${d.slice(2, 5)}`
  if (d.length > 5) saida += `.${d.slice(5, 8)}`
  if (d.length > 8) saida += `/${d.slice(8, 12)}`
  if (d.length > 12) saida += `-${d.slice(12)}`

  return saida
}

/**
 * Dígito verificador do CPF (módulo 11).
 *
 * É conferência matemática, não consulta: NÃO existe fonte pública e legal que
 * devolva dado pessoal a partir de CPF, e este produto não vai procurar uma.
 * Ver DESIGN.md, seção 11.
 *
 * Quem chama trata o `false` como AVISO, nunca como bloqueio — no balcão aparece
 * documento antigo e documento de terceiro, e recusar por conta disso travaria
 * atendimento. Mesma razão registrada em ClienteService.ValidarDocumento.
 */
export function cpfEhValido(valor: string) {
  const d = somenteDigitos(valor)

  if (d.length !== 11 || /^(\d)\1{10}$/.test(d)) {
    return false
  }

  const verificador = (ate: number) => {
    let soma = 0

    for (let i = 0; i < ate; i++) {
      soma += Number(d[i]) * (ate + 1 - i)
    }

    const resto = (soma * 10) % 11

    return resto === 10 ? 0 : resto
  }

  return verificador(9) === Number(d[9]) && verificador(10) === Number(d[10])
}

/** Dígito verificador do CNPJ (módulo 11). Evita consulta certa de dar errado. */
export function cnpjEhValido(valor: string) {
  const d = somenteDigitos(valor)

  if (d.length !== 14 || /^(\d)\1{13}$/.test(d)) {
    return false
  }

  const verificador = (ate: number) => {
    let peso = ate - 7
    let soma = 0

    for (let i = 0; i < ate; i++) {
      soma += Number(d[i]) * peso
      peso = peso === 2 ? 9 : peso - 1
    }

    const resto = soma % 11

    return resto < 2 ? 0 : 11 - resto
  }

  return verificador(12) === Number(d[12]) && verificador(13) === Number(d[13])
}
