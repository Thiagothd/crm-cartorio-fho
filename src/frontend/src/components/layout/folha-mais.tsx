import { useNavigate, NavLink } from 'react-router-dom'
import * as DialogPrimitive from '@radix-ui/react-dialog'
import { LogOut } from 'lucide-react'
import { navegacaoVisivel } from '@/app/navegacao'
import { usarAutenticacao } from '@/lib/auth/usar-autenticacao'
import { cn } from '@/lib/utils'

/**
 * Menu completo do celular, numa folha que sobe do rodapé — não um hambúrguer
 * de canto. Primitivos crus do Radix Dialog, não o `DialogContent`
 * compartilhado: o sheet precisa de posição/tamanho totalmente diferentes do
 * diálogo centralizado padrão, e sobrepor classes utilitárias que já vêm do
 * componente arriscaria uma perder para a outra sem previsibilidade.
 */
export function FolhaMais({ aberta, aoFechar }: { aberta: boolean; aoFechar: () => void }) {
  const { atendente, tenant, sair, podeGerirCadastros, podeGerirAtendentes } = usarAutenticacao()
  const navegar = useNavigate()
  const grupos = navegacaoVisivel({ podeGerirCadastros, podeGerirAtendentes })

  return (
    <DialogPrimitive.Root
      open={aberta}
      onOpenChange={(aberto) => {
        if (!aberto) aoFechar()
      }}
    >
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-overlay md:hidden" />
        <DialogPrimitive.Content
          className="anima-subir-do-rodape fixed inset-x-0 bottom-0 z-50 flex max-h-[85dvh] flex-col rounded-t-2xl border-t border-linha-forte bg-tinta shadow-card-hover pb-[env(safe-area-inset-bottom)] focus:outline-none md:hidden"
        >
          <DialogPrimitive.Title className="sr-only">Menu principal</DialogPrimitive.Title>

          <div className="flex items-center gap-2.5 border-b border-linha-forte/35 px-4 py-3">
            <img
              src="/brand/crm-cartorio-simbolo.png"
              alt=""
              width={304}
              height={304}
              className="size-8 shrink-0 object-contain"
            />
            <div className="min-w-0">
              <p className="truncate font-titulo text-sm font-semibold text-branco">CRM Cartório</p>
              <p className="truncate text-[10px] text-linha">
                {tenant?.nomeFantasia ?? tenant?.razaoSocial ?? 'Cartório'}
              </p>
            </div>
          </div>

          <div className="flex-1 overflow-y-auto p-3">
            {grupos.map((grupo, indiceDoGrupo) => (
              <div key={grupo.titulo ?? `grupo-${indiceDoGrupo}`} className="mb-3">
                {grupo.titulo ? (
                  <p className="mb-1 px-3 text-[10px] uppercase tracking-wider text-linha/70">{grupo.titulo}</p>
                ) : null}
                <div className="flex flex-col gap-0.5">
                  {grupo.entradas
                    .flatMap((entrada) => (entrada.tipo === 'submenu' ? entrada.itens : [entrada]))
                    .map((item) => (
                      <NavLink
                        key={item.rota}
                        to={item.rota}
                        end={item.rota === '/'}
                        onClick={aoFechar}
                        className={({ isActive }) =>
                          cn(
                            'flex h-11 touch-manipulation items-center gap-3 rounded-lg px-3 text-sm transition-colors',
                            isActive ? 'bg-branco font-medium text-selo' : 'text-linha hover:bg-branco/10 hover:text-branco',
                          )
                        }
                      >
                        <item.icone className="size-4 shrink-0" aria-hidden />
                        {item.rotulo}
                      </NavLink>
                    ))}
                </div>
              </div>
            ))}
          </div>

          <div className="border-t border-linha-forte/35 p-3">
            <p className="truncate px-3 text-xs text-linha">
              {atendente?.nome}
              {atendente?.cargo && atendente.cargo !== 'NaoInformado' ? ` · ${atendente.cargoDescrito}` : ''}
            </p>
            <button
              type="button"
              onClick={() => {
                sair()
                aoFechar()
                navegar('/login')
              }}
              className="mt-1 flex h-11 w-full touch-manipulation items-center gap-3 rounded-lg px-3 text-sm text-linha transition-colors hover:bg-branco/10 hover:text-branco"
            >
              <LogOut className="size-4" aria-hidden />
              Sair
            </button>
          </div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  )
}
