/**
 * Dataset fictício e rico, só para gerar os screenshots do roteiro público
 * (`roteiro/`). Independente de `fixtures/api-mock.ts` de propósito — este
 * arquivo nunca é importado pelos testes reais (`specs/*.e2e.ts`), então
 * enriquecer ou trocar aqui nunca quebra a suíte de 25+ testes.
 *
 * Nenhum nome, CNPJ, telefone ou endereço aqui é real. O cartório fictício
 * ("Cartório Modelo") não representa o piloto do cartório parceiro — o roteiro é público,
 * e o sistema prevê multi-tenant como trabalho futuro (Fase 3), então a
 * documentação generaliza para qualquer serventia cliente.
 */

export type Perfil = 'Atendente' | 'Supervisor' | 'Administrador'

/** Fixo — nunca `Date.now()` real, para as capturas nunca variarem entre execuções. */
export const AGORA = '2026-09-09T17:00:00.000Z'
export const AGORA_MS = Date.parse(AGORA)

function horasAtras(h: number) {
  return new Date(AGORA_MS - h * 3_600_000).toISOString()
}
function diasAtras(d: number) {
  return new Date(AGORA_MS - d * 86_400_000).toISOString()
}
function horasNaFrente(h: number) {
  return new Date(AGORA_MS + h * 3_600_000).toISOString()
}

export const tenant = {
  id: 'tenant-demo',
  razaoSocial: 'Cartório Modelo Ltda.',
  nomeFantasia: 'Cartório Modelo',
  cnpj: '11222333000181',
  whatsappNumeroExibicao: '(38) 99999-0000',
  whatsappConfigurado: true,
}

const PERMISSOES_BALCAO = [
  'atendimento.ver', 'atendimento.assumir', 'atendimento.responder', 'atendimento.mover', 'atendimento.transferir',
  'clientes.ver', 'atendimento.abrir_novo', 'atendimento.vincular_servico', 'atendimento.encerrar',
  'clientes.cadastrar', 'clientes.editar',
]
const PERMISSOES_SUPERVISAO = [
  ...PERMISSOES_BALCAO,
  'servicos.ver', 'servicos.editar', 'etapas.ver', 'etapas.editar', 'setores.ver', 'setores.editar',
  'automacao.mensagens.ver', 'automacao.mensagens.editar', 'automacao.respostas.ver', 'automacao.respostas.editar',
  'automacao.encerramento.ver', 'automacao.encerramento.editar', 'ia.ver', 'ia.conhecimento.ver', 'ia.conhecimento.editar',
  'aparencia.ver', 'aparencia.editar',
  'atendimento.reabrir', 'atendimento.mexer_no_de_terceiro', 'clientes.excluir', 'clientes.importar_agenda',
  'relatorios.ver', 'relatorios.equipe', 'relatorios.saude_ia',
]
const PERMISSOES_ADMINISTRACAO = [
  'atendimento.ver', 'atendimento.assumir', 'atendimento.responder', 'atendimento.abrir_novo', 'atendimento.mover',
  'atendimento.vincular_servico', 'atendimento.encerrar', 'atendimento.reabrir', 'atendimento.transferir', 'atendimento.mexer_no_de_terceiro',
  'clientes.ver', 'clientes.cadastrar', 'clientes.editar', 'clientes.excluir', 'clientes.importar_agenda',
  'servicos.ver', 'servicos.editar', 'etapas.ver', 'etapas.editar', 'setores.ver', 'setores.editar',
  'automacao.mensagens.ver', 'automacao.mensagens.editar', 'automacao.respostas.ver', 'automacao.respostas.editar',
  'automacao.encerramento.ver', 'automacao.encerramento.editar', 'ia.ver', 'ia.conhecimento.ver', 'ia.conhecimento.editar',
  'relatorios.ver', 'relatorios.equipe', 'relatorios.saude_ia', 'relatorios.saude_whatsapp',
  'whatsapp.ver', 'whatsapp.parear', 'whatsapp.regras', 'whatsapp.testar_envio',
  'aparencia.ver', 'aparencia.editar', 'serventia.editar',
  'equipe.ver', 'equipe.cadastrar', 'equipe.editar', 'equipe.redefinir_senha', 'equipe.excluir', 'equipe.permissoes', 'equipe.mfa_administrar',
  'seguranca.eventos.ver',
]

/** Catálogo reduzido — só o suficiente para a tela de Permissões renderizar de forma realista. */
export const CATALOGO_DE_PERMISSOES = [
  { chave: 'atendimento.ver', rotulo: 'Abrir a tela de Atendimento', descricao: 'Ver a fila e as conversas do WhatsApp.', bloco: 'Atendimento', ehTela: true },
  { chave: 'atendimento.assumir', rotulo: 'Puxar conversa da fila', descricao: 'Assumir um atendimento que está esperando responsável.', bloco: 'Atendimento', ehTela: false },
  { chave: 'atendimento.responder', rotulo: 'Escrever e enviar mensagem', descricao: 'Responder o cliente e enviar anexo.', bloco: 'Atendimento', ehTela: false },
  { chave: 'atendimento.transferir', rotulo: 'Passar a conversa para outra pessoa', descricao: 'Transferir o atendimento para outro atendente.', bloco: 'Atendimento', ehTela: false },
  { chave: 'atendimento.encerrar', rotulo: 'Encerrar atendimento', descricao: 'Finalizar e arquivar o atendimento.', bloco: 'Atendimento', ehTela: false },
  { chave: 'atendimento.reabrir', rotulo: 'Reabrir atendimento encerrado', descricao: 'Trazer de volta uma conversa já finalizada.', bloco: 'Atendimento', ehTela: false },
  { chave: 'clientes.ver', rotulo: 'Abrir a tela de Clientes', descricao: 'Ver a lista e a ficha de cada cliente.', bloco: 'Clientes', ehTela: true },
  { chave: 'clientes.cadastrar', rotulo: 'Cadastrar cliente novo', descricao: 'Criar a ficha de um cliente.', bloco: 'Clientes', ehTela: false },
  { chave: 'clientes.excluir', rotulo: 'Excluir cliente', descricao: 'Apagar definitivamente a ficha de um cliente.', bloco: 'Clientes', ehTela: false },
  { chave: 'servicos.ver', rotulo: 'Ver o catálogo de serviços', descricao: 'Ver os atos oferecidos e o prazo de cada um.', bloco: 'Catálogo e estrutura', ehTela: true },
  { chave: 'servicos.editar', rotulo: 'Cadastrar e alterar serviço', descricao: 'Criar, renomear e ajustar prazo de serviço.', bloco: 'Catálogo e estrutura', ehTela: false },
  { chave: 'equipe.ver', rotulo: 'Abrir a tela de Equipe', descricao: 'Ver a lista de atendentes e seus dados.', bloco: 'Equipe e segurança', ehTela: true },
  { chave: 'equipe.permissoes', rotulo: 'Decidir o que cada pessoa acessa', descricao: 'Montar e ajustar as permissões de outro atendente.', bloco: 'Equipe e segurança', ehTela: false },
]

