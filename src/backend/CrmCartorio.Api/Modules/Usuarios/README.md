# Módulo: Usuarios

Atendentes (funcionários da serventia) e autenticação.

`Atendente` **não** é `Cliente`. Cliente é o contato do WhatsApp e nunca acessa o
sistema.

## Perfis

| Perfil | Pode |
| --- | --- |
| `Atendente` | Assumir conversa livre, responder a sua, liberar a sua, mover etapa/setor da sua, encerrar a sua |
| `Supervisor` | Tudo acima + transferir conversa de terceiro + reabrir + gerir cadastros |
| `Administrador` | Tudo acima + gerir atendentes |

Aguarda validação do cartório parceiro (`docs/REQUISITOS.md`, seção 5, pendência 7). É o
único item da lista de pendências que exigiria mexer em código se a resposta dele
pedir permissão mais fina.

## Senha

PBKDF2-HMAC-SHA256, 210.000 iterações, salt por usuário. O número de iterações
vai dentro do próprio hash (`pbkdf2.sha256$<iter>$<salt>$<hash>`), então
aumentá-lo no futuro não invalida as senhas já cadastradas.

## Detalhes do login que não são acidentais

- **Mensagem única** para e-mail inexistente, senha errada e usuário desativado.
  Diferenciar os três entregaria a um atacante a lista de e-mails válidos.
- **A senha é conferida mesmo quando o e-mail não existe**, contra um hash
  descartável. Sair antes faria o tempo de resposta revelar quais e-mails existem.
- **Ninguém desativa o próprio acesso.** Um administrador se trancando para fora
  deixaria o cartório sem quem gerencie acessos, e o desbloqueio sairia por SQL
  manual.
- **`ObterSessaoAsync` não emite token novo.** O cliente já tem o dele e a
  validade não muda por consultar a sessão.

## Pendência da Fase 3

`AutenticacaoService.AutenticarAsync` busca o atendente só pelo e-mail, porque no
login ainda não se sabe o tenant. O e-mail é único **por tenant**, não
globalmente — com mais de um cartório na base essa busca fica ambígua. Antes do
multi-tenant real, o tenant precisa ser resolvido ali (subdomínio, código do
cartório na tela de login, ou índice único global). Está comentado no código.
