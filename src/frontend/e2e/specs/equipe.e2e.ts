import { expect, test, type Page } from '@playwright/test'
import { autenticarLocalmente, criarEstadoE2E, instalarApiMock } from '../fixtures/api-mock-seguro'

/**
 * Cobertura E2E da tela de Equipe inteira — cadastro, edição, redefinição de
 * senha, permissões granulares, MFA administrativo e exclusão. Nenhuma destas
 * telas tinha spec dedicada antes: `administracao.e2e.ts` só verificava que a
 * lista renderiza os três perfis.
 */
async function preparar(page: Page, perfil: 'Administrador' | 'Supervisor' = 'Administrador') {
  const estado = criarEstadoE2E(perfil)
  await instalarApiMock(page, estado)
  await autenticarLocalmente(page, perfil)
  await page.goto('/equipe')
  return estado
}

/** O cartão da linha não tem role de lista — escopa pela classe do cartão + o nome dentro dele. */
function linha(page: Page, nome: string) {
  return page.locator('div.rounded-card').filter({ hasText: nome })
}

test('lista mostra o resumo e os atendentes cadastrados', async ({ page }) => {
  await preparar(page)

  await expect(page.getByText('Na equipe')).toBeVisible()
  await expect(page.getByText('Ativos hoje')).toBeVisible()
  await expect(page.getByText('Bruno SemHistorico')).toBeVisible()
  await expect(page.getByText('Carla ComHistorico')).toBeVisible()
  await expect(page.getByText('Diego Diretor')).toBeVisible()
})

test('cadastra atendente novo com cargo, classe e setor', async ({ page }) => {
  const estado = await preparar(page)

  await page.getByRole('button', { name: /^Novo/ }).click()
  await expect(page.getByRole('dialog')).toContainText('Novo atendente')

  await page.getByLabel('Nome').fill('Fernanda Nova')
  await page.getByLabel('E-mail (login)').fill('fernanda@teste.local')
  await page.getByLabel('Senha inicial').fill('senhaforte123')
  await page.getByRole('button').filter({ hasText: 'Atua no cartório' }).click()
  await page.getByLabel('Classe da conta').click()
  await page.getByRole('option', { name: 'Supervisor', exact: true }).click()

  await page.getByRole('button', { name: 'Salvar' }).click()

  await expect(page.getByText('Atendente cadastrado.')).toBeVisible()
  await expect.poll(() => estado.atendentes.some((a) => a.email === 'fernanda@teste.local')).toBe(true)
  await expect(page.getByText('Fernanda Nova')).toBeVisible()
})

test('e-mail já em uso recusa no próprio campo, sem fechar o diálogo', async ({ page }) => {
  await preparar(page)

  await page.getByRole('button', { name: /^Novo/ }).click()
  await page.getByLabel('Nome').fill('Duplicado')
  await page.getByLabel('E-mail (login)').fill('admin@teste.local')
  await page.getByLabel('Senha inicial').fill('senhaforte123')
  await page.getByRole('button', { name: 'Salvar' }).click()

  await expect(page.getByRole('alert')).toContainText('e-mail')
  await expect(page.getByRole('dialog')).toBeVisible()
})

test('editar: ninguém desativa o próprio acesso', async ({ page }) => {
  await preparar(page)

  // Alice Administradora é quem está logada.
  await linha(page, 'Alice Administradora').getByRole('button', { name: 'Editar' }).click()
  await expect(page.getByRole('dialog')).toContainText('Editar atendente')

  const interruptor = page.getByLabel('Acesso ativo')
  await expect(interruptor).toBeDisabled()
  await expect(page.getByText('Você não pode desativar o próprio acesso.')).toBeVisible()
})