export const MODELOS_DE_PERMISSAO = [
  { modelo: 'Recepcao', rotulo: 'Recepção', descricao: 'Só recebe e encaminha — a mais enxuta.', chaves: ['atendimento.ver', 'atendimento.assumir', 'atendimento.responder', 'atendimento.transferir', 'clientes.ver'] },
  { modelo: 'Balcao', rotulo: 'Balcão', descricao: 'Atende do começo ao fim.', chaves: PERMISSOES_BALCAO },
  { modelo: 'Catalogo', rotulo: 'Catálogo', descricao: 'Cuida do cadastro e da automação, não atende.', chaves: ['servicos.ver', 'servicos.editar', 'clientes.ver'] },
  { modelo: 'Supervisao', rotulo: 'Supervisão', descricao: 'Toca a equipe do dia — reabre, transfere e vê os relatórios.', chaves: PERMISSOES_SUPERVISAO },
  { modelo: 'Administracao', rotulo: 'Administração', descricao: 'Responde pelo sistema.', chaves: PERMISSOES_ADMINISTRACAO },
  { modelo: 'Auditoria', rotulo: 'Auditoria', descricao: 'Vê tudo, não muda nada.', chaves: CATALOGO_DE_PERMISSOES.map((c) => c.chave) },
]

export const usuarios = {
  Atendente: {
    id: 'atendente-juliana', nome: 'Paula Ribeiro', email: 'paula@cartoriomodelo.exemplo', perfil: 'Atendente' as Perfil,
    cargo: 'Escrevente', cargoDescrito: 'Escrevente', setorPadraoId: 'setor-civil', setorPadraoNome: 'Registro Civil',
    ativo: true, ultimoAcessoEm: horasAtras(1), mfaAtivo: false, modelo: null, permissoes: PERMISSOES_BALCAO,
  },
  Supervisor: {
    id: 'atendente-marta', nome: 'Carla Menezes', email: 'carla@cartoriomodelo.exemplo', perfil: 'Supervisor' as Perfil,
    cargo: 'EscreventeAutorizado', cargoDescrito: 'Escrevente autorizado', setorPadraoId: 'setor-notas', setorPadraoNome: 'Tabelionato de Notas',
    ativo: true, ultimoAcessoEm: horasAtras(2), mfaAtivo: true, modelo: null, permissoes: PERMISSOES_SUPERVISAO,
  },
  Administrador: {
    id: 'atendente-beatriz', nome: 'Beatriz Fonseca', email: 'beatriz@cartoriomodelo.exemplo', perfil: 'Administrador' as Perfil,
    cargo: 'Titular', cargoDescrito: 'Titular', setorPadraoId: 'setor-notas', setorPadraoNome: 'Tabelionato de Notas',
    ativo: true, ultimoAcessoEm: horasAtras(0.2), mfaAtivo: true, modelo: null, permissoes: PERMISSOES_ADMINISTRACAO,
  },
}

/** Contas extras só para a tela de Equipe parecer uma equipe de verdade. */
export const atendentesExtras = [
  {
    id: 'atendente-carlos', nome: 'Carlos Nogueira', email: 'carlos@cartoriomodelo.exemplo', perfil: 'Atendente' as Perfil,
    cargo: 'Auxiliar', cargoDescrito: 'Auxiliar', setorPadraoId: 'setor-notas', setorPadraoNome: 'Tabelionato de Notas',
    ativo: false, ultimoAcessoEm: diasAtras(40), mfaAtivo: false, modelo: null, permissoes: PERMISSOES_BALCAO,
  },
  {
    // Titular/Administrador que NÃO é quem está logado — só assim o botão de
    // Segurança (MFA) aparece na linha dela (a própria conta logada não
    // mostra o ícone para si mesma).
    id: 'atendente-roberta', nome: 'Roberta Ximenes', email: 'roberta@cartoriomodelo.exemplo', perfil: 'Administrador' as Perfil,
    cargo: 'Titular', cargoDescrito: 'Titular', setorPadraoId: null, setorPadraoNome: null,
    ativo: true, ultimoAcessoEm: diasAtras(2), mfaAtivo: true, modelo: null, permissoes: PERMISSOES_ADMINISTRACAO,
  },
]

function clienteBase(dados: {
  id: string; nome: string; telefone: string; tipo?: 'Fisica' | 'Juridica'; documento?: string | null
  qualificacao: number; totalConversas?: number; temConversaAtiva?: boolean; nomePendente?: boolean
  cidade?: string | null; email?: string | null
}) {
  const comEndereco = dados.qualificacao >= 50
  return {
    id: dados.id, nome: dados.nome, tipo: dados.tipo ?? 'Fisica', telefoneWhatsapp: dados.telefone,
    telefoneAlternativo: null, email: dados.email ?? null,
    documento: dados.documento ?? null, rg: null, orgaoEmissor: null, ufOrgaoEmissor: null,
    dataNascimento: null, estadoCivil: 'NaoInformado', regimeDeBens: 'NaoInformado', profissao: null,
    nacionalidade: 'Brasileira', naturalidade: null, nomeDoPai: null, nomeDaMae: null,
    razaoSocial: dados.tipo === 'Juridica' ? `${dados.nome} Ltda.` : null, representanteLegal: null,
    cep: comEndereco ? '39505000' : null, logradouro: comEndereco ? 'Praça São Francisco' : null,
    numero: comEndereco ? '70' : null, complemento: null, bairro: comEndereco ? 'Centro' : null,
    cidade: comEndereco ? (dados.cidade ?? 'Gameleiras') : null, uf: comEndereco ? 'MG' : null,
    observacoes: null, ativo: true, primeiroContatoEm: diasAtras(60), totalConversas: dados.totalConversas ?? 1,
    temConversaAtiva: dados.temConversaAtiva ?? false, percentualDeQualificacao: dados.qualificacao,
    idade: null, fotoPerfilUrl: null, nomePendente: dados.nomePendente ?? false, nomePerguntadoEm: null,
  }
}

export const clientes = [
  clienteBase({ id: 'cliente-maria', nome: 'Maria Aparecida Souza', telefone: '5538999990001', documento: '11122233396', qualificacao: 90, totalConversas: 3, temConversaAtiva: true, email: 'maria.souza@exemplo.com' }),
  clienteBase({ id: 'cliente-joao', nome: 'João Pedro Ramos', telefone: '5538999990002', documento: '22233344407', qualificacao: 65, totalConversas: 1, temConversaAtiva: true }),
  clienteBase({ id: 'cliente-sandra', nome: 'Sandra Regina Oliveira', telefone: '5538999990003', documento: null, qualificacao: 25, totalConversas: 1, temConversaAtiva: true }),
  clienteBase({ id: 'cliente-antonio', nome: 'Antônio Carlos Duarte', telefone: '5538999990004', documento: '33344455518', qualificacao: 100, totalConversas: 5, temConversaAtiva: false }),
  clienteBase({ id: 'cliente-construtora', nome: 'Construtora Vale Verde', telefone: '5538999990005', tipo: 'Juridica', documento: '11222333000181', qualificacao: 80, totalConversas: 2, temConversaAtiva: true }),
  clienteBase({ id: 'cliente-rosangela', nome: 'Rosângela Ferreira Lima', telefone: '5538999990006', documento: null, qualificacao: 40, totalConversas: 1 }),
  clienteBase({ id: 'cliente-pendente', nome: '5538999990007', telefone: '5538999990007', documento: null, qualificacao: 10, totalConversas: 1, temConversaAtiva: true, nomePendente: true }),
  clienteBase({ id: 'cliente-pedro', nome: 'Pedro Henrique Alves', telefone: '5538999990008', documento: '44455566629', qualificacao: 55, totalConversas: 2 }),
]

export const setores = [
  { id: 'setor-notas', nome: 'Tabelionato de Notas', descricao: 'Escrituras, procurações, reconhecimento de firma e autenticações', ordem: 1, ativo: true, conversasAtivas: 4 },
  { id: 'setor-civil', nome: 'Registro Civil', descricao: 'Certidões de nascimento, casamento, óbito e averbações', ordem: 2, ativo: true, conversasAtivas: 3 },
]

