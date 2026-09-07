import { useNavigate } from 'react-router-dom'
import { KeyRound, Pencil, Settings2, ShieldAlert } from 'lucide-react'
import { Avatar } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { formatarDataHora } from '@/lib/formatacao'
import { cn } from '@/lib/utils'
import type { Atendente } from '@/lib/api/tipos'

/**
 * Uma linha da lista de Equipe — cartão elevado (Fase 3.1, painel de
 * administração), não tabela. Três linhas de informação, igual ao que a
 * ficha de cliente já faz: identidade, contato/cargo, e por último setor e
 * último acesso. O modelo de permissão não aparece aqui — só na própria tela
 * de permissões, aberta pelo ícone de engrenagem.
 */
export function LinhaDeAtendente({
  atendente,
  aoEditar,
  aoRedefinirSenha,
  aoAbrirMfa,
}: {
  atendente: Atendente
  aoEditar: () => void
  aoRedefinirSenha: () => void
  aoAbrirMfa: () => void
}) {
  const navegar = useNavigate()
  const podeMfa = atendente.perfil === 'Administrador' || atendente.cargo === 'Titular'
  const semDoisFatores = podeMfa && !atendente.mfaAtivo

  return (
    <div
      className={cn(
        'group flex flex-col gap-2 rounded-card border border-linha bg-branco p-3.5 shadow-card transition-all duration-200 hover:-translate-y-0.5 hover:shadow-card-hover sm:flex-row sm:items-center sm:gap-3.5',
        !atendente.ativo && 'opacity-60',
      )}
    >
      <Avatar nome={atendente.nome} tamanho="md" />

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="truncate font-medium text-tinta">{atendente.nome}</span>
          {!atendente.ativo ? (
            <Badge variant="secondary">Sem acesso</Badge>
          ) : semDoisFatores ? (
            <Badge variant="destructive">Sem 2 etapas</Badge>
          ) : null}
        </div>

        <p className="truncate text-xs text-tinta-media">
          {atendente.email}
          {atendente.cargo !== 'NaoInformado' ? ` · ${atendente.cargoDescrito}` : ''}
        </p>

        <div className="mt-1 flex flex-wrap items-center gap-x-2.5 gap-y-1 text-[11px] text-tinta-fraca">
          {atendente.setorPadraoNome ? <span>{atendente.setorPadraoNome}</span> : null}
          <span>{atendente.ultimoAcessoEm ? `Acesso em ${formatarDataHora(atendente.ultimoAcessoEm)}` : 'Nunca acessou'}</span>
        </div>
      </div>

      <div className="flex items-center gap-1 self-end opacity-100 transition-opacity duration-150 sm:self-auto md:opacity-0 md:group-hover:opacity-100 md:group-focus-within:opacity-100">
        {podeMfa ? (
          <Button variant="ghost" size="icon-sm" title="Segurança (MFA)" onClick={aoAbrirMfa}>
            <ShieldAlert />
          </Button>
        ) : null}
        <Button variant="ghost" size="icon-sm" title="Redefinir senha" onClick={aoRedefinirSenha}>
          <KeyRound />
        </Button>
        <Button
          variant="ghost"
          size="icon-sm"
          title="Permissões"
          onClick={() => navegar(`/equipe/${atendente.id}/permissoes`)}
        >
          <Settings2 />
        </Button>
        <Button variant="ghost" size="icon-sm" title="Editar" onClick={aoEditar}>
          <Pencil />
        </Button>
      </div>
    </div>
  )
}