test('editar outro atendente permite desativar, com aviso de sessão encerrada', async ({ page }) => {
  const estado = await preparar(page)

  await linha(page, 'Bruno SemHistorico').getByRole('button', { name: 'Editar' }).click()
  await page.getByLabel('Acesso ativo').click()
  await expect(page.getByText('Desativar encerra as sessões abertas desta pessoa agora mesmo.')).toBeVisible()

  await page.getByRole('button', { name: 'Salvar' }).click()
  await expect(page.getByText('Atendente atualizado.')).toBeVisible()
  await expect.poll(() => estado.atendentes.find((a) => a.id === 'atendente-sem-historico')?.ativo).toBe(false)
})

test('redefinir senha exige 8 caracteres e desbloqueia a conta', async ({ page }) => {
  await preparar(page)

  await linha(page, 'Bruno SemHistorico').getByRole('button', { name: 'Redefinir senha' }).click()
  await expect(page.getByRole('dialog')).toContainText('Redefinir senha')

  const campo = page.getByLabel('Nova senha')
  await campo.fill('curta')
  await expect(page.getByRole('button', { name: 'Redefinir' })).toBeDisabled()

  await campo.fill('senhaforte123')
  await expect(page.getByRole('button', { name: 'Redefinir' })).toBeEnabled()
  await page.getByRole('button', { name: 'Redefinir' }).click()

  await expect(page.getByText('redefinida.')).toBeVisible()
})

test('gerar senha forte preenche o campo com 14 caracteres', async ({ page }) => {
  await preparar(page)

  await page.getByRole('button', { name: /^Novo/ }).click()
  const campoSenha = page.getByLabel('Senha inicial')
  await page.getByTitle('Gerar senha forte').click()

  await expect(campoSenha).not.toHaveValue('')
  const valor = await campoSenha.inputValue()
  expect(valor.length).toBe(14)
})

// ------------------------------------------------------------- Permissões

test('permissões: aplicar modelo, alternar item e ver o efeito de dependência de tela', async ({ page }) => {
  const estado = await preparar(page)

  await linha(page, 'Bruno SemHistorico').getByRole('button', { name: 'Permissões' }).click()
  await expect(page).toHaveURL(/\/equipe\/.+\/permissoes/)
  await expect(page.getByText('Sem mudanças')).toBeVisible()

  // Aplicar o modelo Supervisão marca clientes.cadastrar, que depende de clientes.ver.
  await page.getByRole('button', { name: 'Supervisão' }).click()
  await expect(page.getByText(/\d+ mudança/)).toBeVisible()

  const cadastrarCliente = page.getByRole('checkbox', { name: 'Cadastrar cliente novo' })
  await expect(cadastrarCliente).toBeChecked()

  // Desmarcar a TELA de Clientes esmaece a ação, que continua marcada mas some de efeito.
  // O rótulo acessível do checkbox inclui a nota de "sem efeito" quando ela aparece.
  await page.getByRole('checkbox', { name: 'Abrir a tela de Clientes' }).uncheck()
  await expect(
    page.getByRole('checkbox', { name: 'Cadastrar cliente novo Criar a ficha de um cliente. Sem acesso à tela, esta ação não faz efeito.' }),
  ).toBeVisible()
  await expect(cadastrarCliente).toBeChecked()

  await page.getByRole('button', { name: 'Salvar' }).click()
  await expect(page.getByText('Permissões atualizadas.')).toBeVisible()

  await expect.poll(() => estado.atendentes.find((a) => a.id === 'atendente-sem-historico')?.permissoes.includes('clientes.ver')).toBe(false)
  await expect.poll(() => estado.atendentes.find((a) => a.id === 'atendente-sem-historico')?.permissoes.includes('clientes.cadastrar')).toBe(true)
})

test('permissões: descartar volta ao estado salvo sem chamar a API', async ({ page }) => {
  await preparar(page)

  await linha(page, 'Bruno SemHistorico').getByRole('button', { name: 'Permissões' }).click()
  await page.getByRole('checkbox', { name: 'Escrever e enviar mensagem' }).click()
  await expect(page.getByText(/\d+ mudança/)).toBeVisible()

  await page.getByRole('button', { name: 'Descartar' }).click()
  await expect(page.getByText('Sem mudanças')).toBeVisible()
})