/** As mesmas 4 tintas fechadas de DESIGN.md 2.2 — nunca inventar cor fora dessas. */
export const etapas = [
  { id: 'etapa-inicial', nome: 'Atendimento inicial', descricao: 'Triagem, identifica a demanda', ordem: 1, cor: '#2E5170', ehFinal: false, ativo: true, conversasAtivas: 2 },
  { id: 'etapa-captacao', nome: 'Captação do serviço', descricao: 'Identifica o tipo de ato, coleta documentos', ordem: 2, cor: '#6B3F73', ehFinal: false, ativo: true, conversasAtivas: 2 },
  { id: 'etapa-processamento', nome: 'Em processamento / exigência', descricao: 'Análise, pendência', ordem: 3, cor: '#8A6516', ehFinal: false, ativo: true, conversasAtivas: 2 },
  { id: 'etapa-pronto', nome: 'Pronto / entrega', descricao: 'Concluído, cliente avisado', ordem: 4, cor: '#3D5C3A', ehFinal: true, ativo: true, conversasAtivas: 1 },
]

export const servicos = [
  { id: 'servico-firma', nome: 'Reconhecimento de firma', codigo: 'NT-01', categoria: 'Notas', categoriaDescrita: 'Notas', descricao: 'Reconhecimento de assinatura por semelhança ou autenticidade', ativo: true, setorId: 'setor-notas', setorNome: 'Tabelionato de Notas', documentosNecessarios: 'Documento oficial com foto', conversasAtivas: 2, prazo: { id: 'prazo-firma', prazoHoras: 4, percentualAlerta: 80, apenasDiasUteis: true, prazoDescrito: '4 horas (dias úteis)' } },
  { id: 'servico-escritura', nome: 'Escritura de compra e venda', codigo: 'NT-02', categoria: 'Notas', categoriaDescrita: 'Notas', descricao: 'Lavratura de escritura pública de compra e venda de imóvel', ativo: true, setorId: 'setor-notas', setorNome: 'Tabelionato de Notas', documentosNecessarios: 'RG, CPF, certidão de casamento (se houver) e matrícula atualizada do imóvel', conversasAtivas: 1, prazo: { id: 'prazo-escritura', prazoHoras: 120, percentualAlerta: 70, apenasDiasUteis: true, prazoDescrito: '5 dias úteis' } },
  { id: 'servico-procuracao', nome: 'Procuração pública', codigo: 'NT-03', categoria: 'Notas', categoriaDescrita: 'Notas', descricao: 'Lavratura de procuração por instrumento público', ativo: true, setorId: 'setor-notas', setorNome: 'Tabelionato de Notas', documentosNecessarios: 'RG e CPF do outorgante e do outorgado', conversasAtivas: 0, prazo: { id: 'prazo-procuracao', prazoHoras: 24, percentualAlerta: 80, apenasDiasUteis: true, prazoDescrito: '1 dia útil' } },
  { id: 'servico-nascimento', nome: 'Certidão de nascimento', codigo: 'RC-01', categoria: 'RegistroCivil', categoriaDescrita: 'Registro Civil', descricao: 'Segunda via de certidão de nascimento', ativo: true, setorId: 'setor-civil', setorNome: 'Registro Civil', documentosNecessarios: 'Nome completo e data de nascimento do registrado', conversasAtivas: 2, prazo: { id: 'prazo-nascimento', prazoHoras: 24, percentualAlerta: 80, apenasDiasUteis: true, prazoDescrito: '1 dia útil' } },
  { id: 'servico-casamento', nome: 'Certidão de casamento', codigo: 'RC-02', categoria: 'RegistroCivil', categoriaDescrita: 'Registro Civil', descricao: 'Segunda via de certidão de casamento', ativo: true, setorId: 'setor-civil', setorNome: 'Registro Civil', documentosNecessarios: 'Nome completo dos cônjuges e data do casamento', conversasAtivas: 1, prazo: null },
  { id: 'servico-obito', nome: 'Certidão de óbito', codigo: 'RC-03', categoria: 'RegistroCivil', categoriaDescrita: 'Registro Civil', descricao: 'Segunda via de certidão de óbito', ativo: false, setorId: 'setor-civil', setorNome: 'Registro Civil', documentosNecessarios: null, conversasAtivas: 0, prazo: null },
]

export const respostasRapidas = [
  { id: 'rapida-horario', atalho: 'horario', titulo: 'Horário de atendimento', conteudo: 'Atendemos de segunda a sexta-feira, das 8h às 17h.', ativo: true },
  { id: 'rapida-prazo', atalho: 'prazo', titulo: 'Prazo médio', conteudo: 'O prazo varia por tipo de ato — posso confirmar o prazo exato do seu caso.', ativo: true },
  { id: 'rapida-valor', atalho: 'valor', titulo: 'Sobre valores', conteudo: 'Os valores seguem a tabela de emolumentos do estado, fixada em lei. Posso te passar o valor do ato específico.', ativo: true },
  { id: 'rapida-firma', atalho: 'firma', titulo: 'Reconhecimento de firma', conteudo: 'Para reconhecer firma, basta trazer um documento oficial com foto.', ativo: true },
  { id: 'rapida-escritura', atalho: 'escritura', titulo: 'Escritura', conteudo: 'Para lavrar escritura, precisamos de RG, CPF e a matrícula atualizada do imóvel.', ativo: true },
  { id: 'rapida-certidao', atalho: 'certidao', titulo: 'Segunda via de certidão', conteudo: 'Para emitir a segunda via, preciso do nome completo e da data do registro.', ativo: true },
]

export const mensagensAutomaticas = [
  { id: 'auto-boas-vindas', gatilho: 'NovoAtendimento', gatilhoDescrito: 'Primeiro contato do cliente', etapaAlvoId: null, etapaAlvoNome: null, conteudo: 'Olá, {NomeCliente}! Aqui é o {NomeCartorio}. Sobre qual assunto você precisa de ajuda?\n{MenuSetores}', ativo: true },
  { id: 'auto-triagem-sem', gatilho: 'TriagemSemCorrespondencia', gatilhoDescrito: 'Resposta não reconhecida no menu de setor', etapaAlvoId: null, etapaAlvoNome: null, conteudo: 'Não entendi sua escolha. Já vou te encaminhar para um de nossos atendentes.', ativo: true },
  { id: 'auto-triagem-com', gatilho: 'TriagemComCorrespondencia', gatilhoDescrito: 'Setor reconhecido, falta escolher o serviço', etapaAlvoId: null, etapaAlvoNome: null, conteudo: 'Certo! Qual desses serviços você precisa?\n{MenuServicos}', ativo: true },
  { id: 'auto-servico', gatilho: 'ServicoEscolhido', gatilhoDescrito: 'Cliente escolheu o serviço', etapaAlvoId: null, etapaAlvoNome: null, conteudo: 'Para {Servico} você vai precisar de:\n{DocumentosNecessarios}\n\nPrazo estimado: {PrazoServico}.', ativo: true },
  { id: 'auto-continuacao', gatilho: 'MenuDeContinuacao', gatilhoDescrito: 'Atendimento estruturado terminou', etapaAlvoId: null, etapaAlvoNome: null, conteudo: 'Posso ajudar com mais alguma coisa?\n1 - Ver outro serviço\n2 - Trocar de assunto\n3 - Falar com um atendente\n4 - Encerrar', ativo: true },
  { id: 'auto-etapa-final', gatilho: 'EtapaAlterada', gatilhoDescrito: 'Atendimento muda de etapa', etapaAlvoId: 'etapa-pronto', etapaAlvoNome: 'Pronto / entrega', conteudo: 'Seu atendimento {Protocolo} está concluído. Obrigado por escolher o {NomeCartorio}!', ativo: true },
  { id: 'auto-prazo-alerta', gatilho: 'PrazoProximoVencimento', gatilhoDescrito: 'Prazo perto de vencer', etapaAlvoId: null, etapaAlvoNome: null, conteudo: 'Seu atendimento {Protocolo} está perto do prazo previsto. Já estamos cuidando disso.', ativo: false },
  { id: 'auto-prazo-vencido', gatilho: 'PrazoVencido', gatilhoDescrito: 'Prazo vencido', etapaAlvoId: null, etapaAlvoNome: null, conteudo: 'Pedimos desculpas pelo atraso no seu atendimento {Protocolo}. Já estamos resolvendo.', ativo: false },
  { id: 'auto-encerramento-pergunta', gatilho: 'ConfirmacaoDeEncerramento', gatilhoDescrito: 'Conversa parada: posso encerrar?', etapaAlvoId: null, etapaAlvoNome: null, conteudo: 'Ainda precisa de alguma coisa?\n1 - Sim, continuar\n2 - Não, pode encerrar', ativo: true },
  { id: 'auto-pedido-atendente', gatilho: 'PedidoDeAtendente', gatilhoDescrito: 'Pedido de atendente no meio do atendimento', etapaAlvoId: null, etapaAlvoNome: null, conteudo: 'Sem problema! Já vou chamar um de nossos atendentes para continuar com você.', ativo: true },
]

