import { ShieldAlert, UserCheck, Users, UserCog } from 'lucide-react'
import { CartaoDeIndicador } from '@/features/indicadores/cartao-de-indicador'
import type { Atendente } from '@/lib/api/tipos'

/** Início do dia local, para "ativos hoje" não depender do fuso do servidor. */
function ehHoje(iso: string | null) {
  if (!iso) return false
  const data = new Date(iso)
  const hoje = new Date()
  return (
    data.getFullYear() === hoje.getFullYear() &&
    data.getMonth() === hoje.getMonth() &&
    data.getDate() === hoje.getDate()
  )
}

/**
 * A faixa de indicadores no topo da tela — mesmo vocabulário de
 * `CartaoDeIndicador` que a Visão Geral e Relatórios já usam. "Sem 2 etapas"
 * é o único em `selo` (bordô), e só aparece com número: uma conta privilegiada
 * sem MFA é uma pendência de segurança, não um dado neutro — e some da
 * paleta de acento quando chega a zero.
 */
export function ResumoDaEquipe({ atendentes }: { atendentes: Atendente[] }) {
  const ativosHoje = atendentes.filter((a) => a.ativo && ehHoje(a.ultimoAcessoEm)).length
  const administram = atendentes.filter((a) => a.ativo && a.permissoes.includes('equipe.permissoes')).length

  // MFA obrigatório só se aplica a quem SEC-03 trata como conta privilegiada
  // (Administrador ou Titular) — contar todo mundo sem MFA misturaria "nunca
  // precisou" com "precisa e não tem".
  const semDoisFatores = atendentes.filter(
    (a) => a.ativo && !a.mfaAtivo && (a.perfil === 'Administrador' || a.cargo === 'Titular'),
  ).length

  return (
    <div className="grade-com-vida grid grid-cols-2 gap-3 lg:grid-cols-4">
      <CartaoDeIndicador rotulo="Na equipe" valor={String(atendentes.length)} icone={Users} />
      <CartaoDeIndicador
        rotulo="Ativos hoje"
        valor={String(ativosHoje)}
        detalhe="Fizeram login hoje"
        icone={UserCheck}
        cor="verde"
      />
      <CartaoDeIndicador
        rotulo="Administram"
        valor={String(administram)}
        detalhe="Com equipe.permissoes"
        icone={UserCog}
      />
      <CartaoDeIndicador
        rotulo="Sem 2 etapas"
        valor={String(semDoisFatores)}
        detalhe={semDoisFatores > 0 ? 'Deveriam ter matrícula' : 'Todo mundo em dia'}
        icone={ShieldAlert}
        cor={semDoisFatores > 0 ? 'selo' : 'verde'}
      />
    </div>
  )
}
