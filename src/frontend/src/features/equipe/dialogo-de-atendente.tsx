import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Dices } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
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
import { setoresApi } from '@/lib/api/endpoints'
import { ErroDaApi } from '@/lib/api/cliente-http'
import { cn } from '@/lib/utils'
import { usarCriarAtendente, usarAtualizarAtendente } from './consultas'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import type { Atendente, CargoNaServentia, PerfilDeAtendente } from '@/lib/api/tipos'

const SEM_SETOR = '__nenhum__'

const CLASSES: { valor: PerfilDeAtendente; rotulo: string }[] = [
  { valor: 'Atendente', rotulo: 'Atendente' },
  { valor: 'Supervisor', rotulo: 'Supervisor' },
  { valor: 'Administrador', rotulo: 'Administrador' },
]

/**
 * Espelho de `CargoNaServentia` — lista fechada, vem da Lei 8.935/94, não da
 * preferência do cartório.
 */
const CARGOS: { valor: CargoNaServentia; rotulo: string; descricao: string }[] = [
  { valor: 'NaoInformado', rotulo: 'Não informado', descricao: 'Deixe assim se não souber ainda' },
  { valor: 'Titular', rotulo: 'Titular', descricao: 'Responde pela serventia' },
  { valor: 'Substituto', rotulo: 'Substituto', descricao: 'Substitui o titular' },
  { valor: 'EscreventeAutorizado', rotulo: 'Escrevente autorizado', descricao: 'Autorizado a praticar atos' },
  { valor: 'Escrevente', rotulo: 'Escrevente', descricao: 'Atua no cartório' },
  { valor: 'Auxiliar', rotulo: 'Auxiliar', descricao: 'Apoio administrativo' },
]

function gerarSenhaForte() {
  const alfabeto = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%'
  const bytes = new Uint32Array(14)
  crypto.getRandomValues(bytes)
  return Array.from(bytes, (b) => alfabeto[b % alfabeto.length]).join('')
}

