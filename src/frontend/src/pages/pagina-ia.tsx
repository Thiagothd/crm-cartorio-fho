import { useQuery } from '@tanstack/react-query'
import { iaApi } from '@/lib/api/endpoints'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { cn } from '@/lib/utils'
import { formatarNumero, formatarPercentual } from '@/lib/formatacao'

/**
 * v1 deliberadamente simples: status, disponibilidade e consumo do dia.
 * Configuração de prompt, modelo e teto fica no `appsettings` do servidor —
 * fora desta tela de propósito: é regra de segurança, não redação, e o
 * AGENTS.md pede exatamente isso ("não criar configuração profunda da IA
 * sem necessidade").
 */
export function PaginaIa() {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['ia-situacao'],
    queryFn: () => iaApi.situacao(),
    staleTime: 30_000,
  })

  return (
    <div className="flex-1 overflow-y-auto p-3 md:p-6">
      <div className="mx-auto flex max-w-2xl flex-col gap-4">
        <p className="text-sm text-tinta-media">
          A Resposta Inteligente responde só com o que está cadastrado no catálogo de
          serviços e nas respostas rápidas — nunca inventa prazo, valor ou documento. Só
          escala para um atendente quando o cliente pede explicitamente.
        </p>

        {isLoading ? (
          <div className="flex flex-col gap-3">
            <Skeleton className="h-20 w-full" />
            <Skeleton className="h-28 w-full" />
          </div>
        ) : isError || !data ? (
          <EstadoDaInterface tipo="erro" descricao="Não foi possível carregar a situação da IA." />
        ) : (
          <>
            <Card>
              <CardHeader className="flex-row items-center justify-between gap-3">
                <div>
                  <CardTitle className="font-titulo text-base">Status</CardTitle>
                  <CardDescription>Modelo {data.modelo}</CardDescription>
                </div>
                <Badge variant={data.estaConfigurada ? 'success' : 'outline'}>
                  {data.estaConfigurada ? 'Ativa' : data.habilitada ? 'Sem chave configurada' : 'Desligada'}
                </Badge>
              </CardHeader>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="font-titulo text-base">Consumo de hoje</CardTitle>
                <CardDescription>
                  Teto de custo, não de qualidade — estourado, o atendimento humano assume o
                  resto do dia. Reinicia à meia-noite UTC.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <div className="flex items-baseline gap-2">
                  <span className="font-titulo text-3xl text-tinta">{formatarNumero(data.usadoHoje)}</span>
                  <span className="text-sm text-tinta-media">
                    de {formatarNumero(data.limiteDiario)} respostas
                  </span>
                </div>

                <div className="mt-3 h-2 overflow-hidden rounded-full bg-superficie">
                  <div
                    className={cn(
                      'h-full rounded-full transition-[width] duration-[var(--duracao-padrao)]',
                      data.usadoHoje >= data.limiteDiario ? 'bg-selo' : 'bg-verde',
                    )}
                    style={{
                      width: `${Math.min(100, (data.usadoHoje / Math.max(1, data.limiteDiario)) * 100)}%`,
                    }}
                  />
                </div>

                <p className="mt-2 text-xs text-tinta-media">
                  {formatarPercentual(Math.min(100, (data.usadoHoje / Math.max(1, data.limiteDiario)) * 100))}{' '}
                  do teto diário
                </p>
              </CardContent>
            </Card>
          </>
        )}
      </div>
    </div>
  )
}
