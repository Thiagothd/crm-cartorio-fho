import { Lock, LockOpen } from 'lucide-react'
import { Painel } from '@/components/ui/painel'
import { Skeleton } from '@/components/ui/skeleton'
import { EstadoDaInterface } from '@/components/ui/estado-da-interface'
import { formatarDataHora, formatarNumero } from '@/lib/formatacao'
import { usarProtecaoAtiva } from './consultas'

function Cartao({ rotulo, valor }: { rotulo: string; valor: string }) {
  return (
    <div className="rounded-md border border-linha bg-superficie px-3 py-3">
      <p className="text-xs text-tinta-media">{rotulo}</p>
      <p className="mt-1 font-dado text-sm font-semibold text-tinta">{valor}</p>
    </div>
  )
}

/**
 * "O que já está protegido" — para o cartório conferir sem precisar ler
 * código ou documentação. `cifraAtiva` chega `false` até a cifra em repouso
 * (PR seguinte do plano de proteção de dados) existir; a tela mostra isso
 * honestamente, nunca inventa um valor.
 */
export function PainelDeProtecao() {
  const { data, isLoading, isError } = usarProtecaoAtiva()

  return (
    <Painel
      titulo="Proteção ativa"
      descricao="Resumo do que protege os dados dos seus clientes hoje, neste cartório."
    >
      {isLoading ? (
        <Skeleton className="h-20 w-full" />
      ) : isError || !data ? (
        <EstadoDaInterface tipo="erro" titulo="Não foi possível carregar" descricao="Tente novamente em instantes." />
      ) : (
        <div className="flex flex-col gap-3">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <div className="col-span-2 flex items-center gap-3 rounded-md border border-linha bg-superficie px-3 py-3 sm:col-span-1">
              {data.cifraAtiva ? (
                <Lock className="h-5 w-5 shrink-0 text-tinta" strokeWidth={1.6} />
              ) : (
                <LockOpen className="h-5 w-5 shrink-0 text-tinta-media" strokeWidth={1.6} />
              )}
              <div>
                <p className="text-xs text-tinta-media">Cifra em repouso</p>
                <p className="text-sm font-semibold text-tinta">
                  {data.cifraAtiva ? 'Ativa' : 'Ainda não ativa'}
                </p>
              </div>
            </div>

            {data.cifraAtiva && data.cifraDesde ? (
              <Cartao rotulo="Ativa desde" valor={formatarDataHora(data.cifraDesde)} />
            ) : null}

            {data.cifraAtiva && data.versaoDaChave ? (
              <Cartao rotulo="Versão da chave" valor={data.versaoDaChave} />
            ) : null}

            <Cartao rotulo="Acessos registrados" valor={formatarNumero(data.totalDeRegistrosDeAcesso)} />
          </div>

          <dl className="grid gap-1.5 text-xs text-tinta-media sm:grid-cols-[auto_1fr] sm:gap-x-3">
            <dt className="font-semibold text-tinta">Cifra em repouso</dt>
            <dd>
              {data.cifraAtiva
                ? 'O texto das conversas e os dados sensíveis da ficha (CPF, RG, filiação, endereço, data de nascimento) estão gravados cifrados no banco. Quem abre o banco ou o backup direto vê só texto embaralhado — a leitura legível só acontece pelo sistema, com permissão e registro.'
                : 'O texto das conversas e os dados sensíveis da ficha ainda estão gravados em texto legível no banco. O sistema já sabe cifrar; falta a chave ser configurada no servidor deste ambiente. Até lá, a proteção em vigor é a trilha de acesso abaixo.'}
            </dd>
            {data.cifraAtiva ? (
              <>
                <dt className="font-semibold text-tinta">Ativa desde</dt>
                <dd>Quando o conteúdo que já existia foi convertido para cifrado. Tudo gravado depois disso já nasce cifrado.</dd>
                <dt className="font-semibold text-tinta">Versão da chave</dt>
                <dd>Identificador da chave em uso. Muda só quando a chave é trocada — nunca precisa ser lembrado por ninguém.</dd>
              </>
            ) : null}
            <dt className="font-semibold text-tinta">Acessos registrados</dt>
            <dd>Total de vezes, desde o início, que alguém abriu uma conversa, baixou um anexo ou abriu a ficha completa de um cliente. Cada um aparece na tabela de acessos abaixo.</dd>
          </dl>
        </div>
      )}
    </Painel>
  )
}
