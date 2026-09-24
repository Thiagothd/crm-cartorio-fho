import { useState, type FormEvent, type ReactNode } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import {
  ArrowLeft,
  AtSign,
  Eye,
  EyeOff,
  KeyRound,
  Loader2,
  ShieldCheck,
  TriangleAlert,
} from 'lucide-react'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { ErroDaApi } from '@/lib/api/cliente-http'
import type { DesafioMfa } from '@/lib/api/tipos'
import { Button } from '@/components/ui/button'
import { EntradaDeCodigoOtp } from '@/components/ui/entrada-de-codigo-otp'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { SeletorDeTema } from '@/components/ui/seletor-de-tema'

/**
 * Tela de entrada.
 *
 * Redesenhada em 2026-08-21 para a mesma linguagem da Visão Geral: cartão com
 * `rounded-card` e elevação real, título em sans bold (não na serifa do
 * "documento oficial"), campos de `rounded-lg` com ícone dentro, e entrada
 * animada. A serifa fica onde ela é identidade e não interface: dentro do
 * lockup da marca.
 *
 * O cartão é levemente translúcido com `backdrop-blur`: ele flutua sobre a
 * fotografia, e um branco 100% opaco recorta a imagem como um adesivo. A 96% a
 * textura da madeira atravessa de leve nas bordas e o cartão passa a pertencer
 * à cena — sem custo de contraste, porque o texto continua sobre branco quase
 * cheio.
 */