const encerramento = { minutosParaPerguntar: 20, minutosParaEncerrar: 30, horaInicial: 8, horaFinal: 18 }

export const conhecimentos = [
  {
    id: 'conhecimento-reagendamento', titulo: 'Política de reagendamento', tema: 'Atendimento',
    conteudo: 'O reagendamento pode ser pedido até 24h antes do horário marcado, sem custo.',
    tipoServicoId: null, tipoServicoNome: null, vigenteDesde: null, vigenteAte: null,
    versao: 2, criadoPorNome: 'Beatriz Fonseca', atualizadoPorNome: 'Beatriz Fonseca',
    ativo: true, criadoEm: diasAtras(90), atualizadoEm: diasAtras(10),
  },
  {
    id: 'conhecimento-mutirao', titulo: 'Mutirão de firmas de janeiro', tema: 'Campanha',
    conteudo: 'Durante o mutirão, o reconhecimento de firma sai no mesmo dia.',
    tipoServicoId: 'servico-firma', tipoServicoNome: 'Reconhecimento de firma',
    vigenteDesde: '2026-01-01', vigenteAte: '2026-01-31',
    versao: 1, criadoPorNome: 'Beatriz Fonseca', atualizadoPorNome: null,
    ativo: true, criadoEm: diasAtras(200), atualizadoEm: diasAtras(200),
  },
]

export const canal = {
  situacao: {
    habilitado: true, conectado: true, estado: 'open', numero: '5538999990000',
    perfilNome: 'Cartório Modelo', perfilFotoUrl: null, qrCodeBase64: null,
    conectadoEm: diasAtras(20),
  },
  situacaoDesconectada: {
    habilitado: true, conectado: false, estado: 'close', numero: null,
    perfilNome: null, perfilFotoUrl: null,
    qrCodeBase64:
      'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=',
    conectadoEm: null,
  },
  regras: {
    modo: 'TodosMenosBloqueados', origem: 'Banco',
    liberados: [],
    bloqueados: [
      { id: 'numero-bloqueado-1', telefone: '5538999998888', rotulo: 'Trote recorrente', tipo: 'Bloqueado', criadoPorNome: 'Beatriz Fonseca', criadoEm: diasAtras(5) },
    ],
  },
  descartes: {
    totalHoje: 3,
    recentes: [
      { telefone: '5538999998888', motivo: 'Bloqueado', criadoEm: horasAtras(2) },
      { telefone: '5538999997777', motivo: 'ForaDaLista', criadoEm: horasAtras(5) },
    ],
  },
  conexoes: [
    { numero: '5538999990000', perfilNome: 'Cartório Modelo', conectadoEm: diasAtras(20), desconectadoEm: null, motivo: 'Pareamento', atendenteNome: 'Beatriz Fonseca' },
    { numero: '5538999990000', perfilNome: 'Cartório Modelo', conectadoEm: diasAtras(45), desconectadoEm: diasAtras(21), motivo: 'QuedaDoGateway', atendenteNome: null },
  ],
}

// ------------------------------------------------------- Conversas e mensagens

function mensagem(dados: Partial<ReturnType<typeof mensagemBase>> & { id: string; conversaId: string; autorTipo: string; conteudo: string; enviadaEm: string }) {
  return { ...mensagemBase(dados.conversaId, dados.enviadaEm), ...dados }
}
function mensagemBase(conversaId: string, enviadaEm: string) {
  return {
    id: '', conversaId, autorTipo: 'Cliente', atendenteId: null, atendenteNome: null, conteudo: '', tipo: 'Texto',
    statusEntrega: 'Lida', ehAutomatica: false, fixada: false, oculta: false, ocultaPorNome: null,
    encaminhadaDeProtocolo: null, enviadaEm, lidaEm: enviadaEm, mensagemRespondidaId: null, respondidaAutor: null,
    respondidaConteudo: null, arquivoNome: null, arquivoTipoMime: null, arquivoTamanhoBytes: null, geradaPorIa: false,
  }
}