// ------------------------------------------------------------------ MFA

test('MFA: emitir ticket e resetar exigem senha e código do próprio ator', async ({ page }) => {
  await preparar(page)

  // Diego Diretor é Titular e não é quem está logado — só assim o ícone existe na linha dele.
  await linha(page, 'Diego Diretor').getByRole('button', { name: 'Segurança (MFA)' }).click()

  await expect(page.getByRole('dialog')).toContainText('Segurança (MFA) — Diego Diretor')
  const botaoEmitir = page.getByRole('button', { name: 'Emitir ticket' })
  const botaoResetar = page.getByRole('button', { name: 'Resetar MFA' })
  await expect(botaoEmitir).toBeDisabled()
  await expect(botaoResetar).toBeDisabled()

  await page.getByLabel('Sua senha').fill('minhasenha123')
  await page.getByLabel('Seu código de autenticador').fill('123456')
  await expect(botaoEmitir).toBeEnabled()

  await botaoEmitir.click()
  await expect(page.getByText('Ticket emitido')).toBeVisible()
  await expect(page.getByText('TICKET-E2E')).toBeVisible()
})

test('MFA: resetar sem motivo recusa, e a confirmação nativa protege a ação destrutiva', async ({ page }) => {
  const estado = await preparar(page)

  await linha(page, 'Diego Diretor').getByRole('button', { name: 'Segurança (MFA)' }).click()

  await page.getByLabel('Sua senha').fill('minhasenha123')
  await page.getByLabel('Seu código de autenticador').fill('123456')
  await page.getByRole('button', { name: 'Resetar MFA' }).click()

  await expect(page.getByRole('alert')).toContainText('motivo')

  await page.getByLabel('Motivo (obrigatório só para resetar)').fill('Perdeu o celular')
  page.once('dialog', (d) => d.accept())
  await page.getByRole('button', { name: 'Resetar MFA' }).click()

  await expect(page.getByText('MFA de Diego Diretor resetado.')).toBeVisible()
  await expect.poll(() => estado.atendentes.find((a) => a.id === 'atendente-privilegiado')?.mfaAtivo).toBe(false)
})

// -------------------------------------------------------------- Exclusão

test('exclusão: atendente sem histórico é removido da lista', async ({ page }) => {
  const estado = await preparar(page)

  await linha(page, 'Bruno SemHistorico').getByRole('button', { name: 'Excluir' }).click()

  await expect(page.getByRole('dialog')).toContainText('Excluir Bruno SemHistorico?')
  await page.getByRole('button', { name: 'Excluir', exact: true }).click()

  await expect(page.getByText('Bruno SemHistorico excluído.')).toBeVisible()
  await expect(linha(page, 'Bruno SemHistorico')).toHaveCount(0)
  await expect.poll(() => estado.atendentes.some((a) => a.id === 'atendente-sem-historico')).toBe(false)
})

test('exclusão: atendente com histórico recusa e oferece desativar', async ({ page }) => {
  await preparar(page)

  await linha(page, 'Carla ComHistorico').getByRole('button', { name: 'Excluir' }).click()

  // A tela só sabe que há histórico DEPOIS da tentativa recusada (409) — não
  // há como saber de antemão, diferente de Clientes (ver o comentário do componente).
  await page.getByRole('button', { name: 'Excluir', exact: true }).click()

  await expect(page.getByRole('dialog')).toContainText('Este cadastro tem histórico')
  await expect(page.getByText('Carla ComHistorico já tem atendimento ou ação registrada')).toBeVisible()

  await page.getByRole('button', { name: 'Abrir cadastro para desativar' }).click()
  await expect(page.getByRole('dialog')).toContainText('Editar atendente')
  await expect(page.getByLabel('Nome')).toHaveValue('Carla ComHistorico')
})

test('exclusão: a própria linha não mostra o ícone de excluir', async ({ page }) => {
  await preparar(page)

  await expect(linha(page, 'Alice Administradora').getByRole('button', { name: 'Excluir' })).toHaveCount(0)
})
