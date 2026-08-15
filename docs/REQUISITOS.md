# REQUISITOS

> **Status:** confirmados. Fase 1 implementada e ampliada para a apresentação.
> **Última atualização:** 2026-08-14

Origem de cada requisito:

| Sigla | Significado |
| --- | --- |
| **CLI** | Pedido direto do cartório parceiro (mensagem do cliente) |
| **MKT** | Derivado da pesquisa de mercado / concorrência |
| **TEC** | Decisão técnica nossa para sustentar um requisito CLI ou MKT |

---

## 1. Requisitos funcionais — Fase 1

| ID | Requisito | Origem | Prioridade | Status |
| --- | --- | --- | --- | --- |
| RF-001 | Atendente autentica com e-mail e senha e recebe token de sessão | TEC | Alta | ✅ |
| RF-002 | Administrador cadastra, edita, ativa e desativa atendentes | TEC | Alta | ✅ |
| RF-003 | Sistema mantém cadastro de clientes (contato final), identificados pelo número de WhatsApp | CLI | Alta | ✅ |
| RF-004 | Inbox lista as conversas do cartório em ordem de última mensagem | CLI | Alta | ✅ |
| RF-005 | Inbox filtra por setor, etapa, status, atendente responsável e busca textual | MKT | Alta | ✅ |
| RF-006 | Vários atendentes operam o mesmo número de WhatsApp | CLI | Alta | ✅ |
| RF-007 | Toda mensagem enviada registra **qual atendente** a enviou, e a interface exibe isso | CLI | Alta | ✅ |
| RF-008 | Atendente **assume** uma conversa, tornando-se o responsável | CLI | Alta | ✅ |
| RF-009 | Conversa assumida fica travada para os demais atendentes | MKT | Alta | ✅ |
| RF-010 | Tentativa de assumir conversa já assumida retorna conflito tratado, com o nome de quem assumiu | TEC | Alta | ✅ |
| RF-011 | Atribuição, liberação e novas mensagens refletem em tempo real em todos os painéis abertos | TEC | Alta | ✅ |
| RF-012 | Atendente responsável pode **liberar** a conversa de volta para a fila | MKT | Média | ✅ |
| RF-013 | Supervisor/administrador pode **transferir** conversa para outro atendente | MKT | Média | ✅ |
| RF-014 | Encerrar atendimento arquiva a conversa e a remove da lista ativa | CLI | Alta | ✅ |
| RF-015 | Conteúdo de conversa encerrada **nunca é apagado** — soft-delete, sem exclusão física | CLI | Alta | ✅ |
| RF-016 | Conversa encerrada pode ser reaberta, com registro de quem reabriu | MKT | Média | ✅ |
| RF-017 | Conversa avança por etapas de funil configuráveis | CLI | Alta | ✅ |
| RF-018 | Conversa é roteada para um setor, dimensão independente da etapa | MKT | Alta | ✅ |
| RF-019 | Setores são cadastrados e ordenados por tenant, sem alteração de código | MKT | Alta | ✅ |
| RF-020 | Etapas de funil são cadastradas e ordenadas por tenant, sem alteração de código | MKT | Alta | ✅ |
| RF-021 | Toda mudança de etapa ou de setor grava histórico com autor, data e observação | MKT | Alta | ✅ |
| RF-022 | Catálogo de tipos de serviço cartorário, editável por tenant | MKT | Alta | ✅ |
| RF-023 | Cada tipo de serviço tem prazo (SLA) configurável, com percentual de alerta | MKT | Média | ✅ |
| RF-024 | Conversa vinculada a um tipo de serviço ganha prazo-limite calculado | MKT | Média | ✅ |
| RF-025 | Sistema classifica a conversa como Em dia / Em alerta / Atrasada conforme o SLA | MKT | Média | ✅ |
| RF-026 | Cada atendimento recebe protocolo automático, único no tenant | MKT | Alta | ✅ |
| RF-027 | Painel exibe contadores por etapa, por setor e por situação de prazo | MKT | Média | ✅ |
| RF-028 | Envio de mensagem simulado, sem WhatsApp real, para validar o fluxo inteiro | TEC | Alta | ✅ |
| RF-029 | Base de demonstração com clientes, conversas e mensagens fictícias | TEC | Alta | ✅ |
| RF-030 | Mensagens do cliente não lidas são contabilizadas e zeradas ao abrir a conversa | MKT | Média | ✅ |
| RF-031 | Indicadores do painel e etapas do funil aplicam o filtro correspondente na inbox ao clicar | CLI | Alta | ✅ |
| RF-032 | Menu principal usa superfície escura da paleta e pode ser recolhido sem perder o acesso às rotas | CLI | Média | ✅ |
| RF-033 | Mensagens exibem claramente o nome do cliente e o nome do atendente que respondeu | CLI | Alta | ✅ |
| RF-034 | Atendente pode copiar uma mensagem e responder citando uma mensagem anterior da mesma conversa | CLI | Alta | ✅ |
| RF-035 | Interface exibe os estados Pendente / Enviada / Entregue / Lida / Falha; na Fase 1, a leitura é simulada quando o cliente responde | CLI | Alta | ✅ |
| RF-036 | Cadastro de cliente guarda qualificação da parte (documentos, estado civil, filiação, profissão e endereço) de forma progressiva | CLI | Alta | ✅ |
| RF-037 | Clientes podem ser filtrados por tipo, estado civil, cidade, documento ausente, endereço incompleto e atendimento ativo | CLI | Alta | ✅ |
| RF-038 | Mensagens automáticas são **cadastro editável por tenant**, com gatilho, texto e marcadores — sem alteração de código | CLI | Alta | ✅ |
| RF-039 | Primeiro contato recebe boas-vindas com menu montado a partir dos **setores ativos**, atualizado sozinho quando o cadastro muda | CLI | Alta | ✅ |
| RF-040 | Resposta do cliente ao menu atribui o setor automaticamente; **uma tentativa** e o atendimento cai para uma pessoa | CLI | Alta | ✅ |
| RF-041 | Mudança de etapa dispara aviso ao cliente conforme regra cadastrada (ex.: "Pronto / entrega") | CLI | Alta | ✅ |
| RF-042 | Prazo perto de vencer e prazo vencido disparam aviso, no mesmo percentual já configurado por serviço | CLI | Média | ✅ |
| RF-043 | Mensagem gerada pelo sistema aparece no fio com rótulo visual "Automática", distinta da escrita por atendente | CLI | Alta | ✅ |
| RF-044 | Respostas rápidas são cadastro por tenant, com atalho, título e texto | CLI | Alta | ✅ |
| RF-045 | Digitar `/` no campo de resposta abre o seletor de respostas rápidas, filtra ao digitar e **insere no campo** — nunca envia sozinho | CLI | Alta | ✅ |
| RF-046 | Cargo na serventia é campo próprio, independente do perfil de acesso | CLI | Média | ✅ |

