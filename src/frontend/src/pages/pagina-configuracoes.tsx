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

      {/* min-w-0: sem isso, um item flex sem largura explícita não encolhe
          abaixo do conteúdo mais largo que carrega — e o trilho horizontal do
          fluxo de mensagens automáticas (fluxo-de-mensagens.tsx) tenta ser tão
          largo quanto a soma de todos os pontos. Sem o freio aqui, era esta
          div que crescia para caber tudo, e o `overflow-x-auto` local do
          trilho nunca chegava a ativar — o excesso só ficava cortado no
          `overflow-hidden` do <main> do Shell, sem barra de rolagem nenhuma. */}
      <div className="min-w-0 flex-1 overflow-y-auto p-3 md:p-6">
        <Tabs defaultValue="etapas">
          {/* flex-wrap, e não o scroll horizontal padrão do TabsList: com até
              7 abas (5 fixas + WhatsApp e Atendentes pra quem gerencia), rolar
              pra ver o resto escondia opção — quem procura "Atendentes" não
              sabe que precisa arrastar pra achar. Quebrando linha, todas ficam
              visíveis de uma vez; no desktop a largura sobra e continua numa
              linha só, sem mudar nada de como já era. */}
          <TabsList className="flex-wrap gap-1">
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
