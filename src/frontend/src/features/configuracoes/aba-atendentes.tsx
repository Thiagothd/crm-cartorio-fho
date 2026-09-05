import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Copy, KeyRound, Pencil, Plus, ShieldAlert } from 'lucide-react'
import { toast } from 'sonner'
import { Avatar } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { atendentesApi, setoresApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { formatarDataHora } from '@/lib/formatacao'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import type { Atendente, CargoNaServentia, PerfilDeAtendente } from '@/lib/api/tipos'

const SEM_SETOR = '__nenhum__'

const perfis: { valor: PerfilDeAtendente; rotulo: string; descricao: string }[] = [
  { valor: 'Atendente', rotulo: 'Atendente', descricao: 'Opera as próprias conversas' },
  {
    valor: 'Supervisor',
    rotulo: 'Supervisor',
    descricao: 'Transfere conversa de terceiro, reabre e gere cadastros',
  },
  { valor: 'Administrador', rotulo: 'Administrador', descricao: 'Tudo, incluindo gerir atendentes' },
]

/**
 * Espelho de `CargoNaServentia` no backend.
 *
 * Lista fechada: os cargos vêm da estrutura da Lei 8.935/94, não da preferência do
 * cartório. O que é configurável é quem ocupa cada um.
 */
const cargos: { valor: CargoNaServentia; rotulo: string }[] = [
  { valor: 'NaoInformado', rotulo: 'Não informado' },
  { valor: 'Titular', rotulo: 'Titular' },
  { valor: 'Substituto', rotulo: 'Substituto' },
  { valor: 'EscreventeAutorizado', rotulo: 'Escrevente autorizado' },
  { valor: 'Escrevente', rotulo: 'Escrevente' },
  { valor: 'Auxiliar', rotulo: 'Auxiliar' },
]

export function AbaAtendentes() {
  const { podeGerirAtendentes } = usarAutenticacao()
  const [emEdicao, setEmEdicao] = useState<Atendente | null>(null)
  const [aberto, setAberto] = useState(false)
  const [senhaDe, setSenhaDe] = useState<Atendente | null>(null)
  const [mfaDe, setMfaDe] = useState<Atendente | null>(null)

  const clienteDeConsultas = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['atendentes-gestao'],
    queryFn: () => atendentesApi.listar(true),
  })

  function recarregar() {
    void clienteDeConsultas.invalidateQueries({ queryKey: ['atendentes-gestao'] })
    void clienteDeConsultas.invalidateQueries({ queryKey: ['atendentes'] })
  }

  if (!podeGerirAtendentes) {
    return (
      <p className="text-sm text-tinta-media">
        Apenas o perfil Administrador gerencia atendentes.
      </p>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-start sm:justify-between sm:gap-4">
        <p className="max-w-2xl text-sm text-tinta-media">
          Quem acessa o sistema. <strong className="text-tinta">Cargo</strong> é a função na
          serventia; <strong className="text-tinta">perfil</strong> é o que a pessoa pode fazer
          aqui dentro. São independentes — um escrevente autorizado pode administrar o painel.
          Desativar corta o acesso sem apagar histórico: as mensagens enviadas continuam com o
          nome de quem as escreveu.
        </p>

        <Button
          size="sm"
          className="sm:shrink-0"
          onClick={() => {
            setEmEdicao(null)
            setAberto(true)
          }}
        >
          <Plus />
          Novo atendente
        </Button>
      </div>

      {isLoading ? (
        <div className="flex flex-col gap-2">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : (
        <div className="overflow-hidden rounded-md border border-linha">
          <table className="w-full table-fixed text-sm md:table-auto">
            <thead className="bg-superficie text-xs text-tinta-media">
              <tr>
                <th className="px-3 py-2.5 text-left font-medium md:px-4">Atendente</th>
                <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Cargo</th>
                <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Perfil</th>
                <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Setor padrão</th>
                <th className="hidden px-4 py-2.5 text-left font-medium md:table-cell">Último acesso</th>
                <th className="w-28 md:w-20" />
              </tr>
            </thead>

            <tbody>
              {data?.map((atendente) => (
                <tr key={atendente.id} className="border-t border-linha hover:bg-superficie">
                  <td className="px-3 py-3 md:px-4 md:py-2.5">
                    <div className="flex items-center gap-2.5">
                      <Avatar nome={atendente.nome} tamanho="sm" />
                      <div className="min-w-0">
                        <div className="flex items-center gap-2">
                          <span className="font-medium">{atendente.nome}</span>
                          {!atendente.ativo ? <Badge variant="secondary">Inativo</Badge> : null}
                        </div>
                        <span className="block truncate text-xs text-tinta-media">{atendente.email}</span>
                        <span className="mt-1 block text-[11px] text-tinta-fraca md:hidden">
                          {atendente.cargo === 'NaoInformado' ? 'Cargo não informado' : atendente.cargoDescrito}
                          {' · '}
                          {atendente.perfil}
                        </span>
                      </div>
                    </div>
                  </td>

                  {/* Cargo é texto e perfil é etiqueta: o cargo descreve a pessoa,
                      o perfil concede permissão. Tratamentos diferentes evitam que
                      as duas colunas se confundam de relance. */}
                  <td className="hidden px-4 py-2.5 text-tinta-media md:table-cell">
                    {atendente.cargo === 'NaoInformado' ? '—' : atendente.cargoDescrito}
                  </td>

                  <td className="hidden px-4 py-2.5 md:table-cell">
                    <Badge variant="outline">{atendente.perfil}</Badge>
                  </td>

                  <td className="hidden px-4 py-2.5 text-tinta-media md:table-cell">
                    {atendente.setorPadraoNome ?? '—'}
                  </td>

                  <td className="hidden px-4 py-2.5 text-tinta-media md:table-cell">
                    {atendente.ultimoAcessoEm ? formatarDataHora(atendente.ultimoAcessoEm) : 'nunca'}
                  </td>

                  <td className="px-1 py-2.5 md:px-2">
                    <div className="flex items-center gap-1">
                      {/* SEC-03 — so faz sentido para quem PODE estar sob a
                          politica de MFA obrigatorio (Administrador ou Titular). */}
                      {atendente.perfil === 'Administrador' || atendente.cargo === 'Titular' ? (
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          title="Segurança (MFA)"
                          onClick={() => setMfaDe(atendente)}
                        >
                          <ShieldAlert />
                        </Button>
                      ) : null}
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        title="Redefinir senha"
                        onClick={() => setSenhaDe(atendente)}
                      >
                        <KeyRound />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        onClick={() => {
                          setEmEdicao(atendente)
                          setAberto(true)
                        }}
                      >
                        <Pencil />
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <DialogoDeAtendente
        atendente={emEdicao}
        aberto={aberto}
        aoFechar={() => setAberto(false)}
        aoSalvar={recarregar}
      />

      <DialogoDeSenha atendente={senhaDe} aoFechar={() => setSenhaDe(null)} />

      <DialogoDeMfaAdministrativo atendente={mfaDe} aoFechar={() => setMfaDe(null)} />
    </div>
  )
}

/**
 * SEC-03 — emitir ticket de matrícula privilegiada ou resetar o MFA de outra
 * conta. As duas exigem step-up do PRÓPRIO ator (senha + código) — um JWT
 * roubado de um administrador não basta sozinho para nenhuma das duas.
 */
function DialogoDeMfaAdministrativo({
  atendente,
  aoFechar,
}: {
  atendente: Atendente | null
  aoFechar: () => void
}) {
  const [senhaDoAtor, setSenhaDoAtor] = useState('')
  const [codigoDoAtor, setCodigoDoAtor] = useState('')
  const [motivo, setMotivo] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [ticket, setTicket] = useState<string | null>(null)

  useEffect(() => {
    setSenhaDoAtor('')
    setCodigoDoAtor('')
    setMotivo('')
    setErro(null)
    setTicket(null)
  }, [atendente])

  async function emitirTicket() {
    if (!atendente) return
    setEnviando(true)
    setErro(null)

    try {
      const resposta = await atendentesApi.emitirTicketDeMfa(atendente.id, senhaDoAtor, codigoDoAtor)
      setTicket(resposta.ticket)
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível emitir o ticket.')
    } finally {
      setEnviando(false)
    }
  }

  async function resetar() {
    if (!atendente) return
    if (!motivo.trim()) {
      setErro('Informe o motivo do reset.')
      return
    }
    if (!window.confirm(`Resetar o MFA de ${atendente.nome}? A conta fica sem login até nova matrícula.`)) {
      return
    }

    setEnviando(true)
    setErro(null)

    try {
      await atendentesApi.resetarMfa(atendente.id, senhaDoAtor, codigoDoAtor, motivo)
      toast.success(`MFA de ${atendente.nome} resetado.`)
      aoFechar()
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível resetar.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Dialog open={Boolean(atendente)} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Segurança (MFA) — {atendente?.nome}</DialogTitle>
          <DialogDescription>
            Confirme sua própria senha e código de autenticador para qualquer ação aqui.
          </DialogDescription>
        </DialogHeader>

        {ticket ? (
          <div className="flex flex-col gap-2 rounded-md border border-ambar/40 bg-ambar/[0.06] p-3">
            <p className="text-xs text-tinta-media">
              Ticket emitido — mostrado uma única vez. Entregue por canal fora de banda (nunca
              e-mail ou chat).
            </p>
            <code className="break-all rounded border border-linha bg-branco px-2 py-1.5 text-xs">
              {ticket}
            </code>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => {
                void navigator.clipboard.writeText(ticket)
                toast.success('Ticket copiado.')
              }}
            >
              <Copy className="size-3.5" />
              Copiar
            </Button>
          </div>
        ) : (
          <div className="flex flex-col gap-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="mfa-senha-ator">Sua senha</Label>
              <Input
                id="mfa-senha-ator"
                type="password"
                autoComplete="current-password"
                value={senhaDoAtor}
                onChange={(e) => setSenhaDoAtor(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="mfa-codigo-ator">Seu código de autenticador</Label>
              <Input
                id="mfa-codigo-ator"
                inputMode="numeric"
                value={codigoDoAtor}
                onChange={(e) => setCodigoDoAtor(e.target.value)}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="mfa-motivo">Motivo (obrigatório só para resetar)</Label>
              <Input id="mfa-motivo" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
            </div>

            {erro ? (
              <p role="alert" className="text-xs text-erro">
                {erro}
              </p>
            ) : null}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Fechar
          </Button>
          {!ticket ? (
            <>
              <Button
                variant="outline"
                disabled={enviando || !senhaDoAtor || !codigoDoAtor}
                onClick={() => void emitirTicket()}
              >
                Emitir ticket
              </Button>
              <Button
                variant="destructive"
                disabled={enviando || !senhaDoAtor || !codigoDoAtor}
                onClick={() => void resetar()}
              >
                Resetar MFA
              </Button>
            </>
          ) : null}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function DialogoDeAtendente({
  atendente,
  aberto,
  aoFechar,
  aoSalvar,
}: {
  atendente: Atendente | null
  aberto: boolean
  aoFechar: () => void
  aoSalvar: () => void
}) {
  const [nome, setNome] = useState('')
  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [perfil, setPerfil] = useState<PerfilDeAtendente>('Atendente')
  const [cargo, setCargo] = useState<CargoNaServentia>('NaoInformado')
  const [setorPadraoId, setSetorPadraoId] = useState(SEM_SETOR)
  const [ativo, setAtivo] = useState(true)
  const [salvando, setSalvando] = useState(false)

  const { data: setores } = useQuery({
    queryKey: ['setores'],
    queryFn: () => setoresApi.listar(),
    enabled: aberto,
  })

  useEffect(() => {
    if (!aberto) return

    setNome(atendente?.nome ?? '')
    setEmail(atendente?.email ?? '')
    setSenha('')
    setPerfil(atendente?.perfil ?? 'Atendente')
    setCargo(atendente?.cargo ?? 'NaoInformado')
    setSetorPadraoId(atendente?.setorPadraoId ?? SEM_SETOR)
    setAtivo(atendente?.ativo ?? true)
  }, [aberto, atendente])

  async function salvar() {
    setSalvando(true)

    try {
      const setor = setorPadraoId === SEM_SETOR ? null : setorPadraoId

      if (atendente) {
        await atendentesApi.atualizar(atendente.id, {
          nome,
          email,
          perfil,
          cargo,
          setorPadraoId: setor,
          ativo,
        })
      } else {
        await atendentesApi.criar({ nome, email, senha, perfil, cargo, setorPadraoId: setor })
      }

      toast.success(atendente ? 'Atendente atualizado.' : 'Atendente cadastrado.')
      aoSalvar()
      aoFechar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{atendente ? 'Editar atendente' : 'Novo atendente'}</DialogTitle>
          <DialogDescription>
            Cargo e perfil são independentes: o cargo diz o que a pessoa é na serventia, o perfil
            diz o que ela pode fazer no sistema.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="atendente-nome">Nome</Label>
            <Input
              id="atendente-nome"
              value={nome}
              onChange={(e) => setNome(e.target.value)}
              placeholder="Aparece em cada mensagem que a pessoa enviar"
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="atendente-email">E-mail (login)</Label>
            <Input
              id="atendente-email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </div>

          {!atendente ? (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="atendente-senha">Senha inicial</Label>
              <Input
                id="atendente-senha"
                type="password"
                value={senha}
                onChange={(e) => setSenha(e.target.value)}
              />
              <p className="text-xs text-tinta-media">Mínimo de 8 caracteres.</p>
            </div>
          ) : null}

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="atendente-cargo">Cargo na serventia</Label>
            <Select value={cargo} onValueChange={(v) => setCargo(v as CargoNaServentia)}>
              <SelectTrigger id="atendente-cargo">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {cargos.map((c) => (
                  <SelectItem key={c.valor} value={c.valor}>
                    {c.rotulo}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-xs text-tinta-media">
              Informação funcional. Não concede permissão nenhuma no sistema.
            </p>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="atendente-perfil">Perfil de acesso</Label>
            <Select value={perfil} onValueChange={(v) => setPerfil(v as PerfilDeAtendente)}>
              <SelectTrigger id="atendente-perfil">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {perfis.map((p) => (
                  <SelectItem key={p.valor} value={p.valor}>
                    {p.rotulo}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-xs text-tinta-media">
              {perfis.find((p) => p.valor === perfil)?.descricao}
            </p>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="atendente-setor">Setor padrão (opcional)</Label>
            <Select value={setorPadraoId} onValueChange={setSetorPadraoId}>
              <SelectTrigger id="atendente-setor">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SEM_SETOR}>Sem setor padrão</SelectItem>
                {setores?.map((setor) => (
                  <SelectItem key={setor.id} value={setor.id}>
                    {setor.nome}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {atendente ? (
            <div className="flex items-center justify-between rounded-md border border-linha px-3 py-2">
              <div>
                <Label htmlFor="atendente-ativo">Acesso ativo</Label>
                <p className="text-xs text-tinta-media">
                  Você não pode desativar o próprio acesso.
                </p>
              </div>
              <Switch id="atendente-ativo" checked={ativo} onCheckedChange={setAtivo} />
            </div>
          ) : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Cancelar
          </Button>
          <Button onClick={() => void salvar()} disabled={salvando}>
            Salvar
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function DialogoDeSenha({
  atendente,
  aoFechar,
}: {
  atendente: Atendente | null
  aoFechar: () => void
}) {
  const [novaSenha, setNovaSenha] = useState('')
  const [salvando, setSalvando] = useState(false)

  useEffect(() => {
    setNovaSenha('')
  }, [atendente])

  async function salvar() {
    if (!atendente) return

    setSalvando(true)

    try {
      await atendentesApi.redefinirSenha(atendente.id, novaSenha)
      toast.success(`Senha de ${atendente.nome} redefinida.`)
      aoFechar()
    } catch (falha) {
      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível redefinir.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Dialog open={Boolean(atendente)} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Redefinir senha</DialogTitle>
          <DialogDescription>{atendente?.nome}</DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="nova-senha">Nova senha</Label>
          <Input
            id="nova-senha"
            type="password"
            value={novaSenha}
            onChange={(e) => setNovaSenha(e.target.value)}
          />
          <p className="text-xs text-tinta-media">Mínimo de 8 caracteres.</p>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Cancelar
          </Button>
          <Button onClick={() => void salvar()} disabled={salvando || novaSenha.length < 8}>
            Redefinir
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