---

## 2. Requisitos não funcionais — Fase 1

| ID | Requisito | Origem | Status |
| --- | --- | --- | --- |
| RNF-001 | Backend em ASP.NET Core 10 + EF Core 10, arquitetura vertical slice | TEC | ✅ |
| RNF-002 | Frontend em React + Vite + TypeScript `strict` + Tailwind + shadcn/ui | TEC | ✅ |
| RNF-003 | Banco SQL Server, hospedagem em IIS próprio — sem custo adicional de infra | TEC | ✅ |
| RNF-004 | Toda entidade de negócio carrega `TenantId` desde a Fase 1, mesmo com um tenant só | TEC | ✅ |
| RNF-005 | Toda consulta de dado de negócio filtra por `TenantId` | TEC | ✅ |
| RNF-006 | Senha armazenada com hash PBKDF2 e salt por usuário — nunca texto claro | TEC | ✅ |
| RNF-007 | Nenhum DDL/DML executado pelo agente; scripts ficam em `sql/` para revisão manual | TEC | ✅ |
| RNF-008 | Nenhuma exclusão física de conversa ou mensagem em nenhum caminho de código | CLI | ✅ |
| RNF-009 | Concorrência de escrita em `Conversa` controlada por `rowversion` | TEC | ✅ |
| RNF-010 | Erro de negócio retorna `ProblemDetails` com código estável, não stack trace | TEC | ✅ |
| RNF-011 | Domínio nomeado em português; termos técnicos em inglês | TEC | ✅ |
| RNF-012 | Nenhuma biblioteca não oficial de WhatsApp em nenhuma fase | TEC | ✅ |
| RNF-013 | Segredo (JWT, connection string) fora do controle de versão | TEC | ✅ |

---

## 3. Fora de escopo (acordado)

Registrado para defesa contra crescimento de escopo:

| Item | Por quê |
| --- | --- |
| Financeiro / emolumentos | Emolumento é tabela estadual por lei. Não é preço livre nem nosso problema |
| Emissão de selo digital | Competência do sistema oficial da serventia |
| Integração TJMG / CENSEC / ONR / SREI / CRC / Central de Protesto | Nenhuma foi pedida; cada uma é projeto próprio com exigência regulatória |
| Substituir o sistema oficial do cartório | Somos complemento de atendimento |
| Funil de vendas / prospecção / oportunidade | "Funil" aqui é progressão de atendimento, não pipeline comercial |
| Portal de consulta para o cliente final | Não pedido. O canal do cliente é o WhatsApp |
| App mobile nativo | Web responsivo atende |
| Chatbot **por IA** | O bot de triagem da Fase 1 é regra de cadastro: número da opção ou nome do setor, uma tentativa, e cai para humano. Interpretar linguagem livre é outra coisa — candidato a Fase 4 |
| Anexo de arquivo (foto de documento) | Fase 2 — depende do formato de mídia da Cloud API |
| Confirmação real de entrega e leitura | A interface está pronta, mas o dado verdadeiro vem do webhook da Meta na Fase 2 |
| Relatório para Corregedoria | Não pedido |