export function DialogoDeAtendente({
  atendente,
  aberto,
  aoFechar,
}: {
  atendente: Atendente | null
  aberto: boolean
  aoFechar: () => void
}) {
  const { atendente: eu } = usarAutenticacao()
  const [nome, setNome] = useState('')
  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [perfil, setPerfil] = useState<PerfilDeAtendente>('Atendente')
  const [cargo, setCargo] = useState<CargoNaServentia>('NaoInformado')
  const [setorPadraoId, setSetorPadraoId] = useState(SEM_SETOR)
  const [ativo, setAtivo] = useState(true)
  const [ehContaTecnica, setEhContaTecnica] = useState(false)
  const [erroDeEmail, setErroDeEmail] = useState<string | null>(null)

  const criar = usarCriarAtendente()
  const atualizar = usarAtualizarAtendente()
  const salvando = criar.isPending || atualizar.isPending

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
    setEhContaTecnica(atendente?.ehContaTecnica ?? false)
    setErroDeEmail(null)
  }, [aberto, atendente])

  const desativandoASiMesmo = Boolean(atendente) && atendente!.id === eu?.id && !ativo
  const desativandoAlguem = Boolean(atendente?.ativo) && !ativo

  async function salvar() {
    setErroDeEmail(null)
    const setor = setorPadraoId === SEM_SETOR ? null : setorPadraoId

    try {
      if (atendente) {
        await atualizar.mutateAsync({ id: atendente.id, nome, email, perfil, cargo, setorPadraoId: setor, ativo })
      } else {
        await criar.mutateAsync({ nome, email, senha, perfil, cargo, setorPadraoId: setor, ehContaTecnica })
      }

      toast.success(atendente ? 'Atendente atualizado.' : 'Atendente cadastrado.')
      aoFechar()
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.codigo === 'email_em_uso') {
        // Recusa cai no campo, nao em toast — e o proprio campo que esta errado.
        setErroDeEmail(falha.message)
        return
      }

      toast.error(falha instanceof ErroDaApi ? falha.message : 'Não foi possível salvar.')
    }
  }

  return (
    <Dialog open={aberto} onOpenChange={(v) => !v && aoFechar()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{atendente ? 'Editar atendente' : 'Novo atendente'}</DialogTitle>
          <DialogDescription>
            Cargo é o que a pessoa é na serventia. Classe da conta é a exigência de segurança —
            o que ela acessa se ajusta depois, em Permissões.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-5">
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
              onChange={(e) => {
                setEmail(e.target.value)
                setErroDeEmail(null)
              }}
              aria-invalid={Boolean(erroDeEmail)}
            />
            {erroDeEmail ? (
              <p role="alert" className="text-xs text-erro">
                {erroDeEmail}
              </p>
            ) : null}
          </div>

          {!atendente ? (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="atendente-senha">Senha inicial</Label>
              <div className="flex gap-2">
                <Input
                  id="atendente-senha"
                  type="text"
                  value={senha}
                  onChange={(e) => setSenha(e.target.value)}
                  placeholder="Mínimo de 8 caracteres"
                  className="font-mono"
                />
                <Button
                  type="button"
                  variant="outline"
                  size="icon"
                  title="Gerar senha forte"
                  onClick={() => setSenha(gerarSenhaForte())}
                >
                  <Dices />
                </Button>
              </div>
              <p className="text-xs text-tinta-media">
                Gere uma senha forte e entregue por canal fora de banda, ou digite uma provisória.
              </p>
            </div>
          ) : null}

          {!atendente ? (
            <div className="flex flex-col gap-1.5 rounded-lg border border-linha px-3 py-2.5">
              <div className="flex items-center justify-between">
                <Label htmlFor="atendente-conta-tecnica">Conta de suporte técnico</Label>
                <Switch id="atendente-conta-tecnica" checked={ehContaTecnica} onCheckedChange={setEhContaTecnica} />
              </div>
              <p className="text-xs text-tinta-media">
                Blindagem LGPD: nasce sem nenhuma permissão de ler conversa, ficha ou relatório —
                só ganha acesso temporário quando um administrador aprovar um pedido, pela tela
                "Acesso aos dados". Decisão de agora — não pode ser mudada depois de salvar.
              </p>
            </div>
          ) : atendente.ehContaTecnica ? (
            <p className="rounded-lg border border-linha bg-superficie px-3 py-2.5 text-xs text-tinta-media">
              Esta é uma conta de suporte técnico — só lê dado de cliente com acesso técnico
              aprovado pela tela "Acesso aos dados".
            </p>
          ) : null}

          <fieldset className="flex flex-col gap-2">
            <legend className="mb-1 text-sm font-medium text-tinta">Cargo na serventia</legend>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
              {CARGOS.map((c) => (
                <button
                  key={c.valor}
                  type="button"
                  aria-pressed={cargo === c.valor}
                  onClick={() => setCargo(c.valor)}
                  className={cn(
                    'flex flex-col gap-0.5 rounded-lg border p-2.5 text-left transition-all duration-150',
                    cargo === c.valor
                      ? 'border-tinta bg-superficie shadow-card'
                      : 'border-linha hover:border-linha-forte hover:bg-superficie',
                  )}
                >
                  <span className="text-xs font-semibold text-tinta">{c.rotulo}</span>
                  <span className="text-[11px] text-tinta-media">{c.descricao}</span>
                </button>
              ))}
            </div>
            <p className="text-xs text-tinta-media">Informação funcional — não concede permissão nenhuma.</p>
          </fieldset>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="atendente-classe">Classe da conta</Label>
            <Select value={perfil} onValueChange={(v) => setPerfil(v as PerfilDeAtendente)}>
              <SelectTrigger id="atendente-classe">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {CLASSES.map((c) => (
                  <SelectItem key={c.valor} value={c.valor}>
                    {c.rotulo}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-xs text-tinta-media">
              Define a exigência de segurança — Administrador e Titular ficam sujeitos à
              verificação em duas etapas obrigatória. O que a pessoa acessa se ajusta em
              Permissões, depois de salvar.
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
            <div className="flex flex-col gap-1.5 rounded-lg border border-linha px-3 py-2.5">
              <div className="flex items-center justify-between">
                <Label htmlFor="atendente-ativo">Acesso ativo</Label>
                <Switch
                  id="atendente-ativo"
                  checked={ativo}
                  disabled={atendente.id === eu?.id}
                  onCheckedChange={setAtivo}
                />
              </div>
              <p className="text-xs text-tinta-media">
                {atendente.id === eu?.id
                  ? 'Você não pode desativar o próprio acesso.'
                  : desativandoAlguem
                    ? 'Desativar encerra as sessões abertas desta pessoa agora mesmo.'
                    : 'Desativar corta o acesso sem apagar histórico — as mensagens já enviadas continuam com o nome de quem as escreveu.'}
              </p>
            </div>
          ) : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={aoFechar}>
            Cancelar
          </Button>
          <Button
            onClick={() => void salvar()}
            disabled={
              salvando ||
              !nome.trim() ||
              !email.trim() ||
              (!atendente && senha.length < 8) ||
              desativandoASiMesmo
            }
          >
            Salvar
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
