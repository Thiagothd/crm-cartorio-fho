import { expect, type Page, type Route } from '@playwright/test'

type Perfil = 'Atendente' | 'Supervisor' | 'Administrador'

const agora = '2026-08-31T20:00:00.000Z'

const tenant = {
  id: 'tenant-e2e',
  razaoSocial: 'Cartório E2E Ltda.',
  nomeFantasia: 'Cartório Modelo E2E',
  cnpj: '12345678000199',
  whatsappNumeroExibicao: '(11) 99999-0000',
  whatsappConfigurado: false,
}

const usuarios = {
  Atendente: {
    id: 'atendente-e2e', nome: 'Ana Atendente', email: 'atendente@teste.local', perfil: 'Atendente' as Perfil,
    cargo: 'Escrevente', cargoDescrito: 'Escrevente', setorPadraoId: 'setor-notas', setorPadraoNome: 'Notas', ativo: true, ultimoAcessoEm: agora,
  },
  Supervisor: {
    id: 'supervisor-e2e', nome: 'Sofia Supervisora', email: 'supervisor@teste.local', perfil: 'Supervisor' as Perfil,
    cargo: 'Substituto', cargoDescrito: 'Substituto', setorPadraoId: 'setor-notas', setorPadraoNome: 'Notas', ativo: true, ultimoAcessoEm: agora,
  },
  Administrador: {
    id: 'admin-e2e', nome: 'Alice Administradora', email: 'admin@teste.local', perfil: 'Administrador' as Perfil,
    cargo: 'Titular', cargoDescrito: 'Titular', setorPadraoId: 'setor-notas', setorPadraoNome: 'Notas', ativo: true, ultimoAcessoEm: agora,
  },
}

function sessao(perfil: Perfil) {
  return {
    token: `token-${perfil.toLowerCase()}`,
    expiraEm: '2026-09-01T04:00:00.000Z',
    atendente: usuarios[perfil],
    tenant,
  }
}

function clienteBase(id: string, nome: string, telefone: string) {
  return {
    id, nome, tipo: 'Fisica', telefoneWhatsapp: telefone, telefoneAlternativo: null, email: `${id}@teste.local`,
    documento: id === 'cliente-2' ? null : '12345678901', rg: null, orgaoEmissor: null, ufOrgaoEmissor: null,
    dataNascimento: null, estadoCivil: 'NaoInformado', regimeDeBens: 'NaoInformado', profissao: null,
    nacionalidade: 'Brasileira', naturalidade: null, nomeDoPai: null, nomeDaMae: null, razaoSocial: null,
    representanteLegal: null, cep: id === 'cliente-2' ? null : '01001000', logradouro: id === 'cliente-2' ? null : 'Praça da Sé',
    numero: id === 'cliente-2' ? null : '100', complemento: null, bairro: id === 'cliente-2' ? null : 'Sé',
    cidade: id === 'cliente-2' ? null : 'São Paulo', uf: id === 'cliente-2' ? null : 'SP', observacoes: null,
    ativo: true, primeiroContatoEm: agora, totalConversas: id === 'cliente-1' ? 1 : 0, temConversaAtiva: id === 'cliente-1',
    percentualDeQualificacao: id === 'cliente-2' ? 20 : 82, idade: null, fotoPerfilUrl: null, nomePendente: false, nomePerguntadoEm: null,
  }
}

export interface EstadoE2E {
  perfil: Perfil
  setores: any[]
  etapas: any[]
  servicos: any[]
  atendentes: any[]
  respostasRapidas: any[]
  clientes: any[]
  conversas: any[]
  detalhes: Record<string, any>
  conflitoAoAssumir: boolean
  chamadasExternas: string[]
}