---

## 4. Requisitos das fases seguintes

Registrados para não se perderem. **Não implementar agora.**

### Fase 2 — WhatsApp real

| ID | Requisito | Bloqueio |
| --- | --- | --- |
| RF-101 | Receber mensagem via webhook da Cloud API da Meta | Pré-req. A e B |
| RF-102 | Enviar mensagem via Graph API | Pré-req. A e B |
| RF-103 | Enviar template aprovado fora da janela de 24h | Pré-req. A e B |
| RF-104 | Receber e armazenar mídia (imagem, documento, áudio) | Pré-req. A e B |
| RF-105 | Refletir status de entrega real (enviada / entregue / lida / falha) | Pré-req. A e B |
| RF-106 | Painel de custo por mensagem, considerando a cobrança de serviço a partir de 2026-10-01 | Pré-req. A e B |

**Pré-requisito A:** CNPJ do cartório + conta Meta Business verificada.
**Pré-requisito B:** número de telefone dedicado, não ativo no app WhatsApp pessoal.

Ambos são processos externos com terceiros. **Iniciar em paralelo à Fase 1.**

### Fase 3 — Multi-tenant real

| ID | Requisito |
| --- | --- |
| RF-201 | Isolamento efetivo por cartório cliente, com verificação em todas as camadas |
| RF-202 | Onboarding de novo cartório sem intervenção manual no banco |
| RF-203 | Embedded Signup da Meta — cada cliente verifica a própria conta com o próprio CNPJ |
| RF-204 | Cobrança / assinatura por tenant |

RF-203 é decisão de risco, não só técnica: com Embedded Signup, a equipe do projeto nunca
manuseia documento de terceiro.

---

## 5. Pendências de validação com o cartório parceiro

**Isto é o que precisa da palavra dele antes da apresentação.**

### 5.1 Prioridade alta — dados que não fecham

Estão gravados no sistema exatamente como informados, para conferência direta
contra a página oficial. **Aparecem na tela**, então erro aqui custa credibilidade.

| # | Dado | Está no sistema | Problema |
| --- | --- | --- | --- |
| 1 | **CNPJ** | `21.35.122/6000-10` | 13 dígitos; CNPJ tem **14**. É o documento que a Meta valida no pré-requisito A da Fase 2 — errado ali, o processo trava |
| 2 | **Telefone** | `(38) 99977-891` | 10 dígitos; celular com DDD tem **11** |
| 3 | **CNS** | `04.679-7` | Conferir o formato na fonte |

### 5.2 Nomes de pessoas

| # | Pendência | Está no sistema |
| --- | --- | --- |
| 4 | Titular e substituto | Helena Campos Nogueira e Rafael Campos Nogueira — **nomes fictícios** |
| 5 | **Demais atendentes** | "Carla Menezes" e "Paula Ribeiro" são **FICTÍCIOS**. Existem porque a trava de conversa só se demonstra com mais de um atendente na fila. Nomes fictícios de demonstração |

### 5.3 Catálogo de serviços

Setores agora refletem as atribuições reais (Notas + Registro Civil), e os atos de
Registro de Imóveis foram removidos. Restam três pontos:

| # | Pendência | Está no sistema |
| --- | --- | --- |
| 6 | **"Notificação extrajudicial" é atribuição desta serventia?** | Listado, categoria "Outros", com aviso `AGUARDANDO CONFIRMAÇÃO` na descrição. Costuma correr no Registro de Títulos e Documentos. Se não for, desativar pela tela |
| 7 | **Atos que faltam no catálogo** | Ver seção 5.6 — lista de candidatos que notei ausentes |
| 8 | **Prazos (SLA) reais** | Valores plausíveis, **não reais**. Cadastro puro |

### 5.4 Configuração do funil

| # | Pendência | Está no sistema | Impacto se mudar |
| --- | --- | --- | --- |
| 9 | Nomes e ordem das **etapas** | Atendimento inicial → Captação → Em processamento/exigência → Pronto/entrega | Nenhum no código — é cadastro |
| 10 | Etapa que conta como **final** | "Pronto / entrega" | Nenhum no código — é flag no cadastro |
| 11 | **Formato do protocolo** | `AAAAMM-NNNN` (ex.: `202608-0042`) | **Requer código** se houver numeração oficial de livro a respeitar |
| 12 | Encerrar **exige motivo**? | Motivo é opcional | Uma linha de validação |

### 5.5 Perfis de acesso

