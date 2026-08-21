import { AbaAtendentes } from '@/features/configuracoes/aba-atendentes'

/**
 * `AbaAtendentes` já checa `podeGerirAtendentes` internamente e mostra um
 * aviso textual se não tiver permissão — mantido como rede de segurança. O
 * gate real desta rota é `permissao: 'administrador'` em `app/navegacao.ts`.
 */
export function PaginaEquipe() {
  return (
    <div className="flex-1 overflow-y-auto p-3 md:p-6">
      <AbaAtendentes />
    </div>
  )
}
