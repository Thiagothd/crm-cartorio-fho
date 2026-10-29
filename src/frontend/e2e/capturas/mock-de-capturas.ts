import type { Page, Route } from '@playwright/test'
import * as demo from './estado-de-demonstracao'

/**
 * Route handler PRÓPRIO para gerar os screenshots do roteiro público —
 * NUNCA importado por `specs/*.e2e.ts`. Cobre endpoints que
 * `fixtures/api-mock.ts` não precisa cobrir (MFA completo, indicadores das 4
 * abas, IA, consultas de CEP/CNPJ, importação, ações de conversa) porque o
 * objetivo aqui é gerar tela cheia e realista, não exercitar um caso de teste.
 */

type Estado = {
  perfilAtivo: demo.Perfil
  atendentes: any[]
  clientes: any[]
  conversas: any[]
  detalhes: Record<string, { conversa: any; mensagens: any[]; historico: any[] }>
  setores: any[]
  etapas: any[]
  servicos: any[]
  respostasRapidas: any[]
  mensagensAutomaticas: any[]
  conhecimentos: any[]
  pendencias: any[]
  canal: typeof demo.canal
  mfaAtiva: boolean
}

function sincronizarPendenciasNasConversas(estado: Estado) {
  for (const c of estado.conversas) {
    const abertas = estado.pendencias.filter(
      (p) => p.clienteId === c.clienteId && p.status !== 'Quitada',
    )
    c.clientePendenciasAbertas = abertas.length
    c.clienteSaldoDevedor = abertas.reduce((acc, p) => acc + (p.saldoRestante ?? 0), 0)
    if (estado.detalhes[c.id]) {
      estado.detalhes[c.id].conversa = c
    }
  }
}

export function criarEstadoDeCaptura(perfil: demo.Perfil = 'Administrador'): Estado {
  const estado: Estado = {
    perfilAtivo: perfil,
    atendentes: [demo.usuarios.Atendente, demo.usuarios.Supervisor, demo.usuarios.Administrador, ...demo.atendentesExtras],
    clientes: demo.clientes.map((c) => ({ ...c })),
    conversas: demo.conversas.map((c) => ({ ...c })),
    detalhes: JSON.parse(JSON.stringify(demo.detalhes)),
    setores: demo.setores.map((s) => ({ ...s })),
    etapas: demo.etapas.map((e) => ({ ...e })),
    servicos: demo.servicos.map((s) => ({ ...s })),
    respostasRapidas: demo.respostasRapidas.map((r) => ({ ...r })),
    mensagensAutomaticas: demo.mensagensAutomaticas.map((m) => ({ ...m })),
    conhecimentos: demo.conhecimentos.map((c) => ({ ...c })),
    pendencias: JSON.parse(JSON.stringify(demo.pendenciasFinanceiras)),
    canal: { ...demo.canal, situacao: { ...demo.canal.situacao } },
    mfaAtiva: true,
  }
  sincronizarPendenciasNasConversas(estado)
  return estado
}

function usuarioLogado(estado: Estado) {
  return estado.atendentes.find((a) => a.perfil === estado.perfilAtivo) ?? demo.usuarios.Administrador
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json; charset=utf-8', body: JSON.stringify(body) })
}
function problema(route: Route, status: number, codigo: string, title: string, extras: Record<string, unknown> = {}) {
  return json(route, { type: 'about:blank', status, codigo, title, ...extras }, status)
}
function corpoJson(route: Route) {
  try { return route.request().postDataJSON() as any } catch { return {} }
}
function sessao(estado: Estado) {
  const atendente = usuarioLogado(estado)
  return { token: `token-${atendente.perfil.toLowerCase()}`, expiraEm: '2026-12-31T00:00:00.000Z', atendente, tenant: demo.tenant }
}