| # | Pendência | Está no sistema | Impacto se mudar |
| --- | --- | --- | --- |
| 13 | Quem pode transferir conversa de terceiro | Supervisor e Administrador | Ajuste de regra |
| 14 | ~~**Cargo por atendente**~~ **RESOLVIDO** | `Atendentes.Cargo` é coluna própria, independente de `Perfil`. Valores da Lei 8.935/94: Titular, Substituto, Escrevente autorizado, Escrevente, Auxiliar. Editável na tela de Atendentes | Nenhum — a separação foi feita antes que outras telas passassem a depender do campo fundido |

### 5.6 Atos possivelmente ausentes do catálogo

Notei que faltam atos comuns para uma serventia de Notas + Registro Civil.
**Não foram adicionados** — inventar prazo e nome de ato seria repetir o erro dos
cinco setores. Levar a lista para o cartório parceiro confirmar quais entram:

| Atribuição | Ato candidato |
| --- | --- |
| Registro Civil | Habilitação de casamento |
| Registro Civil | Averbação em registro civil (divórcio, óbito, alteração de nome) |
| Registro Civil | Registro de nascimento (o ato, não a certidão) |
| Registro Civil | Registro de óbito |
| Notas | Ata notarial |
| Notas | Testamento público |
| Notas | Escritura de inventário e partilha |
| Notas | Escritura de divórcio consensual |
| Notas | Escritura declaratória de união estável |

### 5.7 Mensagens automáticas — o que precisa da palavra dele

**Esta é a seção mais sensível da lista.** Todo o resto que aguarda validação é
configuração interna; aqui é texto que sai do cartório e chega ao cliente com o nome
da serventia. Um erro de tom custa mais caro que um erro de cadastro.

Tudo abaixo é **cadastro puro** — o cartório parceiro reescreve, liga e desliga pela tela, sem
release. Nada exige código.

**Todas foram preenchidas com a melhor suposição defensável**, em vez de ficarem em
aberto. O que está no sistema hoje é usável numa apresentação; o que falta é ele
confirmar ou trocar.

| # | Pendência | Suposição adotada | Como trocar |
| --- | --- | --- | --- |
| 15 | Texto de cada mensagem | Redação profissional e cordial, em português correto, padrão de atendimento de cartório. Endereço e horário são os dados oficiais do CNJ | Configurações → Mensagens automáticas |
| 16 | Quais avisos automáticos | Conclusão **ligado**, prazo **desligado** — ver tabela abaixo | Chave "Regra ativa" no cadastro |
| 17 | Menu de triagem | **Por setor** (Notas / Registro Civil). É a estrutura de dados que já existe, e sair dela para "por tipo de ato" depois é mais barato que o contrário | O menu sai de `{MenuSetores}`; trocar exige decidir a nova estrutura |
| 18 | Horário e endereço no texto | "de segunda a sexta-feira, das 8h às 17h", "Praça São Francisco, nº 70, Centro, Gameleiras/MG" — dados oficiais confirmados (CONTEXTO.md §2) | Editar o texto da mensagem |
| 19 | Respostas rápidas | Seis cadastradas: `/horario`, `/prazo`, `/valor`, `/firma`, `/escritura`, `/certidao` | Configurações → Respostas rápidas |

> **Pendência 5 continua aberta:** "Carla Menezes" e "Paula Ribeiro" seguem sendo
> **nomes fictícios**, mantidos porque a trava de conversa só se demonstra com mais
> de um atendente na fila. Nomes fictícios de demonstração.

**A decisão que importa — automático ou manual, por aviso:**

| Aviso | Situação hoje | Por que essa escolha, e o risco de mudar |
| --- | --- | --- |
| Boas-vindas + menu | **Ativo** | Baixo risco: é o primeiro contato e ninguém está esperando resposta humana ainda |
| Encaminhamento para atendente | **Ativo** | Só sai quando o cliente escreveu algo fora do menu. Não avisar ali deixaria a pessoa no vácuo |
| Ato concluído ("Pronto / entrega") | **Ativo** | É o aviso de maior valor percebido. Risco: dispara na mudança de etapa, então mover a conversa por engano avisa o cliente cedo demais |
| Prazo perto de vencer | **DESATIVADO** | Pode soar como desculpa antecipada. cartório parceiro decide |
| Prazo vencido | **DESATIVADO** | Contar ao cliente que o cartório estourou o próprio prazo é postura comercial, não detalhe técnico. **Não ligar sem a palavra dele** |

Os dois avisos de prazo nascem desativados de propósito: o mecanismo está pronto e
demonstrável, mas nenhuma mensagem sai até que ele diga que quer.

> As pendências 6 a 10 e 15 a 19 são **cadastro puro**: o cartório parceiro reconfigura pela tela
> sem que uma linha de código mude. As que exigem código estão marcadas — 11 e 14.
