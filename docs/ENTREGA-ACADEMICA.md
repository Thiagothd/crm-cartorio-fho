# Entrega acadêmica

> **Status:** em preparação para a N2.
> **Última atualização:** 2026-11-08

Projeto Final de Aplicações Integradas em Engenharia da Computação, FHO.
Integrantes: Thiago Antunes de Oliveira (RA 114667), Matheus Moreira (RA 114882) e Matheus Sheid dos Santos (RA 109896).
Professor: Thiago Giroto Milani.

## 1. Pré-requisitos do roteiro

| # | Requisito | Como o projeto atende | Onde conferir |
| --- | --- | --- | --- |
| 1 | Desenvolvimento próprio em linguagem de programação | Backend em C# e frontend em TypeScript, com a regra de atendimento implementada no projeto | `src/backend/CrmCartorio.Api/Modules/`, `src/frontend/src/features/` |
| 2 | Integração com LLM | Resposta restrita ao catálogo e à base de conhecimento, com escalonamento para humano e contingência entre provedores | `Modules/RespostaInteligente/` |
| 3 | Banco de dados SQL | SQL Server com esquema versionado em scripts revisados | `sql/`, `docs/MODELO-DADOS.md` |
| 4 | Interface amigável e responsiva | Painel do atendente com regras visuais documentadas | `src/frontend/`, `docs/DESIGN.md` |
| 5 | Ideia inovadora para problema real | Atendimento automatizado para cartórios, com resposta fundamentada e canal real | `docs/CONTEXTO.md` |

## 2. Objetivos específicos do pré-projeto

| # | Objetivo | Situação | Evidência |
| --- | --- | --- | --- |
| 1 | Clientes, catálogo e histórico em SQL Server | Implementado | `Modules/Clientes`, `Modules/Servicos`, `Modules/Atendimento` |
| 2 | Canal real de mensagens com WhatsApp | Implementado | `Modules/CanalWhatsApp`, `verificar-canal-whatsapp.cs` |
| 3 | Bot de triagem por setor | Implementado | `verificar-automacao.cs`, `verificar-menu-em-niveis.cs` |
| 4 | LLM restrita ao catálogo, com medição de acerto e escalonamento | Implementado e medido em amostra de 18 perguntas | `artigo/`, `avaliacao/`, `src/backend/avaliar-ia.cs` |
| 5 | Contingência entre dois provedores | Implementada, verificada e ensaiada com falha simulada e provedor real | `verificar-resposta-inteligente.cs`, `avaliacao/resultados/` |
| 6 | Painel responsivo com filtros, em duas resoluções | Implementado; 11 telas em 1366x768 e 390x844 sem estouro horizontal | `artigo/`, seção de resultados |
| 7 | Validação com cartório parceiro usando dados fictícios | Demonstração em 09/09/2026, com observações registradas no artigo | `artigo/`, seção de resultados |

## 3. Linha do tempo

O histórico deste repositório vai de 15/08/2026 a 04/11/2026. O pré-projeto (N1)
foi entregue em 15/09/2026 e declara que o trabalho partiu de uma base existente.

| Período | Commits | Conteúdo |
| --- | --- | --- |
| 15/08 a 14/09 | 273 | Base do sistema: autenticação, clientes, inbox, trava de conversa, catálogo, mensagens automáticas, canal WhatsApp, resposta inteligente, contingência entre provedores e blindagem LGPD |
| 22/09 a 01/10 | 21 | Chave geral de automação, correção de mensagem enviada, resposta pelo celular, tema escuro, menu em níveis e sequência de coleta de dados, controle do robô |
| 28/10 a 04/11 | 11 | Devedores e cobrança, áudio de voz, colagem de imagem, proteção contra robô do outro lado, atualização do modelo de IA e correções do canal |

A contagem considera apenas os commits preservados nesta cópia. Commits que só
alteravam arquivos omitidos não aparecem.

## 4. Mudanças em relação ao pré-projeto

- **Uso em piloto:** o cartório parceiro passou a utilizar o sistema em piloto durante o semestre. A avaliação acadêmica e a demonstração usam somente dados fictícios.
- **Provedores de IA:** na demonstração, o Gemini 3.6 Flash é tentado primeiro e o Claude Haiku 5.5 assume em caso de falha.
- **Funcionalidades adicionais:** controle de devedores, áudio e colagem de imagem foram incluídos a partir da validação com o cartório parceiro.

## 5. Integridade do repositório

Autores, datas e ordem dos commits são os reais. Por privacidade, nomes de pessoas
e telefones foram substituídos por valores fictícios, e arquivos operacionais do
servidor foram omitidos.