/** Conversa 1 — a "rica": atendida por Paula, com anexo, citação e anotação interna. Esta é a que abrimos na maioria dos screenshots de Atendimento. */
const conversaRica = {
  id: 'conversa-rica', protocolo: '202609-0001', clienteId: 'cliente-maria', clienteNome: 'Maria Aparecida Souza', clienteTelefone: '5538999990001',
  atendenteId: 'atendente-juliana', atendenteNome: 'Paula Ribeiro', setorId: 'setor-civil', setorNome: 'Registro Civil',
  etapaId: 'etapa-processamento', etapaNome: 'Em processamento / exigência', etapaCor: '#8A6516',
  tipoServicoId: 'servico-nascimento', tipoServicoNome: 'Certidão de nascimento', status: 'Ativa',
  iniciadaEm: horasAtras(3), ultimaMensagemEm: horasAtras(0.2), mensagensNaoLidas: 0, aguardandoTriagem: false,
  prazoIniciadoEm: horasAtras(3), prazoLimite: horasNaFrente(21), situacaoPrazo: 'EmDia',
  previaUltimaMensagem: 'Perfeito, recebi. Vou dar entrada agora.', encerradaEm: null, encerradaPorNome: null,
  motivoEncerramento: null, reabertaEm: null, clienteFotoPerfilUrl: null, clienteNomePendente: false,
}
const mensagensRica = [
  mensagem({ id: 'm1', conversaId: 'conversa-rica', autorTipo: 'Cliente', conteudo: 'Boa tarde! Preciso de uma certidão de nascimento urgente.', enviadaEm: horasAtras(3) }),
  mensagem({ id: 'm2', conversaId: 'conversa-rica', autorTipo: 'Sistema', ehAutomatica: true, conteudo: 'Olá, Maria Aparecida Souza! Aqui é o Cartório Modelo. Sobre qual assunto você precisa de ajuda?\n1 - Tabelionato de Notas\n2 - Registro Civil', enviadaEm: horasAtras(2.9) }),
  mensagem({ id: 'm3', conversaId: 'conversa-rica', autorTipo: 'Cliente', conteudo: '2', enviadaEm: horasAtras(2.85) }),
  mensagem({ id: 'm4', conversaId: 'conversa-rica', autorTipo: 'Sistema', ehAutomatica: true, conteudo: 'Certo! Qual desses serviços você precisa?\n1 - Certidão de nascimento\n2 - Certidão de casamento', enviadaEm: horasAtras(2.8) }),
  mensagem({ id: 'm5', conversaId: 'conversa-rica', autorTipo: 'Cliente', conteudo: '1', enviadaEm: horasAtras(2.75) }),
  mensagem({ id: 'm6', conversaId: 'conversa-rica', autorTipo: 'Sistema', ehAutomatica: true, conteudo: 'Para Certidão de nascimento você vai precisar de:\nNome completo e data de nascimento do registrado\n\nPrazo estimado: 1 dia útil.', enviadaEm: horasAtras(2.7) }),
  mensagem({ id: 'm7', conversaId: 'conversa-rica', autorTipo: 'Sistema', ehAutomatica: false, conteudo: 'Triagem automática encaminhou para Registro Civil.', enviadaEm: horasAtras(2.69) }),
  mensagem({ id: 'm8', conversaId: 'conversa-rica', autorTipo: 'Atendente', atendenteId: 'atendente-juliana', atendenteNome: 'Paula Ribeiro', conteudo: 'Olá, Dona Maria! Vou te ajudar com a certidão. Pode confirmar o nome completo e a data de nascimento da pessoa?', statusEntrega: 'Lida', enviadaEm: horasAtras(2.5) }),
  mensagem({ id: 'm9', conversaId: 'conversa-rica', autorTipo: 'Cliente', tipo: 'Imagem', conteudo: 'documento.jpg', arquivoNome: 'documento.jpg', arquivoTipoMime: 'image/jpeg', arquivoTamanhoBytes: 184320, enviadaEm: horasAtras(1) }),
  mensagem({ id: 'm10', conversaId: 'conversa-rica', autorTipo: 'Cliente', conteudo: 'Segue o documento com os dados dela.', enviadaEm: horasAtras(0.95) }),
  mensagem({ id: 'm11', conversaId: 'conversa-rica', autorTipo: 'Atendente', atendenteId: 'atendente-juliana', atendenteNome: 'Paula Ribeiro', conteudo: 'Perfeito, recebi. Vou dar entrada agora.', mensagemRespondidaId: 'm10', respondidaAutor: 'Maria Aparecida Souza', respondidaConteudo: 'Segue o documento com os dados dela.', statusEntrega: 'Entregue', enviadaEm: horasAtras(0.2) }),
]
const historicoRica = [
  { id: 'h1', tipoMudanca: 'Abertura', etapaAnterior: null, etapaNova: 'Atendimento inicial', setorAnterior: null, setorNovo: null, atendenteNome: null, ocorridoEm: horasAtras(3), observacao: null },
  { id: 'h2', tipoMudanca: 'Setor', etapaAnterior: null, etapaNova: null, setorAnterior: null, setorNovo: 'Registro Civil', atendenteNome: null, ocorridoEm: horasAtras(2.69), observacao: 'Triagem automática' },
  { id: 'h3', tipoMudanca: 'Etapa', etapaAnterior: 'Atendimento inicial', etapaNova: 'Captação do serviço', setorAnterior: null, setorNovo: null, atendenteNome: 'Paula Ribeiro', ocorridoEm: horasAtras(2.6), observacao: null },
  { id: 'h4', tipoMudanca: 'Etapa', etapaAnterior: 'Captação do serviço', etapaNova: 'Em processamento / exigência', setorAnterior: null, setorNovo: null, atendenteNome: 'Paula Ribeiro', ocorridoEm: horasAtras(0.9), observacao: null },
]

/** Conversa 2 — recém-chegada, aguardando triagem, ninguém respondeu ainda (na fila). */
const conversaNova = {
  id: 'conversa-nova', protocolo: '202609-0002', clienteId: 'cliente-pendente', clienteNome: '5538999990007', clienteTelefone: '5538999990007',
  atendenteId: null, atendenteNome: null, setorId: null, setorNome: null,
  etapaId: 'etapa-inicial', etapaNome: 'Atendimento inicial', etapaCor: '#2E5170',
  tipoServicoId: null, tipoServicoNome: null, status: 'Ativa',
  iniciadaEm: horasAtras(0.1), ultimaMensagemEm: horasAtras(0.1), mensagensNaoLidas: 1, aguardandoTriagem: true,
  prazoIniciadoEm: null, prazoLimite: null, situacaoPrazo: 'SemPrazo',
  previaUltimaMensagem: 'boa tarde, queria saber sobre uma procuração', encerradaEm: null, encerradaPorNome: null,
  motivoEncerramento: null, reabertaEm: null, clienteFotoPerfilUrl: null, clienteNomePendente: true,
}
const mensagensNova = [
  mensagem({ id: 'n1', conversaId: 'conversa-nova', autorTipo: 'Cliente', conteudo: 'boa tarde, queria saber sobre uma procuração', statusEntrega: 'Entregue', lidaEm: null, enviadaEm: horasAtras(0.1) }),
]

/** Conversa 3 — a IA respondeu sozinha, ninguém escalou, ainda na fila. Mostra o rótulo "IA". */
const conversaIa = {
  id: 'conversa-ia', protocolo: '202609-0003', clienteId: 'cliente-pedro', clienteNome: 'Pedro Henrique Alves', clienteTelefone: '5538999990008',
  atendenteId: null, atendenteNome: null, setorId: 'setor-notas', setorNome: 'Tabelionato de Notas',
  etapaId: 'etapa-inicial', etapaNome: 'Atendimento inicial', etapaCor: '#2E5170',
  tipoServicoId: null, tipoServicoNome: null, status: 'Ativa',
  iniciadaEm: horasAtras(0.5), ultimaMensagemEm: horasAtras(0.4), mensagensNaoLidas: 0, aguardandoTriagem: false,
  prazoIniciadoEm: null, prazoLimite: null, situacaoPrazo: 'SemPrazo',
  previaUltimaMensagem: 'Claro! O reconhecimento de firma pode ser feito por semelhança...', encerradaEm: null,
  encerradaPorNome: null, motivoEncerramento: null, reabertaEm: null, clienteFotoPerfilUrl: null, clienteNomePendente: false,
}
const mensagensIa = [
  mensagem({ id: 'i1', conversaId: 'conversa-ia', autorTipo: 'Cliente', conteudo: '1', enviadaEm: horasAtras(0.5) }),
  mensagem({ id: 'i2', conversaId: 'conversa-ia', autorTipo: 'Sistema', ehAutomatica: true, conteudo: 'Certo! Qual desses serviços você precisa?\n1 - Reconhecimento de firma\n2 - Escritura de compra e venda\n3 - Procuração pública', enviadaEm: horasAtras(0.49) }),
  mensagem({ id: 'i3', conversaId: 'conversa-ia', autorTipo: 'Cliente', conteudo: 'o reconhecimento de firma precisa ser com o dono do documento presente?', enviadaEm: horasAtras(0.45) }),
  mensagem({ id: 'i4', conversaId: 'conversa-ia', autorTipo: 'Sistema', ehAutomatica: true, geradaPorIa: true, conteudo: 'Claro! O reconhecimento de firma pode ser feito por semelhança (compara com a assinatura já registrada no cartório) ou por autenticidade (a pessoa assina na hora, na presença do tabelião). Quer que eu já registre esse atendimento como reconhecimento de firma?', enviadaEm: horasAtras(0.4) }),
]

