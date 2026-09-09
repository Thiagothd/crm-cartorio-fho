import * as React from 'react'
import { cn } from '@/lib/utils'

interface EntradaDeCodigoOtpProps
  extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'onChange' | 'value' | 'type'> {
  value: string
  onChange: (valor: string) => void
  /** Quantos dígitos o código tem. 6 é o padrão do TOTP (RFC 6238). */
  quantidade?: number
}

/**
 * Campo de código de verificação com aparência de dígitos separados.
 *
 * **É um único `<input>` nativo, de propósito.** A alternativa comum — um
 * `<input>` por dígito — quebra três coisas que aqui não podem quebrar:
 * colar o código de uma vez, o preenchimento automático de `one-time-code`, e
 * o nome acessível do campo (leitor de tela passa a anunciar seis caixas sem
 * rótulo em vez de um campo "Código"). Com um campo só, tudo isso continua
 * sendo comportamento nativo do navegador, sem JavaScript nenhum no meio.
 *
 * A aparência de células vem só de CSS: fonte monoespaçada (onde `1ch` é
 * exatamente a largura do glifo), `letter-spacing` calculado para cada dígito
 * cair no centro da sua célula, e um `repeating-linear-gradient` desenhando
 * os divisores. Como a matemática é em `ch` + `rem`, o alinhamento é exato a
 * qualquer tamanho de fonte.
 *
 * O componente NÃO decide borda, raio nem elevação: isso vem por `className`
 * de quem usa — a tela de login segue a linguagem de painel (Fase 3.1) e a de
 * Configurações segue a de documento (Fase 1), e um componente compartilhado
 * não pode escolher por elas.
 */
export const EntradaDeCodigoOtp = React.forwardRef<HTMLInputElement, EntradaDeCodigoOtpProps>(
  ({ value, onChange, quantidade = 6, className, style, ...props }, ref) => {
    const larguraDaCelula = '2.75rem'

    return (
      <input
        ref={ref}
        type="text"
        inputMode="numeric"
        autoComplete="one-time-code"
        autoCapitalize="none"
        autoCorrect="off"
        spellCheck={false}
        maxLength={quantidade}
        value={value}
        // Só dígitos: colar "123 456" ou "123-456" preenche igual, em vez de
        // recusar em silêncio e deixar a pessoa achando que o campo travou.
        onChange={(evento) => onChange(evento.target.value.replace(/\D/g, '').slice(0, quantidade))}
        className={cn(
          // Alinhado à esquerda de propósito: `text-center` centralizaria o
          // trecho já digitado, e com 4 de 6 dígitos os números apareceriam
          // deslocados das próprias células enquanto se digita.
          'h-14 rounded-md border border-linha-forte bg-branco text-left font-dado text-lg text-tinta',
          'transition-colors duration-[var(--duracao-rapida)]',
          'aria-invalid:border-erro aria-invalid:text-erro',
          'disabled:cursor-not-allowed disabled:opacity-50',
          className,
        )}
        style={{
          // Sem isto, o cursor de texto nativo herda o mesmo letterSpacing
          // aplicado depois do ÚLTIMO caractere — invisível enquanto sobra
          // pelo menos uma célula vazia à frente para "receber" o cursor,
          // mas com os 6 dígitos completos o cursor vaza para uma 7ª célula
          // que não existe (achado ao vivo, ver Protocolo de Verificação
          // 2026-09-08/1). O preenchimento em célula continua vindo só do
          // texto — nunca dependeu do cursor para parecer "cheio".
          caretColor: 'transparent',
          // Largura exata das células, para o gradiente e o texto usarem a
          // mesma régua.
          width: `calc(${larguraDaCelula} * ${quantidade})`,
          letterSpacing: `calc(${larguraDaCelula} - 1ch)`,
          // O último dígito não tem célula à direita para "empurrar", então
          // sem esta compensação o conjunto fica meio caractere à esquerda.
          textIndent: `calc((${larguraDaCelula} - 1ch) / 2)`,
          backgroundImage: `repeating-linear-gradient(to right, transparent 0, transparent calc(${larguraDaCelula} - 1px), var(--cor-linha) calc(${larguraDaCelula} - 1px), var(--cor-linha) ${larguraDaCelula})`,
          // O gradiente desenha um divisor a cada célula — inclusive depois da
          // última, que ficaria em cima da borda direita. Encolher o fundo em
          // 1px remove exatamente esse.
          backgroundSize: `calc(100% - 1px) 100%`,
          backgroundRepeat: 'no-repeat',
          ...style,
        }}
        {...props}
      />
    )
  },
)

EntradaDeCodigoOtp.displayName = 'EntradaDeCodigoOtp'
