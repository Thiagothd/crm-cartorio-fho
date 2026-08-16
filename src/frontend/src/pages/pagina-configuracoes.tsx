import { CabecalhoDaPagina } from '@/components/layout/shell'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { AbaSetores } from '@/features/configuracoes/aba-setores'
import { AbaEtapas } from '@/features/configuracoes/aba-etapas'
import { AbaAtendentes } from '@/features/configuracoes/aba-atendentes'
import { AbaMensagensAutomaticas } from '@/features/configuracoes/aba-mensagens-automaticas'
import { AbaRespostasRapidas } from '@/features/configuracoes/aba-respostas-rapidas'
import { AbaAparencia } from '@/features/configuracoes/aba-aparencia'
import { AbaWhatsApp } from '@/features/configuracoes/aba-whatsapp'
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
            <TabsTrigger value="aparencia">Aparência</TabsTrigger>
            {/* Pareia o canal e vê o número conectado — decisão de quem responde
                pela serventia, mesma régua dos atendentes. */}
            {podeGerirAtendentes ? <TabsTrigger value="whatsapp">WhatsApp</TabsTrigger> : null}
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

          <TabsContent value="aparencia">
            <AbaAparencia />
          </TabsContent>

          {podeGerirAtendentes ? (
            <TabsContent value="whatsapp">
              <AbaWhatsApp />
            </TabsContent>
          ) : null}

          {podeGerirAtendentes ? (
            <TabsContent value="atendentes">
              <AbaAtendentes />
            </TabsContent>
          ) : null}
        </Tabs>
      </div>
    </>
  )
}
