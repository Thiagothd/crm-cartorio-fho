# Contexto do projeto

> **Status:** vigente para a entrega acadêmica.
> **Última atualização:** 2026-11-08

## 1. O problema

Serventias extrajudiciais recebem grande volume de perguntas repetidas sobre
documentos, prazos e emolumentos. O contato acontece em boa parte pelo WhatsApp,
de forma manual: o funcionário lê, procura a informação e responde.

O setor já usa o canal, mas automatiza pouco. O Raio-X dos Cartórios (ANOREG/BR)
aponta que 60% das serventias usam aplicativos de mensagem no atendimento e que
8,25% implementaram algum chatbot.

O desafio técnico é automatizar sem perder exatidão. Prazo legal, valor de taxa e
documento exigido não podem ser inventados por um modelo de linguagem.

## 2. Serventia de referência

Os requisitos foram levantados com um cartório parceiro de Notas e Registro Civil
em Gameleiras/MG. Os dados institucionais usados na configuração de demonstração são:

| Dado | Valor |
| --- | --- |
| Atribuições | Tabelionato de Notas e Registro Civil |
| Cidade | Gameleiras/MG |
| Endereço | Praça São Francisco, nº 70, Centro |
| Horário | Segunda a sexta-feira, das 8h às 17h |

Os nomes de titular, substituto e atendentes presentes no repositório são fictícios.

## 3. Soluções existentes

Já existem soluções comerciais de atendimento por WhatsApp voltadas a cartórios,
em geral oferecidas como serviço em nuvem sobre a API oficial da Meta. O projeto
explora um caminho diferente:

- **Resposta fundamentada:** o modelo só usa informação cadastrada e escala para uma pessoa quando não tem base para responder.
- **Resiliência:** dois provedores de IA com troca automática.
- **Dados sob controle da serventia:** hospedagem própria, trilha de acesso e acesso técnico autorizado.

## 4. Glossário

| Termo | Significado |
| --- | --- |
| Serventia | Unidade cartorária extrajudicial |
| Emolumento | Valor do ato, definido por tabela estadual |
| Ato | Serviço praticado pelo cartório |
| Funil | Progressão do atendimento por etapas |
| Grounding | Restrição da resposta do modelo a um contexto fornecido |