export function PaginaLogin() {
  const { atendente, carregando, entrar, concluirLoginComMfa } = usarAutenticacao()
  const navegar = useNavigate()

  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [senhaVisivel, setSenhaVisivel] = useState(false)
  const [capsLockAtivo, setCapsLockAtivo] = useState(false)

  // SEC-03 — etapa 2. `desafio` preenchido é o único sinal de que a conta
  // exige segundo fator; nenhuma sessão existe enquanto ele não for resolvido.
  const [desafio, setDesafio] = useState<DesafioMfa | null>(null)
  const [codigoMfa, setCodigoMfa] = useState('')
  const [usandoCodigoDeRecuperacao, setUsandoCodigoDeRecuperacao] = useState(false)
  // Opt-in explícito, nunca marcado por padrão: computador de balcão de
  // cartório é compartilhado, e "lembrar" por engano numa máquina de uso
  // comum é exatamente o que não pode acontecer.
  const [confiarNesteDispositivo, setConfiarNesteDispositivo] = useState(false)

  if (!carregando && atendente) {
    return <Navigate to="/" replace />
  }

  async function aoEnviarSenha(evento: FormEvent) {
    evento.preventDefault()
    setErro(null)
    setEnviando(true)

    try {
      const resultado = await entrar(email, senha)

      if (resultado.tipo === 'mfa') {
        setDesafio(resultado.desafio)
        setSenha('')
        return
      }

      navegar('/', { replace: true })
    } catch (falha) {
      setErro(
        falha instanceof ErroDaApi
          ? falha.message
          : 'Não foi possível conectar ao servidor. Verifique se a API está no ar.',
      )
    } finally {
      setEnviando(false)
    }
  }

  async function aoEnviarCodigo(evento: FormEvent) {
    evento.preventDefault()
    if (!desafio) return

    setErro(null)
    setEnviando(true)

    try {
      await concluirLoginComMfa(desafio.desafio, codigoMfa, confiarNesteDispositivo)
      navegar('/', { replace: true })
    } catch (falha) {
      setErro(
        falha instanceof ErroDaApi
          ? falha.message
          : 'Não foi possível conectar ao servidor. Verifique se a API está no ar.',
      )
      setCodigoMfa('')
    } finally {
      setEnviando(false)
    }
  }

  function voltarParaSenha() {
    setDesafio(null)
    setCodigoMfa('')
    setErro(null)
    setUsandoCodigoDeRecuperacao(false)
    setConfiarNesteDispositivo(false)
  }

  function alternarModoDoCodigo() {
    setUsandoCodigoDeRecuperacao((usando) => !usando)
    setCodigoMfa('')
    setErro(null)
  }

  const descricaoDoEmail = erro ? 'erro-login' : undefined
  const descricaoDaSenha =
    [capsLockAtivo ? 'aviso-caps-lock' : null, erro ? 'erro-login' : null]
      .filter(Boolean)
      .join(' ') || undefined

  return (
    <main className="login-tela relative flex min-h-dvh items-center justify-center px-4 pb-[max(1rem,env(safe-area-inset-bottom))] pt-[max(1rem,env(safe-area-inset-top))] sm:px-8 lg:justify-start lg:px-16 xl:px-28">
      {/* Véu escuro só na faixa do cartão. A fotografia já tem madeira escura à
          esquerda, mas isso depende do recorte, e o recorte muda com a largura
          da janela — num monitor largo o cartão podia cair sobre a parte clara
          do aparelho. O véu garante o contraste em qualquer viewport sem
          escurecer a cena inteira. */}
      <div
        aria-hidden
        className="login-veu pointer-events-none absolute inset-0 bg-gradient-to-r from-tinta/70 via-tinta/25 to-transparent"
      />

      {/* A caixa cresce por FAIXA DE TELA, não proporcionalmente: 27rem até o
          notebook (onde já estava bom e nada muda), 30rem em 1280px e 33rem em
          1536px. Num monitor grande a caixa de largura fixa parecia perdida no
          meio da fotografia — o entorno cresceu e ela não. O teto de 33rem
          existe porque campo de formulário largo demais piora a leitura: o olho
          percorre a linha inteira para achar um `input` que continua com o mesmo
          conteúdo. Quem cresce junto é o espaço interno e a tipografia, não só a
          largura. */}
      <section
        aria-labelledby="titulo-login"
        className="relative w-full max-w-[27rem] overflow-hidden rounded-card border border-branco/25 bg-branco/[0.96] shadow-[0_24px_70px_-20px_rgba(11,11,15,.55)] backdrop-blur-md xl:max-w-[30rem] 2xl:max-w-[33rem]"
        style={{ animation: 'crm-subir-e-esmaecer 560ms cubic-bezier(0.16, 1, 0.3, 1) both' }}
      >
        <div className="px-7 pb-7 pt-8 sm:px-9 sm:pb-8 sm:pt-9 2xl:px-11 2xl:pb-10 2xl:pt-11">
          {/* `decoding="sync"` + `fetchPriority="high"`: é a primeira coisa que a
              pessoa vê, e a marca aparecendo depois do formulário dá a sensação
              de página quebrada. */}
          <div className="mb-4 flex justify-end"><SeletorDeTema /></div>
          <img
            src="/brand/crm-cartorio-logo-horizontal.webp"
            alt="CRM Cartório"
            width={820}
            height={186}
            decoding="sync"
            fetchPriority="high"
            className="marca-do-produto h-auto w-full max-w-[15rem] 2xl:max-w-[17rem]"
          />

          {/* Visível, não mais `sr-only`. O título é também o nome acessível da
              região (via `aria-labelledby`), então ele precisa ser descritivo —
              e um `aria-label` diferente do texto na tela criaria duas verdades
              para a mesma coisa. */}
          {/* Selo discreto: dá a entender, antes de qualquer texto, que esta
              é uma etapa de segurança e não um segundo login. Só aparece na
              etapa do código — no formulário de senha a marca já basta. */}
          {desafio ? (
            <span
              aria-hidden
              className="mt-7 inline-flex size-9 items-center justify-center rounded-lg border border-linha bg-superficie text-selo"
            >
              <ShieldCheck className="size-4" />
            </span>
          ) : null}

          <h1
            id="titulo-login"
            className={`text-2xl font-bold tracking-[-0.02em] text-tinta 2xl:text-[28px] ${desafio ? 'mt-3' : 'mt-7 2xl:mt-8'}`}
          >
            {desafio ? 'Confirme sua identidade' : 'Entrar no CRM'}
          </h1>
          <p className="mt-1.5 text-sm leading-relaxed text-tinta-media 2xl:text-[15px]">
            {desafio
              ? usandoCodigoDeRecuperacao
                ? 'Digite um dos códigos de recuperação que você guardou ao ativar a verificação em duas etapas.'
                : 'Digite o código de 6 dígitos gerado pelo seu aplicativo autenticador.'
              : 'Use seu e-mail e senha para continuar os atendimentos da serventia.'}
          </p>

          {desafio ? (
            <form onSubmit={aoEnviarCodigo} className="mt-6 flex flex-col gap-4">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="codigo-mfa" className="text-[13px] font-semibold text-tinta">
                  Código
                </Label>

                {/* Um campo por modo, nunca um campo que muda de formato: o
                    código do autenticador tem 6 dígitos e ganha as células; o
                    de recuperação tem 4 blocos alfanuméricos e precisa de
                    largura, não de grade. */}
                {usandoCodigoDeRecuperacao ? (
                  <Input
                    id="codigo-mfa"
                    type="text"
                    inputMode="text"
                    autoComplete="one-time-code"
                    autoCapitalize="characters"
                    autoFocus
                    spellCheck={false}
                    required
                    aria-invalid={erro ? true : undefined}
                    aria-describedby={erro ? 'erro-login' : undefined}
                    value={codigoMfa}
                    onChange={(evento) => {
                      setCodigoMfa(evento.target.value)
                      setErro(null)
                    }}
                    placeholder="XXXX-XXXX-XXXX-XXXX"
                    className="h-12 rounded-lg border-linha bg-superficie text-center font-dado tracking-[0.1em] md:h-12 md:text-sm"
                  />
                ) : (
                  <EntradaDeCodigoOtp
                    id="codigo-mfa"
                    autoFocus
                    required
                    value={codigoMfa}
                    onChange={(valor) => {
                      setCodigoMfa(valor)
                      setErro(null)
                    }}
                    disabled={enviando}
                    aria-invalid={erro ? true : undefined}
                    aria-describedby={erro ? 'erro-login' : undefined}
                    className="self-center rounded-lg border-linha bg-superficie"
                  />
                )}

                <button
                  type="button"
                  onClick={alternarModoDoCodigo}
                  disabled={enviando}
                  className="self-start text-[13px] font-medium text-tinta-media underline-offset-4 transition-colors hover:text-tinta hover:underline disabled:opacity-50"
                >
                  {usandoCodigoDeRecuperacao
                    ? 'Usar o código do aplicativo'
                    : 'Perdeu o acesso ao aplicativo? Usar um código de recuperação'}
                </button>
              </div>

              {/* Opt-in, nunca padrão — ver o comentário do estado. O texto de
                  apoio diz o que ACONTECE ("não pediremos o código neste
                  navegador"), não o que a caixa é. */}
              {/* O texto de apoio fica FORA do <label>, ligado só por
                  `aria-describedby`: dentro dele, a frase inteira entraria no
                  nome acessível da caixa ("Confiar neste dispositivo… não
                  pediremos o código…"), o que atrapalha leitor de tela e
                  colide com o próprio campo "Código" ao lado. */}
              <div className="flex flex-col gap-1 rounded-lg border border-linha bg-superficie/60 px-3 py-2.5">
                <label
                  htmlFor="confiar-dispositivo"
                  className="flex cursor-pointer items-center gap-2.5 text-[13px] font-medium text-tinta"
                >
                  <input
                    id="confiar-dispositivo"
                    type="checkbox"
                    checked={confiarNesteDispositivo}
                    onChange={(evento) => setConfiarNesteDispositivo(evento.target.checked)}
                    disabled={enviando}
                    aria-describedby="ajuda-confiar-dispositivo"
                    className="size-4 shrink-0 accent-[var(--cor-selo)]"
                  />
                  Confiar neste dispositivo por 30 dias
                </label>
                <p id="ajuda-confiar-dispositivo" className="pl-[26px] text-[11px] leading-relaxed text-tinta-media">
                  Não pediremos o código novamente neste navegador por 30 dias. A senha continua
                  sendo exigida em todo acesso.
                </p>
              </div>

              {erro ? (
                <div className="flex items-start gap-2.5 rounded-lg border border-selo/30 bg-selo/[0.07] px-3 py-2.5">
                  <TriangleAlert className="mt-0.5 size-4 shrink-0 text-selo" aria-hidden />
                  <p id="erro-login" role="alert" className="text-[13px] leading-relaxed text-selo">
                    {erro}
                  </p>
                </div>
              ) : null}

              <Button
                type="submit"
                disabled={enviando || (!usandoCodigoDeRecuperacao && codigoMfa.length < 6)}
                className="mt-1 h-12 w-full rounded-lg bg-gradient-to-b from-primary to-primary/90 text-[15px] font-semibold shadow-card transition-all duration-200 hover:-translate-y-px hover:shadow-card-hover disabled:hover:translate-y-0 md:h-12"
              >
                {enviando ? <Loader2 className="animate-spin" aria-hidden="true" /> : null}
                {enviando ? 'Verificando…' : 'Confirmar'}
              </Button>

              <button
                type="button"
                onClick={voltarParaSenha}
                disabled={enviando}
                className="inline-flex items-center justify-center gap-1.5 self-center text-[13px] font-medium text-tinta-media transition-colors hover:text-tinta disabled:opacity-50"
              >
                <ArrowLeft className="size-3.5" aria-hidden />
                Voltar
              </button>
            </form>
          ) : (
          <form onSubmit={aoEnviarSenha} className="mt-7 flex flex-col gap-4">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="email" className="text-[13px] font-semibold text-tinta">
                E-mail
              </Label>
              <CampoComIcone icone={AtSign}>
                <Input
                  id="email"
                  type="email"
                  inputMode="email"
                  autoComplete="username"
                  autoCapitalize="none"
                  spellCheck={false}
                  required
                  aria-invalid={erro ? true : undefined}
                  aria-describedby={descricaoDoEmail}
                  value={email}
                  onChange={(evento) => {
                    setEmail(evento.target.value)
                    setErro(null)
                  }}
                  placeholder="nome@cartorio.com.br"
                  className="h-12 rounded-lg border-linha bg-superficie pl-11 md:h-12 md:text-sm"
                />
              </CampoComIcone>
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="senha" className="text-[13px] font-semibold text-tinta">
                Senha
              </Label>
              <CampoComIcone icone={KeyRound}>
                <Input
                  id="senha"
                  type={senhaVisivel ? 'text' : 'password'}
                  autoComplete="current-password"
                  required
                  aria-invalid={erro ? true : undefined}
                  aria-describedby={descricaoDaSenha}
                  value={senha}
                  onChange={(evento) => {
                    setSenha(evento.target.value)
                    setErro(null)
                  }}
                  onKeyDown={(evento) => setCapsLockAtivo(evento.getModifierState('CapsLock'))}
                  onKeyUp={(evento) => setCapsLockAtivo(evento.getModifierState('CapsLock'))}
                  onBlur={() => setCapsLockAtivo(false)}
                  className="h-12 rounded-lg border-linha bg-superficie pl-11 pr-12 md:h-12 md:text-sm"
                />
                <button
                  type="button"
                  onClick={() => setSenhaVisivel((visivel) => !visivel)}
                  className="absolute inset-y-0 right-0 flex w-12 touch-manipulation items-center justify-center rounded-r-lg text-tinta-media transition-colors hover:text-tinta"
                  aria-label={senhaVisivel ? 'Ocultar senha' : 'Mostrar senha'}
                  aria-pressed={senhaVisivel}
                >
                  {senhaVisivel ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
                </button>
              </CampoComIcone>

              {capsLockAtivo ? (
                <p
                  id="aviso-caps-lock"
                  role="status"
                  className="mt-0.5 inline-flex items-center gap-1.5 text-xs font-medium text-ambar"
                >
                  <TriangleAlert className="size-3.5 shrink-0" aria-hidden />
                  Caps Lock está ativado.
                </p>
              ) : null}
            </div>

            {/* Erro como bloco tingido de `rounded-lg`, não como filete à
                esquerda: o filete era o vocabulário de documento, e num aviso
                que precisa ser notado a área tingida chega antes ao olho. */}
            {erro ? (
              <div className="flex items-start gap-2.5 rounded-lg border border-selo/30 bg-selo/[0.07] px-3 py-2.5">
                <TriangleAlert className="mt-0.5 size-4 shrink-0 text-selo" aria-hidden />
                <p id="erro-login" role="alert" className="text-[13px] leading-relaxed text-selo">
                  {erro}
                </p>
              </div>
            ) : null}

            {/* Gradiente sutil de `selo` para `selo/90` — dá volume ao botão sem
                introduzir cor nova, e é a mesma técnica das barras do gráfico. O
                levantar de 1px no hover é o mesmo dos cartões da Visão Geral. */}
            <Button
              type="submit"
              disabled={enviando}
              className="mt-2 h-12 w-full rounded-lg bg-gradient-to-b from-primary to-primary/90 text-[15px] font-semibold shadow-card transition-all duration-200 hover:-translate-y-px hover:shadow-card-hover disabled:hover:translate-y-0 md:h-12"
            >
              {enviando ? <Loader2 className="animate-spin" aria-hidden="true" /> : null}
              {enviando ? 'Entrando…' : 'Entrar'}
            </Button>
          </form>
          )}
        </div>

        {/* Rodapé em faixa própria, encostado na borda do cartão: separa o aviso
            institucional do formulário sem gastar uma linha de borda no meio do
            conteúdo. */}
        <div className="flex items-start gap-2.5 border-t border-linha bg-superficie/70 px-7 py-4 sm:px-9 2xl:px-11 2xl:py-5">
          <span
            aria-hidden
            className="flex size-7 shrink-0 items-center justify-center rounded-lg bg-branco text-selo shadow-card"
          >
            <ShieldCheck className="size-3.5" />
          </span>
          <p className="text-[11px] leading-relaxed text-tinta-media">
            O acesso é individual e protegido pelas permissões da sua serventia.
          </p>
        </div>
      </section>
    </main>
  )
}

/**
 * Envolve um campo para receber ícone à esquerda (e, na senha, o olho à
 * direita). O ícone é decorativo — o nome do campo vem do `<Label>`, então
 * `aria-hidden` aqui é correto e não perde informação.
 */
function CampoComIcone({
  icone: Icone,
  children,
}: {
  icone: typeof AtSign
  children: ReactNode
}) {
  return (
    <div className="relative">
      <span
        aria-hidden
        className="pointer-events-none absolute inset-y-0 left-0 flex w-11 items-center justify-center text-tinta-fraca"
      >
        <Icone className="size-4" />
      </span>
      {children}
    </div>
  )
}