export async function instalarMockDeCapturas(page: Page, estado = criarEstadoDeCaptura()) {
  await page.route('**/hubs/**', (route) => route.abort('blockedbyclient'))

  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname
    const metodo = request.method()

    // ------------------------------------------------------------- Sessão
    if (path === '/api/autenticacao/serventia' && metodo === 'GET') return json(route, { nome: demo.tenant.nomeFantasia })
    if (path === '/api/autenticacao/login' && metodo === 'POST') {
      const corpo = corpoJson(route)
      if (corpo.senha === 'ComMfa123!') {
        return json(route, { requerMfa: true, sessao: null, desafio: { desafio: 'desafio-captura', tipo: 'SegundoFator', expiraEm: '2026-12-31T00:00:00.000Z' } })
      }
      return json(route, { requerMfa: false, sessao: sessao(estado), desafio: null })
    }
    if (path === '/api/autenticacao/login/mfa' && metodo === 'POST') return json(route, sessao(estado))
    if (path === '/api/autenticacao/sessao' && metodo === 'GET') return json(route, sessao(estado))

    // ----------------------------------------------------------------- MFA
    if (path === '/api/mfa/situacao' && metodo === 'GET') return json(route, estado.mfaAtiva ? demo.mfaSituacaoAtiva : demo.mfaSituacaoInativa)
    if (path === '/api/mfa/iniciar' && metodo === 'POST') {
      return json(route, { otpauthUri: 'otpauth://totp/CRM%20Cartorio:beatriz%40cartoriomodelo.exemplo?secret=JBSWY3DPEHPK3PXP&issuer=CRM%20Cartorio', segredoBase32: 'JBSWY3DPEHPK3PXP', expiraEm: '2026-12-31T00:00:00.000Z' })
    }
    if (path === '/api/mfa/confirmar' && metodo === 'POST') {
      estado.mfaAtiva = true
      return json(route, { sessao: sessao(estado), codigosDeRecuperacao: Array.from({ length: 10 }, (_, i) => `XXXX-XXXX-${String(i).padStart(4, '0')}`) })
    }
    if (path === '/api/mfa/desativar' && metodo === 'POST') { estado.mfaAtiva = false; return route.fulfill({ status: 204 }) }
    if (path === '/api/mfa/codigos' && metodo === 'POST') return json(route, { codigosDeRecuperacao: Array.from({ length: 10 }, (_, i) => `YYYY-YYYY-${String(i).padStart(4, '0')}`) })
    if (path === '/api/mfa/dispositivos-confiaveis/esquecer' && metodo === 'POST') return route.fulfill({ status: 204 })
    if (path === '/api/mfa/matricula-privilegiada/sessao' && metodo === 'POST') return json(route, { desafio: 'desafio-matricula', tipo: 'MatriculaPrivilegiada', expiraEm: '2026-12-31T00:00:00.000Z' })
    if (path === '/api/mfa/matricula-privilegiada/iniciar-autenticador' && metodo === 'POST') {
      return json(route, { otpauthUri: 'otpauth://totp/CRM%20Cartorio:titular%40cartoriomodelo.exemplo?secret=NBSWY3DPFQQHO33S&issuer=CRM%20Cartorio', segredoBase32: 'NBSWY3DPFQQHO33S', expiraEm: '2026-12-31T00:00:00.000Z' })
    }
    if (path === '/api/mfa/matricula-privilegiada/confirmar' && metodo === 'POST') {
      return json(route, { sessao: sessao(estado), codigosDeRecuperacao: Array.from({ length: 10 }, (_, i) => `ZZZZ-ZZZZ-${String(i).padStart(4, '0')}`) })
    }

    // ------------------------------------------------------- Estrutura
    if (path === '/api/setores' && metodo === 'GET') return json(route, estado.setores)
    if (path === '/api/etapas' && metodo === 'GET') return json(route, estado.etapas)
    if (path === '/api/tipos-servico' && metodo === 'GET') return json(route, estado.servicos.filter((s) => url.searchParams.get('incluirInativos') === 'true' || s.ativo))
    if (path === '/api/respostas-rapidas' && metodo === 'GET') return json(route, estado.respostasRapidas)
    if (path === '/api/mensagens-automaticas' && metodo === 'GET') return json(route, estado.mensagensAutomaticas.filter((m) => url.searchParams.get('incluirInativas') === 'true' || m.ativo))
    if (path === '/api/serventia/chaves-gerais' && metodo === 'GET') return json(route, { mensagensAutomaticasHabilitadas: true, respostaInteligenteHabilitada: true })
    if (path === '/api/serventia/encerramento-automatico' && metodo === 'GET') return json(route, { minutosParaPerguntar: 20, minutosParaEncerrar: 30, horaInicial: 8, horaFinal: 18 })
    if (path === '/api/serventia/aparencia' && metodo === 'GET') return json(route, { aparenciaDaConversa: 'PapelLiso' })

    // ------------------------------------------------------ Conhecimento IA
    if (path === '/api/conhecimento-de-ia' && metodo === 'GET') return json(route, estado.conhecimentos.filter((c) => url.searchParams.get('incluirInativos') === 'true' || c.ativo))
    const conhecimentoHistorico = path.match(/^\/api\/conhecimento-de-ia\/([^/]+)\/historico$/)
    if (conhecimentoHistorico && metodo === 'GET') {
      const alvo = estado.conhecimentos.find((c) => c.id === conhecimentoHistorico[1])
      return json(route, alvo && alvo.versao > 1 ? [{ versao: 1, titulo: alvo.titulo, conteudo: 'Texto da versão anterior, antes do último ajuste de redação.', tema: alvo.tema, tipoServicoNome: alvo.tipoServicoNome, vigenteDesde: null, vigenteAte: null, ativo: true, alteradoPorNome: 'Beatriz Fonseca', ocorridoEm: demo.AGORA }] : [])
    }

    // ------------------------------------------------------------ WhatsApp
    if (path === '/api/whatsapp/situacao' && metodo === 'GET') return json(route, estado.canal.situacao)
    if (path === '/api/whatsapp/regras' && metodo === 'GET') return json(route, estado.canal.regras)
    if (path === '/api/whatsapp/descartes' && metodo === 'GET') return json(route, estado.canal.descartes)
    if (path === '/api/whatsapp/conexoes' && metodo === 'GET') return json(route, estado.canal.conexoes)
    if (path === '/api/whatsapp/parear' && metodo === 'POST') { estado.canal.situacao = demo.canal.situacaoDesconectada; return json(route, estado.canal.situacao) }
    if (path === '/api/whatsapp/trocar-numero' && metodo === 'POST') { estado.canal.situacao = demo.canal.situacaoDesconectada; return json(route, estado.canal.situacao) }
    if (path === '/api/whatsapp/desconectar' && metodo === 'POST') { estado.canal.situacao = { ...estado.canal.situacao, conectado: false, estado: 'close' }; return json(route, estado.canal.situacao) }
    if (path === '/api/whatsapp/testar-envio' && metodo === 'POST') return json(route, { enviado: true, motivo: null })
    if (path === '/api/whatsapp/regras/modo' && metodo === 'PUT') { estado.canal.regras = { ...estado.canal.regras, modo: corpoJson(route).modo }; return json(route, estado.canal.regras) }
    if (path === '/api/whatsapp/regras/numeros' && metodo === 'POST') {
      const corpo = corpoJson(route)
      const novo = { id: `numero-${Date.now()}`, telefone: corpo.telefone, rotulo: corpo.rotulo, tipo: corpo.tipo, criadoEm: demo.AGORA, criadoPorNome: usuarioLogado(estado).nome }
      if (corpo.tipo === 'Bloqueado') estado.canal.regras.bloqueados.push(novo); else estado.canal.regras.liberados.push(novo)
      return json(route, novo, 201)
    }

    // ---------------------------------------------------------- Consultas
    const cep = path.match(/^\/api\/consultas\/cep\/([^/]+)$/)
    if (cep && metodo === 'GET') return json(route, { cep: cep[1], logradouro: 'Praça São Francisco', bairro: 'Centro', cidade: 'Gameleiras', uf: 'MG' })
    const cnpj = path.match(/^\/api\/consultas\/cnpj\/([^/]+)$/)
    if (cnpj && metodo === 'GET') return json(route, { cnpj: cnpj[1], razaoSocial: 'Construtora Vale Verde Ltda.', nomeFantasia: 'Construtora Vale Verde', endereco: { cep: '39505000', logradouro: 'Avenida Central', bairro: 'Centro', cidade: 'Gameleiras', uf: 'MG' } })

    // ----------------------------------------------------------- Conversas
    if (path === '/api/conversas/resumo' && metodo === 'GET') {
      const ativas = estado.conversas.filter((c) => c.status === 'Ativa')
      return json(route, {
        totalAtivas: ativas.length, naFila: ativas.filter((c) => !c.atendenteId).length,
        aguardandoResposta: ativas.filter((c) => c.mensagensNaoLidas > 0).length,
        minhasConversas: ativas.filter((c) => c.atendenteId === usuarioLogado(estado).id).length,
        encerradasHoje: 1, emAlerta: ativas.filter((c) => c.situacaoPrazo === 'EmAlerta').length,
        atrasadas: ativas.filter((c) => c.situacaoPrazo === 'Atrasada').length,
        porEtapa: estado.etapas.map((e) => ({ id: e.id, nome: e.nome, cor: e.cor, total: ativas.filter((c) => c.etapaId === e.id).length })),
        porSetor: estado.setores.map((s) => ({ id: s.id, nome: s.nome, cor: null, total: ativas.filter((c) => c.setorId === s.id).length })),
      })
    }
    if (path === '/api/conversas' && metodo === 'GET') {
      sincronizarPendenciasNasConversas(estado)
      const busca = (url.searchParams.get('busca') ?? '').toLowerCase()
      let itens = estado.conversas
      const status = url.searchParams.get('status')
      if (status) itens = itens.filter((c) => c.status === status)
      if (url.searchParams.get('apenasNaFila') === 'true') itens = itens.filter((c) => !c.atendenteId)
      if (url.searchParams.get('apenasMinhas') === 'true') itens = itens.filter((c) => c.atendenteId === usuarioLogado(estado).id)
      if (url.searchParams.get('apenasDevedores') === 'true') itens = itens.filter((c) => (c.clientePendenciasAbertas ?? 0) > 0)
      const etapaId = url.searchParams.get('etapaId'); if (etapaId) itens = itens.filter((c) => c.etapaId === etapaId)
      const setorId = url.searchParams.get('setorId'); if (setorId) itens = itens.filter((c) => c.setorId === setorId)
      const situacaoPrazo = url.searchParams.get('situacaoPrazo'); if (situacaoPrazo) itens = itens.filter((c) => c.situacaoPrazo === situacaoPrazo)
      if (busca) itens = itens.filter((c) => `${c.clienteNome} ${c.protocolo} ${c.clienteTelefone}`.toLowerCase().includes(busca))
      return json(route, { itens, total: itens.length, pagina: 1, tamanho: 60 })
    }
    const conversaDetalhe = path.match(/^\/api\/conversas\/([^/]+)$/)
    if (conversaDetalhe && metodo === 'GET') {
      sincronizarPendenciasNasConversas(estado)
      const encontrado = estado.detalhes[conversaDetalhe[1]]
      return encontrado ? json(route, encontrado) : problema(route, 404, 'conversa_nao_encontrada', 'Conversa não encontrada.')
    }
    if (path === '/api/conversas' && metodo === 'POST') {
      const corpo = corpoJson(route)
      const cliente = estado.clientes.find((c) => c.id === corpo.clienteId)
      const nova = { id: `conversa-${Date.now()}`, protocolo: '202609-0099', clienteId: corpo.clienteId, clienteNome: cliente?.nome ?? 'Novo cliente', clienteTelefone: cliente?.telefoneWhatsapp ?? '', atendenteId: null, atendenteNome: null, setorId: corpo.setorId ?? null, setorNome: estado.setores.find((s) => s.id === corpo.setorId)?.nome ?? null, etapaId: corpo.etapaId ?? estado.etapas[0].id, etapaNome: estado.etapas[0].nome, etapaCor: estado.etapas[0].cor, tipoServicoId: null, tipoServicoNome: null, status: 'Ativa', iniciadaEm: demo.AGORA, ultimaMensagemEm: demo.AGORA, mensagensNaoLidas: 0, aguardandoTriagem: !corpo.setorId, prazoIniciadoEm: null, prazoLimite: null, situacaoPrazo: 'SemPrazo', previaUltimaMensagem: corpo.primeiraMensagem ?? null, encerradaEm: null, encerradaPorNome: null, motivoEncerramento: null, reabertaEm: null, clienteFotoPerfilUrl: null, clienteNomePendente: false }
      estado.conversas.unshift(nova)
      estado.detalhes[nova.id] = { conversa: nova, mensagens: [], historico: [] }
      return json(route, nova, 201)
    }
    for (const [acao, aplicar] of Object.entries({
      assumir: (c: any) => { c.atendenteId = usuarioLogado(estado).id; c.atendenteNome = usuarioLogado(estado).nome },
      liberar: (c: any) => { c.atendenteId = null; c.atendenteNome = null },
      ler: (c: any) => { c.mensagensNaoLidas = 0 },
    })) {
      const m = path.match(new RegExp(`^/api/conversas/([^/]+)/${acao}$`))
      if (m && metodo === 'POST') {
        const alvo = estado.conversas.find((c) => c.id === m[1])
        if (!alvo) return problema(route, 404, 'conversa_nao_encontrada', 'Conversa não encontrada.')
        aplicar(alvo)
        if (estado.detalhes[alvo.id]) estado.detalhes[alvo.id].conversa = alvo
        return acao === 'ler' ? route.fulfill({ status: 204 }) : json(route, alvo)
      }
    }
    const transferir = path.match(/^\/api\/conversas\/([^/]+)\/transferir$/)
    if (transferir && metodo === 'POST') {
      const corpo = corpoJson(route)
      const alvo = estado.conversas.find((c) => c.id === transferir[1])
      const destino = estado.atendentes.find((a) => a.id === corpo.atendenteDestinoId)
      if (alvo) { alvo.atendenteId = destino?.id ?? null; alvo.atendenteNome = destino?.nome ?? null; estado.detalhes[alvo.id].conversa = alvo }
      return json(route, alvo)
    }
    const mover = path.match(/^\/api\/conversas\/([^/]+)\/mover$/)
    if (mover && metodo === 'POST') {
      const corpo = corpoJson(route)
      const alvo = estado.conversas.find((c) => c.id === mover[1])
      if (alvo) {
        if (corpo.etapaId) { const e = estado.etapas.find((x) => x.id === corpo.etapaId); alvo.etapaId = e.id; alvo.etapaNome = e.nome; alvo.etapaCor = e.cor }
        if (corpo.setorId) { const s = estado.setores.find((x) => x.id === corpo.setorId); alvo.setorId = s.id; alvo.setorNome = s.nome }
        estado.detalhes[alvo.id].conversa = alvo
      }
      return json(route, alvo)
    }
    const vincularServico = path.match(/^\/api\/conversas\/([^/]+)\/servico$/)
    if (vincularServico && metodo === 'POST') {
      const corpo = corpoJson(route)
      const alvo = estado.conversas.find((c) => c.id === vincularServico[1])
      const servico = estado.servicos.find((s) => s.id === corpo.tipoServicoId)
      if (alvo && servico) { alvo.tipoServicoId = servico.id; alvo.tipoServicoNome = servico.nome; estado.detalhes[alvo.id].conversa = alvo }
      return json(route, alvo)
    }
    const encerrar = path.match(/^\/api\/conversas\/([^/]+)\/encerrar$/)
    if (encerrar && metodo === 'POST') {
      const alvo = estado.conversas.find((c) => c.id === encerrar[1])
      if (alvo) { alvo.status = 'Encerrada'; alvo.encerradaEm = demo.AGORA; alvo.encerradaPorNome = usuarioLogado(estado).nome; alvo.motivoEncerramento = corpoJson(route).motivo ?? null; estado.detalhes[alvo.id].conversa = alvo }
      return json(route, alvo)
    }
    const reabrir = path.match(/^\/api\/conversas\/([^/]+)\/reabrir$/)
    if (reabrir && metodo === 'POST') {
      const alvo = estado.conversas.find((c) => c.id === reabrir[1])
      if (alvo) { alvo.status = 'Ativa'; alvo.atendenteId = usuarioLogado(estado).id; alvo.atendenteNome = usuarioLogado(estado).nome; alvo.reabertaEm = demo.AGORA; estado.detalhes[alvo.id].conversa = alvo }
      return json(route, alvo)
    }
    const enviarMensagem = path.match(/^\/api\/conversas\/([^/]+)\/mensagens$/)
    if (enviarMensagem && metodo === 'POST') {
      const corpo = corpoJson(route)
      const atendente = usuarioLogado(estado)
      const msg = { id: `msg-${Date.now()}`, conversaId: enviarMensagem[1], autorTipo: 'Atendente', atendenteId: atendente.id, atendenteNome: atendente.nome, conteudo: corpo.conteudo, tipo: 'Texto', statusEntrega: 'Enviada', ehAutomatica: false, fixada: false, oculta: false, ocultaPorNome: null, encaminhadaDeProtocolo: null, enviadaEm: demo.AGORA, lidaEm: null, mensagemRespondidaId: corpo.mensagemRespondidaId ?? null, respondidaAutor: null, respondidaConteudo: null, arquivoNome: null, arquivoTipoMime: null, arquivoTamanhoBytes: null, geradaPorIa: false }
      estado.detalhes[enviarMensagem[1]]?.mensagens.push(msg)
      return json(route, msg, 201)
    }
    // PNG mínimo válido (1x1, marrom) — só para o anexo de imagem não cair em "não foi possível carregar".
    const anexoBinario = path.match(/^\/api\/conversas\/([^/]+)\/mensagens\/([^/]+)\/anexo$/)
    if (anexoBinario && metodo === 'GET') {
      const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==', 'base64')
      return route.fulfill({ status: 200, contentType: 'image/png', body: png })
    }

    const enviarAnexo = path.match(/^\/api\/conversas\/([^/]+)\/mensagens\/anexo$/)
    if (enviarAnexo && metodo === 'POST') {
      const atendente = usuarioLogado(estado)
      const msg = { id: `msg-anexo-${Date.now()}`, conversaId: enviarAnexo[1], autorTipo: 'Atendente', atendenteId: atendente.id, atendenteNome: atendente.nome, conteudo: 'documento-anexado.pdf', tipo: 'Documento', statusEntrega: 'Enviada', ehAutomatica: false, fixada: false, oculta: false, ocultaPorNome: null, encaminhadaDeProtocolo: null, enviadaEm: demo.AGORA, lidaEm: null, mensagemRespondidaId: null, respondidaAutor: null, respondidaConteudo: null, arquivoNome: 'documento-anexado.pdf', arquivoTipoMime: 'application/pdf', arquivoTamanhoBytes: 204800, geradaPorIa: false }
      estado.detalhes[enviarAnexo[1]]?.mensagens.push(msg)
      return json(route, msg, 201)
    }
    const fixar = path.match(/^\/api\/conversas\/([^/]+)\/mensagens\/([^/]+)\/fixar$/)
    if (fixar && metodo === 'POST') {
      const lista = estado.detalhes[fixar[1]]?.mensagens ?? []
      const fixarNovo = url.searchParams.get('fixar') === 'true'
      for (const m of lista) m.fixada = fixarNovo && m.id === fixar[2]
      return json(route, lista.find((m: any) => m.id === fixar[2]))
    }
    const ocultar = path.match(/^\/api\/conversas\/([^/]+)\/mensagens\/ocultar$/)
    if (ocultar && metodo === 'POST') {
      const corpo = corpoJson(route)
      const lista = estado.detalhes[ocultar[1]]?.mensagens ?? []
      for (const m of lista) if (corpo.mensagemIds.includes(m.id)) { m.oculta = corpo.ocultar; m.ocultaPorNome = corpo.ocultar ? usuarioLogado(estado).nome : null }
      return json(route, { afetadas: corpo.mensagemIds.length })
    }
    const destinos = path.match(/^\/api\/conversas\/([^/]+)\/destinos$/)
    if (destinos && metodo === 'GET') {
      return json(route, estado.conversas.filter((c) => c.id !== destinos[1] && c.status === 'Ativa').map((c) => ({ id: c.id, protocolo: c.protocolo, clienteNome: c.clienteNome, setorNome: c.setorNome, atendenteNome: c.atendenteNome })))
    }
    const encaminhar = path.match(/^\/api\/conversas\/([^/]+)\/mensagens\/encaminhar$/)
    if (encaminhar && metodo === 'POST') return json(route, { encaminhadas: corpoJson(route).mensagemIds?.length ?? 0 })

    // ------------------------------------------------------------ Clientes
    if (path === '/api/clientes/opcoes' && metodo === 'GET') return json(route, { cidades: ['Gameleiras', 'Montes Claros'] })
    if (path === '/api/clientes' && metodo === 'GET') {
      const busca = (url.searchParams.get('busca') ?? '').toLowerCase()
      let itens = estado.clientes
      if (url.searchParams.get('incluirInativos') !== 'true') itens = itens.filter((c) => c.ativo)
      if (url.searchParams.get('semDocumento') === 'true') itens = itens.filter((c) => !c.documento)
      if (url.searchParams.get('semEndereco') === 'true') itens = itens.filter((c) => !c.logradouro)
      if (url.searchParams.get('comConversaAtiva') === 'true') itens = itens.filter((c) => c.temConversaAtiva)
      if (busca) itens = itens.filter((c) => `${c.nome} ${c.telefoneWhatsapp}`.toLowerCase().includes(busca))
      return json(route, { itens, total: itens.length, pagina: 1, tamanho: 100 })
    }
    if (path === '/api/clientes' && metodo === 'POST') {
      const corpo = corpoJson(route)
      const novo = { ...estado.clientes[0], ...corpo, id: `cliente-${Date.now()}`, percentualDeQualificacao: 20, totalConversas: 0, temConversaAtiva: false, ativo: true }
      estado.clientes.push(novo)
      return json(route, novo, 201)
    }
    const clienteAlvo = path.match(/^\/api\/clientes\/([^/]+)$/)
    if (clienteAlvo && metodo === 'PUT') {
      const alvo = estado.clientes.find((c) => c.id === clienteAlvo[1])
      if (!alvo) return problema(route, 404, 'cliente_nao_encontrado', 'Cliente não encontrado.')
      Object.assign(alvo, corpoJson(route)); return json(route, alvo)
    }
    if (path === '/api/clientes/importacao/analisar' && metodo === 'POST') {
      return json(route, {
        resumo: { totalLidas: 3, novas: 2, jaCadastradas: 1, invalidas: 0, ambiguas: 0, duplicadas: 0 },
        linhas: [
          { numeroDaLinha: 1, nome: 'Fernanda Cunha', telefoneBruto: '(38) 99999-4444', telefoneE164: '5538999994444', situacao: 'Nova', nomeDoCadastroExistente: null },
          { numeroDaLinha: 2, nome: 'Roberto Dias', telefoneBruto: '(38) 99999-5555', telefoneE164: '5538999995555', situacao: 'Nova', nomeDoCadastroExistente: null },
          { numeroDaLinha: 3, nome: 'Maria Aparecida Souza', telefoneBruto: '(38) 99999-0001', telefoneE164: '5538999990001', situacao: 'JaCadastrada', nomeDoCadastroExistente: 'Maria Aparecida Souza' },
        ],
      })
    }
    if (path === '/api/clientes/importacao/confirmar' && metodo === 'POST') return json(route, { criados: corpoJson(route).linhas?.length ?? 0, ignorados: 0 })

    // --------------------------------------------------------- Indicadores
    if (path === '/api/indicadores/visao-geral' && metodo === 'GET') return json(route, demo.visaoGeral)
    if (path === '/api/indicadores/alertas' && metodo === 'GET') return json(route, demo.alertas)
    if (path === '/api/indicadores/painel' && metodo === 'GET') return json(route, demo.painel)
    if (path === '/api/indicadores/atendimentos' && metodo === 'GET') return json(route, demo.relatorioDeAtendimentos)
    if (path === '/api/indicadores/atendentes' && metodo === 'GET') return json(route, demo.relatorioDeAtendentes)
    if (path === '/api/indicadores/clientes' && metodo === 'GET') return json(route, demo.relatorioDeClientes)

    // ----------------------------------------------------------------- IA
    if (path === '/api/ia/situacao' && metodo === 'GET') return json(route, demo.situacaoDaIa)
    if (path === '/api/ia/analise' && metodo === 'GET') return json(route, demo.analiseDaIa)

    // ------------------------------------------------------------ Equipe
    if (path === '/api/atendentes' && metodo === 'GET') return json(route, estado.atendentes)
    const atendenteObter = path.match(/^\/api\/atendentes\/([^/]+)$/)
    if (atendenteObter && metodo === 'GET') { const a = estado.atendentes.find((x) => x.id === atendenteObter[1]); return a ? json(route, a) : problema(route, 404, 'atendente_nao_encontrado', 'Não encontrado.') }
    if (path === '/api/atendentes' && metodo === 'POST') {
      const corpo = corpoJson(route)
      const novo = { ...demo.usuarios.Atendente, ...corpo, id: `atendente-${Date.now()}`, cargoDescrito: corpo.cargo, ativo: true, mfaAtivo: false, modelo: null, permissoes: [] }
      estado.atendentes.push(novo); return json(route, novo, 201)
    }
    const atendenteEditar = path.match(/^\/api\/atendentes\/([^/]+)$/)
    if (atendenteEditar && metodo === 'PUT') { const a = estado.atendentes.find((x) => x.id === atendenteEditar[1]); if (a) Object.assign(a, corpoJson(route)); return json(route, a) }
    const permissoesGet = path.match(/^\/api\/atendentes\/([^/]+)\/permissoes$/)
    if (permissoesGet && metodo === 'GET') {
      const a = estado.atendentes.find((x) => x.id === permissoesGet[1])
      if (!a) return problema(route, 404, 'atendente_nao_encontrado', 'Não encontrado.')
      return json(route, { atendenteId: a.id, modelo: a.modelo, chaves: a.permissoes, catalogo: demo.CATALOGO_DE_PERMISSOES, modelos: demo.MODELOS_DE_PERMISSAO })
    }
    if (permissoesGet && metodo === 'PUT') {
      const a = estado.atendentes.find((x) => x.id === permissoesGet[1])
      const corpo = corpoJson(route)
      if (a) { a.modelo = corpo.modelo; a.permissoes = corpo.chaves }
      return route.fulfill({ status: 204 })
    }
    const senha = path.match(/^\/api\/atendentes\/([^/]+)\/senha$/)
    if (senha && metodo === 'POST') return route.fulfill({ status: 204 })
    const ticket = path.match(/^\/api\/atendentes\/([^/]+)\/mfa\/ticket$/)
    if (ticket && metodo === 'POST') return json(route, { ticket: 'TICKET-DEMO-1234-5678', expiraEm: demo.AGORA })
    const reset = path.match(/^\/api\/atendentes\/([^/]+)\/mfa\/reset$/)
    if (reset && metodo === 'POST') return route.fulfill({ status: 204 })

    if (path === '/api/setores' && metodo === 'POST') { const novo = { id: `setor-${Date.now()}`, conversasAtivas: 0, ...corpoJson(route) }; estado.setores.push(novo); return json(route, novo, 201) }
    if (path === '/api/etapas' && metodo === 'POST') { const novo = { id: `etapa-${Date.now()}`, conversasAtivas: 0, ...corpoJson(route) }; estado.etapas.push(novo); return json(route, novo, 201) }
    if (path === '/api/etapas/reordenar' && metodo === 'POST') { const ids = corpoJson(route) as string[]; estado.etapas.sort((a, b) => ids.indexOf(a.id) - ids.indexOf(b.id)); return json(route, estado.etapas) }
    if (path === '/api/tipos-servico' && metodo === 'POST') { const corpo = corpoJson(route); const novo = { ...corpo, id: `servico-${Date.now()}`, conversasAtivas: 0 }; estado.servicos.push(novo); return json(route, novo, 201) }
    if (path === '/api/respostas-rapidas' && metodo === 'POST') { const novo = { id: `rapida-${Date.now()}`, ...corpoJson(route) }; estado.respostasRapidas.push(novo); return json(route, novo, 201) }
    if (path === '/api/mensagens-automaticas' && metodo === 'POST') { const novo = { id: `auto-${Date.now()}`, gatilhoDescrito: '', etapaAlvoNome: null, ...corpoJson(route) }; estado.mensagensAutomaticas.push(novo); return json(route, novo, 201) }
    if (path === '/api/conhecimento-de-ia' && metodo === 'POST') { const novo = { id: `conhecimento-${Date.now()}`, versao: 1, criadoPorNome: usuarioLogado(estado).nome, atualizadoPorNome: null, criadoEm: demo.AGORA, atualizadoEm: demo.AGORA, ...corpoJson(route) }; estado.conhecimentos.push(novo); return json(route, novo, 201) }
    if (path === '/api/serventia/encerramento-automatico' && metodo === 'PUT') return json(route, corpoJson(route))
    if (path === '/api/serventia/aparencia' && metodo === 'PUT') return json(route, corpoJson(route))

    // --------------------------------------------------------- Devedores
    if (path === '/api/devedores' && metodo === 'GET') {
      const hoje = new Date().toISOString().slice(0, 10)
      const recorte = url.searchParams.get('recorte') ?? 'abertas'
      const busca = (url.searchParams.get('busca') ?? '').toLowerCase()
      const tipoServicoId = url.searchParams.get('tipoServicoId')
      const ordenar = url.searchParams.get('ordenar') ?? 'maior_valor'

      const emAbertoTodas = estado.pendencias.filter((p) => p.status === 'Aberta')
      const quitadasTodas = estado.pendencias.filter((p) => p.status === 'Quitada')
      const vencidasTodas = emAbertoTodas.filter(
        (p) => p.dataPromessaPagamento && p.dataPromessaPagamento < hoje,
      )
      const hojeTodas = emAbertoTodas.filter((p) => p.dataPromessaPagamento === hoje)

      const resumo = {
        totalEmAberto: emAbertoTodas.reduce((acc, p) => acc + (p.saldoRestante ?? 0), 0),
        clientesDevedores: new Set(emAbertoTodas.map((p) => p.clienteId)).size,
        pendenciasAbertas: emAbertoTodas.length,
        pendenciasSemValor: emAbertoTodas.filter((p) => p.valorOriginal === null).length,
        promessasVencidasTotal: vencidasTodas.reduce((acc, p) => acc + (p.saldoRestante ?? 0), 0),
        promessasVencidasQuantidade: vencidasTodas.length,
        promessasParaHojeTotal: hojeTodas.reduce((acc, p) => acc + (p.saldoRestante ?? 0), 0),
        promessasParaHojeQuantidade: hojeTodas.length,
        recuperadoNoMesTotal: quitadasTodas.reduce((acc, p) => acc + (p.valorPago ?? 0), 0),
        recuperadoNoMesQuantidade: quitadasTodas.length,
      }

      let lancamentos = [...estado.pendencias]
      if (recorte === 'abertas') lancamentos = lancamentos.filter((p) => p.status === 'Aberta')
      else if (recorte === 'vencidas')
        lancamentos = lancamentos.filter(
          (p) => p.status === 'Aberta' && p.dataPromessaPagamento && p.dataPromessaPagamento < hoje,
        )
      else if (recorte === 'hoje')
        lancamentos = lancamentos.filter(
          (p) => p.status === 'Aberta' && p.dataPromessaPagamento === hoje,
        )
      else if (recorte === 'sem_cobranca')
        lancamentos = lancamentos.filter((p) => p.status === 'Aberta' && (p.totalCobrancas ?? 0) === 0)
      else if (recorte === 'parciais')
        lancamentos = lancamentos.filter((p) => p.status === 'Aberta' && (p.valorPago ?? 0) > 0)
      else if (recorte === 'sem_valor')
        lancamentos = lancamentos.filter((p) => p.status === 'Aberta' && p.valorOriginal === null)
      else if (recorte === 'quitadas')
        lancamentos = lancamentos.filter((p) => p.status === 'Quitada')

      if (tipoServicoId) {
        lancamentos = lancamentos.filter((p) => p.tipoServicoId === tipoServicoId)
      }

      if (busca) {
        lancamentos = lancamentos.filter((p) =>
          `${p.clienteNome} ${p.clienteTelefone} ${p.titulo} ${p.observacoes ?? ''}`
            .toLowerCase()
            .includes(busca),
        )
      }

      if (ordenar === 'maior_valor') {
        lancamentos.sort((a, b) => (b.saldoRestante ?? 0) - (a.saldoRestante ?? 0))
      } else if (ordenar === 'mais_antigas') {
        lancamentos.sort((a, b) => String(a.dataReferencia).localeCompare(String(b.dataReferencia)))
      } else if (ordenar === 'promessa_proxima') {
        lancamentos.sort((a, b) =>
          String(a.dataPromessaPagamento ?? '9999-99-99').localeCompare(
            String(b.dataPromessaPagamento ?? '9999-99-99'),
          ),
        )
      } else if (ordenar === 'nome') {
        lancamentos.sort((a, b) => String(a.clienteNome).localeCompare(String(b.clienteNome)))
      } else {
        lancamentos.sort((a, b) => String(b.criadoEm).localeCompare(String(a.criadoEm)))
      }

      const porCliente = new Map<string, any[]>()
      for (const p of lancamentos) {
        const atual = porCliente.get(p.clienteId) ?? []
        atual.push(p)
        porCliente.set(p.clienteId, atual)
      }

      const devedores = Array.from(porCliente.entries()).map(([clienteId, pends]) => {
        const primeira = pends[0]
        const abertasCli = pends.filter((x) => x.status === 'Aberta')
        const baseCalculo = abertasCli.length > 0 ? abertasCli : pends
        const promessas = abertasCli
          .map((x) => x.dataPromessaPagamento)
          .filter(Boolean)
          .sort()
        return {
          clienteId,
          clienteNome: primeira.clienteNome,
          clienteTelefone: primeira.clienteTelefone,
          clienteDocumento: primeira.clienteDocumento ?? null,
          clienteFotoPerfilUrl: primeira.clienteFotoPerfilUrl ?? '',
          conversaAtivaId: primeira.conversaAtivaId ?? null,
          quantidadePendenciasAbertas: abertasCli.length,
          quantidadeSemValor: abertasCli.filter((x) => x.valorOriginal === null).length,
          valorOriginalTotal: baseCalculo.reduce((acc, x) => acc + (x.valorOriginal ?? 0), 0),
          valorPagoTotal: baseCalculo.reduce((acc, x) => acc + (x.valorPago ?? 0), 0),
          saldoRestanteTotal: abertasCli.reduce((acc, x) => acc + (x.saldoRestante ?? 0), 0),
          dataReferenciaMaisAntiga: [...baseCalculo]
            .map((x) => x.dataReferencia)
            .sort()[0],
          proximaPromessaPagamento: promessas[0] ?? null,
          temPromessaVencida: abertasCli.some(
            (x) => x.dataPromessaPagamento && x.dataPromessaPagamento < hoje,
          ),
          temPromessaHoje: abertasCli.some((x) => x.dataPromessaPagamento === hoje),
          ultimaCobrancaEm:
            [...pends]
              .map((x) => x.ultimaCobrancaEm)
              .filter(Boolean)
              .sort()
              .reverse()[0] ?? null,
          totalCobrancas: pends.reduce((acc, x) => acc + (x.totalCobrancas ?? 0), 0),
          pendencias: pends,
        }
      })

      return json(route, {
        resumo,
        devedores,
        lancamentos,
        totalLancamentos: lancamentos.length,
      })
    }

    const devedorPorCliente = path.match(/^\/api\/devedores\/cliente\/([^/]+)$/)
    if (devedorPorCliente && metodo === 'GET') {
      const clienteId = devedorPorCliente[1]
      const cliente = estado.clientes.find((c) => c.id === clienteId)
      const pends = estado.pendencias.filter((p) => p.clienteId === clienteId)
      const abertas = pends.filter((p) => p.status === 'Aberta')
      return json(route, {
        clienteId,
        clienteNome: cliente?.nome ?? pends[0]?.clienteNome ?? 'Cliente',
        clienteTelefone: cliente?.telefoneWhatsapp ?? pends[0]?.clienteTelefone ?? '',
        pendenciasAbertas: abertas.length,
        saldoDevedor: abertas.reduce((acc, p) => acc + (p.saldoRestante ?? 0), 0),
        pendencias: pends,
      })
    }

    if (path === '/api/devedores' && metodo === 'POST') {
      const corpo = corpoJson(route)
      let cliente = estado.clientes.find((c) => c.id === corpo.clienteId)
      if (!cliente && corpo.novoClienteTelefone) {
        const digitos = String(corpo.novoClienteTelefone).replace(/\D/g, '')
        const e164 = digitos.startsWith('55') ? digitos : `55${digitos}`
        cliente = estado.clientes.find((c) => c.telefoneWhatsapp === e164)
        if (!cliente) {
          cliente = {
            ...estado.clientes[0],
            id: `cliente-${Date.now()}`,
            nome: corpo.novoClienteNome || e164,
            telefoneWhatsapp: e164,
            documento: corpo.novoClienteDocumento ?? null,
            percentualDeQualificacao: 25,
            totalConversas: 0,
            temConversaAtiva: false,
            ativo: true,
            nomePendente: false,
          }
          estado.clientes.push(cliente)
        }
      }
      if (!cliente) {
        return problema(route, 400, 'cliente_obrigatorio', 'Selecione ou cadastre um cliente.')
      }
      const servico = estado.servicos.find((s) => s.id === corpo.tipoServicoId)
      const conversaAtiva = estado.conversas.find(
        (c) => c.clienteId === cliente.id && c.status === 'Ativa',
      )
      const valorOriginal =
        typeof corpo.valorOriginal === 'number' && corpo.valorOriginal > 0
          ? corpo.valorOriginal
          : null
      const atendente = usuarioLogado(estado)
      const nova = {
        id: `pendencia-${Date.now()}`,
        clienteId: cliente.id,
        clienteNome: cliente.nome,
        clienteTelefone: cliente.telefoneWhatsapp,
        clienteDocumento: cliente.documento ?? null,
        clienteFotoPerfilUrl: `/api/clientes/${cliente.id}/foto`,
        conversaOrigemId: corpo.conversaOrigemId ?? conversaAtiva?.id ?? null,
        conversaAtivaId: conversaAtiva?.id ?? null,
        tipoServicoId: servico?.id ?? null,
        tipoServicoNome: servico?.nome ?? null,
        titulo: corpo.titulo,
        observacoes: corpo.observacoes ?? null,
        valorOriginal,
        valorPago: 0,
        saldoRestante: valorOriginal,
        status: 'Aberta',
        dataReferencia: corpo.dataReferencia || new Date().toISOString().slice(0, 10),
        dataPromessaPagamento: corpo.dataPromessaPagamento ?? null,
        diasEmAberto: 0,
        diasDeAtraso: null,
        ultimaCobrancaEm: null,
        totalCobrancas: 0,
        registradoPorAtendenteId: atendente.id,
        registradoPorNome: atendente.nome,
        criadoEm: new Date().toISOString(),
        quitadaEm: null,
        quitadoPorAtendenteId: null,
        quitadoPorNome: null,
        pagamentos: [],
      }
      estado.pendencias.unshift(nova)
      sincronizarPendenciasNasConversas(estado)
      return json(route, nova, 201)
    }

    const devedorEditar = path.match(/^\/api\/devedores\/([^/]+)$/)
    if (devedorEditar && metodo === 'PUT') {
      const alvo = estado.pendencias.find((p) => p.id === devedorEditar[1])
      if (!alvo) return problema(route, 404, 'pendencia_nao_encontrada', 'Pendência não encontrada.')
      const corpo = corpoJson(route)
      const servico = estado.servicos.find((s) => s.id === corpo.tipoServicoId)
      alvo.titulo = corpo.titulo ?? alvo.titulo
      alvo.observacoes = corpo.observacoes ?? null
      alvo.tipoServicoId = servico?.id ?? null
      alvo.tipoServicoNome = servico?.nome ?? null
      alvo.valorOriginal =
        typeof corpo.valorOriginal === 'number' && corpo.valorOriginal > 0
          ? corpo.valorOriginal
          : null
      alvo.saldoRestante =
        alvo.valorOriginal !== null
          ? Math.max(0, Number((alvo.valorOriginal - alvo.valorPago).toFixed(2)))
          : null
      alvo.dataReferencia = corpo.dataReferencia ?? alvo.dataReferencia
      alvo.dataPromessaPagamento = corpo.dataPromessaPagamento ?? null
      sincronizarPendenciasNasConversas(estado)
      return json(route, alvo)
    }

    if (devedorEditar && metodo === 'DELETE') {
      estado.pendencias = estado.pendencias.filter((p) => p.id !== devedorEditar[1])
      sincronizarPendenciasNasConversas(estado)
      return route.fulfill({ status: 204 })
    }

    const devedorPagamento = path.match(/^\/api\/devedores\/([^/]+)\/pagamentos$/)
    if (devedorPagamento && metodo === 'POST') {
      const alvo = estado.pendencias.find((p) => p.id === devedorPagamento[1])
      if (!alvo) return problema(route, 404, 'pendencia_nao_encontrada', 'Pendência não encontrada.')
      const corpo = corpoJson(route)
      const atendente = usuarioLogado(estado)
      const valorLancado =
        typeof corpo.valor === 'number' && corpo.valor > 0
          ? corpo.valor
          : alvo.saldoRestante ?? alvo.valorOriginal ?? 0
      if (valorLancado > 0) {
        alvo.pagamentos.unshift({
          id: `pagamento-${Date.now()}`,
          pendenciaFinanceiraId: alvo.id,
          valor: valorLancado,
          meioPagamento: corpo.meioPagamento || 'Pix',
          observacao: corpo.observacao ?? null,
          pagoEm: new Date().toISOString(),
          registradoPorAtendenteId: atendente.id,
          registradoPorNome: atendente.nome,
        })
        alvo.valorPago = Number((alvo.valorPago + valorLancado).toFixed(2))
      }
      if (corpo.quitarTotalmente || (alvo.valorOriginal && alvo.valorPago >= alvo.valorOriginal)) {
        alvo.status = 'Quitada'
        alvo.saldoRestante = 0
        alvo.quitadaEm = new Date().toISOString()
        alvo.quitadoPorAtendenteId = atendente.id
        alvo.quitadoPorNome = atendente.nome
      } else {
        alvo.status = 'Aberta'
        alvo.saldoRestante =
          alvo.valorOriginal !== null
            ? Math.max(0, Number((alvo.valorOriginal - alvo.valorPago).toFixed(2)))
            : null
      }
      if (corpo.enviarComprovanteWhatsapp && corpo.mensagemComprovante?.trim()) {
        const textoRecibo = corpo.mensagemComprovante.trim()
        let conversa = estado.conversas.find(
          (c) => c.clienteId === alvo.clienteId && c.status === 'Ativa',
        )
        if (!conversa) {
          const agoraIso = new Date().toISOString()
          conversa = {
            id: `conversa-${Date.now()}`,
            protocolo: '202609-0099',
            clienteId: alvo.clienteId,
            clienteNome: alvo.clienteNome,
            clienteTelefone: alvo.clienteTelefone,
            atendenteId: atendente.id,
            atendenteNome: atendente.nome,
            setorId: estado.setores[0].id,
            setorNome: estado.setores[0].nome,
            etapaId: estado.etapas[0].id,
            etapaNome: estado.etapas[0].nome,
            etapaCor: estado.etapas[0].cor,
            tipoServicoId: alvo.tipoServicoId,
            tipoServicoNome: alvo.tipoServicoNome,
            status: 'Ativa',
            iniciadaEm: agoraIso,
            ultimaMensagemEm: agoraIso,
            mensagensNaoLidas: 0,
            aguardandoTriagem: false,
            prazoIniciadoEm: null,
            prazoLimite: null,
            situacaoPrazo: 'SemPrazo',
            previaUltimaMensagem: textoRecibo,
            encerradaEm: null,
            encerradaPorNome: null,
            motivoEncerramento: null,
            reabertaEm: null,
            clienteFotoPerfilUrl: null,
            clienteNomePendente: false,
          }
          estado.conversas.unshift(conversa)
          estado.detalhes[conversa.id] = { conversa, mensagens: [], historico: [] }
        } else {
          conversa.atendenteId = atendente.id
          conversa.atendenteNome = atendente.nome
          conversa.aguardandoTriagem = false
          conversa.mensagensNaoLidas = 0
          conversa.ultimaMensagemEm = new Date().toISOString()
          conversa.previaUltimaMensagem = textoRecibo
        }
        estado.detalhes[conversa.id]?.mensagens.push({
          id: `msg-recibo-${Date.now()}`,
          conversaId: conversa.id,
          autorTipo: 'Atendente',
          atendenteId: atendente.id,
          atendenteNome: atendente.nome,
          conteudo: textoRecibo,
          tipo: 'Texto',
          statusEntrega: 'Enviada',
          ehAutomatica: false,
          fixada: false,
          oculta: false,
          ocultaPorNome: null,
          encaminhadaDeProtocolo: null,
          enviadaEm: new Date().toISOString(),
          lidaEm: null,
          mensagemRespondidaId: null,
          respondidaAutor: null,
          respondidaConteudo: null,
          arquivoNome: null,
          arquivoTipoMime: null,
          arquivoTamanhoBytes: null,
          geradaPorIa: false,
        })
      }
      sincronizarPendenciasNasConversas(estado)
      return json(route, alvo)
    }

    const devedorReabrir = path.match(/^\/api\/devedores\/([^/]+)\/reabrir$/)
    if (devedorReabrir && metodo === 'POST') {
      const alvo = estado.pendencias.find((p) => p.id === devedorReabrir[1])
      if (!alvo) return problema(route, 404, 'pendencia_nao_encontrada', 'Pendência não encontrada.')
      alvo.quitadaEm = null
      alvo.quitadoPorAtendenteId = null
      alvo.quitadoPorNome = null
      alvo.status = 'Aberta'
      alvo.saldoRestante =
        alvo.valorOriginal !== null
          ? Math.max(0, Number((alvo.valorOriginal - alvo.valorPago).toFixed(2)))
          : null
      sincronizarPendenciasNasConversas(estado)
      return json(route, alvo)
    }

    if (path === '/api/devedores/cobrar-whatsapp' && metodo === 'POST') {
      const corpo = corpoJson(route)
      const cliente = estado.clientes.find((c) => c.id === corpo.clienteId)
      if (!cliente) return problema(route, 404, 'cliente_nao_encontrado', 'Cliente não encontrado.')
      const atendente = usuarioLogado(estado)
      let conversa = estado.conversas.find(
        (c) => c.clienteId === cliente.id && c.status === 'Ativa',
      )
      if (!conversa) {
        conversa = {
          id: `conversa-${Date.now()}`,
          protocolo: '202609-0098',
          clienteId: cliente.id,
          clienteNome: cliente.nome,
          clienteTelefone: cliente.telefoneWhatsapp,
          atendenteId: atendente.id,
          atendenteNome: atendente.nome,
          setorId: estado.setores[0].id,
          setorNome: estado.setores[0].nome,
          etapaId: estado.etapas[0].id,
          etapaNome: estado.etapas[0].nome,
          etapaCor: estado.etapas[0].cor,
          tipoServicoId: null,
          tipoServicoNome: null,
          status: 'Ativa',
          iniciadaEm: new Date().toISOString(),
          ultimaMensagemEm: new Date().toISOString(),
          mensagensNaoLidas: 0,
          aguardandoTriagem: false,
          prazoIniciadoEm: null,
          prazoLimite: null,
          situacaoPrazo: 'SemPrazo',
          previaUltimaMensagem: corpo.mensagem,
          encerradaEm: null,
          encerradaPorNome: null,
          motivoEncerramento: null,
          reabertaEm: null,
          clienteFotoPerfilUrl: null,
          clienteNomePendente: false,
        }
        estado.conversas.unshift(conversa)
        estado.detalhes[conversa.id] = { conversa, mensagens: [], historico: [] }
      } else {
        conversa.atendenteId = atendente.id
        conversa.atendenteNome = atendente.nome
        conversa.aguardandoTriagem = false
        conversa.mensagensNaoLidas = 0
        conversa.ultimaMensagemEm = new Date().toISOString()
        conversa.previaUltimaMensagem = corpo.mensagem
      }
      const msg = {
        id: `msg-cobranca-${Date.now()}`,
        conversaId: conversa.id,
        autorTipo: 'Atendente',
        atendenteId: atendente.id,
        atendenteNome: atendente.nome,
        conteudo: corpo.mensagem,
        tipo: 'Texto',
        statusEntrega: 'Enviada',
        ehAutomatica: false,
        fixada: false,
        oculta: false,
        ocultaPorNome: null,
        encaminhadaDeProtocolo: null,
        enviadaEm: new Date().toISOString(),
        lidaEm: null,
        mensagemRespondidaId: null,
        respondidaAutor: null,
        respondidaConteudo: null,
        arquivoNome: null,
        arquivoTipoMime: null,
        arquivoTamanhoBytes: null,
        geradaPorIa: false,
      }
      estado.detalhes[conversa.id]?.mensagens.push(msg)

      let pendenciasAtualizadas = 0
      for (const p of estado.pendencias) {
        if (
          p.clienteId === cliente.id &&
          p.status === 'Aberta' &&
          (!corpo.pendenciaIds?.length || corpo.pendenciaIds.includes(p.id))
        ) {
          p.ultimaCobrancaEm = new Date().toISOString()
          p.totalCobrancas = (p.totalCobrancas ?? 0) + 1
          p.conversaAtivaId = conversa.id
          pendenciasAtualizadas++
        }
      }
      sincronizarPendenciasNasConversas(estado)
      return json(route, { conversaId: conversa.id, mensagemId: msg.id, pendenciasAtualizadas })
    }

    const abrirConversaDevedor = path.match(/^\/api\/devedores\/cliente\/([^/]+)\/abrir-conversa$/)
    if (abrirConversaDevedor && metodo === 'POST') {
      const cliente = estado.clientes.find((c) => c.id === abrirConversaDevedor[1])
      if (!cliente) return problema(route, 404, 'cliente_nao_encontrado', 'Cliente não encontrado.')
      const atendente = usuarioLogado(estado)
      let conversa = estado.conversas.find(
        (c) => c.clienteId === cliente.id && c.status === 'Ativa',
      )
      let criadaAgora = false
      if (!conversa) {
        criadaAgora = true
        const agoraIso = new Date().toISOString()
        conversa = {
          id: `conversa-${Date.now()}`,
          protocolo: '202609-0097',
          clienteId: cliente.id,
          clienteNome: cliente.nome,
          clienteTelefone: cliente.telefoneWhatsapp,
          atendenteId: atendente.id,
          atendenteNome: atendente.nome,
          setorId: estado.setores[0].id,
          setorNome: estado.setores[0].nome,
          etapaId: estado.etapas[0].id,
          etapaNome: estado.etapas[0].nome,
          etapaCor: estado.etapas[0].cor,
          tipoServicoId: null,
          tipoServicoNome: null,
          status: 'Ativa',
          iniciadaEm: agoraIso,
          ultimaMensagemEm: agoraIso,
          mensagensNaoLidas: 0,
          aguardandoTriagem: false,
          prazoIniciadoEm: null,
          prazoLimite: null,
          situacaoPrazo: 'SemPrazo',
          previaUltimaMensagem: null,
          encerradaEm: null,
          encerradaPorNome: null,
          motivoEncerramento: null,
          reabertaEm: null,
          clienteFotoPerfilUrl: null,
          clienteNomePendente: false,
        }
        estado.conversas.unshift(conversa)
        estado.detalhes[conversa.id] = {
          conversa,
          mensagens: [
            {
              id: `msg-sistema-${Date.now()}`,
              conversaId: conversa.id,
              autorTipo: 'Sistema',
              atendenteId: null,
              atendenteNome: 'Sistema',
              conteudo: `Atendimento aberto por ${atendente.nome} a partir de Devedores.`,
              tipo: 'Texto',
              statusEntrega: 'Entregue',
              ehAutomatica: false,
              fixada: false,
              oculta: false,
              ocultaPorNome: null,
              encaminhadaDeProtocolo: null,
              enviadaEm: agoraIso,
              lidaEm: agoraIso,
              mensagemRespondidaId: null,
              respondidaAutor: null,
              respondidaConteudo: null,
              arquivoNome: null,
              arquivoTipoMime: null,
              arquivoTamanhoBytes: null,
              geradaPorIa: false,
            },
          ],
          historico: [],
        }
      } else {
        conversa.atendenteId = atendente.id
        conversa.atendenteNome = atendente.nome
        conversa.aguardandoTriagem = false
        conversa.mensagensNaoLidas = 0
      }
      for (const p of estado.pendencias) {
        if (p.clienteId === cliente.id && p.status === 'Aberta') {
          p.conversaAtivaId = conversa.id
        }
      }
      sincronizarPendenciasNasConversas(estado)
      return json(route, { conversaId: conversa.id, criadaAgora })
    }

    if (path === '/api/devedores/importacao' && metodo === 'POST') {
      const corpo = corpoJson(route)
      const itens = Array.isArray(corpo.itens) ? corpo.itens : []
      let pendenciasCriadas = 0
      let clientesNovosCriados = 0
      let clientesExistentesVinculados = 0
      let ignorados = 0
      const avisos: string[] = []
      const hoje = new Date().toISOString().slice(0, 10)
      const atendente = usuarioLogado(estado)

      itens.forEach((item: any, idx: number) => {
        let cliente = item.clienteId
          ? estado.clientes.find((c) => c.id === item.clienteId)
          : undefined
        if (!cliente && item.telefone) {
          const digitos = String(item.telefone).replace(/\D/g, '')
          if (digitos.length >= 10) {
            const e164 = digitos.startsWith('55') ? digitos : `55${digitos}`
            cliente = estado.clientes.find((c) => c.telefoneWhatsapp === e164)
            if (cliente) {
              clientesExistentesVinculados++
            } else {
              cliente = {
                ...estado.clientes[0],
                id: `cliente-${Date.now()}-${idx}`,
                nome: item.nome || e164,
                telefoneWhatsapp: e164,
                documento: item.documento ?? null,
                percentualDeQualificacao: 25,
                totalConversas: 0,
                temConversaAtiva: false,
                ativo: true,
                nomePendente: false,
              }
              estado.clientes.push(cliente)
              clientesNovosCriados++
            }
          }
        } else if (cliente) {
          clientesExistentesVinculados++
        }

        if (!cliente) {
          ignorados++
          avisos.push(`Item ${idx + 1}: cliente ou telefone inválido.`)
          return
        }

        const valorOriginal =
          typeof item.valorOriginal === 'number' && item.valorOriginal > 0
            ? item.valorOriginal
            : null
        const servico = estado.servicos.find((s) => s.id === corpo.tipoServicoPadraoId)

        estado.pendencias.unshift({
          id: `pendencia-${Date.now()}-${idx}`,
          clienteId: cliente.id,
          clienteNome: cliente.nome,
          clienteTelefone: cliente.telefoneWhatsapp,
          clienteDocumento: cliente.documento ?? null,
          clienteFotoPerfilUrl: `/api/clientes/${cliente.id}/foto`,
          conversaOrigemId: null,
          conversaAtivaId: null,
          tipoServicoId: servico?.id ?? null,
          tipoServicoNome: servico?.nome ?? null,
          titulo: item.titulo || corpo.tituloPadrao || 'Pendência financeira',
          observacoes: item.observacoes ?? null,
          valorOriginal,
          valorPago: 0,
          saldoRestante: valorOriginal,
          status: 'Aberta',
          dataReferencia: hoje,
          dataPromessaPagamento: item.dataPromessaPagamento ?? corpo.dataPromessaPadrao ?? null,
          diasEmAberto: 0,
          diasDeAtraso: null,
          ultimaCobrancaEm: null,
          totalCobrancas: 0,
          registradoPorAtendenteId: atendente.id,
          registradoPorNome: atendente.nome,
          criadoEm: new Date().toISOString(),
          quitadaEm: null,
          quitadoPorAtendenteId: null,
          quitadoPorNome: null,
          pagamentos: [],
        })
        pendenciasCriadas++
      })
      sincronizarPendenciasNasConversas(estado)
      return json(route, {
        pendenciasCriadas,
        clientesNovosCriados,
        clientesExistentesVinculados,
        ignorados,
        avisos,
      })
    }

    return problema(route, 501, 'mock_de_captura_nao_implementado', `Endpoint não implementado nas capturas: ${metodo} ${path}`)
  })

  // Registrado por último: no Playwright, a rota mais recente tem
  // precedência, então este `continue()` intercepta antes do catch-all acima
  // — sem isso, `**/api/**` também casaria com `/src/lib/api/*.ts` (o próprio
  // módulo-fonte do Vite) e devolveria 501 em vez do código real do cliente HTTP.
  await page.route('**/src/lib/api/**', (route) => route.continue())

  await page.route('https://**/*', (route) => route.abort('blockedbyclient'))

  return estado
}

export async function autenticarComoDemo(page: Page, perfil: demo.Perfil = 'Administrador') {
  const atendente = perfil === 'Atendente' ? demo.usuarios.Atendente : perfil === 'Supervisor' ? demo.usuarios.Supervisor : demo.usuarios.Administrador
  await page.addInitScript(({ token }) => localStorage.setItem('crm-cartorio.token', token), { token: `token-${atendente.perfil.toLowerCase()}` })
  await page.addInitScript(() => localStorage.setItem('crm-cartorio.menu-fixado', 'true'))
}
