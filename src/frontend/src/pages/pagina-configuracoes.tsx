import { CabecalhoDaPagina } from '@/components/layout/shell'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { AbaSetores } from '@/features/configuracoes/aba-setores'
import { AbaEtapas } from '@/features/configuracoes/aba-etapas'
import { AbaAtendentes } from '@/features/configuracoes/aba-atendentes'
import { AbaMensagensAutomaticas } from '@/features/configuracoes/aba-mensagens-automaticas'
import { AbaRespostasRapidas } from '@/features/configuracoes/aba-respostas-rapidas'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'

export function PaginaConfiguracoes() {
  const { tenant, podeGerirAtendentes } = usarAutenticacao()

  return (
    <>
      <CabecalhoDaPagina
        titulo="Configurações"
        descricao={tenant?.razaoSocial ?? undefined}
      />

      <div className="flex-1 overflow-y-auto p-6">
        <Tabs defaultValue="etapas">
          <TabsList>
            <TabsTrigger value="etapas">Etapas do funil</TabsTrigger>
            <TabsTrigger value="setores">Setores</TabsTrigger>
            <TabsTrigger value="mensagens">Mensagens automáticas</TabsTrigger>
            <TabsTrigger value="respostas">Respostas rápidas</TabsTrigger>
            {podeGerirAtendentes ? <TabsTrigger value="atendentes">Atendentes</TabsTrigger> : null}
          </TabsList>

          <TabsContent value="etapas">
            <AbaEtapas />
          </TabsContent>

          <TabsContent value="setores">
            <AbaSetores />
          </TabsContent>

          <TabsContent value="mensagens">
            <AbaMensagensAutomaticas />
          </TabsContent>

          <TabsContent value="respostas">
            <AbaRespostasRapidas />
          </TabsContent>

          {podeGerirAtendentes ? (
            <TabsContent value="atendentes">
              <AbaAtendentes />
            </TabsContent>
          ) : null}
        </Tabs>

        <div className="mt-8 rounded-md border border-linha bg-superficie px-4 py-3 text-sm">
          <p className="font-medium">WhatsApp</p>
          <p className="mt-1 text-tinta-media">
            {tenant?.whatsappConfigurado
              ? `Número conectado: ${tenant.whatsappNumeroExibicao ?? '—'}`
              : 'Ainda não conectado. A integração real é a Fase 2 e depende de dois pré-requisitos externos: CNPJ com conta Meta Business verificada e um número de telefone dedicado, que não pode estar ativo no aplicativo WhatsApp pessoal.'}
          </p>
        </div>
      </div>
    </>
  )
}
