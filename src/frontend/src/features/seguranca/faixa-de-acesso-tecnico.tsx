import { useQuery } from '@tanstack/react-query'
import { ShieldAlert } from 'lucide-react'
import { acessoTecnicoApi } from '@/lib/api/endpoints'
import { formatarHora } from '@/lib/formatacao'

/**
 * Blindagem LGPD: "a equipe do projeto pede, o cartório aprova, a faixa avisa, a
 * trilha registra" — docs/PROTECAO-DE-DADOS.md. Visível a QUALQUER usuário
 * autenticado do tenant enquanto houver acesso técnico aprovado e ainda
 * dentro do prazo — nunca só a quem administra.
 *
 * Cor deliberadamente NEUTRA (não bordô): esta faixa aparece em toda tela do
 * sistema, e o bordô é orçamento escasso (DESIGN.md 2.1, no máximo 3 usos
 * por tela) que cada tela já gasta com a própria ação primária — gastar um
 * quarto uso aqui, em toda tela, estouraria o limite sistematicamente.
 */
export function FaixaDeAcessoTecnico() {
  const { data } = useQuery({
    queryKey: ['seguranca', 'acesso-tecnico', 'ativa'],
    queryFn: () => acessoTecnicoApi.ativa(),
    // 60s: e' um aviso, nao um cronometro — nao vale a pena gastar mais rede
    // por isso, e um atraso de ate um minuto para o aviso sumir/aparecer nao
    // muda nenhuma decisao de quem esta so lendo a tela.
    refetchInterval: 60_000,
    staleTime: 30_000,
  })

  if (!data) {
    return null
  }

  return (
    <div className="flex items-center gap-2 border-b border-linha bg-superficie px-3 py-2 text-xs text-tinta-media md:px-6">
      <ShieldAlert className="h-4 w-4 shrink-0" strokeWidth={1.6} />
      <p className="min-w-0 truncate">
        <span className="font-semibold text-tinta">Acesso técnico da equipe do projeto ativo</span> até{' '}
        {formatarHora(data.validaAte)} — {data.motivo}
      </p>
    </div>
  )
}