export function criarEstadoE2E(perfil: Perfil = 'Administrador'): EstadoE2E {
  const setores = [
    { id: 'setor-notas', nome: 'Notas', descricao: 'Atos notariais', ordem: 1, ativo: true, conversasAtivas: 1 },
    { id: 'setor-civil', nome: 'Registro Civil', descricao: 'Registro civil', ordem: 2, ativo: true, conversasAtivas: 0 },
  ]
  const etapas = [
    { id: 'etapa-triagem', nome: 'Triagem', descricao: null, ordem: 1, cor: '#7f1d1d', ehFinal: false, ativo: true, conversasAtivas: 1 },
    { id: 'etapa-final', nome: 'Concluído', descricao: null, ordem: 2, cor: '#166534', ehFinal: true, ativo: true, conversasAtivas: 0 },
  ]
  const servicos = [
    { id: 'servico-1', nome: 'Reconhecimento de firma', codigo: 'RF', categoria: 'Notas', categoriaDescrita: 'Notas', descricao: 'Reconhecimento de assinatura', ativo: true,
      prazo: { id: 'prazo-1', prazoHoras: 24, percentualAlerta: 80, apenasDiasUteis: true, prazoDescrito: '1 dia útil' }, conversasAtivas: 1,
      setorId: 'setor-notas', setorNome: 'Notas', documentosNecessarios: 'Documento com foto' },
  ]
  const atendentes = Object.values(usuarios)
  const respostasRapidas = [
    { id: 'rapida-1', atalho: 'docs', titulo: 'Documentos necessários', conteudo: 'Por favor, envie seu documento com foto.', ativo: true },
    { id: 'rapida-2', atalho: 'ola', titulo: 'Saudação', conteudo: 'Olá! Como posso ajudar?', ativo: true },
  ]
  const clientes = [
    clienteBase('cliente-1', 'Marina Souza', '5511999991111'),
    clienteBase('cliente-2', 'Carlos Lima', '5511999992222'),
  ]
  const conversa = {
    id: 'conversa-1', protocolo: 'ATD-2026-0001', clienteId: 'cliente-1', clienteNome: 'Marina Souza', clienteTelefone: '5511999991111',
    atendenteId: null, atendenteNome: null, setorId: 'setor-notas', setorNome: 'Notas', etapaId: 'etapa-triagem', etapaNome: 'Triagem', etapaCor: '#7f1d1d',
    tipoServicoId: 'servico-1', tipoServicoNome: 'Reconhecimento de firma', status: 'Ativa', iniciadaEm: agora, ultimaMensagemEm: agora,
    mensagensNaoLidas: 2, aguardandoTriagem: false, prazoIniciadoEm: agora, prazoLimite: '2026-09-01T20:00:00.000Z', situacaoPrazo: 'EmDia',
    previaUltimaMensagem: 'Preciso reconhecer uma firma.', encerradaEm: null, encerradaPorNome: null, motivoEncerramento: null, reabertaEm: null,
    clienteFotoPerfilUrl: null, clienteNomePendente: false,
  }
  const mensagens = [
    { id: 'msg-1', conversaId: 'conversa-1', autorTipo: 'Cliente', atendenteId: null, atendenteNome: null, conteudo: 'Preciso reconhecer uma firma.', tipo: 'Texto', statusEntrega: 'Lida', ehAutomatica: false, fixada: false, oculta: false, ocultaPorNome: null, encaminhadaDeProtocolo: null, enviadaEm: agora, lidaEm: agora, mensagemRespondidaId: null, respondidaAutor: null, respondidaConteudo: null, arquivoNome: null, arquivoTipoMime: null, arquivoTamanhoBytes: null, geradaPorIa: false },
  ]
  return {
    perfil, setores, etapas, servicos, atendentes, respostasRapidas, clientes,
    conversas: [conversa],
    detalhes: { 'conversa-1': { conversa, mensagens, historico: [] } },
    conflitoAoAssumir: false,
    chamadasExternas: [],
  }
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json; charset=utf-8', body: JSON.stringify(body) })
}

function problema(route: Route, status: number, codigo: string, title: string, extras: Record<string, unknown> = {}) {
  return json(route, { type: 'about:blank', status, codigo, title, ...extras }, status)
}

function perfilPeloToken(token: string | undefined): Perfil {
  if (token?.includes('atendente')) return 'Atendente'
  if (token?.includes('supervisor')) return 'Supervisor'
  return 'Administrador'
}

