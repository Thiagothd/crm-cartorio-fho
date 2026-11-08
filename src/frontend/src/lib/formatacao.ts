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

const mesCurto = new Intl.DateTimeFormat('pt-BR', { month: 'short', year: '2-digit' })

const mesExtenso = new Intl.DateTimeFormat('pt-BR', { month: 'long', year: 'numeric' })

function paraData(valor: string | Date): Date {
  return valor instanceof Date ? valor : new Date(valor)
}

export function formatarHora(valor: string | Date) {
  return horaCurta.format(paraData(valor))
}

/** "21/08" — usado no eixo de gráfico e em qualquer lugar que precise só do dia. */
export function formatarDiaCurto(valor: string | Date) {
  return dataCurta.format(paraData(valor))
}

export function formatarDataHora(valor: string | Date) {
  return dataHora.format(paraData(valor))
}

/** "ago/26" — eixo X de gráfico mensal, onde não cabe o nome inteiro. */
export function formatarMesCurto(valor: string | Date) {
  // `replace` remove o ponto que o pt-BR põe na abreviação ("ago." -> "ago").
  return mesCurto.format(paraData(valor)).replace('.', '')
}

/** "agosto de 2026" — título de dica e cabeçalho, onde há espaço. */
export function formatarMesExtenso(valor: string | Date) {
  return mesExtenso.format(paraData(valor))
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

/**
 * Duração absoluta em linguagem de balcão: "42 min", "3h 20min", "2d 4h".
 *
 * Diferente de `descreverDistanciaDePrazo`: aquela mede a distância até AGORA
 * e já vem com "faltam"/"atrasado" embutido — não serve para "tempo até a
 * primeira resposta" ou qualquer duração que já aconteceu e não muda mais.
 */
export function formatarDuracaoMinutos(minutos: number | null): string | null {
  if (minutos === null || !Number.isFinite(minutos)) {
    return null
  }

  const totalMinutos = Math.round(Math.abs(minutos))

  if (totalMinutos < 60) {
    return `${totalMinutos} min`
  }

  const totalHoras = Math.floor(totalMinutos / 60)
  const minutosRestantes = totalMinutos % 60

  if (totalHoras < 24) {
    return minutosRestantes === 0 ? `${totalHoras}h` : `${totalHoras}h ${minutosRestantes}min`
  }

  const dias = Math.floor(totalHoras / 24)
  const horasRestantes = totalHoras % 24

  return horasRestantes === 0 ? `${dias}d` : `${dias}d ${horasRestantes}h`
}

/**
 * Duração curta em segundos, para latência de IA (1,4s a ~20s) — faixa boa
 * demais para `formatarDuracaoMinutos`, que arredonda pro minuto inteiro e
 * mostraria "0 min" para qualquer resposta abaixo de 30s.
 */
export function formatarSegundos(segundos: number | null): string | null {
  if (segundos === null || !Number.isFinite(segundos)) {
    return null
  }

  return `${segundos.toFixed(1).replace('.', ',')}s`
}

/**
 * Variação percentual com sinal explícito, para cards de delta:
 * 12.3 -> "+12,3%", -8 -> "-8,0%", 0 -> "0,0%". `null` quando não há base de
 * comparação (ver `DeltaPercentual` no backend) — a tela mostra "novo", nunca
 * inventa um percentual sobre zero.
 */
export function formatarDelta(valor: number | null): string | null {
  if (valor === null || !Number.isFinite(valor)) {
    return null
  }

  const sinal = valor > 0 ? '+' : ''
  return `${sinal}${valor.toFixed(1).replace('.', ',')}%`
}

/** Número inteiro com separador de milhar em pt-BR: 1234 -> "1.234". */
export function formatarNumero(valor: number): string {
  return new Intl.NumberFormat('pt-BR').format(valor)
}

/** 0 a 100 já em percentual, uma casa decimal: 33.333 -> "33,3%". */
export function formatarPercentual(valor: number): string {
  return `${valor.toFixed(1).replace('.', ',')}%`
}

/** Hora do dia para eixo de gráfico: 14 -> "14h". */
export function formatarHoraDoDia(hora: number): string {
  return `${hora}h`
}

/** Valor em reais, pt-BR: 12.5 -> "R$ 12,50". Usado só para estimativa de custo — nunca fatura de verdade. */
export function formatarReais(valor: number): string {
  return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(valor)
}

/** 5538999000045 -> (38) 99900-0045. Numero nao brasileiro sai como veio. */
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

/**
 * Como chamar o contato na tela. Sem nome utilizável, o telefone formatado toma
 * o lugar — melhor "(19) 99000-1488" do que um card cujo nome é ".".
 *
 * O `pendente` vem pronto do backend (`nomePendente` / `clienteNomePendente`):
 * a regra do que conta como nome utilizável mora em `NomeDeContato.EhUtil`, e
 * reimplementá-la aqui em TypeScript criaria dois donos da mesma decisão.
 */
export function nomeDeExibicao(nome: string, telefone: string, pendente: boolean) {
  return pendente ? formatarTelefone(telefone) : nome
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
