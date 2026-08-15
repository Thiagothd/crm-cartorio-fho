import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { KeyRound, Pencil, Plus } from 'lucide-react'
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
      <div className="flex items-start justify-between gap-4">
        <p className="max-w-2xl text-sm text-tinta-media">
          Quem acessa o sistema. <strong className="text-tinta">Cargo</strong> é a função na
          serventia; <strong className="text-tinta">perfil</strong> é o que a pessoa pode fazer
          aqui dentro. São independentes — um escrevente autorizado pode administrar o painel.
          Desativar corta o acesso sem apagar histórico: as mensagens enviadas continuam com o
          nome de quem as escreveu.
        </p>

        <Button
          size="sm"
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
          <table className="w-full text-sm">
            <thead className="bg-superficie text-xs text-tinta-media">
              <tr>
                <th className="px-4 py-2.5 text-left font-medium">Atendente</th>
                <th className="px-4 py-2.5 text-left font-medium">Cargo</th>
                <th className="px-4 py-2.5 text-left font-medium">Perfil</th>
                <th className="px-4 py-2.5 text-left font-medium">Setor padrão</th>
                <th className="px-4 py-2.5 text-left font-medium">Último acesso</th>
                <th className="w-20" />
              </tr>
            </thead>

            <tbody>
              {data?.map((atendente) => (
                <tr key={atendente.id} className="border-t border-linha hover:bg-superficie">
                  <td className="px-4 py-2.5">
                    <div className="flex items-center gap-2.5">
                      <Avatar nome={atendente.nome} tamanho="sm" />
                      <div>
                        <div className="flex items-center gap-2">
                          <span className="font-medium">{atendente.nome}</span>
                          {!atendente.ativo ? <Badge variant="secondary">Inativo</Badge> : null}
                        </div>
                        <span className="text-xs text-tinta-media">{atendente.email}</span>
                      </div>
                    </div>
                  </td>

                  {/* Cargo é texto e perfil é etiqueta: o cargo descreve a pessoa,
                      o perfil concede permissão. Tratamentos diferentes evitam que
                      as duas colunas se confundam de relance. */}
                  <td className="px-4 py-2.5 text-tinta-media">
                    {atendente.cargo === 'NaoInformado' ? '—' : atendente.cargoDescrito}
                  </td>

                  <td className="px-4 py-2.5">
                    <Badge variant="outline">{atendente.perfil}</Badge>
                  </td>

                  <td className="px-4 py-2.5 text-tinta-media">
                    {atendente.setorPadraoNome ?? '—'}
                  </td>

                  <td className="px-4 py-2.5 text-tinta-media">
                    {atendente.ultimoAcessoEm ? formatarDataHora(atendente.ultimoAcessoEm) : 'nunca'}
                  </td>

                  <td className="px-2 py-2.5">
                    <div className="flex items-center gap-1">
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
    </div>
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
