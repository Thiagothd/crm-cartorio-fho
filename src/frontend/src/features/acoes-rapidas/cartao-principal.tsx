import { MessageSquarePlus, Plus, UserRoundPlus } from 'lucide-react'

export function CartaoPrincipal({
  aoAbrirAtendimento,
  aoAbrirCliente,
}: {
  aoAbrirAtendimento: () => void
  aoAbrirCliente: () => void
}) {
  return (
    // NÃO é um <button> envolvendo tudo: há duas ações aqui dentro, e botão
    // dentro de botão é HTML inválido e armadilha de leitor de tela. É um
    // painel que contém dois botões.
    //
    // `[--cor-foco:var(--cor-linha)]`: o outline global é `tinta-media`, que
    // sobre `bg-tinta` é invisível. Mesma correção do rail (rail-de-navegacao.tsx).
    <div className="col-span-2 flex flex-col justify-between gap-4 rounded-card border border-tinta bg-tinta p-4 text-branco shadow-card [--cor-foco:var(--cor-linha)] sm:p-5 md:row-span-2">
      <div className="flex flex-col gap-2">
        <span
          aria-hidden
          className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-branco/10 text-branco"
        >
          <MessageSquarePlus className="size-5" />
        </span>

        <h3 className="text-base font-bold leading-tight tracking-tight text-branco sm:text-[17px]">
          Novo atendimento
        </h3>

        {/* Escondida no celular: ali o bloco inteiro já está empurrando o
            gráfico para baixo da dobra, e o título já diz o que o botão faz. */}
        <p className="hidden text-xs leading-relaxed text-linha sm:block">
          Abre a conversa e gera o protocolo — sem sair desta tela.
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        {/* Branco sobre tinta, NÃO bordô — o orçamento de bordô da tela
            já vai para "Atrasados" e as etiquetas de prazo de "Requer atenção". */}
        <button
          type="button"
          onClick={aoAbrirAtendimento}
          className="inline-flex min-h-11 flex-1 touch-manipulation items-center justify-center gap-2 rounded-lg bg-branco px-4 text-sm font-semibold text-tinta shadow-card transition-all duration-200 hover:bg-superficie hover:shadow-card-hover md:min-h-10"
        >
          <Plus className="size-4 shrink-0" aria-hidden />
          Abrir atendimento
        </button>

        <button
          type="button"
          onClick={aoAbrirCliente}
          className="inline-flex min-h-11 shrink-0 touch-manipulation items-center justify-center gap-1.5 rounded-lg border border-linha-forte/40 px-3.5 text-xs font-semibold text-linha transition-colors duration-200 hover:border-linha hover:bg-branco/10 hover:text-branco md:min-h-10"
        >
          <UserRoundPlus className="size-3.5 shrink-0" aria-hidden />
          Novo cliente
        </button>
      </div>
    </div>
  )
}