/** Conversa 4 — encerrada, etapa final, mostra o selo "Ato pronto" e a opção de reabrir. */
const conversaEncerrada = {
  id: 'conversa-encerrada', protocolo: '202608-0042', clienteId: 'cliente-antonio', clienteNome: 'Antônio Carlos Duarte', clienteTelefone: '5538999990004',
  atendenteId: 'atendente-marta', atendenteNome: 'Carla Menezes', setorId: 'setor-notas', setorNome: 'Tabelionato de Notas',
  etapaId: 'etapa-pronto', etapaNome: 'Pronto / entrega', etapaCor: '#3D5C3A',
  tipoServicoId: 'servico-procuracao', tipoServicoNome: 'Procuração pública', status: 'Encerrada',
  iniciadaEm: diasAtras(6), ultimaMensagemEm: diasAtras(5), mensagensNaoLidas: 0, aguardandoTriagem: false,
  prazoIniciadoEm: diasAtras(6), prazoLimite: diasAtras(5), situacaoPrazo: 'EmDia',
  previaUltimaMensagem: 'Muito obrigado pela atenção!', encerradaEm: diasAtras(5), encerradaPorNome: 'Carla Menezes',
  motivoEncerramento: 'Procuração entregue ao cliente.', reabertaEm: null, clienteFotoPerfilUrl: null, clienteNomePendente: false,
}
const mensagensEncerrada = [
  mensagem({ id: 'e1', conversaId: 'conversa-encerrada', autorTipo: 'Cliente', conteudo: 'A procuração já está pronta?', enviadaEm: diasAtras(5.1) }),
  mensagem({ id: 'e2', conversaId: 'conversa-encerrada', autorTipo: 'Atendente', atendenteId: 'atendente-marta', atendenteNome: 'Carla Menezes', conteudo: 'Está sim! Já pode retirar no balcão.', enviadaEm: diasAtras(5.05) }),
  mensagem({ id: 'e3', conversaId: 'conversa-encerrada', autorTipo: 'Sistema', ehAutomatica: true, conteudo: 'Seu atendimento 202608-0042 está concluído. Obrigado por escolher o Cartório Modelo!', enviadaEm: diasAtras(5.02) }),
  mensagem({ id: 'e4', conversaId: 'conversa-encerrada', autorTipo: 'Cliente', conteudo: 'Muito obrigado pela atenção!', enviadaEm: diasAtras(5) }),
]

/** Conversa 5 — travada com Carla, prazo atrasado. Mostra cadeado + etiqueta "Atrasada". */
const conversaAtrasada = {
  id: 'conversa-atrasada', protocolo: '202609-0004', clienteId: 'cliente-construtora', clienteNome: 'Construtora Vale Verde', clienteTelefone: '5538999990005',
  atendenteId: 'atendente-marta', atendenteNome: 'Carla Menezes', setorId: 'setor-notas', setorNome: 'Tabelionato de Notas',
  etapaId: 'etapa-processamento', etapaNome: 'Em processamento / exigência', etapaCor: '#8A6516',
  tipoServicoId: 'servico-escritura', tipoServicoNome: 'Escritura de compra e venda', status: 'Ativa',
  iniciadaEm: diasAtras(8), ultimaMensagemEm: diasAtras(1), mensagensNaoLidas: 1, aguardandoTriagem: false,
  prazoIniciadoEm: diasAtras(8), prazoLimite: diasAtras(1), situacaoPrazo: 'Atrasada',
  previaUltimaMensagem: 'A matrícula atualizada já chegou?', encerradaEm: null, encerradaPorNome: null,
  motivoEncerramento: null, reabertaEm: null, clienteFotoPerfilUrl: null, clienteNomePendente: false,
}
const mensagensAtrasada = [
  mensagem({ id: 'a1', conversaId: 'conversa-atrasada', autorTipo: 'Cliente', conteudo: 'A matrícula atualizada já chegou?', statusEntrega: 'Entregue', lidaEm: null, enviadaEm: diasAtras(1) }),
]

/** Conversa 6 — na fila, para demonstrar filtro "Na fila" e "Em alerta" (prazo perto de vencer). */
const conversaFilaAlerta = {
  id: 'conversa-fila-alerta', protocolo: '202609-0005', clienteId: 'cliente-joao', clienteNome: 'João Pedro Ramos', clienteTelefone: '5538999990002',
  atendenteId: null, atendenteNome: null, setorId: 'setor-civil', setorNome: 'Registro Civil',
  etapaId: 'etapa-captacao', etapaNome: 'Captação do serviço', etapaCor: '#6B3F73',
  tipoServicoId: 'servico-casamento', tipoServicoNome: 'Certidão de casamento', status: 'Ativa',
  iniciadaEm: horasAtras(20), ultimaMensagemEm: horasAtras(4), mensagensNaoLidas: 2, aguardandoTriagem: false,
  prazoIniciadoEm: horasAtras(20), prazoLimite: horasNaFrente(4), situacaoPrazo: 'EmAlerta',
  previaUltimaMensagem: 'Vou enviar os dados do casamento agora.', encerradaEm: null, encerradaPorNome: null,
  motivoEncerramento: null, reabertaEm: null, clienteFotoPerfilUrl: null, clienteNomePendente: false,
}
const mensagensFilaAlerta = [
  mensagem({ id: 'f1', conversaId: 'conversa-fila-alerta', autorTipo: 'Cliente', conteudo: 'Vou enviar os dados do casamento agora.', statusEntrega: 'Entregue', lidaEm: null, enviadaEm: horasAtras(4) }),
]

/** Conversa 7 — mensagem fixada e mensagem oculta, para os dois recursos do menu de contexto. */
const conversaFixadaOculta = {
  id: 'conversa-fixada', protocolo: '202609-0006', clienteId: 'cliente-rosangela', clienteNome: 'Rosângela Ferreira Lima', clienteTelefone: '5538999990006',
  atendenteId: 'atendente-juliana', atendenteNome: 'Paula Ribeiro', setorId: 'setor-civil', setorNome: 'Registro Civil',
  etapaId: 'etapa-captacao', etapaNome: 'Captação do serviço', etapaCor: '#6B3F73',
  tipoServicoId: 'servico-nascimento', tipoServicoNome: 'Certidão de nascimento', status: 'Ativa',
  iniciadaEm: diasAtras(1), ultimaMensagemEm: horasAtras(6), mensagensNaoLidas: 0, aguardandoTriagem: false,
  prazoIniciadoEm: diasAtras(1), prazoLimite: horasNaFrente(1), situacaoPrazo: 'EmAlerta',
  previaUltimaMensagem: 'Combinado, te aviso assim que ficar pronta.', encerradaEm: null, encerradaPorNome: null,
  motivoEncerramento: null, reabertaEm: null, clienteFotoPerfilUrl: null, clienteNomePendente: false,
}
const mensagensFixadaOculta = [
  mensagem({ id: 'x1', conversaId: 'conversa-fixada', autorTipo: 'Cliente', conteudo: 'Nome completo: Rosângela Ferreira Lima, nascida em 14/03/1990.', fixada: true, enviadaEm: diasAtras(1) }),
  mensagem({ id: 'x2', conversaId: 'conversa-fixada', autorTipo: 'Cliente', conteudo: 'ignora essa mensagem, mandei sem querer', oculta: true, ocultaPorNome: 'Paula Ribeiro', enviadaEm: horasAtras(20) }),
  mensagem({ id: 'x3', conversaId: 'conversa-fixada', autorTipo: 'Atendente', atendenteId: 'atendente-juliana', atendenteNome: 'Paula Ribeiro', conteudo: 'Combinado, te aviso assim que ficar pronta.', enviadaEm: horasAtras(6) }),
]