function corpoJson(route: Route) {
  try { return route.request().postDataJSON() as any } catch { return {} }
}

export async function instalarApiMock(page: Page, estado = criarEstadoE2E()) {
  await page.route('**/hubs/**', async (route) => route.abort('blockedbyclient'))

  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname
    const metodo = request.method()

    if (path === '/api/autenticacao/serventia' && metodo === 'GET') return json(route, { nome: tenant.nomeFantasia })

    if (path === '/api/autenticacao/login' && metodo === 'POST') {
      const corpo = corpoJson(route)
      const perfil = Object.values(usuarios).find((u) => u.email === String(corpo.email ?? '').toLowerCase())?.perfil
      if (!perfil || corpo.senha !== 'Teste123!') return problema(route, 401, 'credenciais_invalidas', 'E-mail ou senha inválidos.')
      estado.perfil = perfil
      return json(route, sessao(perfil))
    }

    if (path === '/api/autenticacao/sessao' && metodo === 'GET') {
      const token = request.headers()['authorization']
      if (!token) return problema(route, 401, 'nao_autenticado', 'Não autenticado.')
      return json(route, sessao(perfilPeloToken(token)))
    }

    if (path === '/api/setores' && metodo === 'GET') return json(route, estado.setores)
    if (path === '/api/etapas' && metodo === 'GET') return json(route, estado.etapas)
    if (path === '/api/tipos-servico' && metodo === 'GET') return json(route, estado.servicos)
    if (path === '/api/atendentes' && metodo === 'GET') return json(route, estado.atendentes)
    if (path === '/api/respostas-rapidas' && metodo === 'GET') return json(route, estado.respostasRapidas)
    if (path === '/api/serventia/aparencia' && metodo === 'GET') return json(route, { aparenciaDaConversa: 'PapelLiso' })

    if (path === '/api/conversas/resumo' && metodo === 'GET') {
      return json(route, { totalAtivas: estado.conversas.length, naFila: estado.conversas.filter((c) => !c.atendenteId).length, minhasConversas: estado.conversas.filter((c) => c.atendenteId === usuarios[estado.perfil].id).length, encerradasHoje: 0, emAlerta: 0, atrasadas: 0,
        porEtapa: estado.etapas.map((e) => ({ id: e.id, nome: e.nome, cor: e.cor, total: estado.conversas.filter((c) => c.etapaId === e.id).length })),
        porSetor: estado.setores.map((s) => ({ id: s.id, nome: s.nome, cor: null, total: estado.conversas.filter((c) => c.setorId === s.id).length })) })
    }

    if (path === '/api/conversas' && metodo === 'GET') {
      const busca = (url.searchParams.get('busca') ?? '').toLowerCase()
      const itens = estado.conversas.filter((c) => !busca || `${c.clienteNome} ${c.protocolo}`.toLowerCase().includes(busca))
      return json(route, { itens, total: itens.length, pagina: 1, tamanho: 50 })
    }

    const detalhe = path.match(/^\/api\/conversas\/([^/]+)$/)
    if (detalhe && metodo === 'GET') {
      const encontrado = estado.detalhes[detalhe[1]]
      return encontrado ? json(route, encontrado) : problema(route, 404, 'conversa_nao_encontrada', 'Conversa não encontrada.')
    }

    const ler = path.match(/^\/api\/conversas\/([^/]+)\/ler$/)
    if (ler && metodo === 'POST') {
      const alvo = estado.conversas.find((c) => c.id === ler[1])
      if (alvo) alvo.mensagensNaoLidas = 0
      if (estado.detalhes[ler[1]]) estado.detalhes[ler[1]].conversa.mensagensNaoLidas = 0
      return route.fulfill({ status: 204 })
    }

    const assumir = path.match(/^\/api\/conversas\/([^/]+)\/assumir$/)
    if (assumir && metodo === 'POST') {
      if (estado.conflitoAoAssumir) return problema(route, 409, 'conversa_ja_assumida', 'Conversa já assumida.', { atendenteNome: 'Outro Atendente' })
      const alvo = estado.conversas.find((c) => c.id === assumir[1])
      if (!alvo) return problema(route, 404, 'conversa_nao_encontrada', 'Conversa não encontrada.')
      alvo.atendenteId = usuarios[estado.perfil].id
      alvo.atendenteNome = usuarios[estado.perfil].nome
      estado.detalhes[alvo.id].conversa = alvo
      return json(route, alvo)
    }

    const liberar = path.match(/^\/api\/conversas\/([^/]+)\/liberar$/)
    if (liberar && metodo === 'POST') {
      const alvo = estado.conversas.find((c) => c.id === liberar[1])
      if (!alvo) return problema(route, 404, 'conversa_nao_encontrada', 'Conversa não encontrada.')
      alvo.atendenteId = null; alvo.atendenteNome = null; estado.detalhes[alvo.id].conversa = alvo
      return json(route, alvo)
    }

    const enviar = path.match(/^\/api\/conversas\/([^/]+)\/mensagens$/)
    if (enviar && metodo === 'POST') {
      const corpo = corpoJson(route)
      const mensagem = { id: `msg-${Date.now()}`, conversaId: enviar[1], autorTipo: 'Atendente', atendenteId: usuarios[estado.perfil].id, atendenteNome: usuarios[estado.perfil].nome, conteudo: corpo.conteudo, tipo: 'Texto', statusEntrega: 'Enviada', ehAutomatica: false, fixada: false, oculta: false, ocultaPorNome: null, encaminhadaDeProtocolo: null, enviadaEm: agora, lidaEm: null, mensagemRespondidaId: corpo.mensagemRespondidaId ?? null, respondidaAutor: corpo.mensagemRespondidaId ? 'Marina Souza' : null, respondidaConteudo: corpo.mensagemRespondidaId ? 'Preciso reconhecer uma firma.' : null, arquivoNome: null, arquivoTipoMime: null, arquivoTamanhoBytes: null, geradaPorIa: false }
      estado.detalhes[enviar[1]].mensagens.push(mensagem)
      return json(route, mensagem, 201)
    }

    if (path === '/api/clientes/opcoes' && metodo === 'GET') return json(route, { cidades: ['São Paulo'] })
    if (path === '/api/clientes' && metodo === 'GET') {
      const busca = (url.searchParams.get('busca') ?? '').toLowerCase()
      const semDocumento = url.searchParams.get('semDocumento') === 'true'
      const semEndereco = url.searchParams.get('semEndereco') === 'true'
      let itens = estado.clientes.filter((c) => !busca || `${c.nome} ${c.telefoneWhatsapp}`.toLowerCase().includes(busca))
      if (semDocumento) itens = itens.filter((c) => !c.documento)
      if (semEndereco) itens = itens.filter((c) => !c.cep)
      return json(route, { itens, total: itens.length, pagina: 1, tamanho: 100 })
    }
    if (path === '/api/clientes' && metodo === 'POST') {
      const corpo = corpoJson(route)
      const novo = { ...clienteBase(`cliente-${estado.clientes.length + 1}`, corpo.nome, corpo.telefoneWhatsapp), ...corpo }
      estado.clientes.push(novo)
      return json(route, novo, 201)
    }
    const cliente = path.match(/^\/api\/clientes\/([^/]+)$/)
    if (cliente && metodo === 'PUT') {
      const alvo = estado.clientes.find((c) => c.id === cliente[1])
      if (!alvo) return problema(route, 404, 'cliente_nao_encontrado', 'Cliente não encontrado.')
      Object.assign(alvo, corpoJson(route)); return json(route, alvo)
    }
    if (cliente && metodo === 'DELETE') {
      const indice = estado.clientes.findIndex((c) => c.id === cliente[1])
      if (indice < 0) return problema(route, 404, 'cliente_nao_encontrado', 'Cliente não encontrado.')
      if (estado.clientes[indice].temConversaAtiva) return problema(route, 409, 'cliente_possui_atendimento', 'Cliente possui atendimento vinculado.')
      estado.clientes.splice(indice, 1); return route.fulfill({ status: 204 })
    }

    if (path === '/api/indicadores/visao-geral' && metodo === 'GET') {
      return json(route, {
        periodo: { de: agora, ate: agora, granularidade: 'dia' },
        agora: { ativas: 1, naFila: 1, emAlerta: 0, atrasadas: 0 },
        noPeriodo: { abertos: 1, encerrados: 0, primeiraRespostaMedianaMinutos: 4, primeiraRespostaMediaMinutos: 4, amostraPrimeiraResposta: 1 },
        fluxoDeAtendimentos: [], requerAtencao: [], cargaDaEquipe: estado.perfil === 'Atendente' ? null : [], servicosMaisProcurados: [], distribuicaoPorEtapa: [], saudeDaOperacao: null,
      })
    }
    if (path === '/api/indicadores/alertas' && metodo === 'GET') return json(route, [])

    if (path === '/api/atendentes' && metodo === 'POST') {
      const corpo = corpoJson(route); const novo = { ...usuarios.Atendente, ...corpo, id: `atendente-${estado.atendentes.length + 1}`, cargoDescrito: corpo.cargo, setorPadraoNome: null, ativo: true, ultimoAcessoEm: null }
      estado.atendentes.push(novo); return json(route, novo, 201)
    }
    const atendente = path.match(/^\/api\/atendentes\/([^/]+)$/)
    if (atendente && metodo === 'PUT') {
      const alvo = estado.atendentes.find((a) => a.id === atendente[1]); if (!alvo) return problema(route, 404, 'atendente_nao_encontrado', 'Atendente não encontrado.')
      Object.assign(alvo, corpoJson(route)); return json(route, alvo)
    }

    if (path === '/api/setores' && metodo === 'POST') {
      const corpo = corpoJson(route); const novo = { id: `setor-${estado.setores.length + 1}`, conversasAtivas: 0, ...corpo }; estado.setores.push(novo); return json(route, novo, 201)
    }
    if (path === '/api/etapas/reordenar' && metodo === 'POST') {
      const ids = corpoJson(route) as string[]; estado.etapas.sort((a, b) => ids.indexOf(a.id) - ids.indexOf(b.id)); estado.etapas.forEach((e, i) => e.ordem = i + 1); return json(route, estado.etapas)
    }
    if (path === '/api/etapas' && metodo === 'POST') {
      const corpo = corpoJson(route); const novo = { id: `etapa-${estado.etapas.length + 1}`, conversasAtivas: 0, ...corpo }; estado.etapas.push(novo); return json(route, novo, 201)
    }
    if (path === '/api/tipos-servico' && metodo === 'POST') {
      const corpo = corpoJson(route); const novo = { id: `servico-${estado.servicos.length + 1}`, conversasAtivas: 0, categoriaDescrita: corpo.categoria, setorNome: estado.setores.find((s) => s.id === corpo.setorId)?.nome ?? null, prazo: corpo.prazoHoras ? { id: `prazo-${Date.now()}`, prazoHoras: corpo.prazoHoras, percentualAlerta: corpo.percentualAlerta ?? 80, apenasDiasUteis: corpo.apenasDiasUteis ?? true, prazoDescrito: `${corpo.prazoHoras}h` } : null, ...corpo }; estado.servicos.push(novo); return json(route, novo, 201)
    }

    return problema(route, 501, 'mock_nao_implementado', `Endpoint E2E não implementado: ${metodo} ${path}`)
  })

  await page.route('https://**/*', async (route) => {
    estado.chamadasExternas.push(route.request().url())
    await route.abort('blockedbyclient')
  })

  return estado
}

export async function autenticarLocalmente(page: Page, perfil: Perfil = 'Administrador') {
  await page.addInitScript(({ token }) => localStorage.setItem('crm-cartorio.token', token), {
    token: `token-${perfil.toLowerCase()}`,
  })
}

export async function entrarPelaTela(page: Page, perfil: Perfil = 'Administrador') {
  const usuario = usuarios[perfil]
  await page.goto('/login')
  await page.getByLabel('E-mail').fill(usuario.email)
  await page.getByLabel('Senha').fill('Teste123!')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page).toHaveURL(/\/$/)
}
