/** Formatacao para exibicao. Datas chegam em UTC e saem no fuso do navegador. */

const horaCurta = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' })

const dataCurta = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit' })

const dataHora = new Intl.DateTimeFormat('pt-BR', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})

function paraData(valor: string | Date): Date {
  return valor instanceof Date ? valor : new Date(valor)
}

export function formatarHora(valor: string | Date) {
  return horaCurta.format(paraData(valor))
}

export function formatarDataHora(valor: string | Date) {
  return dataHora.format(paraData(valor))
}

/** Hoje mostra so a hora; nos ultimos 7 dias, o dia da semana; antes disso, a data. */
export function formatarMomentoRelativo(valor: string | Date) {
  const data = paraData(valor)
  const agora = new Date()

  const mesmoDia =
    data.getDate() === agora.getDate() &&
    data.getMonth() === agora.getMonth() &&
    data.getFullYear() === agora.getFullYear()

  if (mesmoDia) {
    return formatarHora(data)
  }

  const diferencaEmDias = Math.floor((agora.getTime() - data.getTime()) / 86_400_000)

  if (diferencaEmDias === 1) {
    return 'ontem'
  }

  if (diferencaEmDias < 7 && diferencaEmDias > 0) {
    return new Intl.DateTimeFormat('pt-BR', { weekday: 'short' }).format(data).replace('.', '')
  }

  return dataCurta.format(data)
}

/** Distancia ate o prazo em linguagem de balcao: "faltam 3h", "atrasado 2d". */
export function descreverDistanciaDePrazo(limite: string | Date | null): string | null {
  if (!limite) {
    return null
  }

  const alvo = paraData(limite).getTime()
  const agora = Date.now()
  const minutos = Math.round((alvo - agora) / 60_000)
  const atrasado = minutos < 0
  const absoluto = Math.abs(minutos)

  const texto =
    absoluto < 60
      ? `${absoluto}min`
      : absoluto < 1440
        ? `${Math.floor(absoluto / 60)}h`
        : `${Math.floor(absoluto / 1440)}d`

  return atrasado ? `atrasado ${texto}` : `faltam ${texto}`
}

/** 5538999000045 -> (38) 99912-0045. Numero nao brasileiro sai como veio. */
export function formatarTelefone(e164: string) {
  const digitos = e164.replace(/\D/g, '')

  if (digitos.startsWith('55') && (digitos.length === 13 || digitos.length === 12)) {
    const ddd = digitos.slice(2, 4)
    const resto = digitos.slice(4)
    const meio = resto.length === 9 ? resto.slice(0, 5) : resto.slice(0, 4)
    const fim = resto.length === 9 ? resto.slice(5) : resto.slice(4)
    return `(${ddd}) ${meio}-${fim}`
  }

  return `+${digitos}`
}

export function formatarDocumento(documento: string | null) {
  if (!documento) {
    return null
  }

  const d = documento.replace(/\D/g, '')

  if (d.length === 11) {
    return `${d.slice(0, 3)}.${d.slice(3, 6)}.${d.slice(6, 9)}-${d.slice(9)}`
  }

  if (d.length === 14) {
    return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}`
  }

  return documento
}

export function iniciaisDe(nome: string) {
  const partes = nome.trim().split(/\s+/).filter(Boolean)

  if (partes.length === 0) {
    return '?'
  }

  if (partes.length === 1) {
    return partes[0].slice(0, 2).toUpperCase()
  }

  return (partes[0][0] + partes[partes.length - 1][0]).toUpperCase()
}

/**
 * Tamanho de arquivo legivel. Cartorio recebe foto de documento o dia todo, e
 * "2,4 MB" diz mais a quem vai abrir num computador de balcao do que "2517483".
 *
 * Nasceu dentro de `anexo-da-mensagem.tsx`, e subiu para ca quando o terceiro
 * lugar precisou dele: o balao do fio, a lista de documentos da galeria e o
 * cabecalho do visualizador. Tres copias divergiriam na virgula decimal.
 */
export function formatarTamanhoDeArquivo(bytes: number | null): string | null {
  if (bytes === null || bytes <= 0) {
    return null
  }

  if (bytes < 1024) {
    return `${bytes} B`
  }

  const kb = bytes / 1024
  if (kb < 1024) {
    return `${Math.round(kb)} KB`
  }

  const mb = kb / 1024
  if (mb < 1024) {
    return `${mb.toFixed(1).replace('.', ',')} MB`
  }

  // Um degrau a mais que a versao original, para nao existir "1024,0 MB" na
  // tela. Anexo desse tamanho nao passa pelo teto de 16MB do envio, mas pode
  // ter entrado por outro caminho amanha.
  return `${(mb / 1024).toFixed(1).replace('.', ',')} GB`
}

/**
 * "certidao-nascimento.PDF" -> "PDF". Nulo quando nao ha extensao reconhecivel.
 *
 * Teto de 5 caracteres de proposito: nome com ponto no meio ("Joao S. Silva
 * procuracao") nao pode virar uma "extensao" de trinta letras na tela.
 */
export function extensaoDoArquivo(nome: string | null): string | null {
  if (!nome) {
    return null
  }

  const ponto = nome.lastIndexOf('.')

  if (ponto <= 0 || ponto === nome.length - 1) {
    return null
  }

  const extensao = nome.slice(ponto + 1)

  return extensao.length <= 5 ? extensao.toUpperCase() : null
}