/** Mais linhas simples só para a fila parecer cheia — sem fio detalhado (não são abertas nos screenshots). */
function conversaSimples(id: string, protocolo: string, clienteId: string, clienteNome: string, telefone: string, setorId: string, setorNome: string, etapaId: string, etapaNome: string, etapaCor: string, horas: number) {
  return {
    id, protocolo, clienteId, clienteNome, clienteTelefone: telefone, atendenteId: null, atendenteNome: null,
    setorId, setorNome, etapaId, etapaNome, etapaCor, tipoServicoId: null, tipoServicoNome: null, status: 'Ativa',
    iniciadaEm: horasAtras(horas), ultimaMensagemEm: horasAtras(horas - 0.05), mensagensNaoLidas: 1, aguardandoTriagem: false,
    prazoIniciadoEm: null, prazoLimite: null, situacaoPrazo: 'SemPrazo', previaUltimaMensagem: 'Obrigado, aguardo retorno.',
    encerradaEm: null, encerradaPorNome: null, motivoEncerramento: null, reabertaEm: null, clienteFotoPerfilUrl: null, clienteNomePendente: false,
  }
}

export const conversas = [
  conversaRica, conversaNova, conversaIa, conversaEncerrada, conversaAtrasada, conversaFilaAlerta, conversaFixadaOculta,
  conversaSimples('conversa-extra-1', '202609-0007', 'cliente-sandra', 'Sandra Regina Oliveira', '5538999990003', 'setor-civil', 'Registro Civil', 'etapa-inicial', 'Atendimento inicial', '#2E5170', 6),
  conversaSimples('conversa-extra-2', '202609-0008', 'cliente-pedro', 'Pedro Henrique Alves', '5538999990008', 'setor-notas', 'Tabelionato de Notas', 'etapa-captacao', 'Captação do serviço', '#6B3F73', 30),
]

export const detalhes: Record<string, { conversa: any; mensagens: any[]; historico: any[] }> = {
  'conversa-rica': { conversa: conversaRica, mensagens: mensagensRica, historico: historicoRica },
  'conversa-nova': { conversa: conversaNova, mensagens: mensagensNova, historico: [] },
  'conversa-ia': { conversa: conversaIa, mensagens: mensagensIa, historico: [] },
  'conversa-encerrada': { conversa: conversaEncerrada, mensagens: mensagensEncerrada, historico: [] },
  'conversa-atrasada': { conversa: conversaAtrasada, mensagens: mensagensAtrasada, historico: [] },
  'conversa-fila-alerta': { conversa: conversaFilaAlerta, mensagens: mensagensFilaAlerta, historico: [] },
  'conversa-fixada': { conversa: conversaFixadaOculta, mensagens: mensagensFixadaOculta, historico: [] },
}
for (const c of conversas) {
  if (!detalhes[c.id]) detalhes[c.id] = { conversa: c, mensagens: [], historico: [] }
}

// -------------------------------------------------------------- Indicadores

export const painel = {
  dozeMeses: Array.from({ length: 12 }, (_, i) => {
    const mes = new Date(Date.UTC(2025, 9 + i, 1)).toISOString().slice(0, 7)
    const abertos = 18 + ((i * 7) % 14)
    const encerrados = 15 + ((i * 5) % 12)
    return { mes, abertos, encerrados, emAberto: 6 + ((i * 3) % 9) }
  }),
  janelas: [
    { chave: 'hoje', rotulo: 'Hoje', de: AGORA, ate: AGORA, abertos: 3, encerrados: 1, primeiraRespostaMedianaMinutos: 6, amostraPrimeiraResposta: 3 },
    { chave: '7dias', rotulo: 'Últimos 7 dias', de: diasAtras(7), ate: AGORA, abertos: 24, encerrados: 19, primeiraRespostaMedianaMinutos: 8, amostraPrimeiraResposta: 19 },
    { chave: 'mes', rotulo: 'Este mês', de: diasAtras(9), ate: AGORA, abertos: 31, encerrados: 24, primeiraRespostaMedianaMinutos: 9, amostraPrimeiraResposta: 24 },
  ],
}

export const visaoGeral = {
  periodo: { de: diasAtras(30), ate: AGORA, granularidade: 'dia' },
  agora: { ativas: conversas.filter((c) => c.status === 'Ativa').length, naFila: conversas.filter((c) => c.status === 'Ativa' && !c.atendenteId).length, emAlerta: 1, atrasadas: 1, aguardandoResposta: 3 },
  noPeriodo: { abertos: 31, encerrados: 24, primeiraRespostaMedianaMinutos: 8, primeiraRespostaMediaMinutos: 11, amostraPrimeiraResposta: 24 },
  fluxoDeAtendimentos: Array.from({ length: 14 }, (_, i) => ({ inicio: diasAtras(13 - i), abertos: 1 + (i % 4), encerrados: (i % 3), emAberto: 5 + (i % 6), primeiraRespostaMedianaMinutos: i % 3 === 0 ? null : 6 + (i % 5) })),
  requerAtencao: [
    { chave: 'atrasadas', rotulo: 'Atrasadas', total: 1, itens: [{ conversaId: 'conversa-atrasada', protocolo: '202609-0004', clienteNome: 'Construtora Vale Verde', clienteTelefone: '5538999990005', clienteFotoPerfilUrl: null, etapaNome: 'Em processamento / exigência', etapaCor: '#8A6516', situacaoPrazo: 'Atrasada', prazoLimite: diasAtras(1), ultimaMensagemEm: diasAtras(1), atendenteNome: 'Carla Menezes' }] },
    { chave: 'fila', rotulo: 'Na fila', total: 3, itens: [{ conversaId: 'conversa-nova', protocolo: '202609-0002', clienteNome: '5538999990007', clienteTelefone: '5538999990007', clienteFotoPerfilUrl: null, etapaNome: 'Atendimento inicial', etapaCor: '#2E5170', situacaoPrazo: 'SemPrazo', prazoLimite: null, ultimaMensagemEm: horasAtras(0.1), atendenteNome: null }] },
  ],
  cargaDaEquipe: [
    { atendenteId: 'atendente-juliana', atendenteNome: 'Paula Ribeiro', ativas: 2, emAlerta: 1, atrasadas: 0 },
    { atendenteId: 'atendente-marta', atendenteNome: 'Carla Menezes', ativas: 2, emAlerta: 0, atrasadas: 1 },
  ],
  servicosMaisProcurados: [
    { tipoServicoId: 'servico-nascimento', nome: 'Certidão de nascimento', total: 12, percentual: 30 },
    { tipoServicoId: 'servico-firma', nome: 'Reconhecimento de firma', total: 9, percentual: 22 },
    { tipoServicoId: 'servico-escritura', nome: 'Escritura de compra e venda', total: 5, percentual: 12 },
  ],
  distribuicaoPorEtapa: etapas.map((e) => ({ id: e.id, nome: e.nome, cor: e.cor, total: e.conversasAtivas })),
  minhaDistribuicaoPorEtapa: etapas.map((e) => ({ id: e.id, nome: e.nome, cor: e.cor, total: Math.max(0, e.conversasAtivas - 1) })),
  saude: {
    whatsApp: { habilitado: true, conectado: true, estado: 'open' },
    ia: { habilitada: true, estaConfigurada: true, modelo: 'Claude Haiku 4.5', limiteDiario: 300, usadoHoje: 42 },
    automacao: { regrasAtivas: 8, regrasInativas: 2 },
  },
}

