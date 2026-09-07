import type { ModeloDePermissao } from '@/lib/api/tipos'

/**
 * Espelho de `CatalogoDePermissoes.DescreverModelo` — só o rótulo curto, para
 * o chip da linha da lista (que não carrega o catálogo inteiro, só
 * `Atendente.modelo`). A descrição completa e o conjunto de cada modelo vêm
 * do backend em `GET /api/atendentes/:id/permissoes`, nunca duplicados aqui.
 */
export const ROTULO_DO_MODELO: Record<ModeloDePermissao, string> = {
  Recepcao: 'Recepção',
  Balcao: 'Balcão',
  Catalogo: 'Catálogo',
  Supervisao: 'Supervisão',
  Administracao: 'Administração',
  Auditoria: 'Auditoria',
}

export const ORDEM_DOS_MODELOS: ModeloDePermissao[] = [
  'Recepcao',
  'Balcao',
  'Catalogo',
  'Supervisao',
  'Administracao',
  'Auditoria',
]
