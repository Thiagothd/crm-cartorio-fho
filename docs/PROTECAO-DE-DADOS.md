# Proteção de dados

> **Status:** vigente para a entrega acadêmica.
> **Última atualização:** 2026-11-08

## 1. Princípios

O sistema trata dados pessoais de clientes de cartório e segue os princípios de
minimização e rastreabilidade da LGPD. Todos os testes e a demonstração acadêmica
usam dados fictícios. Como o texto da mensagem é enviado a uma API externa de IA,
usar dado fictício evita que informação pessoal real saia do sistema na avaliação.

## 2. Controles implementados

| Controle | Função | Onde |
| --- | --- | --- |
| Trilha de acesso | Registra quem abriu qual conversa ou ficha e quando | `Modules/Seguranca` |
| Cifra em repouso | Protege campos sensíveis no banco | `Modules/Seguranca` |
| Acesso técnico autorizado | Exige aprovação na tela, com prazo limitado e revogável | `Modules/Seguranca`, `sql/046-acesso-tecnico.sql` |
| Mascaramento de PII | Reduz exposição de dados em registros | `Modules/Seguranca` |
| Permissões granulares | Controlam o que cada atendente pode fazer | `Modules/Usuarios` |
| Autenticação em dois fatores | Protege contas com acesso administrativo | `Modules/Usuarios` |
| Identificação do autor | Registra qual atendente enviou cada mensagem | `Modules/Atendimento` |

## 3. Cifra em repouso

A cifra nasce dormente em todo ambiente e só é ativada quando a chave é
provisionada manualmente. A chave nunca entra no repositório.

## 4. Limitações

A proteção depende também do ambiente de hospedagem e dos provedores externos de
IA e de WhatsApp. O uso em produção definitiva exige revisão jurídica e a API
oficial da Meta.