export const relatorioDeAtendimentos = {
  periodo: visaoGeral.periodo,
  abertos: 31, encerrados: 24, emAbertoAgora: 7, primeiraRespostaMedianaMinutos: 8, amostraPrimeiraResposta: 24,
  totalMensal: painel.dozeMeses,
  clientesMaisAtendidos: [
    { clienteId: 'cliente-antonio', nome: 'Antônio Carlos Duarte', fotoPerfilUrl: null, telefoneWhatsapp: '5538999990004', total: 5, percentual: 16, ultimoAtendimentoEm: diasAtras(5) },
    { clienteId: 'cliente-maria', nome: 'Maria Aparecida Souza', fotoPerfilUrl: null, telefoneWhatsapp: '5538999990001', total: 3, percentual: 10, ultimoAtendimentoEm: horasAtras(0.2) },
  ],
  clientesAtendidosNoPeriodo: 18,
  distribuicaoPorSetor: setores.map((s) => ({ id: s.id, nome: s.nome, cor: null, total: s.conversasAtivas })),
  distribuicaoPorEtapa: etapas.map((e) => ({ id: e.id, nome: e.nome, cor: e.cor, total: e.conversasAtivas })),
  horariosDeMaiorDemanda: Array.from({ length: 24 }, (_, h) => (h >= 8 && h <= 17 ? 2 + ((h * 3) % 9) : 0)),
  absorcaoDaAutomacao: { humanas: 40, automaticas: 35, ia: 25, percentualAutomatizado: 60 },
}

export const relatorioDeAtendentes = {
  periodo: visaoGeral.periodo, totalDeAtendentes: 3, comConversaAtiva: 2, naFilaSemResponsavel: 3,
  encerramentos: { porAtendente: 20, peloCliente: 4 },
  atendentes: [
    { atendenteId: 'atendente-beatriz', nome: 'Beatriz Fonseca', cargoDescrito: 'Titular', perfil: 'Administrador', ativas: 0, emAlerta: 0, atrasadas: 0, encerradosNoPeriodo: 6, primeirasRespostasNoPeriodo: 6, primeiraRespostaMedianaMinutos: 5, ultimoAcessoEm: horasAtras(0.2) },
    { atendenteId: 'atendente-marta', nome: 'Carla Menezes', cargoDescrito: 'Escrevente autorizado', perfil: 'Supervisor', ativas: 2, emAlerta: 0, atrasadas: 1, encerradosNoPeriodo: 9, primeirasRespostasNoPeriodo: 9, primeiraRespostaMedianaMinutos: 7, ultimoAcessoEm: horasAtras(2) },
    { atendenteId: 'atendente-juliana', nome: 'Paula Ribeiro', cargoDescrito: 'Escrevente', perfil: 'Atendente', ativas: 2, emAlerta: 1, atrasadas: 0, encerradosNoPeriodo: 9, primeirasRespostasNoPeriodo: 9, primeiraRespostaMedianaMinutos: 10, ultimoAcessoEm: horasAtras(1) },
  ],
}

export const relatorioDeClientes = {
  periodo: visaoGeral.periodo, total: clientes.length, novosNoPeriodo: 3, comAtendimentoAtivo: clientes.filter((c) => c.temConversaAtiva).length,
  pessoaFisica: clientes.filter((c) => c.tipo === 'Fisica').length, pessoaJuridica: clientes.filter((c) => c.tipo === 'Juridica').length,
  semDocumento: clientes.filter((c) => !c.documento).length,
  novosPorMes: painel.dozeMeses.map((m) => ({ mes: m.mes, total: 2 + (m.abertos % 5) })),
  maisAtendidos: relatorioDeAtendimentos.clientesMaisAtendidos,
}

export const alertas = [
  { id: 'alerta-atrasada', tipo: 'prazo_atrasado', severidade: 'Critica', titulo: '1 atendimento atrasado', descricao: 'Construtora Vale Verde — 202609-0004', quando: diasAtras(1), rota: '/atendimentos/conversa-atrasada' },
  { id: 'alerta-fila', tipo: 'fila_sem_responsavel', severidade: 'Alerta', titulo: '3 conversas na fila sem responsável', descricao: null, quando: horasAtras(0.1), rota: '/atendimentos' },
]

// ------------------------------------------------------------------------ IA

export const situacaoDaIa = {
  habilitada: true, estaConfigurada: true,
  gemini: { nome: 'Gemini', modelo: 'gemini-3.7-flash', configurado: false, status: 'SemChave', voltaATentarEm: null, motivo: 'Sem chave configurada — free tier desligado temporariamente.' },
  anthropic: { nome: 'Anthropic', modelo: 'claude-haiku-4-5', configurado: true, status: 'Disponivel', voltaATentarEm: null, motivo: null },
  limiteDiario: 300, usadoHojeGeral: 42, limiteDiarioHaiku: 60, usadoHojeHaiku: 42,
  franquia: { limitesCadastrados: false, limiteRpd: null, requisicoesHoje: 0, limiteRpm: null, requisicoesNaJanela: 0, limiteTpm: null, tokensNaJanela: 0, percentualDeSeguranca: 85, sugestaoDeLimiteRpd: null },
  hoje: { totalDeChamadas: 42, atendidasPeloGemini: 0, caidasNoHaiku: 42, percentualGratuito: 0, custoEstimadoHaikuReais: 3.15, tokensDoGemini: 0 },
  motivosDeFallback: { quota: 0, rateLimit: 0, indisponibilidade: 0, configuracao: 42, outros: 0, fallbackExecutado: 0, fallbackPreventivo: 42 },
  economia: { custoRealHaikuReais: 3.15, custoSeFosseTudoHaikuReais: 3.15, economiaEstimadaReais: 0, percentualDeEconomia: 0 },
  periodoDe: diasAtras(1), periodoAte: AGORA,
}

export const analiseDaIa = {
  periodo: { de: diasAtras(7), ate: AGORA, granularidade: 'dia' },
  situacaoAgora: { status: 'Atendendo', detalhe: 'Respondendo pelo Claude Haiku 4.5 (contingência) — Gemini desligado.' },
  valorEntregue: { perguntasAtendidas: 68, perguntasAtendidasDeltaPercentual: 12, taxaDeResolucao: 78, taxaDeResolucaoDeltaPontos: 4, naoSoubeResponder: 15, naoSoubeResponderDeltaPercentual: -3, tempoTipicoDeRespostaSegundos: 4, tempoTipicoDeRespostaDeltaPercentual: -8 },
  volumeAoLongoDoTempo: Array.from({ length: 7 }, (_, i) => ({ inicio: diasAtras(6 - i), entregues: 8 + (i % 4), naoSoubeResponder: 1 + (i % 2), falhas: 0 })),
  resultadoDasTentativas: { entregues: 68, naoSoubeResponder: 15, falhas: 2 },
  assuntosMaisPerguntados: [
    { tipoServicoId: 'servico-nascimento', nome: 'Certidão de nascimento', total: 20, percentual: 29 },
    { tipoServicoId: 'servico-firma', nome: 'Reconhecimento de firma', total: 14, percentual: 20 },
    { tipoServicoId: null, nome: 'Horário de atendimento', total: 10, percentual: 15 },
  ],
  horariosDeMaiorUso: Array.from({ length: 24 }, (_, h) => (h >= 8 && h <= 17 ? 3 + ((h * 2) % 7) : 0)),
  lacunasDeCatalogo: [
    { categoria: 'Notas', tipoServicoId: null, servicoNome: 'Testamento público', total: 4 },
  ],
  insights: [
    { severidade: 'atencao', texto: 'A IA não soube responder 4 perguntas sobre "testamento" — o assunto ainda não está no catálogo.' },
    { severidade: 'informativo', texto: 'O tempo típico de resposta caiu 8% nesta semana.' },
  ],
  rodadasMedidasDesde: diasAtras(30),
}

export const mfaSituacaoAtiva = { ativo: true, obrigatorio: true, codigosDeRecuperacaoRestantes: 8, confirmadoEm: diasAtras(60), dispositivosConfiaveisAtivos: 1 }
export const mfaSituacaoInativa = { ativo: false, obrigatorio: false, codigosDeRecuperacaoRestantes: null, confirmadoEm: null, dispositivosConfiaveisAtivos: 0 }
